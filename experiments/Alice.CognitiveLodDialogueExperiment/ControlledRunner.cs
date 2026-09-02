using System.Net;
using System.Text;
using System.Text.Json;
using Alice.Activities;
using Alice.Actors;
using Alice.Authority;
using Alice.Cognition;
using Alice.LivingTown;
using Alice.ModelRuntime;
using Alice.Npc;
using Alice.ProductRuntime;
using Alice.Social;

namespace Alice.CognitiveLodDialogueExperiment;

internal static class ControlledRunner
{
    public static async Task<IReadOnlyList<DialogueLifecycleRecord>> RunAsync(
        StudyInputs inputs,
        TownWorldConfiguration world,
        DialogueSurfaceProfile surface,
        int repeats,
        bool live,
        StudyArtifactWriter? artifacts,
        CancellationToken cancellationToken)
    {
        if (repeats <= 0) throw new ArgumentOutOfRangeException(nameof(repeats));
        var records = new List<DialogueLifecycleRecord>();
        using LiveClients? liveClients = live
            ? LiveClients.Create(world, artifacts ?? throw new ArgumentNullException(nameof(artifacts)))
            : null;
        foreach (DialogueCase studyCase in inputs.Cases.Cases)
        {
            ExpectedCase expected = inputs.ExpectedById[studyCase.CaseId];
            for (int repeat = 1; repeat <= repeats; repeat++)
            {
                DialogueLifecycleRecord record = studyCase.Group == "l0"
                    ? RunL0(studyCase, expected, repeat, artifacts?.RunId ?? "preflight")
                    : await RunModelRouteAsync(
                        studyCase,
                        expected,
                        repeat,
                        world,
                        surface,
                        live,
                        liveClients,
                        artifacts,
                        cancellationToken).ConfigureAwait(false);
                records.Add(record);
                artifacts?.AddLifecycle(record);
            }
        }
        return records;
    }

    private static DialogueLifecycleRecord RunL0(
        DialogueCase studyCase,
        ExpectedCase expected,
        int repeat,
        string runId)
    {
        L0CaseState state = studyCase.L0State
            ?? throw new InvalidDataException($"L0 case {studyCase.CaseId} has no scorer state.");
        ActorId host = new($"host-{studyCase.CaseId.ToLowerInvariant()}-{repeat}");
        ActorId speaker = new(studyCase.SpeakerId);
        ActorId responder = new(studyCase.ResponderId);
        string suffix = $"{studyCase.CaseId.ToLowerInvariant()}-{repeat}";
        var session = new ConversationSession(
            new ConversationSessionId($"dialogue-lod-{suffix}"),
            [speaker, responder]);
        var gatheringRef = new GatheringRef($"dialogue-lod-gathering-{suffix}");
        var placeRef = new PlaceRef($"dialogue-lod-place-{suffix}");
        var invite = new SemanticDialogueAct(
            new SemanticDialogueActId($"dialogue-lod-invite-{suffix}"),
            SemanticDialogueActKind.Invite,
            speaker,
            [responder],
            new DialogueTopicRef("dialogue-lod-controlled-invite"),
            [],
            new DialogueInvitePayload(gatheringRef, 1, responder, null));
        _ = session.Accept(invite);
        var gathering = new ScheduledGathering(
            gatheringRef,
            host,
            placeRef,
            new SimTime(10),
            new SimTime(30),
            [speaker],
            ScheduledGatheringLifecycle.Planned,
            1);
        var gatheringAuthority = new ScheduledGatheringAuthorityRuntime(
            [host, speaker, responder],
            [placeRef],
            [new GatheringHostPlaceUseAuthorityFact(gatheringRef, host, placeRef)],
            [gathering]);
        var authority = new InvitationAcceptanceAuthorityRuntime(gatheringAuthority);
        var store = new DecisionNeedStore();
        NpcState npc = CreateL0Npc(responder, speaker, state);
        var schedulingSnapshot = new ConversationResponseSchedulingSnapshot([session], []);
        ConversationResponseSchedulingResult scheduling = ConversationResponseScheduler.Schedule(schedulingSnapshot);
        DialogueResponseOpportunity opportunity = scheduling.Selection?.Opportunity
            ?? throw new InvalidOperationException($"L0 case {studyCase.CaseId} did not produce a response opportunity.");
        OrdinaryInviteResponseHostResult result = ConversationResponseHost.StepOrdinaryInviteResponse(
            schedulingSnapshot,
            authority,
            new DecisionNeedDiscoveryRegistrar(store),
            npc,
            [],
            new DecisionNeedWorldRevision(1),
            new SimTime(5));
        string actualRoute = result.ScoringResult?.Outcome == InviteResponseScoringOutcome.OrdinaryCandidate
            ? "L0"
            : "L2";
        string terminalKind = result.ScoringResult?.OrdinaryResponseKind?.ToString() ?? "Failed";
        string? terminalReference = result.RoutingResult?.Commitment?.CommitmentId.Value
            ?? result.RoutingResult?.RecordedTurn?.Act.ActId.Value;
        bool routeMatch = StringComparer.Ordinal.Equals(actualRoute, expected.Route);
        bool terminalAcceptable = routeMatch
            && StringComparer.Ordinal.Equals(terminalKind, expected.TerminalKind)
            && result.Outcome == OrdinaryInviteResponseHostOutcome.Routed;
        return new DialogueLifecycleRecord(
            "alice.dialogue_lod.lifecycle.v1",
            runId,
            "controlled",
            studyCase.CaseId,
            repeat,
            1,
            5,
            opportunity.OpportunityId.Value,
            session.SessionId.Value,
            invite.ActId.Value,
            speaker.Value,
            responder.Value,
            studyCase.SourceKind,
            expected.Route,
            actualRoute,
            true,
            result.ScoringResult?.Outcome.ToString(),
            result.ScoringResult?.Evidence.InviteScore?.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            null,
            null,
            false,
            null,
            [],
            null,
            RegistrationNeedId(result.RegistrationOutcome),
            false,
            null,
            terminalKind,
            terminalReference,
            terminalAcceptable ? null : result.Outcome.ToString(),
            [],
            [],
            routeMatch,
            terminalAcceptable);
    }

