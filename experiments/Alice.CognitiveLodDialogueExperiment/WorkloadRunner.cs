using System.Net;
using System.Text;
using System.Text.Json;
using Alice.Activities;
using Alice.Actors;
using Alice.Cognition;
using Alice.Interaction;
using Alice.LivingTown;
using Alice.ModelRuntime;
using Alice.ProductRuntime;
using Alice.Social;

namespace Alice.CognitiveLodDialogueExperiment;

internal static class WorkloadRunner
{
    private const int TotalDays = 60;
    private const int CheckpointDay = 30;

    public static async Task<int> RunAsync(
        StudyInputs inputs,
        TownWorldConfiguration world,
        DialogueSurfaceProfile surface,
        bool live,
        StudyArtifactWriter artifacts,
        CancellationToken cancellationToken)
    {
        using RuntimeClients clients = RuntimeClients.Create(world, artifacts, live);
        using LivingTownProductComposition product = LivingTownProductComposition.Create(
            world,
            surface,
            new UnavailableInterpreter(),
            clients.LocalAutonomy,
            clients.LocalDialogue,
            clients.Remote);
        using var metrics = new WorkloadMetricsCollector(
            artifacts.OutputDirectory,
            artifacts.RunId,
            world.Runtime.TicksPerDay,
            world.Runtime.SimulationTickIntervalMilliseconds,
            world.Population.Actors.Select(value => value.Identity.ActorId),
            artifacts);
        long finalTick = checked(world.Runtime.TicksPerDay * TotalDays - 1);
        long checkpointTick = checked(world.Runtime.TicksPerDay * CheckpointDay - 1);
        int generatedDialogueOpportunities = 0;
        for (long tick = 0; tick <= finalTick; tick++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = new SimTime(tick);
            ActorExecutionBatch batch = product.Advance(
                now,
                DateTimeOffset.UnixEpoch.AddMilliseconds(tick * world.Runtime.SimulationTickIntervalMilliseconds),
                cancellationToken);
            metrics.RecordExecutionBatch(batch);
            await DrainDialogueAsync(
                product,
                clients.LocalDialogue,
                artifacts,
                world.Runtime.TicksPerDay,
                cancellationToken,
                generated => generatedDialogueOpportunities = checked(generatedDialogueOpportunities + generated))
                .ConfigureAwait(false);
            await DrainAutonomyAsync(product, now, metrics, cancellationToken)
                .ConfigureAwait(false);
            if (tick % world.Runtime.TicksPerDay == world.Runtime.TicksPerDay - 1)
                metrics.CompleteDay(tick / world.Runtime.TicksPerDay + 1);
            if (tick == checkpointTick)
            {
                artifacts.WriteCheckpoint(inputs, generatedDialogueOpportunities, "day30");
                metrics.WriteCheckpoint(CheckpointDay, "day30");
            }
        }
        if (product.HasPendingDialogueResponse)
            throw new InvalidOperationException("The 60-day run ended with an undrained dialogue response queue.");
        artifacts.Complete(inputs, generatedDialogueOpportunities);
        metrics.Complete();
        return generatedDialogueOpportunities;
    }

