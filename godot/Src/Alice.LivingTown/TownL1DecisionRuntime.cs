using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Alice.Activities;
using Alice.Actors;
using Alice.Cognition;
using Alice.Identity;
using Alice.Interaction;
using Alice.ModelRuntime;
using Alice.Npc;
using Alice.Social;
using Alice.World;

namespace Alice.LivingTown;

public interface ITownL1DecisionRequest : IModelRequest<TownL1DecisionResponse>
{
    string ModelSystemPrompt { get; }
    string ModelOutputSchemaJson { get; }
    string CanonicalUserJson { get; }
    string ResponseFormatName { get; }
}

public sealed record TownL1ScheduleOption(
    string OptionId,
    string PlaceRef,
    string Purpose,
    string Obligation,
    long StartsAtTickOfDay,
    long EndsAtTickOfDay);

public sealed class TownL1DecisionRequest : ITownL1DecisionRequest
{
    public const string SystemPrompt =
        "The NPC has two overlapping, locally legal schedule commitments. " +
        "Choose one option, defer, or request Host-validated strategic escalation. " +
        "Use only the supplied identity, personality, goals and schedule facts. " +
        "Escalation evidence_refs may contain only supplied option IDs. " +
        "Do not invent events, alter long-term goals, create DecisionNeed, or produce hidden reasoning. " +
        "For choose, candidate_id must be one supplied option ID, reason_code must be an empty string, " +
        "and evidence_refs must be an empty array. For defer, candidate_id must be an empty string, " +
        "reason_code must be non-empty, and evidence_refs must be an empty array. For request_escalation, " +
        "candidate_id must be an empty string, reason_code must be non-empty, and evidence_refs must contain " +
        "supplied option IDs. Because both supplied options are locally legal, choose one instead of escalating. " +
        "Return only one JSON object matching the schema.";
    public const string OutputSchemaJson = LocalReasonerProtocol.OutputSchemaJson;

    private TownL1DecisionRequest(
        string requestId,
        ActorId actorId,
        IReadOnlyList<TownL1ScheduleOption> options,
        string canonicalUserJson)
    {
        RequestId = requestId;
        ActorId = actorId;
        Options = options;
        CanonicalUserJson = canonicalUserJson;
    }

    public string RequestId { get; }
    public ActorId ActorId { get; }
    public IReadOnlyList<TownL1ScheduleOption> Options { get; }
    public string CanonicalUserJson { get; }
    public string ModelSystemPrompt => SystemPrompt;
    public string ModelOutputSchemaJson => OutputSchemaJson;
    public string ResponseFormatName => "town_l1_schedule_choice";

    public static TownL1DecisionRequest Create(
        string requestId,
        LivingTownNpcRuntime npc,
        TownL1ScheduleOption first,
        TownL1ScheduleOption second)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(npc);
        if (StringComparer.Ordinal.Equals(first.OptionId, second.OptionId))
            throw new ArgumentException("Town L1 options must be distinct.");
        TownL1ScheduleOption[] options = [first, second];
        Array.Sort(options, TownL1ScheduleOptionComparer.Instance);
        return new TownL1DecisionRequest(
            requestId,
            npc.ActorId,
            new ReadOnlyCollection<TownL1ScheduleOption>(options),
            Serialize(npc, options));
    }

    private static string Serialize(
        LivingTownNpcRuntime npc,
        IReadOnlyList<TownL1ScheduleOption> options)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("actor_id", npc.ActorId.Value);
            writer.WriteString("name", npc.State.Profile.DisplayName);
            writer.WritePropertyName("personality_traits");
            writer.WriteStartArray();
            foreach (Alice.Npc.PersonalityTagId trait in npc.State.NpcState.Personality.Traits)
                writer.WriteStringValue(trait.Value);
            writer.WriteEndArray();
            writer.WritePropertyName("current_goals");
            writer.WriteStartArray();
            foreach (string goal in npc.State.Profile.InitialGoalRefs) writer.WriteStringValue(goal);
            writer.WriteEndArray();
            writer.WriteString("decision_problem", "overlapping_schedule_commitments");
            writer.WritePropertyName("local_options");
            writer.WriteStartArray();
            foreach (TownL1ScheduleOption option in options)
            {
                writer.WriteStartObject();
                writer.WriteString("option_id", option.OptionId);
                writer.WriteString("place_ref", option.PlaceRef);
                writer.WriteString("purpose", option.Purpose);
                writer.WriteString("obligation", option.Obligation);
                writer.WriteNumber("starts_at_tick_of_day", option.StartsAtTickOfDay);
                writer.WriteNumber("ends_at_tick_of_day", option.EndsAtTickOfDay);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private sealed class TownL1ScheduleOptionComparer : IComparer<TownL1ScheduleOption>
    {
        public static TownL1ScheduleOptionComparer Instance { get; } = new();
        public int Compare(TownL1ScheduleOption? left, TownL1ScheduleOption? right) =>
            StringComparer.Ordinal.Compare(left?.OptionId, right?.OptionId);
    }
}

