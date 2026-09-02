using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Actors;
using Alice.Npc;

namespace Alice.Cognition;

public abstract class EventCentricPlanOptionalDecisionNeedBinding
{
    private protected EventCentricPlanOptionalDecisionNeedBinding(ActorId actorId)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ActorId = actorId;
    }

    public ActorId ActorId { get; }
    internal abstract ActorId ViewActorId { get; }
}

public sealed class EventCentricCurrentStepBinding : EventCentricPlanOptionalDecisionNeedBinding
{
    public EventCentricCurrentStepBinding(
        ActorId actorId,
        EventCentricCurrentStepViewBinding currentStepBinding)
        : base(actorId)
    {
        ArgumentNullException.ThrowIfNull(currentStepBinding);
        CurrentStepBinding = currentStepBinding;
    }

    public EventCentricCurrentStepViewBinding CurrentStepBinding { get; }
    internal override ActorId ViewActorId => CurrentStepBinding.View.ActorId;
}

public sealed class EventCentricPlanlessStrategicBinding : EventCentricPlanOptionalDecisionNeedBinding
{
    public EventCentricPlanlessStrategicBinding(
        ActorId actorId,
        ActorDecisionView view,
        DecisionNeedKind needKind,
        DecisionProblemCode problemCode,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt,
        SimTime? deadline = null)
        : base(actorId)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(needKind);
        ArgumentNullException.ThrowIfNull(problemCode);
        ArgumentNullException.ThrowIfNull(firstObservedWorldRevision);
        if (deadline is not null && deadline.Value.Ticks < createdAt.Ticks)
        {
            throw new ArgumentOutOfRangeException(nameof(deadline));
        }

        View = view;
        NeedKind = needKind;
        ProblemCode = problemCode;
        FirstObservedWorldRevision = firstObservedWorldRevision;
        CreatedAt = createdAt;
        Deadline = deadline;
    }

    public ActorDecisionView View { get; }
    public DecisionNeedKind NeedKind { get; }
    public DecisionProblemCode ProblemCode { get; }
    public DecisionNeedWorldRevision FirstObservedWorldRevision { get; }
    public SimTime CreatedAt { get; }
    public SimTime? Deadline { get; }
    internal override ActorId ViewActorId => View.ActorId;
}

public enum EventCentricPlanOptionalBindingFailureKind
{
    MissingBinding,
    DuplicateBinding,
    ActorMismatch,
    InvalidPlanlessView
}

public sealed record EventCentricPlanOptionalBindingFailure(
    ActorId ActorId,
    EventCentricPlanOptionalBindingFailureKind Kind);

public abstract class EventCentricPlanOptionalEpochResult
{
    private protected EventCentricPlanOptionalEpochResult()
    {
    }
}

public sealed class EventCentricPlanOptionalBindingFailureResult : EventCentricPlanOptionalEpochResult
{
    private readonly ReadOnlyCollection<EventCentricPlanOptionalBindingFailure> _failures;

    internal EventCentricPlanOptionalBindingFailureResult(
        IEnumerable<EventCentricPlanOptionalBindingFailure> failures)
    {
        _failures = Array.AsReadOnly(failures.ToArray());
    }

    public IReadOnlyList<EventCentricPlanOptionalBindingFailure> Failures => _failures;
}

public abstract class EventCentricPlanOptionalActorOutcome
{
    private protected EventCentricPlanOptionalActorOutcome(
        AffectedNodeFact affectedNodeFact,
        EventCentricDiscoverySeed discoverySeed,
        EventCentricPlanOptionalDecisionNeedBinding binding)
    {
        AffectedNodeFact = affectedNodeFact;
        DiscoverySeed = discoverySeed;
        Binding = binding;
    }

    public AffectedNodeFact AffectedNodeFact { get; }
    public EventCentricDiscoverySeed DiscoverySeed { get; }
    public EventCentricPlanOptionalDecisionNeedBinding Binding { get; }
}