    private static async Task DrainDialogueAsync(
        LivingTownProductComposition product,
        LiveTownL1DialogueRouteClient localClient,
        StudyArtifactWriter artifacts,
        long ticksPerDay,
        CancellationToken cancellationToken,
        Action<int> countGenerated)
    {
        while (product.TryDequeueDialogueResponse(out TownDialogueResponseNeed? need))
        {
            countGenerated(1);
            DialogueResponseOpportunity opportunity = need!.Session.PendingResponseOpportunities.Single(value =>
                value.SourceActId == need.SourceTurn.Act.ActId);
            int attemptStart = artifacts.Attempts.Count;
            TownDialogueRoutingOutcome outcome = await product.DialogueRouting.InvokeAsync(
                need.Session,
                need.SourceTurn,
                need.ActorVisibleText,
                need.QueuedAt,
                cancellationToken).ConfigureAwait(false);
            TownL1DialogueRouteResponse? response = outcome.LocalAppraisalDecoded
                ? TownL1DialogueRouteResponse.Decode(localClient.LastAssistantContent)
                : null;
            IReadOnlyList<string> localAttemptIds = artifacts.AttemptIdsAfter(attemptStart, "dialogue_l1");
            IReadOnlyList<string> remoteAttemptIds = artifacts.AttemptIdsAfter(attemptStart, "dialogue_l2");
            bool escalationRequested = response?.Decision == "request_escalation";
            string terminalKind = outcome switch
            {
                { Route: LivingTownCognitionRoute.L1, Failure: null } => "LocalReply",
                { Route: LivingTownCognitionRoute.L2, Failure: null, L2Outcome: TownL2DialogueSettled } => "StrategicReply",
                _ => "Failed"
            };
            string? terminalReference = outcome.L2Outcome is TownL2DialogueSettled settled
                ? settled.ReplyTurn.Act.ActId.Value
                : outcome.Failure is null
                    ? need.Session.Transcript.Last().Act.ActId.Value
                    : null;
            var record = new DialogueLifecycleRecord(
                "alice.dialogue_lod.lifecycle.v1",
                artifacts.RunId,
                "workload",
                null,
                null,
                need.QueuedAt.Ticks / ticksPerDay + 1,
                need.QueuedAt.Ticks,
                opportunity.OpportunityId.Value,
                need.Session.SessionId.Value,
                need.SourceTurn.Act.ActId.Value,
                need.SourceTurn.Act.Speaker.Value,
                opportunity.Recipient.Value,
                need.SourceTurn.Act.Kind.ToString(),
                null,
                outcome.Route.ToString(),
                false,
                null,
                null,
                response?.Decision,
                outcome.LocalAppraisalDecoded,
                escalationRequested,
                response?.ReasonCode,
                response?.EvidenceRefs ?? [],
                escalationRequested ? outcome.Route == LivingTownCognitionRoute.L2 : null,
                ParseNeedId(outcome.Evidence),
                remoteAttemptIds.Count != 0,
                outcome.L2Outcome?.GetType().Name,
                terminalKind,
                terminalReference,
                outcome.Failure,
                localAttemptIds,
                remoteAttemptIds,
                null,
                null);
            artifacts.AddLifecycle(record);
        }
    }

    private static Task DrainAutonomyAsync(
        LivingTownProductComposition product,
        SimTime now,
        WorkloadMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (product.TryDequeueAutonomyLocalDecision(out TownAutonomyLocalDecisionWork? localWork))
        {
            TownAutonomyCandidate? selected = FirstAvailableAction(localWork!);
            TownAutonomyL1Outcome outcome = selected is null
                ? product.Autonomy.SettleLocalDecision(
                    localWork!,
                    new LocalReasonerDeferProduced(new LocalReasonerDefer("workload_deterministic_defer")),
                    now)
                : product.Autonomy.SettleLocalDecision(
                    localWork!,
                    new LocalReasonerChoiceProduced(
                        new LocalReasonerChoice(new LocalCandidateId(selected.CandidateId))),
                    now);
            string outcomeKind = outcome.EscalatedNeed is not null
                ? "escalation"
                : outcome.Receipt is not null
                    ? "choice"
                    : outcome.Evidence.StartsWith("local model deferred:", StringComparison.Ordinal)
                        ? "defer"
                        : "failure";
            metrics.RecordAutonomyL1(
                localWork!.ActorId.Value,
                now.Ticks,
                outcomeKind,
                outcome.EscalatedNeed is not null && outcome.Accepted,
                []);
            if (outcome.Receipt is not null && outcome.Receipt.Outcome == ActorExecutionOutcome.Completed)
                product.ProjectAutonomySettlement(outcome.Receipt);
        }

        while (product.TryDequeueAutonomyDecision(out TownAutonomyDecisionWork? work))
        {
            metrics.RecordAutonomyL2(work!.ActorId.Value, now.Ticks, "not_dispatched", false, []);
            work.Need.Abort();
            product.Autonomy.CompleteDecisionWork(work.Need);
        }
        return Task.CompletedTask;
    }