public sealed record TownL1DecisionResponse(LocalReasonerCallAttempt Attempt)
{
    public static TownL1DecisionResponse Decode(string? content)
    {
        return new TownL1DecisionResponse(LocalReasonerResponseDecoder.Decode(content));
    }
}

public sealed class LiveTownL1DecisionClient : IModelClient<TownL1DecisionResponse>
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleProviderProfile _profile;

    public LiveTownL1DecisionClient(HttpClient httpClient, OpenAiCompatibleProviderProfile profile)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _profile = profile?.Snapshot() ?? throw new ArgumentNullException(nameof(profile));
    }

    public async ValueTask<ModelClientResult<TownL1DecisionResponse>> InvokeAsync(
        IModelRequest<TownL1DecisionResponse> request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is not ITownL1DecisionRequest localRequest)
            return ModelClientResult<TownL1DecisionResponse>.Unavailable(
                ModelClientExecutionMode.LiveLocal,
                ModelClientUnavailableReason.UnsupportedRequestType);

        using HttpRequestMessage httpRequest = OpenAiCompatibleChatCompletions.CreateStructuredRequest(
            _profile,
            localRequest.ModelSystemPrompt,
            localRequest.CanonicalUserJson,
            localRequest.ModelOutputSchemaJson,
            localRequest.ResponseFormatName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_profile.Timeout);
        int? status = null;
        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
                return Failure(LiveLocalFailureKind.HttpFailure, status);
            BoundedResponseBodyReadResult body = await OpenAiCompatibleChatCompletions.ReadResponseBodyAsync(
                response.Content,
                _profile.MaxResponseBodyBytes,
                timeout.Token).ConfigureAwait(false);
            if (!body.IsComplete || body.Body is null)
                return Failure(LiveLocalFailureKind.ResponseBodyTooLarge, status);
            if (!OpenAiCompatibleChatCompletions.TryReadAssistantContent(body.Body, out string? content))
                return Failure(LiveLocalFailureKind.InvalidResponseEnvelope, status);
            TownL1DecisionResponse decision = TownL1DecisionResponse.Decode(content);
            return ModelClientResult<TownL1DecisionResponse>.Produced(
                decision,
                LiveLocalExecutionEvidence.ContentReceived(
                    _profile,
                    response.StatusCode));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failure(LiveLocalFailureKind.Timeout, status);
        }
        catch (HttpRequestException)
        {
            return Failure(LiveLocalFailureKind.NetworkFailure, status);
        }
        catch (IOException)
        {
            return Failure(LiveLocalFailureKind.NetworkFailure, status);
        }
    }

    private ModelClientResult<TownL1DecisionResponse> Failure(
        LiveLocalFailureKind kind,
        int? status) => ModelClientResult<TownL1DecisionResponse>.Produced(
            new TownL1DecisionResponse(new LocalReasonerCallFailed(LocalReasonerCallFailureKind.InvocationFailed)),
            LiveLocalExecutionEvidence.InvocationFailed(_profile, kind, status));
}

public sealed record TownL1InvocationOutcome(
    ActorId ActorId,
    string? SelectedOptionId,
    bool ModelSelected,
    string Evidence,
    ModelClientExecutionEvidence? ExecutionEvidence);