    private static async Task<DialogueLifecycleRecord> RunModelRouteAsync(
        DialogueCase studyCase,
        ExpectedCase expected,
        int repeat,
        TownWorldConfiguration world,
        DialogueSurfaceProfile surface,
        bool live,
        LiveClients? liveClients,
        StudyArtifactWriter? artifacts,
        CancellationToken cancellationToken)
    {
        LiveTownL1DialogueRouteClient localClient;
        IModelClient<RemotePlannerResponse> remoteClient;
        HttpClient? dryHttp = null;
        if (live)
        {
            localClient = liveClients!.LocalDialogue;
            remoteClient = liveClients.Remote;
        }
        else
        {
            HttpMessageHandler handler = new ScriptedDialogueHandler(studyCase);
            if (artifacts is not null) handler = new ProviderRecordingHandler(handler, artifacts);
            dryHttp = new HttpClient(handler);
            localClient = new LiveTownL1DialogueRouteClient(
                dryHttp,
                ProductModelClientComposition.CreateLocalProfile(
                    world.Runtime.ProviderProfiles,
                    world.Runtime.ProviderQueue));
            remoteClient = new FixedUnavailableModelClient<RemotePlannerResponse>(
                ModelClientExecutionMode.LiveRemote,
                ModelClientUnavailableReason.MissingCredential);
        }

        try
        {
            using LivingTownProductComposition product = LivingTownProductComposition.Create(
                world,
                surface,
                new UnavailableInterpreter(),
                new FixedUnavailableModelClient<TownL1DecisionResponse>(
                    ModelClientExecutionMode.LiveLocal,
                    ModelClientUnavailableReason.UnsupportedRequestType),
                localClient,
                remoteClient);
            ActorId speaker = new(studyCase.SpeakerId);
            ActorId responder = new(studyCase.ResponderId);
            string suffix = $"{studyCase.CaseId.ToLowerInvariant()}-{repeat}";
            var sourceAct = new SemanticDialogueAct(
                new SemanticDialogueActId($"dialogue-lod-source-{suffix}"),
                Enum.Parse<SemanticDialogueActKind>(studyCase.SourceKind, false),
                speaker,
                [responder],
                new DialogueTopicRef("dialogue-lod-controlled"),
                [],
                null,
                DialogueResponseExpectation.Required);
            ConversationOpenResult opened = product.Conversations.Open(
                new ConversationSessionId($"dialogue-lod-{suffix}"),
                [speaker, responder],
                sourceAct,
                new SimTime(10));
            DialogueResponseOpportunity opportunity = opened.Session.PendingResponseOpportunities.Single();
            int attemptStart = artifacts?.Attempts.Count ?? 0;
            TownDialogueRoutingOutcome outcome = await product.DialogueRouting.InvokeAsync(
                opened.Session,
                opened.InitialTurn,
                studyCase.ActorVisibleText,
                new SimTime(10),
                cancellationToken).ConfigureAwait(false);
            TownL1DialogueRouteResponse? localResponse = outcome.LocalAppraisalDecoded
                ? TownL1DialogueRouteResponse.Decode(localClient.LastAssistantContent)
                : null;
            IReadOnlyList<string> localAttemptIds = artifacts?.AttemptIdsAfter(attemptStart, "dialogue_l1") ?? [];
            IReadOnlyList<string> remoteAttemptIds = artifacts?.AttemptIdsAfter(attemptStart, "dialogue_l2") ?? [];
            string actualRoute = outcome.Route.ToString();
            bool routeMatch = StringComparer.Ordinal.Equals(actualRoute, expected.Route);
            string terminalKind = outcome switch
            {
                { Route: LivingTownCognitionRoute.L1, Failure: null } => "LocalReply",
                { Route: LivingTownCognitionRoute.L2, Failure: null, L2Outcome: TownL2DialogueSettled } => "StrategicReply",
                _ => "Failed"
            };
            string? terminalReference = outcome.L2Outcome is TownL2DialogueSettled settled
                ? settled.ReplyTurn.Act.ActId.Value
                : outcome.Failure is null
                    ? opened.Session.Transcript.Last().Act.ActId.Value
                    : null;
            bool terminalAcceptable = routeMatch
                && StringComparer.Ordinal.Equals(terminalKind, expected.TerminalKind)
                && (expected.EscalationReason is null
                    || StringComparer.Ordinal.Equals(localResponse?.ReasonCode, expected.EscalationReason));
            bool escalationRequested = localResponse?.Decision == "request_escalation";
            bool? hostAccepted = escalationRequested ? outcome.Route == LivingTownCognitionRoute.L2 : null;
            return new DialogueLifecycleRecord(
                "alice.dialogue_lod.lifecycle.v1",
                artifacts?.RunId ?? "preflight",
                "controlled",
                studyCase.CaseId,
                repeat,
                1,
                10,
                opportunity.OpportunityId.Value,
                opened.Session.SessionId.Value,
                sourceAct.ActId.Value,
                speaker.Value,
                responder.Value,
                studyCase.SourceKind,
                expected.Route,
                actualRoute,
                false,
                null,
                null,
                localResponse?.Decision,
                outcome.LocalAppraisalDecoded,
                escalationRequested,
                localResponse?.ReasonCode,
                localResponse?.EvidenceRefs ?? [],
                hostAccepted,
                ParseNeedId(outcome.Evidence),
                remoteAttemptIds.Count != 0,
                outcome.L2Outcome?.GetType().Name,
                terminalKind,
                terminalReference,
                outcome.Failure,
                localAttemptIds,
                remoteAttemptIds,
                routeMatch,
                terminalAcceptable);
        }
        finally
        {
            dryHttp?.Dispose();
        }
    }