public sealed class EventCentricPlanOptionalRegistrationReceipt : EventCentricPlanOptionalActorOutcome
{
    internal EventCentricPlanOptionalRegistrationReceipt(
        AffectedNodeFact affectedNodeFact,
        EventCentricDiscoverySeed discoverySeed,
        EventCentricPlanOptionalDecisionNeedBinding binding,
        DecisionNeedRegistrationOutcome registrationOutcome,
        DecisionNeed selectedNeed,
        EventCentricTreatmentRank treatmentRank)
        : base(affectedNodeFact, discoverySeed, binding)
    {
        RegistrationOutcome = registrationOutcome;
        SelectedNeed = selectedNeed;
        TreatmentRank = treatmentRank;
    }

    public DecisionNeedRegistrationOutcome RegistrationOutcome { get; }
    public DecisionNeed SelectedNeed { get; }
    public EventCentricTreatmentRank TreatmentRank { get; }
}

public sealed class EventCentricPlanOptionalNoActiveGoalReceipt : EventCentricPlanOptionalActorOutcome
{
    internal EventCentricPlanOptionalNoActiveGoalReceipt(
        AffectedNodeFact affectedNodeFact,
        EventCentricDiscoverySeed discoverySeed,
        EventCentricPlanlessStrategicBinding binding)
        : base(affectedNodeFact, discoverySeed, binding)
    {
    }
}

public sealed class EventCentricPlanOptionalCompleted : EventCentricPlanOptionalEpochResult
{
    private readonly ReadOnlyCollection<EventCentricPlanOptionalActorOutcome> _actorOutcomes;
    private readonly ReadOnlyCollection<EventCentricPlanOptionalRegistrationReceipt> _queuedSchedule;

    internal EventCentricPlanOptionalCompleted(
        IEnumerable<EventCentricPlanOptionalActorOutcome> actorOutcomes,
        IEnumerable<EventCentricPlanOptionalRegistrationReceipt> queuedSchedule)
    {
        _actorOutcomes = Array.AsReadOnly(actorOutcomes.ToArray());
        _queuedSchedule = Array.AsReadOnly(queuedSchedule.ToArray());
    }

    public IReadOnlyList<EventCentricPlanOptionalActorOutcome> ActorOutcomes => _actorOutcomes;
    public IReadOnlyList<EventCentricPlanOptionalRegistrationReceipt> QueuedSchedule => _queuedSchedule;
}

/// <summary>Registers a complete EventCentric epoch through current-step or planless strategic eligibility.</summary>
public sealed class EventCentricPlanOptionalDecisionNeedPolicy
{
    private readonly DependencyIndex _dependencyIndex;
    private readonly DecisionNeedDiscoveryRegistrar _registrar;
    private readonly EventCentricCurrentStepDecisionNeedProducer _currentStepProducer;

    public EventCentricPlanOptionalDecisionNeedPolicy(
        DependencyIndex dependencyIndex,
        DecisionNeedDiscoveryRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(dependencyIndex);
        ArgumentNullException.ThrowIfNull(registrar);
        _dependencyIndex = dependencyIndex;
        _registrar = registrar;
        _currentStepProducer = new EventCentricCurrentStepDecisionNeedProducer(dependencyIndex, registrar);
    }