public sealed record TownL1DialogueRouteResponse(
    string? Decision,
    string? ReplyKind,
    string? ReplyText,
    string? IncomingEffect,
    string? ReplyEffect,
    double Intensity,
    string? ReasonCode,
    IReadOnlyList<string> EvidenceRefs,
    string? Failure)
{
    public static TownL1DialogueRouteResponse Decode(string? content)
    {
        if (content is null) return Failed("invocation_failed");
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Failed("invalid_structured_output");
            string decision = root.GetProperty("decision").GetString() ?? string.Empty;
            string kind = root.GetProperty("reply_kind").GetString() ?? string.Empty;
            string text = root.GetProperty("reply_text").GetString() ?? string.Empty;
            string incoming = root.GetProperty("incoming_effect").GetString() ?? string.Empty;
            string reply = root.GetProperty("reply_effect").GetString() ?? string.Empty;
            double rawIntensity = root.GetProperty("intensity").GetDouble();
            double intensity = NormalizeIntensity(decision, rawIntensity);
            string reason = root.GetProperty("reason_code").GetString() ?? string.Empty;
            string[] refs = root.GetProperty("evidence_refs").EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty).ToArray();
            string[] names = root.EnumerateObject().Select(value => value.Name).ToArray();
            bool closedShape = names.Order(StringComparer.Ordinal).SequenceEqual(
                new[]
                {
                    "decision", "evidence_refs", "incoming_effect", "intensity",
                    "reason_code", "reply_effect", "reply_kind", "reply_text"
                },
                StringComparer.Ordinal);
            bool effectsValid = intensity is >= 0 and <= 1
                && Enum.TryParse(incoming, false, out TownSocialEffectKind _)
                && Enum.TryParse(reply, false, out TownSocialEffectKind _);
            bool chooseEffectsValid = incoming is "Neutral" or "Support" or "Apology" or "SharedInterest"
                && reply is "Neutral" or "Support" or "Apology" or "SharedInterest";
            bool chooseValid = closedShape && decision == "choose"
                && !string.IsNullOrWhiteSpace(kind) && !string.IsNullOrWhiteSpace(text)
                && Enum.TryParse(kind, false, out SemanticDialogueActKind parsedKind)
                && parsedKind != SemanticDialogueActKind.Invite && effectsValid && chooseEffectsValid
                && intensity <= 0.69 && reason.Length == 0
                && refs.Length <= 1 && refs.All(value => !string.IsNullOrWhiteSpace(value));
            bool strategicSignal = incoming is "Harm" or "Promise" or "Breach" or "Threat"
                || reply is "Harm" or "Promise" or "Breach" or "Threat"
                || intensity >= 0.7;
            bool escalationValid = closedShape && decision is "choose" or "request_escalation"
                && !string.IsNullOrWhiteSpace(reason)
                && refs.Length > 0 && refs.All(value => !string.IsNullOrWhiteSpace(value))
                && effectsValid && strategicSignal;
            if (chooseValid)
                return new(decision, kind, text, incoming, reply, intensity, string.Empty, Array.Empty<string>(), null);
            if (escalationValid)
                return new("request_escalation", string.Empty, string.Empty, incoming, reply, intensity, reason, Array.AsReadOnly(refs), null);
            return Failed("invalid_structured_output");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Failed("invalid_structured_output");
        }
    }

    private static double NormalizeIntensity(string decision, double value)
    {
        if (value < 0 || value > 100) return double.NaN;
        if (value > 1 || (decision == "choose" && value == 1)) return value / 100;
        return value;
    }

    public static TownL1DialogueRouteResponse Failed(string reason) =>
        new(null, null, null, null, null, 0, null, Array.Empty<string>(), reason);
}