    private static NpcState CreateL0Npc(ActorId responder, ActorId speaker, L0CaseState state)
    {
        var personality = new NpcPersonalityState(
            new CognitiveFunctionProfile(0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.2),
            [
                new PersonalityTagId("dialogue-lod-controlled"),
                new PersonalityTagId("routine-social-response")
            ],
            [new WeightedPersonalityValue(
                new ValueIdentity(DeterministicInviteResponseScorer.RoutineInviteAcceptanceValueIdentity),
                state.RoutineInviteAcceptance)]);
        var knowledge = new NpcKnowledgeState(
            new NpcKnownTargetSpatialState([]),
            new NpcKnownOpportunityState([], [], []));
        var social = new NpcSocialState(
            responder,
            [new NpcRelationshipAppraisal(
                speaker,
                state.Familiarity,
                state.Trust,
                state.Affection,
                state.Respect,
                state.Fear,
                state.Grievance)]);
        return new NpcState(responder, personality, knowledge, new NpcPlanningState([], null), social);
    }

    private static string? RegistrationNeedId(DecisionNeedRegistrationOutcome? outcome) => outcome switch
    {
        RegisteredNew value => value.Need.NeedId.Value,
        DuplicateActive value => value.Need.NeedId.Value,
        _ => null
    };

    private static string? ParseNeedId(string evidence)
    {
        const string marker = "DecisionNeed ";
        int start = evidence.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        int end = evidence.IndexOf(':', start);
        return end <= start ? null : evidence[start..end];
    }