    public EventCentricPlanOptionalEpochResult Run(
        IEnumerable<AffectedNodeFact> facts,
        IEnumerable<EventCentricPlanOptionalDecisionNeedBinding> bindings)
    {
        AffectedNodeFact[] factSnapshot = SnapshotFacts(facts);
        EventCentricPlanOptionalDecisionNeedBinding[] bindingSnapshot = SnapshotBindings(bindings);
        Dictionary<ActorId, DiscoveredRoute> bestRouteByActor = DiscoverBestRoutes(factSnapshot);
        EventCentricPlanOptionalBindingFailure[] failures = ValidateBindings(bestRouteByActor, bindingSnapshot);
        if (failures.Length != 0)
        {
            return new EventCentricPlanOptionalBindingFailureResult(failures);
        }

        Dictionary<ActorId, EventCentricPlanOptionalDecisionNeedBinding> bindingByActor = bindingSnapshot
            .ToDictionary(binding => binding.ActorId);
        DiscoveredRoute[] orderedRoutes = bestRouteByActor.Values.ToArray();
        Array.Sort(orderedRoutes, DiscoveredRouteRegistrationComparer.Instance);
        var outcomes = new List<EventCentricPlanOptionalActorOutcome>(orderedRoutes.Length);
        foreach (DiscoveredRoute route in orderedRoutes)
        {
            EventCentricPlanOptionalDecisionNeedBinding binding = bindingByActor[route.Seed.ActorId];
            if (binding is EventCentricPlanlessStrategicBinding planlessBinding
                && planlessBinding.View.ActiveGoals.Count == 0)
            {
                outcomes.Add(new EventCentricPlanOptionalNoActiveGoalReceipt(
                    route.Fact,
                    route.Seed,
                    planlessBinding));
                continue;
            }

            DecisionNeedRegistrationOutcome registrationOutcome = Register(route, binding);
            DecisionNeed selectedNeed = SelectRegisteredNeed(registrationOutcome);
            var treatmentRank = new EventCentricTreatmentRank(
                route.Seed.RankBand,
                route.CanonicalSourceIdentity,
                route.Seed.ActorId,
                selectedNeed.Fingerprint);
            outcomes.Add(new EventCentricPlanOptionalRegistrationReceipt(
                route.Fact,
                route.Seed,
                binding,
                registrationOutcome,
                selectedNeed,
                treatmentRank));
        }

        EventCentricPlanOptionalRegistrationReceipt[] queuedSchedule = outcomes
            .OfType<EventCentricPlanOptionalRegistrationReceipt>()
            .Where(receipt => receipt.SelectedNeed.State == DecisionNeedState.Queued)
            .ToArray();
        Array.Sort(queuedSchedule, RegistrationReceiptTreatmentRankComparer.Instance);
        return new EventCentricPlanOptionalCompleted(outcomes, queuedSchedule);
    }

    private DecisionNeedRegistrationOutcome Register(
        DiscoveredRoute route,
        EventCentricPlanOptionalDecisionNeedBinding binding)
    {
        if (binding is EventCentricCurrentStepBinding currentStep)
        {
            EventCentricCurrentStepViewBinding value = currentStep.CurrentStepBinding;
            return _currentStepProducer.RegisterPreDiscoveredCurrentStep(
                route.Fact,
                route.Seed,
                value.View,
                value.PlanId,
                value.NeedKind,
                value.ProblemCode,
                value.FirstObservedWorldRevision,
                value.CreatedAt,
                value.Deadline).DecisionNeedRegistrationOutcome;
        }

        if (binding is EventCentricPlanlessStrategicBinding planless)
        {
            return _registrar.RegisterPlanlessStrategic(
                planless.View,
                planless.NeedKind,
                planless.ProblemCode,
                EventCentricCurrentStepDecisionNeedProducer.CreateTrace(route.Fact, route.Seed),
                planless.FirstObservedWorldRevision,
                planless.CreatedAt,
                planless.Deadline);
        }

        throw new InvalidOperationException("Plan-optional binding is outside the closed union.");
    }

    private static AffectedNodeFact[] SnapshotFacts(IEnumerable<AffectedNodeFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        AffectedNodeFact[] snapshot = facts.ToArray();
        if (snapshot.Any(fact => fact is null))
        {
            throw new ArgumentException("A discovery epoch cannot contain a null affected fact.", nameof(facts));
        }

        return snapshot;
    }

    private static EventCentricPlanOptionalDecisionNeedBinding[] SnapshotBindings(
        IEnumerable<EventCentricPlanOptionalDecisionNeedBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        EventCentricPlanOptionalDecisionNeedBinding[] snapshot = bindings.ToArray();
        if (snapshot.Any(binding => binding is null))
        {
            throw new ArgumentException("A discovery epoch cannot contain a null plan-optional binding.", nameof(bindings));
        }

        return snapshot;
    }