public sealed class TownL1DialogueRouteRequest : IModelRequest<TownL1DialogueRouteResponse>
{
    public const string SystemPrompt =
        "Answer one NPC dialogue turn with choose or request_escalation. " +
        "You are the NPC identified by actor_id; reply in first person to the incoming speaker and do not address yourself by actor_id. " +
        "Treat current_activity as authoritative present-tense state and never contradict it. " +
        "Use choose for greetings and ordinary conversation; supply reply_text and one listed semantic reply_kind, leave reason_code empty, and use no evidence_refs. " +
        "For greetings, weather, and casual small talk, choose Neutral effects with intensity from 0 to 0.2. " +
        "For bounded advice, present emotion, clarification, or a choice among same-day actions, choose only Neutral, Support, Apology, or SharedInterest effects with intensity from 0.3 to 0.69. " +
        "Worry, shortage, help, and recommendations are not strategic by themselves. Never put Harm, Promise, Breach, or Threat in a choose response. " +
        "Request escalation does not answer the speaker: set reply_kind and reply_text to empty strings. " +
        "It supplies exactly one reason_code from no_feasible_local_action, goal_or_plan_change, commitment_or_debt, " +
        "major_relationship, medical_or_body_deadline, or repeated_visible_failure, plus exactly one cited visible_evidence ID. " +
        "Use one incoming_effect from Harm, Promise, Breach, or Threat, an intensity from 0.7 to 1, and normally Neutral reply_effect. " +
        "For requests to lend, borrow, give, repay, or promise coins or resources, use commitment_or_debt, Promise, and intensity 0.8. " +
        "Request escalation only when the incoming text itself visibly contains promises, threats, breaches, major relationship changes, " +
        "long-term goals, debt, medical/body deadlines, repeated failure, or resource commitments, and cite supplied visible_evidence IDs. " +
        "Intensity is a decimal from 0 to 1, never a percentage: write 0.2, not 20. " +
        "The Host alone validates escalation and creates DecisionNeed. Do not invent people, places, work, history, or other world facts. " +
        "When the supplied JSON does not contain an answer, ask a short clarifying question instead of inventing one. " +
        "Write reply_text in response_language; keep schema keys and enum values in English. Return only schema JSON.";
    public const string OutputSchemaJson = """
        {"type":"object","properties":{"decision":{"type":"string","enum":["choose","request_escalation"]},"reply_kind":{"type":"string","enum":["","Ask","Inform","Clarify","Request","Offer","Recommend","Accept","Decline","CounterOffer","Warn","Apologize","Thank","Complain","Tease","Comfort","Congratulate","CasualComment","ShareNews","ShareGossip"]},"reply_text":{"type":"string"},"incoming_effect":{"type":"string","enum":["Neutral","Support","Apology","SharedInterest","Harm","Promise","Breach","Threat"]},"reply_effect":{"type":"string","enum":["Neutral","Support","Apology","SharedInterest","Harm","Promise","Breach","Threat"]},"intensity":{"type":"number","minimum":0,"maximum":1},"reason_code":{"type":"string","enum":["","no_feasible_local_action","goal_or_plan_change","commitment_or_debt","major_relationship","medical_or_body_deadline","repeated_visible_failure"]},"evidence_refs":{"type":"array","maxItems":1,"items":{"type":"string"}}},"required":["decision","reply_kind","reply_text","incoming_effect","reply_effect","intensity","reason_code","evidence_refs"],"additionalProperties":false}
        """;

    public TownL1DialogueRouteRequest(
        string requestId,
        LivingTownNpcRuntime npc,
        string actorVisibleText,
        string evidenceRef,
        string responseLanguage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRef);
        RequestId = requestId;
        EvidenceRef = evidenceRef;
        LivingTownCurrentActivity currentActivity = npc.GetCurrentActivity();
        CanonicalUserJson = JsonSerializer.Serialize(new
        {
            actor_id = npc.ActorId.Value,
            personality_traits = npc.State.NpcState.Personality.Traits.Select(value => value.Value),
            aspirations = npc.State.Profile.AspirationIds,
            emotion = npc.State.CurrentEmotion.Kind.ToString(),
            satiety = npc.State.SharedActorState.Body.Satiety.Value,
            spirit = npc.State.SharedActorState.Body.Spirit.Value,
            current_activity = new
            {
                kind = currentActivity.Kind.ToString(),
                activity_ref = currentActivity.ActivityRef
            },
            response_language = responseLanguage,
            incoming_text = actorVisibleText,
            visible_evidence = new[] { evidenceRef }
        });
    }

    public string RequestId { get; }
    public string EvidenceRef { get; }
    public string CanonicalUserJson { get; }
}