    private static TownAutonomyCandidate? FirstAvailableAction(TownAutonomyLocalDecisionWork work)
    {
        foreach (TownAutonomyCandidate candidate in work.Candidates)
        {
            if (candidate.Available && candidate.Kind == TownAutonomyCandidateKind.Action) return candidate;
        }
        return null;
    }

    private static string? ParseNeedId(string evidence)
    {
        const string marker = "DecisionNeed ";
        int start = evidence.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        int end = evidence.IndexOf(':', start);
        return end <= start ? null : evidence[start..end];
    }

    private sealed class RuntimeClients : IDisposable
    {
        private RuntimeClients(
            HttpClient http,
            IModelClient<TownL1DecisionResponse> localAutonomy,
            LiveTownL1DialogueRouteClient localDialogue,
            IModelClient<RemotePlannerResponse> remote)
        {
            Http = http;
            LocalAutonomy = localAutonomy;
            LocalDialogue = localDialogue;
            Remote = remote;
        }

        public HttpClient Http { get; }
        public IModelClient<TownL1DecisionResponse> LocalAutonomy { get; }
        public LiveTownL1DialogueRouteClient LocalDialogue { get; }
        public IModelClient<RemotePlannerResponse> Remote { get; }

        public static RuntimeClients Create(
            TownWorldConfiguration world,
            StudyArtifactWriter artifacts,
            bool live)
        {
            OpenAiCompatibleProviderProfile profile = ProductModelClientComposition.CreateLocalProfile(
                world.Runtime.ProviderProfiles,
                world.Runtime.ProviderQueue);
            if (!live)
            {
                var dryHttp = new HttpClient(
                    new ProviderRecordingHandler(new AlwaysChooseDialogueHandler(), artifacts));
                return new RuntimeClients(
                    dryHttp,
                    new FixedUnavailableModelClient<TownL1DecisionResponse>(
                        ModelClientExecutionMode.LiveLocal,
                        ModelClientUnavailableReason.UnsupportedRequestType),
                    new LiveTownL1DialogueRouteClient(dryHttp, profile),
                    new FixedUnavailableModelClient<RemotePlannerResponse>(
                        ModelClientExecutionMode.LiveRemote,
                        ModelClientUnavailableReason.MissingCredential));
            }
            EnsureRemoteCredential(world);
            var handler = new ProviderRecordingHandler(new HttpClientHandler(), artifacts);
            var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            return new RuntimeClients(
                http,
                new FixedUnavailableModelClient<TownL1DecisionResponse>(
                    ModelClientExecutionMode.LiveLocal,
                    ModelClientUnavailableReason.UnsupportedRequestType),
                new LiveTownL1DialogueRouteClient(http, profile),
                ProductModelClientComposition.CreateRemotePlanner(
                    http,
                    world.Runtime.ProviderProfiles,
                    world.Runtime.ProviderQueue));
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

    private sealed class AlwaysChooseDialogueHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            string content = JsonSerializer.Serialize(new
            {
                decision = "choose",
                reply_kind = "CasualComment",
                reply_text = "I hear you. Let us continue with the day.",
                incoming_effect = "Neutral",
                reply_effect = "Neutral",
                intensity = 0.1,
                reason_code = string.Empty,
                evidence_refs = Array.Empty<string>()
            });
            string envelope = JsonSerializer.Serialize(new
            {
                choices = new[] { new { finish_reason = "stop", message = new { content } } },
                usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class UnavailableInterpreter : IPlayerUtteranceInterpreter
    {
        public ValueTask<PlayerUtteranceInterpretation> InterpretAsync(
            PlayerUtteranceInterpretationRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(PlayerUtteranceInterpretation.Unavailable("Not used by workload collection."));
        }
    }
}
