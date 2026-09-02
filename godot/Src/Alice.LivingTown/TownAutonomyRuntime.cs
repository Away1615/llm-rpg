using Alice.Activities;
using Alice.Actors;
using Alice.Cognition;
using Alice.Commitments;
using Alice.Identity;
using Alice.Interaction;
using Alice.Items;
using Alice.ModelRuntime;
using Alice.Navigation;
using Alice.Npc;
using Alice.ProductRuntime;
using Alice.World;
using System.Text.Json;

namespace Alice.LivingTown;

public sealed record TownAutonomyDecisionWork(
    DecisionNeed Need,
    ActorId ActorId,
    TownL2DecisionProblem Problem,
    SimTime QueuedAt,
    TownAutonomyNeedDescriptor Descriptor);

public enum TownAutonomyCandidateKind
{
    Action,
    Evidence
}

public sealed record TownAutonomyCandidate(
    string CandidateId,
    TownAutonomyCandidateKind Kind,
    string CatalogueTargetId,
    string? TravelPlaceId,
    string? EntryId,
    string Label,
    bool Available,
    string? UnavailableReason);

public sealed record TownAutonomyLocalDecisionWork(
    string WorkId,
    string PressureKey,
    string Domain,
    string SubjectRef,
    ActorId ActorId,
    IReadOnlyList<TownAutonomyCandidate> Candidates,
    SimTime QueuedAt,
    int VisibleFailureCount);

public sealed record TownAutonomyNeedDescriptor(
    string NeedKind,
    string ProblemCode,
    string GoalId,
    string GoalTargetRef,
    string SourceId,
    IReadOnlyList<string> EvidenceRefs,
    TownRq1ActivationMode ActivationMode);

public sealed record TownAutonomyL1Outcome(
    LivingTownCognitionRoute Route,
    string Evidence,
    bool Accepted,
    ActorExecutionReceipt? Receipt,
    DecisionNeed? EscalatedNeed);

public sealed record TownAutonomyDebugTriggerOutcome(
    bool Accepted,
    string Evidence,
    ActorExecutionReceipt? Receipt = null,
    LivingTownCognitionRoute ExpectedRoute = LivingTownCognitionRoute.None);

public enum TownAutonomyDebugScenario
{
    ClearNeed,
    AmbiguousNeed,
    StrategicConflict
}

public sealed class TownAutonomyL1Request : ITownL1DecisionRequest
{
    public const string SystemPrompt =
        "Choose one actor-visible local action, defer, or request Host-validated strategic escalation. " +
        "The candidates are generic world actions, not profession scripts. Choose only an available candidate. " +
        "Defer when every blocker is explicitly temporary, such as closed, cooldown, until tick, or growing, " +
        "or when actor-visible evidence is not yet sufficient. Request escalation only with one exact reason_code: " +
        "no_feasible_local_action, goal_or_plan_change, commitment_or_debt, major_relationship, " +
        "medical_or_body_deadline, or repeated_visible_failure. Cite supplied candidate IDs as evidence. " +
        "Use no_feasible_local_action only when no local action is feasible and the blocker is not temporary. " +
        "Do not invent facts or create a DecisionNeed. For choose, candidate_id must be one supplied available " +
        "candidate ID, reason_code must be an empty string, and evidence_refs must be an empty array. For defer, " +
        "candidate_id must be an empty string, reason_code must be non-empty, and evidence_refs must be an empty " +
        "array. For request_escalation, candidate_id must be an empty string, reason_code must be one allowed " +
        "escalation reason, and evidence_refs must contain supplied candidate IDs. Whenever an available candidate " +
        "solves the current local need, choose it instead of escalating. Return only the required JSON object.";

    public TownAutonomyL1Request(
        string requestId,
        TownAutonomyLocalDecisionWork work,
        LivingTownNpcRuntime npc,
        TownActorVitalsSnapshot vitals,
        AssetContainerState inventory)
    {
        RequestId = requestId;
        CanonicalUserJson = JsonSerializer.Serialize(new
        {
            actor_id = work.ActorId.Value,
            name = npc.State.Profile.DisplayName,
            personality_traits = npc.State.NpcState.Personality.Traits.Select(value => value.Value),
            aspirations = npc.State.Profile.AspirationIds,
            current_emotion = npc.State.CurrentEmotion.Kind.ToString(),
            current_goal_refs = npc.State.Profile.InitialGoalRefs,
            domain = work.Domain,
            subject_ref = work.SubjectRef,
            visible_failure_count = work.VisibleFailureCount,
            body = new
            {
                health = vitals.HealthCurrent,
                satiety = vitals.Satiety,
                spirit = vitals.Spirit,
                disease = vitals.Disease.ToString()
            },
            inventory = inventory.Balances.Select(value => new
            {
                asset_id = value.AssetId.Value,
                quantity = value.Quantity
            }),
            candidates = work.Candidates.Select(value => new
            {
                candidate_id = value.CandidateId,
                kind = value.Kind.ToString(),
                target_id = value.TravelPlaceId ?? value.CatalogueTargetId,
                label = value.Label,
                available = value.Available,
                unavailable_reason = value.UnavailableReason
            })
        });
    }

    public string RequestId { get; }
    public string CanonicalUserJson { get; }
    public string ModelSystemPrompt => SystemPrompt;
    public string ModelOutputSchemaJson => LocalReasonerProtocol.OutputSchemaJson;
    public string ResponseFormatName => "town_l1_autonomy_choice";
}

public sealed record TownAutonomyPendingDecisionDurableState(
    string NeedId,
    string ActorId,
    string DecisionId,
    string Kind,
    string SubjectRef,
    string? TargetId,
    IReadOnlyList<string> InvolvedActorIds,
    IReadOnlyList<TownL2CurrentEvidence> CurrentEvidence,
    long QueuedAtTicks,
    string NeedState,
    int AttemptCount,
    TownAutonomyNeedDescriptor Descriptor);

public sealed record TownAutonomyLocalDecisionDurableState(
    string WorkId,
    string PressureKey,
    string Domain,
    string SubjectRef,
    string ActorId,
    IReadOnlyList<TownAutonomyCandidate> Candidates,
    long QueuedAtTicks,
    int VisibleFailureCount);

public sealed record TownAutonomyLongEntry(string Key, long Value);
public sealed record TownAutonomyIntEntry(string Key, int Value);

public sealed record TownAutonomyActionIntentDurableState(
    string ActorId,
    string PressureKey,
    string CatalogueTargetId,
    string TravelPlaceId,
    string EntryId,
    string CognitionRoute,
    string? NeedId = null);

public sealed record TownAutonomyDurableState(
    long ExecutionSequence,
    long LocalRequestSequence,
    long LastAspirationDay,
    IReadOnlyList<TownAutonomyPendingDecisionDurableState> PendingDecisions,
    IReadOnlyList<TownAutonomyLocalDecisionDurableState> PendingLocalDecisions,
    IReadOnlyList<TownAutonomyActionIntentDurableState> PendingActionIntents,
    IReadOnlyList<TownAutonomyLongEntry> LastLocalDecisionDays,
    IReadOnlyList<TownAutonomyLongEntry> NextLocalDecisionAtTicks,
    IReadOnlyList<TownAutonomyIntEntry> VisibleFailureCounts,
    IReadOnlyList<string> PendingPressureKeys);