    private sealed class UnavailableInterpreter : IPlayerUtteranceInterpreter
    {
        public ValueTask<PlayerUtteranceInterpretation> InterpretAsync(
            PlayerUtteranceInterpretationRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(PlayerUtteranceInterpretation.Unavailable("Not used by the experiment."));
        }
    }

    private sealed class ScriptedDialogueHandler : HttpMessageHandler
    {
        private readonly DialogueCase _studyCase;

        public ScriptedDialogueHandler(DialogueCase studyCase) => _studyCase = studyCase;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string requestBody = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string evidenceRef = ReadEvidenceRef(requestBody);
            string content = _studyCase.Group == "l1"
                ? JsonSerializer.Serialize(new
                {
                    decision = "choose",
                    reply_kind = "CasualComment",
                    reply_text = "I understand. Let us keep this simple for now.",
                    incoming_effect = "Neutral",
                    reply_effect = "Neutral",
                    intensity = 0.1,
                    reason_code = string.Empty,
                    evidence_refs = Array.Empty<string>()
                })
                : JsonSerializer.Serialize(new
                {
                    decision = "request_escalation",
                    reply_kind = string.Empty,
                    reply_text = string.Empty,
                    incoming_effect = _studyCase.StrategicEffect!,
                    reply_effect = "Neutral",
                    intensity = 0.8,
                    reason_code = _studyCase.EscalationReason!,
                    evidence_refs = new[] { evidenceRef }
                });
            string envelope = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "stop",
                        message = new { content }
                    }
                },
                usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "application/json")
            };
        }

        private static string ReadEvidenceRef(string requestBody)
        {
            using JsonDocument envelope = JsonDocument.Parse(requestBody);
            string userJson = envelope.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            using JsonDocument user = JsonDocument.Parse(userJson);
            return user.RootElement.GetProperty("visible_evidence")[0].GetString()!;
        }
    }

    private sealed class LiveClients : IDisposable
    {
        private LiveClients(
            HttpClient http,
            LiveTownL1DialogueRouteClient localDialogue,
            IModelClient<RemotePlannerResponse> remote)
        {
            Http = http;
            LocalDialogue = localDialogue;
            Remote = remote;
        }

        public HttpClient Http { get; }
        public LiveTownL1DialogueRouteClient LocalDialogue { get; }
        public IModelClient<RemotePlannerResponse> Remote { get; }

        public static LiveClients Create(TownWorldConfiguration world, StudyArtifactWriter artifacts)
        {
            EnsureRemoteCredential(world);
            var handler = new ProviderRecordingHandler(new HttpClientHandler(), artifacts);
            var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var local = new LiveTownL1DialogueRouteClient(
                http,
                ProductModelClientComposition.CreateLocalProfile(
                    world.Runtime.ProviderProfiles,
                    world.Runtime.ProviderQueue));
            IModelClient<RemotePlannerResponse> remote = ProductModelClientComposition.CreateRemotePlanner(
                http,
                world.Runtime.ProviderProfiles,
                world.Runtime.ProviderQueue);
            return new LiveClients(http, local, remote);
        }

        public void Dispose() => Http.Dispose();

        private static void EnsureRemoteCredential(TownWorldConfiguration world)
        {
            string? name = world.Runtime.ProviderProfiles.RemotePlanner.CredentialEnvironmentVariable;
            if (string.IsNullOrWhiteSpace(name)
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
                throw new InvalidOperationException("The configured remote Provider credential is unavailable.");
        }
    }
}