public sealed class LiveTownL1DialogueRouteClient : IModelClient<TownL1DialogueRouteResponse>
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleProviderProfile _profile;

    public LiveTownL1DialogueRouteClient(HttpClient httpClient, OpenAiCompatibleProviderProfile profile)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _profile = profile?.Snapshot() ?? throw new ArgumentNullException(nameof(profile));
    }

    public string? LastAssistantContent { get; private set; }

    public async ValueTask<ModelClientResult<TownL1DialogueRouteResponse>> InvokeAsync(
        IModelRequest<TownL1DialogueRouteResponse> request,
        CancellationToken cancellationToken)
    {
        if (request is not TownL1DialogueRouteRequest local)
            return ModelClientResult<TownL1DialogueRouteResponse>.Unavailable(
                ModelClientExecutionMode.LiveLocal, ModelClientUnavailableReason.UnsupportedRequestType);
        using HttpRequestMessage httpRequest = OpenAiCompatibleChatCompletions.CreateStructuredRequest(
            _profile, TownL1DialogueRouteRequest.SystemPrompt, local.CanonicalUserJson,
            TownL1DialogueRouteRequest.OutputSchemaJson, "town_l1_dialogue_route");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_profile.Timeout);
        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(
                httpRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return ProducedFailure(LiveLocalFailureKind.HttpFailure, (int)response.StatusCode);
            BoundedResponseBodyReadResult body = await OpenAiCompatibleChatCompletions.ReadResponseBodyAsync(
                response.Content, _profile.MaxResponseBodyBytes, timeout.Token).ConfigureAwait(false);
            if (!body.IsComplete || body.Body is null
                || !OpenAiCompatibleChatCompletions.TryReadAssistantContent(body.Body, out string? content))
                return ProducedFailure(LiveLocalFailureKind.InvalidResponseEnvelope, (int)response.StatusCode);
            LastAssistantContent = content;
            return ModelClientResult<TownL1DialogueRouteResponse>.Produced(
                TownL1DialogueRouteResponse.Decode(content),
                LiveLocalExecutionEvidence.ContentReceived(_profile, response.StatusCode));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return ProducedFailure(LiveLocalFailureKind.Timeout, null); }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return ProducedFailure(LiveLocalFailureKind.NetworkFailure, null);
        }
    }

    private ModelClientResult<TownL1DialogueRouteResponse> ProducedFailure(LiveLocalFailureKind kind, int? status) =>
        ModelClientResult<TownL1DialogueRouteResponse>.Produced(
            TownL1DialogueRouteResponse.Failed(kind.ToString()),
            LiveLocalExecutionEvidence.InvocationFailed(_profile, kind, status));
}

public sealed record TownDialogueRoutingOutcome(
    LivingTownCognitionRoute Route,
    string Evidence,
    string? Failure,
    TownL2DialogueInvocationOutcome? L2Outcome,
    bool LocalAppraisalDecoded);

/// <summary>Local appraisal settles ordinary dialogue and explicitly escalates consequential dialogue to L2.</summary>
public sealed class TownDialogueRoutingRuntime
{
    private static readonly HashSet<string> AllowedEscalationReasons = new(StringComparer.Ordinal)
    {
        "no_feasible_local_action",
        "goal_or_plan_change",
        "commitment_or_debt",
        "major_relationship",
        "medical_or_body_deadline",
        "repeated_visible_failure"
    };

    private readonly LivingTownPopulationRuntime _population;
    private readonly IModelClient<TownL1DialogueRouteResponse> _localClient;
    private readonly TownL2DialogueRuntime _l2;
    private readonly DecisionNeedDiscoveryRegistrar _needRegistrar;
    private long _sequence;

    public TownDialogueRoutingRuntime(
        LivingTownPopulationRuntime population,
        IModelClient<TownL1DialogueRouteResponse> localClient,
        TownL2DialogueRuntime l2,
        DecisionNeedStore needs)
    {
        _population = population ?? throw new ArgumentNullException(nameof(population));
        _localClient = localClient ?? throw new ArgumentNullException(nameof(localClient));
        _l2 = l2 ?? throw new ArgumentNullException(nameof(l2));
        _needRegistrar = new DecisionNeedDiscoveryRegistrar(
            needs ?? throw new ArgumentNullException(nameof(needs)));
    }