/// <summary>
/// Thin product loop that turns body pressure and configured aspirations into the existing shared
/// action and DecisionNeed pipelines. It owns no profession-specific action types.
/// </summary>
public sealed class TownAutonomyRuntime
{
    public const int DemoL2AdmissionBudget = 4;
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
    private readonly RegionSocialGameplayRuntime _gameplay;
    private readonly TownL2PolicyRuntime _policy;
    private readonly IModelClient<TownL1DecisionResponse> _l1Client;
    private readonly long _ticksPerDay;
    private readonly DecisionNeedStore _needs;
    private readonly DecisionNeedDiscoveryRegistrar _registrar;
    private readonly Queue<TownAutonomyDecisionWork> _decisionQueue = [];
    private readonly Queue<TownAutonomyLocalDecisionWork> _localDecisionQueue = [];
    private readonly Dictionary<string, TownAutonomyDecisionWork> _activeDecisionWorks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TownAutonomyLocalDecisionWork> _activeLocalDecisions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastLocalDecisionDay = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _nextLocalDecisionAtTicks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _visibleFailureCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TownAutonomyActionIntentDurableState> _pendingActionIntents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingPressureKeys = new(StringComparer.Ordinal);
    private long _executionSequence;
    private long _localRequestSequence;
    private long _lastAspirationDay = -1;
    private int _l1InFlightCount;