    private static EventCentricPlanOptionalBindingFailure[] ValidateBindings(
        IReadOnlyDictionary<ActorId, DiscoveredRoute> bestRouteByActor,
        IEnumerable<EventCentricPlanOptionalDecisionNeedBinding> bindings)
    {
        EventCentricPlanOptionalDecisionNeedBinding[] bindingSnapshot = bindings.ToArray();
        var failures = new HashSet<EventCentricPlanOptionalBindingFailure>();
        ILookup<ActorId, EventCentricPlanOptionalDecisionNeedBinding> bindingsByActor =
            bindingSnapshot.ToLookup(binding => binding.ActorId);
        foreach (ActorId actorId in bestRouteByActor.Keys)
        {
            int count = bindingsByActor[actorId].Count();
            if (count == 0)
            {
                failures.Add(new EventCentricPlanOptionalBindingFailure(
                    actorId,
                    EventCentricPlanOptionalBindingFailureKind.MissingBinding));
            }
            else if (count > 1)
            {
                failures.Add(new EventCentricPlanOptionalBindingFailure(
                    actorId,
                    EventCentricPlanOptionalBindingFailureKind.DuplicateBinding));
            }
        }

        foreach (IGrouping<ActorId, EventCentricPlanOptionalDecisionNeedBinding> group in bindingsByActor)
        {
            if (group.Count() > 1)
            {
                failures.Add(new EventCentricPlanOptionalBindingFailure(
                    group.Key,
                    EventCentricPlanOptionalBindingFailureKind.DuplicateBinding));
            }

            foreach (EventCentricPlanOptionalDecisionNeedBinding binding in group)
            {
                if (binding.ActorId != binding.ViewActorId)
                {
                    failures.Add(new EventCentricPlanOptionalBindingFailure(
                        binding.ActorId,
                        EventCentricPlanOptionalBindingFailureKind.ActorMismatch));
                }

                if (binding is EventCentricPlanlessStrategicBinding planless
                    && (planless.View.CurrentPlan is not null || planless.View.CurrentStep is not null))
                {
                    failures.Add(new EventCentricPlanOptionalBindingFailure(
                        binding.ActorId,
                        EventCentricPlanOptionalBindingFailureKind.InvalidPlanlessView));
                }
            }
        }

        return failures
            .OrderBy(failure => failure.ActorId.Value, StringComparer.Ordinal)
            .ThenBy(failure => failure.Kind)
            .ToArray();
    }

    private Dictionary<ActorId, DiscoveredRoute> DiscoverBestRoutes(AffectedNodeFact[] facts)
    {
        var bestRouteByActor = new Dictionary<ActorId, DiscoveredRoute>();
        foreach (AffectedNodeFact fact in facts)
        {
            foreach (EventCentricDiscoverySeed seed in _dependencyIndex.Discover(fact))
            {
                var candidate = new DiscoveredRoute(fact, seed);
                if (!bestRouteByActor.TryGetValue(seed.ActorId, out DiscoveredRoute? current)
                    || DiscoveredRouteSelectionComparer.Instance.Compare(candidate, current) < 0)
                {
                    bestRouteByActor[seed.ActorId] = candidate;
                }
            }
        }

        return bestRouteByActor;
    }

    private static DecisionNeed SelectRegisteredNeed(DecisionNeedRegistrationOutcome outcome)
    {
        return outcome switch
        {
            RegisteredNew registered => registered.Need,
            DuplicateActive duplicate => duplicate.Need,
            QueuedSupersession supersession => supersession.Replacement,
            InFlightRevalidationPending pending => pending.Replacement,
            StalePreviouslySeen stale => stale.Need,
            _ => throw new InvalidOperationException("Strategic registration returned an unsupported Store outcome.")
        };
    }