    public string ResponseLanguage { get; set; } = "English";

    public async ValueTask<TownDialogueRoutingOutcome> InvokeAsync(
        ConversationSession session,
        SemanticDialogueTurn sourceTurn,
        string actorVisibleText,
        SimTime now,
        CancellationToken cancellationToken,
        TownL2DialogueTrace? l2Trace = null)
    {
        DialogueResponseOpportunity opportunity = session.PendingResponseOpportunities.Single(value =>
            value.SourceActId == sourceTurn.Act.ActId);
        LivingTownNpcRuntime npc = _population.GetNpc(opportunity.Recipient);
        string evidenceRef = $"current/dialogue/{sourceTurn.Act.ActId.Value}";
        var request = new TownL1DialogueRouteRequest(
            $"town-dialogue-route-{npc.ActorId.Value}-{checked(Interlocked.Increment(ref _sequence))}",
            npc,
            actorVisibleText,
            evidenceRef,
            ResponseLanguage);
        ModelClientResult<TownL1DialogueRouteResponse> local = await _localClient.InvokeAsync(request, cancellationToken);
        if (local.Status != ModelClientResultStatus.Produced || local.Output is not { Failure: null } response)
        {
            _l2.AbandonPendingResponse(session, sourceTurn);
            string failure = local.Status == ModelClientResultStatus.Unavailable
                ? $"local appraisal unavailable: {local.Mode}/{local.UnavailableReason}"
                : $"local appraisal failed: {local.Output?.Failure ?? "invalid result"}";
            return new TownDialogueRoutingOutcome(LivingTownCognitionRoute.L1, failure, failure, null, false);
        }

        if (response.Decision == "choose")
        {
            _ = _l2.SettleLocal(session, sourceTurn, actorVisibleText, response, now);
            return new TownDialogueRoutingOutcome(
                LivingTownCognitionRoute.L1,
                "L1 local appraisal → local model reply",
                null,
                null,
                true);
        }

        if (response.Decision != "request_escalation")
        {
            _l2.AbandonPendingResponse(session, sourceTurn);
            const string failure = "Host rejected an unsupported local dialogue decision.";
            return new TownDialogueRoutingOutcome(LivingTownCognitionRoute.L1, failure, failure, null, true);
        }

        bool visibleEvidence = response.EvidenceRefs.Count == 1
            && StringComparer.Ordinal.Equals(response.EvidenceRefs[0], evidenceRef);
        bool allowedReason = AllowedEscalationReasons.Contains(response.ReasonCode!);
        if (!visibleEvidence || !allowedReason || !RequiresStrategicEscalation(response))
        {
            _l2.AbandonPendingResponse(session, sourceTurn);
            const string failure = "Host rejected escalation: reason, evidence, or strategic threshold was invalid.";
            return new TownDialogueRoutingOutcome(LivingTownCognitionRoute.L1, failure, failure, null, true);
        }
        string reasonCode = response.ReasonCode!;

        DecisionNeed? need = RegisterDialogueNeed(npc, sourceTurn, reasonCode, evidenceRef, now);
        if (need is null)
        {
            _l2.AbandonPendingResponse(session, sourceTurn);
            const string failure = "Host could not register a current dialogue DecisionNeed.";
            return new TownDialogueRoutingOutcome(LivingTownCognitionRoute.L1, failure, failure, null, true);
        }

        need.BeginInFlightAttempt();
        TownL2DialogueInvocationOutcome l2;
        try
        {
            l2 = await _l2.InvokeAsync(
                session, sourceTurn, actorVisibleText, now, cancellationToken, l2Trace, ResponseLanguage);
        }
        catch
        {
            need.Abort();
            throw;
        }

        if (l2 is TownL2DialogueSettled settled)
        {
            need.Resolve(
                now,
                DecisionNeedResolutionKind.Respond,
                new DecisionNeedSemanticActResultReference(settled.ReplyTurn.Act.ActId));
        }
        else
        {
            need.Abort();
        }

        string? l2Failure = l2 switch
        {
            TownL2DialogueSettled => null,
            TownL2DialogueNotReady notReady => notReady.Reason,
            TownL2DialogueProviderUnavailable unavailable => $"{unavailable.Mode}/{unavailable.Reason}",
            TownL2DialogueProviderRejected rejected => rejected.Decision.GetType().Name,
            _ => l2.GetType().Name
        };
        return new TownDialogueRoutingOutcome(
            LivingTownCognitionRoute.L2,
            $"L1 local appraisal → L2 DecisionNeed {need.NeedId.Value}: {reasonCode}",
            l2Failure,
            l2,
            true);
    }