    public TownAutonomyRuntime(
        LivingTownPopulationRuntime population,
        RegionSocialGameplayRuntime gameplay,
        TownL2PolicyRuntime policy,
        IModelClient<TownL1DecisionResponse> l1Client,
        long ticksPerDay,
        DecisionNeedStore needs)
    {
        _population = population ?? throw new ArgumentNullException(nameof(population));
        _gameplay = gameplay ?? throw new ArgumentNullException(nameof(gameplay));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _l1Client = l1Client ?? throw new ArgumentNullException(nameof(l1Client));
        if (ticksPerDay <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerDay));
        _ticksPerDay = ticksPerDay;
        _needs = needs ?? throw new ArgumentNullException(nameof(needs));
        _registrar = new DecisionNeedDiscoveryRegistrar(_needs);
    }

    public DecisionNeedStoreSnapshot DecisionNeeds => _needs.GetRegistrationOrderSnapshot();
    public bool HasInFlightWork => Volatile.Read(ref _l1InFlightCount) != 0;

    public TownAutonomyDebugTriggerOutcome TriggerDebugScenario(
        ActorId actorId,
        TownAutonomyDebugScenario scenario,
        SimTime now)
    {
        LivingTownCognitionRoute route = scenario switch
        {
            TownAutonomyDebugScenario.ClearNeed => LivingTownCognitionRoute.L0,
            TownAutonomyDebugScenario.AmbiguousNeed => LivingTownCognitionRoute.L1,
            TownAutonomyDebugScenario.StrategicConflict => LivingTownCognitionRoute.L2,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        LivingTownNpcRuntime npc = _population.GetNpc(actorId);
        if (HasPendingActorWork(actorId))
            return new TownAutonomyDebugTriggerOutcome(
                false, "actor already has active cognition work", ExpectedRoute: route);

        if (route == LivingTownCognitionRoute.L2)
        {
            string? target = new[] { npc.State.Profile.Workplace?.Value }
                .Concat(npc.State.Profile.KnownPlaceRefs)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .FirstOrDefault(place => _gameplay.GetDecisionActionOffers(actorId, place, now).Count > 0);
            if (target is null || npc.State.Profile.AspirationIds.Count == 0)
                return new TownAutonomyDebugTriggerOutcome(
                    false, "no actor-visible strategic target is available", ExpectedRoute: route);
            string aspiration = npc.State.Profile.AspirationIds[0];
            var descriptor = new TownAutonomyNeedDescriptor(
                "strategic_conflict_unresolved",
                "aspiration_resource_choice",
                $"scenario-aspiration-{actorId.Value}-{now.Ticks}",
                $"place/{target}",
                $"research-scenario/strategic-conflict/{actorId.Value}/{now.Ticks}",
                [$"aspiration/{aspiration}"],
                _policy.Active.Rq1Activation);
            DecisionNeed need = RegisterDecisionNeed(npc, descriptor, now);
            var problem = new TownL2DecisionProblem(
                $"scenario/strategic-conflict/{actorId.Value}/{now.Ticks}",
                "aspiration_resource_choice",
                $"aspiration/{aspiration}",
                target,
                npc.State.NpcState.Social.Appraisals.Select(value => value.OtherActorId).Take(2),
                [new TownL2CurrentEvidence(
                    $"current/scenario/strategic-conflict/{actorId.Value}/{now.Ticks}",
                    descriptor.SourceId,
                    $"Research scenario: {actorId.Value} must reconcile aspiration {aspiration}, "
                    + $"the current schedule, and scarce actions available at {target}.")]);
            EnqueueDecision(new TownAutonomyDecisionWork(need, actorId, problem, now, descriptor));
            return new TownAutonomyDebugTriggerOutcome(
                true,
                $"queued real DecisionNeed {need.NeedId.Value} through the existing L2 pipeline",
                ExpectedRoute: route);
        }

        TownAutonomyCandidate[] candidates = BuildDebugCandidates(npc, now);
        if (candidates.Length == 0)
            return new TownAutonomyDebugTriggerOutcome(
                false, "no currently available generic action is visible", ExpectedRoute: route);
        string scenarioId = scenario == TownAutonomyDebugScenario.ClearNeed ? "clear-need" : "ambiguous-need";
        string pressureKey = $"{actorId.Value}/scenario-{scenarioId}/{now.Ticks}";
        var work = new TownAutonomyLocalDecisionWork(
            $"scenario/{scenarioId}/{actorId.Value}/{now.Ticks}",
            pressureKey,
            scenarioId,
            $"scenario/{scenarioId}/{now.Ticks}",
            actorId,
            candidates,
            now,
            0);
        if (route == LivingTownCognitionRoute.L0)
        {
            TownAutonomyL1Outcome outcome = SettleSelectedCandidate(
                work,
                candidates[0],
                npc,
                now,
                AutonomousNpcCognitionRoute.L0);
            return new TownAutonomyDebugTriggerOutcome(
                outcome.Accepted, outcome.Evidence, outcome.Receipt, route);
        }

        _pendingPressureKeys.Add(pressureKey);
        _activeLocalDecisions.Add(work.WorkId, work);
        _localDecisionQueue.Enqueue(work);
        return new TownAutonomyDebugTriggerOutcome(
            true,
            $"queued live local choice across {candidates.Length} generic actions",
            ExpectedRoute: route);
    }

    public IReadOnlyList<TownL2AdmissionCandidate> PreviewDailyAspirationCandidates(SimTime now)
    {
        long day = now.Ticks / _ticksPerDay;
        var candidates = new List<TownL2AdmissionCandidate>();
        foreach (LivingTownNpcRuntime npc in _population.Npcs)
        {
            string? target = npc.State.Profile.Workplace?.Value;
            if (target is null || npc.State.Profile.AspirationIds.Count == 0
                || !_gameplay.GetDecisionActionOffers(npc.ActorId, target, now).Any()) continue;
            string aspiration = npc.State.Profile.AspirationIds[0];
            candidates.Add(new TownL2AdmissionCandidate(
                npc.ActorId,
                new TownL2DecisionProblem(
                    $"aspiration/{npc.ActorId.Value}/{day}",
                    "aspiration_resource_choice",
                    $"aspiration/{aspiration}",
                    target,
                    npc.State.NpcState.Social.Appraisals.Select(value => value.OtherActorId).Take(2))));
        }
        return candidates;
    }

    public IReadOnlyList<TownL2AdmissionCandidate> PreviewResearchAdmissionCandidates(SimTime now)
    {
        var candidates = new List<TownL2AdmissionCandidate>(_activeDecisionWorks.Values
            .Where(value => value.Need.State is DecisionNeedState.Queued or DecisionNeedState.InFlight)
            .Select(value => new TownL2AdmissionCandidate(value.ActorId, value.Problem)));
        foreach (TownL2AdmissionCandidate candidate in PreviewDailyAspirationCandidates(now))
            if (!candidates.Any(existing =>
                    StringComparer.Ordinal.Equals(existing.Problem.DecisionId, candidate.Problem.DecisionId)))
                candidates.Add(candidate);
        return candidates;
    }

    public TownAutonomyDurableState CaptureDurableState() => new(
        _executionSequence,
        _localRequestSequence,
        _lastAspirationDay,
        _activeDecisionWorks.Values
            .Where(value => value.Need.State is DecisionNeedState.Queued or DecisionNeedState.InFlight)
            .OrderBy(value => value.Need.NeedId.Value, StringComparer.Ordinal)
            .Select(value => new TownAutonomyPendingDecisionDurableState(
            value.Need.NeedId.Value,
            value.ActorId.Value,
            value.Problem.DecisionId,
            value.Problem.Kind,
            value.Problem.SubjectRef,
            value.Problem.TargetId,
            value.Problem.InvolvedActors.Select(actor => actor.Value).ToArray(),
            value.Problem.CurrentEvidence.ToArray(),
            value.QueuedAt.Ticks,
            value.Need.State.ToString(),
            value.Need.AttemptCount,
            value.Descriptor)).ToArray(),
        _activeLocalDecisions.Values
            .OrderBy(value => value.WorkId, StringComparer.Ordinal)
            .Select(value => new TownAutonomyLocalDecisionDurableState(
                value.WorkId,
                value.PressureKey,
                value.Domain,
                value.SubjectRef,
                value.ActorId.Value,
                value.Candidates.ToArray(),
                value.QueuedAt.Ticks,
                value.VisibleFailureCount))
            .ToArray(),
        _pendingActionIntents.Values.OrderBy(value => value.ActorId, StringComparer.Ordinal)
            .ThenBy(value => value.PressureKey, StringComparer.Ordinal)
            .ToArray(),
        _lastLocalDecisionDay.OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new TownAutonomyLongEntry(value.Key, value.Value)).ToArray(),
        _nextLocalDecisionAtTicks.OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new TownAutonomyLongEntry(value.Key, value.Value)).ToArray(),
        _visibleFailureCounts.OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new TownAutonomyIntEntry(value.Key, value.Value)).ToArray(),
        _pendingPressureKeys.OrderBy(value => value, StringComparer.Ordinal).ToArray());

    public void RestoreDurableState(TownAutonomyDurableState state, SimTime settledAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ExecutionSequence < 0 || state.LocalRequestSequence < 0
            || state.LastAspirationDay < -1 || state.LastAspirationDay > settledAt.Ticks / _ticksPerDay)
            throw new InvalidDataException("Saved autonomy day is invalid.");
        _executionSequence = state.ExecutionSequence;
        _localRequestSequence = state.LocalRequestSequence;
        _lastAspirationDay = state.LastAspirationDay;
        _localDecisionQueue.Clear();
        _activeLocalDecisions.Clear();
        _decisionQueue.Clear();
        _activeDecisionWorks.Clear();
        _pendingPressureKeys.Clear();
        _lastLocalDecisionDay.Clear();
        _nextLocalDecisionAtTicks.Clear();
        _visibleFailureCounts.Clear();
        _pendingActionIntents.Clear();
        RestoreLongEntries(state.LastLocalDecisionDays, _lastLocalDecisionDay);
        RestoreLongEntries(state.NextLocalDecisionAtTicks, _nextLocalDecisionAtTicks);
        foreach (TownAutonomyIntEntry value in state.VisibleFailureCounts)
            if (value.Value < 0 || !_visibleFailureCounts.TryAdd(value.Key, value.Value))
                throw new InvalidDataException("Saved autonomy failure count is invalid.");
        foreach (string pressureKey in state.PendingPressureKeys)
            if (!_pendingPressureKeys.Add(pressureKey))
                throw new InvalidDataException("Saved autonomy pressure key is duplicated.");
        foreach (TownAutonomyActionIntentDurableState value in state.PendingActionIntents)
        {
            _ = _population.GetNpc(new ActorId(value.ActorId));
            if (!Enum.TryParse(value.CognitionRoute, false, out AutonomousNpcCognitionRoute route)
                || route is not (AutonomousNpcCognitionRoute.L0 or AutonomousNpcCognitionRoute.L1
                    or AutonomousNpcCognitionRoute.L2)
                || route == AutonomousNpcCognitionRoute.L2 && value.NeedId is null
                || !_pendingActionIntents.TryAdd(value.ActorId, value))
                throw new InvalidDataException("Saved autonomy action intent is invalid.");
        }
        foreach (TownAutonomyPendingDecisionDurableState value in state.PendingDecisions)
        {
            if (value.QueuedAtTicks < 0 || value.QueuedAtTicks > settledAt.Ticks
                || value.AttemptCount < 0
                || !Enum.TryParse(value.NeedState, false, out DecisionNeedState needState)
                || needState is not (DecisionNeedState.Queued or DecisionNeedState.InFlight))
                throw new InvalidDataException("Saved pending autonomy decision is invalid.");
            LivingTownNpcRuntime npc = _population.GetNpc(new ActorId(value.ActorId));
            DecisionNeed need = RegisterDecisionNeed(npc, value.Descriptor, new SimTime(value.QueuedAtTicks));
            if (!StringComparer.Ordinal.Equals(need.NeedId.Value, value.NeedId))
                throw new InvalidDataException("Saved autonomy DecisionNeed identity changed.");
            for (int attempt = 0; attempt < value.AttemptCount; attempt++)
            {
                need.BeginInFlightAttempt();
                if (attempt + 1 < value.AttemptCount || needState == DecisionNeedState.Queued)
                    need.ReturnToQueuedAfterRetryableTransportFailure();
            }
            if (need.State != needState)
                throw new InvalidDataException("Saved autonomy DecisionNeed lifecycle is invalid.");
            var problem = new TownL2DecisionProblem(
                value.DecisionId,
                value.Kind,
                value.SubjectRef,
                value.TargetId,
                value.InvolvedActorIds.Select(actor => new ActorId(actor)),
                value.CurrentEvidence);
            var work = new TownAutonomyDecisionWork(
                need, npc.ActorId, problem, new SimTime(value.QueuedAtTicks), value.Descriptor);
            if (!_activeDecisionWorks.TryAdd(need.NeedId.Value, work))
                throw new InvalidDataException("Saved autonomy DecisionNeed is duplicated.");
            if (needState == DecisionNeedState.Queued) _decisionQueue.Enqueue(work);
            else if (!_pendingActionIntents.Values.Any(intent =>
                         StringComparer.Ordinal.Equals(intent.NeedId, need.NeedId.Value)))
                throw new InvalidDataException("Saved in-flight autonomy DecisionNeed has no action intent.");
        }
        foreach (TownAutonomyLocalDecisionDurableState value in state.PendingLocalDecisions)
        {
            if (value.QueuedAtTicks < 0 || value.QueuedAtTicks > settledAt.Ticks
                || value.VisibleFailureCount < 0)
                throw new InvalidDataException("Saved local autonomy work is invalid.");
            var work = new TownAutonomyLocalDecisionWork(
                value.WorkId,
                value.PressureKey,
                value.Domain,
                value.SubjectRef,
                _population.GetNpc(new ActorId(value.ActorId)).ActorId,
                value.Candidates.ToArray(),
                new SimTime(value.QueuedAtTicks),
                value.VisibleFailureCount);
            if (!_activeLocalDecisions.TryAdd(work.WorkId, work))
                throw new InvalidDataException("Saved local autonomy work is duplicated.");
            _localDecisionQueue.Enqueue(work);
        }
    }

    private static void RestoreLongEntries(
        IEnumerable<TownAutonomyLongEntry> entries,
        IDictionary<string, long> target)
    {
        foreach (TownAutonomyLongEntry value in entries)
            if (value.Value < 0 || !target.TryAdd(value.Key, value.Value))
                throw new InvalidDataException("Saved autonomy clock entry is invalid.");
    }

    public bool TryDequeueDecisionWork(out TownAutonomyDecisionWork? work)
    {
        while (_decisionQueue.TryDequeue(out work))
            if (work.Need.State == DecisionNeedState.Queued) return true;
        work = null;
        return false;
    }

    public bool TryDequeueLocalDecisionWork(out TownAutonomyLocalDecisionWork? work) =>
        _localDecisionQueue.TryDequeue(out work);

    public void QueueL2ActionIntent(
        TownAutonomyDecisionWork work,
        TownGameplayActionOffer offer,
        string catalogueTargetId,
        string travelPlaceId)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogueTargetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(travelPlaceId);
        if (work.Need.State != DecisionNeedState.InFlight
            || !_activeDecisionWorks.ContainsKey(work.Need.NeedId.Value))
            throw new InvalidOperationException("Only an active in-flight L2 decision can travel to an action.");
        var intent = new TownAutonomyActionIntentDurableState(
            work.ActorId.Value,
            $"l2/{work.Need.NeedId.Value}",
            catalogueTargetId,
            travelPlaceId,
            offer.EntryId,
            AutonomousNpcCognitionRoute.L2.ToString(),
            work.Need.NeedId.Value);
        if (!_pendingActionIntents.TryAdd(work.ActorId.Value, intent))
            throw new InvalidOperationException("Actor already has a pending autonomous action intent.");
        _population.GetNpc(work.ActorId).State.SetAutonomousDestination(new LivingTownPlaceRef(travelPlaceId));
    }

    public void CompleteDecisionWork(DecisionNeed need)
    {
        ArgumentNullException.ThrowIfNull(need);
        _activeDecisionWorks.Remove(need.NeedId.Value);
    }

    public async ValueTask<TownAutonomyL1Outcome> InvokeLocalDecisionAsync(
        TownAutonomyLocalDecisionWork work,
        SimTime now,
        CancellationToken cancellationToken,
        Func<SimTime>? settlementTimeSource = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        LivingTownNpcRuntime npc = _population.GetNpc(work.ActorId);
        var request = new TownAutonomyL1Request(
            $"town-autonomy-l1-{work.ActorId.Value}-{checked(Interlocked.Increment(ref _localRequestSequence))}",
            work,
            npc,
            _gameplay.GetVitals(work.ActorId.Value),
            _gameplay.GetContainer(new AssetContainerOwnerId(AssetContainerOwnerKind.Actor, work.ActorId.Value)));
        Interlocked.Increment(ref _l1InFlightCount);
        try
        {
            ModelClientResult<TownL1DecisionResponse> result = await _l1Client.InvokeAsync(request, cancellationToken);
            SimTime settledAt = settlementTimeSource?.Invoke() ?? now;
            if (result.Status != ModelClientResultStatus.Produced)
                return CompleteLocal(work, settledAt, false,
                    $"local provider unavailable: {result.Mode}/{result.UnavailableReason}", null, null);
            return SettleLocalDecision(work, result.Output!.Attempt, settledAt);
        }
        finally
        {
            Interlocked.Decrement(ref _l1InFlightCount);
        }
    }

    public TownAutonomyL1Outcome SettleLocalDecision(
        TownAutonomyLocalDecisionWork work,
        LocalReasonerCallAttempt attempt,
        SimTime now)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(attempt);
        LivingTownNpcRuntime npc = _population.GetNpc(work.ActorId);
        if (attempt is LocalReasonerChoiceProduced choice)
        {
            TownAutonomyCandidate? selected = work.Candidates.SingleOrDefault(value =>
                StringComparer.Ordinal.Equals(value.CandidateId, choice.Choice.NextAction.Value));
            if (selected is null || selected.Kind != TownAutonomyCandidateKind.Action || !selected.Available)
                return CompleteLocal(work, now, false, "local model selected an unavailable candidate", null, null);
            return SettleSelectedCandidate(work, selected, npc, now);
        }

        if (attempt is LocalReasonerDeferProduced deferred)
        {
            _activeLocalDecisions.Remove(work.WorkId);
            _pendingPressureKeys.Remove(work.PressureKey);
            _lastLocalDecisionDay.Remove(work.PressureKey);
            _nextLocalDecisionAtTicks[work.PressureKey] = checked(now.Ticks + Math.Max(1, _ticksPerDay / 8));
            return new TownAutonomyL1Outcome(
                LivingTownCognitionRoute.L1,
                $"local model deferred: {deferred.Decision.ReasonCode}",
                true,
                null,
                null);
        }

        if (attempt is LocalReasonerEscalationRequested escalation)
        {
            bool visible = escalation.Decision.EvidenceRefs.All(reference =>
                work.Candidates.Any(candidate => StringComparer.Ordinal.Equals(candidate.CandidateId, reference)));
            bool admitted = visible && AllowedEscalationReasons.Contains(escalation.Decision.ReasonCode)
                && MeetsEscalationThreshold(work, escalation.Decision.ReasonCode);
            if (!admitted)
                return CompleteLocal(work, now, false,
                    "Host rejected local escalation evidence or threshold", null, null);

            TownAutonomyDecisionWork decision = RegisterEscalatedDecision(
                npc, work, escalation.Decision.ReasonCode, now);
            _activeLocalDecisions.Remove(work.WorkId);
            EnqueueDecision(decision);
            return CompleteLocal(work, now, true,
                $"Host admitted DecisionNeed {decision.Need.NeedId.Value}", null, decision.Need);
        }

        LocalReasonerCallFailed failed = (LocalReasonerCallFailed)attempt;
        return CompleteLocal(work, now, false,
            $"typed L1 failure {failed.FailureKind}; no fallback action", null, null);
    }

    public IReadOnlyList<ActorExecutionReceipt> DiscoverCommitmentPressure(
        Commitment commitment,
        string reason,
        SimTime now)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        LivingTownNpcRuntime npc = _population.GetNpc(commitment.Debtor);
        var receipts = new List<ActorExecutionReceipt>();
        var term = (CoinOrResourceTransferTerm)commitment.Term;
        HashSet<string> assets = [term.AssetRef.Value];
        TownAutonomyCandidate[] candidates = BuildPurchaseCandidates(npc, assets, "commitment", now).ToArray();
        _ = RouteCandidates(
            npc,
            "commitment",
            $"commitment/{commitment.CommitmentId.Value}/{reason}",
            candidates,
            now,
            receipts,
            true);
        return receipts;
    }

    public IReadOnlyList<ActorExecutionReceipt> Advance(SimTime now)
    {
        var receipts = new List<ActorExecutionReceipt>();
        foreach (LivingTownNpcRuntime npc in _population.Npcs)
        {
            TownBodyRuleCommitReceipt? bodyReceipt = _gameplay.CommitNeeds(npc.ActorId.Value, now);
            if (bodyReceipt is not null)
                npc.State.ApplyVitals(bodyReceipt.Current);
            TryExecutePendingAction(npc, now, receipts);
            TryEquipForScheduledWork(npc, now, receipts);
            TryMeetBodyNeed(npc, now, receipts);
            DiscoverToolMaintenance(npc, now, receipts);
            TryRunBusinessWork(npc, now, receipts);
            DiscoverScheduledBlocker(npc, now);
            UpdateAspirationDestination(npc, now);
        }

        long day = now.Ticks / _ticksPerDay;
        long tickOfDay = now.Ticks % _ticksPerDay;
        if (day != _lastAspirationDay && tickOfDay >= _ticksPerDay / 8)
        {
            _lastAspirationDay = day;
            DiscoverDailyAspirations(day, now);
        }
        return receipts;
    }

    private bool TryMeetBodyNeed(
        LivingTownNpcRuntime npc,
        SimTime now,
        ICollection<ActorExecutionReceipt> receipts)
    {
        TownActorVitalsSnapshot vitals = _gameplay.GetVitals(npc.ActorId.Value);
        IReadOnlyList<TownGameplayActionOffer> bodyOffers = _gameplay.GetBodyActionOffers(npc.ActorId, now);

        if (vitals.Disease != Disease.Healthy)
        {
            HashSet<string> medicine = _gameplay.GetCompatibleMedicineAssetIds(npc.ActorId)
                .ToHashSet(StringComparer.Ordinal);
            TownAutonomyCandidate[] ownedMedicine = bodyOffers
                .Where(value => value.Selection.Arguments is ConsumptionActionArguments consumption
                    && medicine.Contains(consumption.SourceItemTypeId.Value))
                .Select(value => Candidate("treatment", "body", null, value))
                .ToArray();
            if (RouteCandidates(npc, "treatment", $"disease/{vitals.Disease}", ownedMedicine, now, receipts))
                return true;

            TownAutonomyCandidate[] treatment = BuildPurchaseCandidates(npc, medicine, "treatment", now).ToArray();
            if (RouteCandidates(npc, "treatment", $"disease/{vitals.Disease}", treatment, now, receipts, true))
                return true;
        }

        if (vitals.Satiety <= 50)
        {
            HashSet<string> foods = _gameplay.GetFoodAssetIds().ToHashSet(StringComparer.Ordinal);
            TownAutonomyCandidate[] ownedFood = bodyOffers
                .Where(value => value.Selection.Arguments is ConsumptionActionArguments consumption
                    && foods.Contains(consumption.SourceItemTypeId.Value))
                .Select(value => Candidate("hunger", "body", null, value))
                .ToArray();
            if (RouteCandidates(npc, "hunger", $"satiety/{vitals.Satiety}", ownedFood, now, receipts))
                return true;

            TownAutonomyCandidate[] foodPurchases = BuildPurchaseCandidates(npc, foods, "hunger", now).ToArray();
            if (RouteCandidates(npc, "hunger", $"satiety/{vitals.Satiety}", foodPurchases, now, receipts,
                    vitals.Satiety <= 25))
                return true;
        }

        if (vitals.Spirit <= 35)
        {
            TownAutonomyCandidate[] rest = bodyOffers
                .Where(value => value.Selection.Arguments is RestActionArguments)
                .Select(value => Candidate("rest", "body", _gameplay.GetTravelPlaceId(value.Selection.Binding), value))
                .ToArray();
            if (RouteCandidates(npc, "rest", $"spirit/{vitals.Spirit}", rest, now, receipts,
                    vitals.Spirit <= 15))
                return true;
        }
        return false;
    }

    private void TryEquipForScheduledWork(
        LivingTownNpcRuntime npc,
        SimTime now,
        ICollection<ActorExecutionReceipt> receipts)
    {
        TownScheduleEntryConfiguration? entry = npc.State.CurrentScheduleEntry;
        if (entry?.ExecutionMode != "Interact" || entry.TargetRef is null || npc.State.IsRoutineTravelling) return;
        string prefix = $"{entry.ActionFamilyId}/";
        TownGameplayActionOffer? blocked = _gameplay.GetDecisionActionOffers(npc.ActorId, entry.TargetRef, now)
            .FirstOrDefault(value => value.EntryId.StartsWith(prefix, StringComparison.Ordinal)
                && !value.Validation.Available
                && value.Validation.Reason?.Contains("carried but not equipped", StringComparison.Ordinal) == true
                && value.Selection.Binding.InstrumentRef is not null);
        string? instanceId = blocked?.Selection.Binding.InstrumentRef?.Value;
        if (instanceId is null) return;
        GameActionSpec equipment = _gameplay.CreateEquipmentChange(npc.ActorId, instanceId);
        if (!_gameplay.Validate(equipment, now).Available) return;
        ActorExecutionReceipt receipt = Dispatch(npc.ActorId, equipment, now, AutonomousNpcCognitionRoute.L0);
        receipts.Add(receipt);
    }

    private void TryRunBusinessWork(
        LivingTownNpcRuntime npc,
        SimTime now,
        ICollection<ActorExecutionReceipt> receipts)
    {
        string? workplace = npc.State.Profile.Workplace?.Value;
        if (workplace is null || npc.State.IsRoutineTravelling
            || npc.State.CurrentScheduleEntry?.Purpose != "Work"
            || !_gameplay.GetManagedShopPlaceIds(npc.ActorId).Contains(workplace, StringComparer.Ordinal)
            || !_gameplay.GetStockTargets(npc.ActorId).Any(value => value.CurrentQuantity < value.TargetQuantity))
            return;

        HashSet<string> external = _gameplay.GetExternalTradeIds(npc.ActorId)
            .Select(value => $"listed-exchange/{value}")
            .ToHashSet(StringComparer.Ordinal);
        TownAutonomyCandidate[] candidates = _gameplay.GetDecisionActionOffers(npc.ActorId, workplace, now)
            .Where(value => value.EntryId.StartsWith("stock-", StringComparison.Ordinal)
                || external.Contains(value.EntryId))
            .Select(value => Candidate("business-stock", workplace, workplace, value))
            .ToArray();
        _ = RouteCandidates(npc, "business-stock", $"stock-target/{workplace}", candidates, now, receipts, true);
    }

    private void DiscoverToolMaintenance(
        LivingTownNpcRuntime npc,
        SimTime now,
        ICollection<ActorExecutionReceipt> receipts)
    {
        ItemInstance? worn = _gameplay.GetItemInstances(
                new AssetContainerOwnerId(AssetContainerOwnerKind.Actor, npc.ActorId.Value))
            .Where(value => value.Durability is <= 25)
            .OrderBy(value => value.Durability)
            .ThenBy(value => value.ItemInstanceId.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (worn is null) return;

        TownAutonomyCandidate[] candidates = npc.State.Profile.KnownPlaceRefs
            .OrderBy(value => value, StringComparer.Ordinal)
            .SelectMany(place => _gameplay.GetDecisionActionOffers(npc.ActorId, place, now)
                .Where(value => value.Selection.Arguments is ServiceExchangeActionArguments service
                    && StringComparer.Ordinal.Equals(service.TargetItemInstanceId, worn.ItemInstanceId.Value))
                .Select(value => Candidate("tool-maintenance", place, place, value)))
            .ToArray();
        _ = RouteCandidates(npc, "tool-maintenance", $"tool/{worn.ItemInstanceId.Value}",
            candidates, now, receipts, worn.Durability == 0);
    }

    private void DiscoverScheduledBlocker(LivingTownNpcRuntime npc, SimTime now)
    {
        TownScheduleEntryConfiguration? entry = npc.State.CurrentScheduleEntry;
        if (entry?.ExecutionMode != "Interact" || entry.TargetRef is null || npc.State.IsRoutineTravelling) return;
        string prefix = $"{entry.ActionFamilyId}/";
        TownGameplayActionOffer? scheduled = _gameplay.GetDecisionActionOffers(npc.ActorId, entry.TargetRef, now)
            .FirstOrDefault(value => value.EntryId.StartsWith(prefix, StringComparison.Ordinal));
        if (scheduled is null || scheduled.Validation.Available) return;

        var candidates = new List<TownAutonomyCandidate>
        {
            Candidate("work-blocker", entry.TargetRef, entry.TargetRef, scheduled)
        };
        foreach (string place in npc.State.Profile.KnownPlaceRefs.OrderBy(value => value, StringComparer.Ordinal))
        {
            foreach (TownGameplayActionOffer offer in _gameplay.GetDecisionActionOffers(npc.ActorId, place, now)
                         .Where(value => value.Validation.Available))
            {
                candidates.Add(Candidate("work-blocker", place, place, offer));
                if (candidates.Count == DecisionGate.MAX_L1_CANDIDATES) break;
            }
            if (candidates.Count == DecisionGate.MAX_L1_CANDIDATES) break;
        }
        _ = RouteCandidates(npc, "work-blocker", $"schedule/{entry.EntryId}/{scheduled.Validation.Reason}",
            candidates, now, null, true);
    }

    private IEnumerable<TownAutonomyCandidate> BuildPurchaseCandidates(
        LivingTownNpcRuntime npc,
        IReadOnlySet<string> assetIds,
        string domain,
        SimTime now)
    {
        var places = new HashSet<string>(npc.State.Profile.KnownPlaceRefs, StringComparer.Ordinal);
        foreach (string assetId in assetIds.OrderBy(value => value, StringComparer.Ordinal))
        foreach (string place in _gameplay.GetShopPlaceIdsOffering(assetId).Where(places.Contains))
        foreach (TownGameplayActionOffer offer in _gameplay.GetDecisionActionOffers(npc.ActorId, place, now))
        {
            if (offer.Selection.Arguments is not ListedExchangeActionArguments exchange
                || !StringComparer.Ordinal.Equals(_gameplay.GetListingAssetId(exchange.ListingId), assetId)) continue;
            yield return Candidate(domain, place, place, offer);
        }
    }

    private bool RouteCandidates(
        LivingTownNpcRuntime npc,
        string domain,
        string subjectRef,
        IEnumerable<TownAutonomyCandidate> source,
        SimTime now,
        ICollection<ActorExecutionReceipt>? receipts,
        bool addBlockedEvidence = false)
    {
        string pressureKey = $"{npc.ActorId.Value}/{domain}";
        long day = now.Ticks / _ticksPerDay;
        if (_pendingPressureKeys.Contains(pressureKey)
            || HasPendingActorWork(npc.ActorId)
            || now.Ticks < _nextLocalDecisionAtTicks.GetValueOrDefault(pressureKey)
            || _lastLocalDecisionDay.GetValueOrDefault(pressureKey, -1) == day)
            return false;

        TownAutonomyCandidate[] candidates = source
            .GroupBy(value => value.CandidateId, StringComparer.Ordinal)
            .Select(value => value.First())
            .OrderByDescending(value => value.Available)
            .ThenBy(value => value.CandidateId, StringComparer.Ordinal)
            .Take(DecisionGate.MAX_L1_CANDIDATES)
            .ToArray();
        if (candidates.Length == 0 && addBlockedEvidence)
        {
            candidates =
            [
                new TownAutonomyCandidate(
                    $"{domain}/no-feasible-local-action",
                    TownAutonomyCandidateKind.Evidence,
                    "none",
                    null,
                    null,
                    "No feasible local action is currently known",
                    false,
                    subjectRef)
            ];
        }
        if (candidates.Length == 0) return false;

        TownAutonomyCandidate[] available = candidates.Where(value => value.Available).ToArray();
        if (available.Length == 1)
        {
            _lastLocalDecisionDay[pressureKey] = day;
            TownAutonomyL1Outcome outcome = SettleSelectedCandidate(
                new TownAutonomyLocalDecisionWork(
                    $"l0/{pressureKey}/{day}", pressureKey, domain, subjectRef,
                    npc.ActorId, candidates, now, _visibleFailureCounts.GetValueOrDefault(pressureKey)),
                available[0], npc, now, AutonomousNpcCognitionRoute.L0);
            if (outcome.Receipt is not null) receipts?.Add(outcome.Receipt);
            return outcome.Accepted;
        }

        _pendingPressureKeys.Add(pressureKey);
        var work = new TownAutonomyLocalDecisionWork(
            $"l1/{pressureKey}/{day}",
            pressureKey,
            domain,
            subjectRef,
            npc.ActorId,
            candidates,
            now,
            _visibleFailureCounts.GetValueOrDefault(pressureKey));
        _activeLocalDecisions.Add(work.WorkId, work);
        _localDecisionQueue.Enqueue(work);
        return true;
    }

    private TownAutonomyCandidate Candidate(
        string domain,
        string catalogueTargetId,
        string? travelPlaceId,
        TownGameplayActionOffer offer) => new(
        $"{domain}/{catalogueTargetId}/{offer.EntryId}",
        TownAutonomyCandidateKind.Action,
        catalogueTargetId,
        travelPlaceId,
        offer.EntryId,
        offer.Label,
        offer.Validation.Available,
        offer.Validation.Reason);

    private TownAutonomyCandidate[] BuildDebugCandidates(LivingTownNpcRuntime npc, SimTime now)
    {
        var candidates = new List<TownAutonomyCandidate>();
        candidates.AddRange(_gameplay.GetBodyActionOffers(npc.ActorId, now)
            .Where(value => value.Validation.Available)
            .Select(value => Candidate("debug-demo", "body", null, value)));
        candidates.AddRange(_gameplay.GetCraftActionOffers(npc.ActorId, now)
            .Where(value => value.Validation.Available)
            .Select(value => Candidate("debug-demo", "craft", null, value)));
        foreach (string place in npc.State.Profile.KnownPlaceRefs.OrderBy(value => value, StringComparer.Ordinal))
            candidates.AddRange(_gameplay.GetDecisionActionOffers(npc.ActorId, place, now)
                .Where(value => value.Validation.Available)
                .Select(value => Candidate("debug-demo", place, place, value)));
        return candidates
            .GroupBy(value => value.CandidateId, StringComparer.Ordinal)
            .Select(value => value.First())
            .OrderBy(value => value.CandidateId, StringComparer.Ordinal)
            .Take(DecisionGate.MAX_L1_CANDIDATES)
            .ToArray();
    }

    private TownAutonomyL1Outcome SettleSelectedCandidate(
        TownAutonomyLocalDecisionWork work,
        TownAutonomyCandidate selected,
        LivingTownNpcRuntime npc,
        SimTime now,
        AutonomousNpcCognitionRoute route = AutonomousNpcCognitionRoute.L1)
    {
        TownGameplayActionOffer? offer = ResolveOffer(work.ActorId, selected, now);
        if (offer is null || !offer.Validation.Available)
            return CompleteLocal(work, now, false,
                offer?.Validation.Reason ?? "selected action is no longer visible", null, null);

        var action = new GameActionSpec(work.ActorId, offer.Selection.Binding, offer.Selection.Arguments);
        if (selected.TravelPlaceId is not null
            && !_gameplay.IsInInteractionRange(npc.State.Position, action, selected.TravelPlaceId))
        {
            var intent = new TownAutonomyActionIntentDurableState(
                work.ActorId.Value,
                work.PressureKey,
                selected.CatalogueTargetId,
                selected.TravelPlaceId,
                selected.EntryId!,
                route.ToString());
            if (!_pendingActionIntents.TryAdd(work.ActorId.Value, intent))
                return CompleteLocal(work, now, false,
                    "actor already has a pending autonomous action intent", null, null);
            npc.State.SetAutonomousDestination(new LivingTownPlaceRef(selected.TravelPlaceId));
            return CompleteLocal(work, now, true,
                $"selected {selected.EntryId}; travelling to {selected.TravelPlaceId}", null, null);
        }

        ActorExecutionReceipt receipt = Dispatch(work.ActorId, action, now, route);
        if (receipt.Outcome == ActorExecutionOutcome.Completed)
            npc.State.ApplyVitals(_gameplay.GetVitals(work.ActorId.Value));
        return CompleteLocal(work, now, receipt.Outcome == ActorExecutionOutcome.Completed,
            receipt.Evidence, receipt, null);
    }

    private TownGameplayActionOffer? ResolveOffer(
        ActorId actorId,
        TownAutonomyCandidate candidate,
        SimTime now)
    {
        if (candidate.EntryId is null) return null;
        IReadOnlyList<TownGameplayActionOffer> offers = candidate.CatalogueTargetId == "body"
            ? _gameplay.GetBodyActionOffers(actorId, now)
            : candidate.CatalogueTargetId == "craft"
                ? _gameplay.GetCraftActionOffers(actorId, now)
                : _gameplay.GetDecisionActionOffers(actorId, candidate.CatalogueTargetId, now);
        return offers.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.EntryId, candidate.EntryId));
    }

    private void TryExecutePendingAction(
        LivingTownNpcRuntime npc,
        SimTime now,
        ICollection<ActorExecutionReceipt> receipts)
    {
        if (!_pendingActionIntents.TryGetValue(npc.ActorId.Value, out TownAutonomyActionIntentDurableState? intent))
            return;
        var candidate = new TownAutonomyCandidate(
            $"intent/{intent.EntryId}", TownAutonomyCandidateKind.Action,
            intent.CatalogueTargetId, intent.TravelPlaceId, intent.EntryId, intent.EntryId, true, null);
        TownGameplayActionOffer? offer = ResolveOffer(npc.ActorId, candidate, now);
        if (offer is null || !offer.Validation.Available)
        {
            _pendingActionIntents.Remove(npc.ActorId.Value);
            AbortLinkedDecision(intent);
            _visibleFailureCounts[intent.PressureKey] = checked(
                _visibleFailureCounts.GetValueOrDefault(intent.PressureKey) + 1);
            return;
        }
        var action = new GameActionSpec(npc.ActorId, offer.Selection.Binding, offer.Selection.Arguments);
        if (!_gameplay.IsInInteractionRange(npc.State.Position, action, intent.TravelPlaceId))
        {
            npc.State.SetAutonomousDestination(new LivingTownPlaceRef(intent.TravelPlaceId));
            return;
        }
        AutonomousNpcCognitionRoute route = Enum.Parse<AutonomousNpcCognitionRoute>(intent.CognitionRoute, false);
        ActorExecutionReceipt receipt = Dispatch(npc.ActorId, action, now, route);
        receipts.Add(receipt);
        _pendingActionIntents.Remove(npc.ActorId.Value);
        npc.State.SetAutonomousDestination(null);
        SettleLinkedDecision(intent, receipt, now);
        if (receipt.Outcome == ActorExecutionOutcome.Completed)
        {
            _visibleFailureCounts.Remove(intent.PressureKey);
            _lastLocalDecisionDay.Remove(intent.PressureKey);
            npc.State.ApplyVitals(_gameplay.GetVitals(npc.ActorId.Value));
        }
        else
        {
            _visibleFailureCounts[intent.PressureKey] = checked(
                _visibleFailureCounts.GetValueOrDefault(intent.PressureKey) + 1);
        }
    }

    private void SettleLinkedDecision(
        TownAutonomyActionIntentDurableState intent,
        ActorExecutionReceipt receipt,
        SimTime now)
    {
        if (intent.NeedId is null) return;
        if (!_activeDecisionWorks.TryGetValue(intent.NeedId, out TownAutonomyDecisionWork? work))
            throw new InvalidOperationException("L2 action intent lost its active DecisionNeed.");
        if (receipt.Outcome == ActorExecutionOutcome.Completed)
            work.Need.Resolve(
                now,
                DecisionNeedResolutionKind.ExecuteAction,
                new DecisionNeedExecutionResultReference(receipt.ExecutionId));
        else
            work.Need.Abort();
        _activeDecisionWorks.Remove(intent.NeedId);
    }

    private void AbortLinkedDecision(TownAutonomyActionIntentDurableState intent)
    {
        if (intent.NeedId is null) return;
        if (_activeDecisionWorks.TryGetValue(intent.NeedId, out TownAutonomyDecisionWork? work)
            && work.Need.State is DecisionNeedState.Queued or DecisionNeedState.InFlight)
            work.Need.Abort();
        _activeDecisionWorks.Remove(intent.NeedId);
    }

    private TownAutonomyL1Outcome CompleteLocal(
        TownAutonomyLocalDecisionWork work,
        SimTime now,
        bool accepted,
        string evidence,
        ActorExecutionReceipt? receipt,
        DecisionNeed? escalatedNeed,
        LivingTownCognitionRoute route = LivingTownCognitionRoute.L1)
    {
        _activeLocalDecisions.Remove(work.WorkId);
        _pendingPressureKeys.Remove(work.PressureKey);
        _nextLocalDecisionAtTicks.Remove(work.PressureKey);
        _lastLocalDecisionDay[work.PressureKey] = now.Ticks / _ticksPerDay;
        if (accepted) _visibleFailureCounts.Remove(work.PressureKey);
        else _visibleFailureCounts[work.PressureKey] = checked(
            _visibleFailureCounts.GetValueOrDefault(work.PressureKey) + 1);
        return new TownAutonomyL1Outcome(route, evidence, accepted, receipt, escalatedNeed);
    }

    private static bool MeetsEscalationThreshold(TownAutonomyLocalDecisionWork work, string reasonCode) =>
        reasonCode switch
        {
            "no_feasible_local_action" => work.Candidates.All(value => !value.Available)
                && !work.Candidates.All(IsTemporaryBlocker),
            "goal_or_plan_change" => work.Domain is "aspiration" or "work-blocker" or "business-stock",
            "commitment_or_debt" => work.Domain == "commitment",
            "major_relationship" => work.Domain is "relationship" or "conversation",
            "medical_or_body_deadline" => work.Domain is "treatment" or "hunger" or "rest",
            "repeated_visible_failure" => work.VisibleFailureCount >= 2,
            _ => false
        };

    private static bool IsTemporaryBlocker(TownAutonomyCandidate candidate)
    {
        string reason = candidate.UnavailableReason ?? string.Empty;
        return reason.Contains("closed", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("cooldown", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("until tick", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("growing", StringComparison.OrdinalIgnoreCase);
    }

    private TownAutonomyDecisionWork RegisterEscalatedDecision(
        LivingTownNpcRuntime npc,
        TownAutonomyLocalDecisionWork work,
        string reasonCode,
        SimTime now)
    {
        TownAutonomyCandidate? target = work.Candidates.FirstOrDefault(value => value.TravelPlaceId is not null);
        string? targetId = target?.TravelPlaceId ?? npc.State.Profile.Workplace?.Value;
        var descriptor = new TownAutonomyNeedDescriptor(
            $"{work.Domain}_unresolved",
            $"{work.Domain}_decision",
            $"autonomy-{npc.ActorId.Value}-{work.Domain}-{now.Ticks}",
            targetId is null ? $"actor/{npc.ActorId.Value}" : $"place/{targetId}",
            $"local-escalation/{work.WorkId}/{reasonCode}",
            work.Candidates.Select(value => value.CandidateId).ToArray(),
            _policy.Active.Rq1Activation);
        DecisionNeed need = RegisterDecisionNeed(npc, descriptor, now);
        var problem = new TownL2DecisionProblem(
            $"autonomy/{npc.ActorId.Value}/{work.Domain}/{now.Ticks}",
            descriptor.ProblemCode,
            work.SubjectRef,
            targetId,
            npc.State.NpcState.Social.Appraisals.Select(value => value.OtherActorId).Take(2),
            [new TownL2CurrentEvidence(
                $"current/{work.PressureKey}/{now.Ticks}",
                descriptor.SourceId,
                $"{work.Domain}: {work.SubjectRef}; local reason {reasonCode}.")]);
        return new TownAutonomyDecisionWork(need, npc.ActorId, problem, now, descriptor);
    }

    private void UpdateAspirationDestination(LivingTownNpcRuntime npc, SimTime now)
    {
        if (_pendingActionIntents.ContainsKey(npc.ActorId.Value)) return;
        if (npc.State.CurrentScheduleEntry is not null)
        {
            npc.State.SetAutonomousDestination(null);
            return;
        }

        TownActorVitalsSnapshot vitals = _gameplay.GetVitals(npc.ActorId.Value);
        if (vitals.Satiety <= 50 && !_gameplay.HasFood(npc.ActorId))
        {
            string? mealShop = _gameplay.GetFoodAssetIds()
                .SelectMany(_gameplay.GetShopPlaceIdsOffering)
                .FirstOrDefault();
            if (mealShop is not null)
            {
                npc.State.SetAutonomousDestination(new LivingTownPlaceRef(mealShop));
                return;
            }
        }

        string[] candidates = npc.State.Profile.KnownPlaceRefs
            .Where(value => npc.State.Profile.Residence?.Value != value
                && npc.State.Profile.PrivateRoom?.Value != value
                && npc.State.Profile.Workplace?.Value != value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0) return;
        long day = now.Ticks / _ticksPerDay;
        int offset = StableOrdinalHash(npc.ActorId.Value) & int.MaxValue;
        string destination = candidates[(int)((day + offset) % candidates.Length)];
        npc.State.SetAutonomousDestination(new LivingTownPlaceRef(destination));
    }

    private static int StableOrdinalHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }
            return (int)hash;
        }
    }

    private ActorExecutionReceipt Dispatch(
        ActorId actorId,
        GameActionSpec action,
        SimTime now,
        AutonomousNpcCognitionRoute route)
    {
        var request = new ActorExecutionRequest(
            new ActorExecutionId($"autonomy/{actorId.Value}/{checked(++_executionSequence)}"),
            actorId,
            ActorExecutionMode.Interact,
            new InteractExecutionPayload(actorId, action),
            now,
            route);
        return ActorExecutionPipeline.Dispatch(request, _gameplay.CreateExecutor(actorId));
    }

    private void DiscoverDailyAspirations(long day, SimTime now)
    {
        var candidates = new List<TownL2AdmissionCandidate>();
        var needsByDecision = new Dictionary<string, DecisionNeed>(StringComparer.Ordinal);
        var descriptorsByDecision = new Dictionary<string, TownAutonomyNeedDescriptor>(StringComparer.Ordinal);
        foreach (TownL2AdmissionCandidate candidate in PreviewDailyAspirationCandidates(now))
        {
            LivingTownNpcRuntime npc = _population.GetNpc(candidate.ActorId);
            if (HasPendingActorWork(npc.ActorId)) continue;
            var descriptor = new TownAutonomyNeedDescriptor(
                "aspiration_unresolved",
                "aspiration_resource_choice",
                $"aspiration-{npc.ActorId.Value}-{day}",
                $"place/{candidate.Problem.TargetId}",
                $"daily-aspiration/{npc.ActorId.Value}/{day}",
                [candidate.Problem.SubjectRef],
                _policy.Active.Rq1Activation);
            DecisionNeed need = RegisterDecisionNeed(npc, descriptor, now);
            string decisionId = candidate.Problem.DecisionId;
            needsByDecision.Add(decisionId, need);
            descriptorsByDecision.Add(decisionId, descriptor);
            candidates.Add(candidate);
        }

        TownL2AdmissionCandidate[] selected = _policy.OrderForAdmission(candidates)
            .Take(DemoL2AdmissionBudget)
            .ToArray();
        var selectedIds = selected.Select(value => value.Problem.DecisionId).ToHashSet(StringComparer.Ordinal);
        foreach ((string decisionId, DecisionNeed need) in needsByDecision)
            if (!selectedIds.Contains(decisionId)) need.Abort();
        foreach (TownL2AdmissionCandidate candidate in selected)
            EnqueueDecision(new TownAutonomyDecisionWork(
                needsByDecision[candidate.Problem.DecisionId],
                candidate.ActorId,
                candidate.Problem,
                now,
                descriptorsByDecision[candidate.Problem.DecisionId]));
    }

    private void EnqueueDecision(TownAutonomyDecisionWork work)
    {
        if (HasPendingActorWork(work.ActorId))
            throw new InvalidOperationException("Actor already has active autonomy work.");
        if (!_activeDecisionWorks.TryAdd(work.Need.NeedId.Value, work))
            throw new InvalidOperationException("Autonomy DecisionNeed work is already active.");
        _decisionQueue.Enqueue(work);
    }

    private bool HasPendingActorWork(ActorId actorId) =>
        _pendingActionIntents.ContainsKey(actorId.Value)
        || _activeLocalDecisions.Values.Any(value => value.ActorId == actorId)
        || _activeDecisionWorks.Values.Any(value => value.ActorId == actorId);

    private DecisionNeed RegisterDecisionNeed(
        LivingTownNpcRuntime npc,
        TownAutonomyNeedDescriptor descriptor,
        SimTime now)
    {
        var goal = new NpcGoal(
            new GoalId(descriptor.GoalId),
            new ReachTargetObjective(new TargetRef(descriptor.GoalTargetRef)));
        NpcState current = npc.State.NpcState;
        var planning = new NpcPlanningState([goal], null);
        var viewState = new NpcState(current.ActorId, current.Personality, current.Knowledge, planning, current.Social);
        ActorDecisionView view = ActorDecisionView.Create(npc.State.SharedActorState, viewState, null);
        DecisionNeedDiscoveryRoute route = descriptor.ActivationMode == TownRq1ActivationMode.AgentCentric
            ? DecisionNeedDiscoveryRoute.AgentCentric
            : DecisionNeedDiscoveryRoute.EventCentric;
        var trace = new DecisionNeedDiscoveryTrace(
            route,
            new DecisionNeedDiscoverySourceId(descriptor.SourceId),
            descriptor.EvidenceRefs.Select(value => new DecisionNeedDiscoveryNodeId(value)));
        DecisionNeedRegistrationOutcome outcome = _registrar.RegisterPlanlessStrategic(
            view,
            new DecisionNeedKind(descriptor.NeedKind),
            new DecisionProblemCode(descriptor.ProblemCode),
            trace,
            new DecisionNeedWorldRevision(checked(now.Ticks + 1)),
            now);
        return outcome switch
        {
            RegisteredNew created => created.Need,
            DuplicateActive duplicate => duplicate.Need,
            QueuedSupersession replacement => replacement.Replacement,
            InFlightRevalidationPending replacement => replacement.Replacement,
            _ => throw new InvalidOperationException("Autonomy DecisionNeed was not registerable.")
        };
    }
}