    private sealed class DiscoveredRoute
    {
        public DiscoveredRoute(AffectedNodeFact fact, EventCentricDiscoverySeed seed)
        {
            Fact = fact;
            Seed = seed;
            CanonicalSourceIdentity = EventCentricCurrentStepDecisionNeedProducer.CreateCanonicalSourceIdentity(
                seed.SourceKind,
                seed.SourceId);
            AffectedNodeCanonicalIdentity =
                EventCentricCurrentStepDecisionNeedProducer.CreateAffectedNodeCanonicalIdentity(fact.AffectedNode);
        }

        public AffectedNodeFact Fact { get; }
        public EventCentricDiscoverySeed Seed { get; }
        public string CanonicalSourceIdentity { get; }
        public string AffectedNodeCanonicalIdentity { get; }
    }

    private sealed class DiscoveredRouteSelectionComparer : IComparer<DiscoveredRoute>
    {
        public static DiscoveredRouteSelectionComparer Instance { get; } = new();

        public int Compare(DiscoveredRoute? left, DiscoveredRoute? right)
        {
            int rankComparison = left!.Seed.RankBand.CompareTo(right!.Seed.RankBand);
            if (rankComparison != 0)
            {
                return rankComparison;
            }

            int sourceComparison = StringComparer.Ordinal.Compare(
                left.CanonicalSourceIdentity,
                right.CanonicalSourceIdentity);
            return sourceComparison != 0
                ? sourceComparison
                : StringComparer.Ordinal.Compare(
                    left.AffectedNodeCanonicalIdentity,
                    right.AffectedNodeCanonicalIdentity);
        }
    }

    private sealed class DiscoveredRouteRegistrationComparer : IComparer<DiscoveredRoute>
    {
        public static DiscoveredRouteRegistrationComparer Instance { get; } = new();

        public int Compare(DiscoveredRoute? left, DiscoveredRoute? right)
        {
            int rankComparison = left!.Seed.RankBand.CompareTo(right!.Seed.RankBand);
            if (rankComparison != 0)
            {
                return rankComparison;
            }

            int sourceComparison = StringComparer.Ordinal.Compare(
                left.CanonicalSourceIdentity,
                right.CanonicalSourceIdentity);
            return sourceComparison != 0
                ? sourceComparison
                : StringComparer.Ordinal.Compare(left.Seed.ActorId.Value, right.Seed.ActorId.Value);
        }
    }

    private sealed class RegistrationReceiptTreatmentRankComparer :
        IComparer<EventCentricPlanOptionalRegistrationReceipt>
    {
        public static RegistrationReceiptTreatmentRankComparer Instance { get; } = new();

        public int Compare(
            EventCentricPlanOptionalRegistrationReceipt? left,
            EventCentricPlanOptionalRegistrationReceipt? right)
        {
            EventCentricTreatmentRank leftRank = left!.TreatmentRank;
            EventCentricTreatmentRank rightRank = right!.TreatmentRank;
            int rankComparison = leftRank.RankBand.CompareTo(rightRank.RankBand);
            if (rankComparison != 0)
            {
                return rankComparison;
            }

            int sourceComparison = StringComparer.Ordinal.Compare(
                leftRank.CanonicalSourceIdentity,
                rightRank.CanonicalSourceIdentity);
            if (sourceComparison != 0)
            {
                return sourceComparison;
            }

            int actorComparison = StringComparer.Ordinal.Compare(
                leftRank.ActorId.Value,
                rightRank.ActorId.Value);
            return actorComparison != 0
                ? actorComparison
                : StringComparer.Ordinal.Compare(leftRank.Fingerprint.Value, rightRank.Fingerprint.Value);
        }
    }
}

internal static class PlanlessStrategicObjectiveCanonicalFields
{
    internal static string KnowFactValue(KnowObjective objective)
    {
        ArgumentNullException.ThrowIfNull(objective);
        return objective.KnowledgeFactRef.Value;
    }
}