    private DecisionNeed? RegisterDialogueNeed(
        LivingTownNpcRuntime npc,
        SemanticDialogueTurn sourceTurn,
        string reasonCode,
        string evidenceRef,
        SimTime now)
    {
        var goal = new NpcGoal(
            new GoalId($"dialogue-{npc.ActorId.Value}-{sourceTurn.Act.ActId.Value}"),
            new ReachTargetObjective(new TargetRef($"dialogue/{sourceTurn.Act.ActId.Value}")));
        NpcState current = npc.State.NpcState;
        var planning = new NpcPlanningState([goal], null);
        var viewState = new NpcState(
            current.ActorId, current.Personality, current.Knowledge, planning, current.Social);
        ActorDecisionView view = ActorDecisionView.Create(npc.State.SharedActorState, viewState, null);
        DecisionNeedDiscoveryRoute route = _l2.Policy.Active.Rq1Activation == TownRq1ActivationMode.AgentCentric
            ? DecisionNeedDiscoveryRoute.AgentCentric
            : DecisionNeedDiscoveryRoute.EventCentric;
        var trace = new DecisionNeedDiscoveryTrace(
            route,
            new DecisionNeedDiscoverySourceId($"dialogue-escalation/{sourceTurn.Act.ActId.Value}"),
            [new DecisionNeedDiscoveryNodeId(evidenceRef), new DecisionNeedDiscoveryNodeId($"reason/{reasonCode}")]);
        DecisionNeedRegistrationOutcome outcome = _needRegistrar.RegisterPlanlessStrategic(
            view,
            new DecisionNeedKind("dialogue_response_unresolved"),
            new DecisionProblemCode("social_dialogue_response"),
            trace,
            new DecisionNeedWorldRevision(checked(now.Ticks + 1)),
            now);
        return outcome switch
        {
            RegisteredNew created => created.Need,
            DuplicateActive duplicate => duplicate.Need.State == DecisionNeedState.Queued ? duplicate.Need : null,
            QueuedSupersession replacement => replacement.Replacement,
            InFlightRevalidationPending replacement => replacement.Replacement,
            _ => null
        };
    }

    private static bool RequiresStrategicEscalation(TownL1DialogueRouteResponse response) =>
        response.IncomingEffect is "Promise" or "Breach" or "Threat" or "Harm"
        || response.ReplyEffect is "Promise" or "Breach" or "Threat" or "Harm"
        || response.Intensity >= 0.7;
}

/// <summary>Product L1 chooses among two configuration-derived overlapping schedule commitments.</summary>
public sealed class TownL1DecisionRuntime
{
    private readonly LivingTownPopulationRuntime _population;
    private readonly IModelClient<TownL1DecisionResponse> _client;
    private long _requestSequence;

    public TownL1DecisionRuntime(
        LivingTownPopulationRuntime population,
        IModelClient<TownL1DecisionResponse> client)
    {
        _population = population ?? throw new ArgumentNullException(nameof(population));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public IReadOnlyList<ActorId> FindInitialConflictActors(int limit)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        var actors = new List<ActorId>();
        foreach (LivingTownNpcRuntime npc in _population.Npcs)
        {
            if (TryFindConflict(npc, out _, out _)) actors.Add(npc.ActorId);
            if (actors.Count == limit) break;
        }
        return new ReadOnlyCollection<ActorId>(actors);
    }

    public async ValueTask<TownL1InvocationOutcome> InvokeAsync(
        ActorId actorId,
        CancellationToken cancellationToken)
    {
        LivingTownNpcRuntime npc = _population.GetNpc(actorId);
        if (!TryFindConflict(npc, out TownL1ScheduleOption? first, out TownL1ScheduleOption? second))
            return new TownL1InvocationOutcome(actorId, null, false, "no overlapping schedule options", null);
        TownL1DecisionRequest request = TownL1DecisionRequest.Create(
            $"town-l1-{actorId.Value}-{checked(Interlocked.Increment(ref _requestSequence))}",
            npc,
            first!,
            second!);
        ModelClientResult<TownL1DecisionResponse> result = await _client.InvokeAsync(
            request,
            cancellationToken);
        if (result.Status == ModelClientResultStatus.Unavailable)
            return new TownL1InvocationOutcome(
                actorId,
                null,
                false,
                $"provider unavailable: {result.UnavailableReason}",
                null);

        TownL1DecisionResponse response = result.Output!;
        if (response.Attempt is LocalReasonerChoiceProduced choice)
        {
            TownL1ScheduleOption? selected = request.Options.FirstOrDefault(value =>
                StringComparer.Ordinal.Equals(value.OptionId, choice.Choice.NextAction.Value));
            if (selected is null)
                return new TownL1InvocationOutcome(actorId, null, false,
                    "unknown option; typed L1 failure with no fallback action", result.ExecutionEvidence);
            if (!npc.State.TryPreferScheduleEntry(selected.OptionId))
                throw new InvalidOperationException("Town L1 selected an option outside the NPC schedule.");
            return new TownL1InvocationOutcome(actorId, selected.OptionId, true,
                $"model selected {selected.OptionId} for overlapping schedule commitments", result.ExecutionEvidence);
        }
        if (response.Attempt is LocalReasonerDeferProduced deferred)
            return new TownL1InvocationOutcome(actorId, null, false,
                $"model deferred: {deferred.Decision.ReasonCode}", result.ExecutionEvidence);
        if (response.Attempt is LocalReasonerEscalationRequested escalation)
        {
            bool visible = escalation.Decision.EvidenceRefs.All(reference =>
                request.Options.Any(option => StringComparer.Ordinal.Equals(option.OptionId, reference)));
            string evidence = visible
                ? $"Host rejected escalation {escalation.Decision.ReasonCode}: feasible local schedule options remain"
                : "Host rejected escalation: evidence is outside actor-visible schedule options";
            return new TownL1InvocationOutcome(actorId, null, false, evidence, result.ExecutionEvidence);
        }
        LocalReasonerCallFailed failed = (LocalReasonerCallFailed)response.Attempt;
        return new TownL1InvocationOutcome(actorId, null, false,
            $"typed L1 failure {failed.FailureKind}; no fallback action", result.ExecutionEvidence);
    }

    private static bool TryFindConflict(
        LivingTownNpcRuntime npc,
        out TownL1ScheduleOption? first,
        out TownL1ScheduleOption? second)
    {
        IReadOnlyList<TownScheduleEntryConfiguration> schedule = npc.State.Profile.Schedule;
        for (int left = 0; left < schedule.Count; left++)
        for (int right = left + 1; right < schedule.Count; right++)
        {
            TownScheduleEntryConfiguration a = schedule[left];
            TownScheduleEntryConfiguration b = schedule[right];
            bool overlaps = a.StartsAtTickOfDay < b.EndsAtTickOfDay
                && b.StartsAtTickOfDay < a.EndsAtTickOfDay;
            if (!overlaps || a.PlaceRef is null || b.PlaceRef is null
                || StringComparer.Ordinal.Equals(a.PlaceRef, b.PlaceRef)) continue;
            first = CreateOption(a);
            second = CreateOption(b);
            return true;
        }
        first = null;
        second = null;
        return false;
    }

    private static TownL1ScheduleOption CreateOption(TownScheduleEntryConfiguration entry) => new(
        entry.EntryId,
        entry.PlaceRef!,
        entry.Purpose,
        entry.Obligation,
        entry.StartsAtTickOfDay,
        entry.EndsAtTickOfDay);
}
