using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Actors;
using Alice.Npc;

namespace Alice.Cognition;

/// <summary>One immutable current-step registration input for an exact Actor.</summary>
public sealed class EventCentricCurrentStepViewBinding
{
    public EventCentricCurrentStepViewBinding(
        ActorCognitionView view,
        PlanId planId,
        DecisionNeedKind needKind,
        DecisionProblemCode problemCode,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt,
        SimTime? deadline = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(planId);
        ArgumentNullException.ThrowIfNull(needKind);
        ArgumentNullException.ThrowIfNull(problemCode);
        ArgumentNullException.ThrowIfNull(firstObservedWorldRevision);
        if (planId != view.SourcePlanId)
        {
            throw new ArgumentException(
                "The binding PlanId must identify the Actor cognition view source plan.",
                nameof(planId));
        }

        if (deadline is not null && deadline.Value.Ticks < createdAt.Ticks)
        {
            throw new ArgumentOutOfRangeException(nameof(deadline));
        }

        View = view;
        PlanId = planId;
        NeedKind = needKind;
        ProblemCode = problemCode;
        FirstObservedWorldRevision = firstObservedWorldRevision;
        CreatedAt = createdAt;
        Deadline = deadline;
    }

    public ActorCognitionView View { get; }
    public PlanId PlanId { get; }
    public DecisionNeedKind NeedKind { get; }
    public DecisionProblemCode ProblemCode { get; }
    public DecisionNeedWorldRevision FirstObservedWorldRevision { get; }
    public SimTime CreatedAt { get; }
    public SimTime? Deadline { get; }
}

/// <summary>The ephemeral EventCentric treatment-local ordering tuple.</summary>
public sealed class EventCentricTreatmentRank
{
    internal EventCentricTreatmentRank(
        EventCentricRankBand rankBand,
        string canonicalSourceIdentity,
        ActorId actorId,
        DecisionNeedFingerprint fingerprint)
    {
        if (!Enum.IsDefined(rankBand))
        {
            throw new ArgumentOutOfRangeException(nameof(rankBand));
        }

        if (string.IsNullOrWhiteSpace(canonicalSourceIdentity))
        {
            throw new ArgumentException("Canonical source identity must be non-empty.", nameof(canonicalSourceIdentity));
        }

        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(fingerprint);
        RankBand = rankBand;
        CanonicalSourceIdentity = canonicalSourceIdentity;
        ActorId = actorId;
        Fingerprint = fingerprint;
    }

    public EventCentricRankBand RankBand { get; }
    public string CanonicalSourceIdentity { get; }
    public ActorId ActorId { get; }
    public DecisionNeedFingerprint Fingerprint { get; }
}

/// <summary>One Actor's selected route, exact registration inputs and Store receipt.</summary>
public sealed class EventCentricCurrentStepDiscoveryEpochRegistrationReceipt
{
    internal EventCentricCurrentStepDiscoveryEpochRegistrationReceipt(
        AffectedNodeFact affectedNodeFact,
        EventCentricDiscoverySeed discoverySeed,
        EventCentricCurrentStepViewBinding binding,
        EventCentricCurrentStepDecisionNeedRegistrationReceipt registrationReceipt,
        DecisionNeed selectedNeed,
        EventCentricTreatmentRank treatmentRank)
    {
        AffectedNodeFact = affectedNodeFact;
        DiscoverySeed = discoverySeed;
        Binding = binding;
        RegistrationReceipt = registrationReceipt;
        SelectedNeed = selectedNeed;
        TreatmentRank = treatmentRank;
    }

    public AffectedNodeFact AffectedNodeFact { get; }
    public EventCentricDiscoverySeed DiscoverySeed { get; }
    public EventCentricCurrentStepViewBinding Binding { get; }
    public EventCentricCurrentStepDecisionNeedRegistrationReceipt RegistrationReceipt { get; }
    public DecisionNeed SelectedNeed { get; }
    public EventCentricTreatmentRank TreatmentRank { get; }
}

/// <summary>The closed immutable result of one EventCentric discovery epoch.</summary>
public abstract class EventCentricCurrentStepDiscoveryEpochResult
{
    private protected EventCentricCurrentStepDiscoveryEpochResult()
    {
    }
}

/// <summary>The complete sorted set of discovered Actors lacking current-step views.</summary>
public sealed class UnresolvedViews : EventCentricCurrentStepDiscoveryEpochResult
{
    private readonly ReadOnlyCollection<ActorId> _missingActorIds;

    internal UnresolvedViews(IEnumerable<ActorId> missingActorIds)
    {
        _missingActorIds = Array.AsReadOnly(missingActorIds.ToArray());
    }

    public IReadOnlyList<ActorId> MissingActorIds => _missingActorIds;
}

/// <summary>All registration evidence and the queued treatment-local schedule for a completed epoch.</summary>
public sealed class Completed : EventCentricCurrentStepDiscoveryEpochResult
{
    private readonly ReadOnlyCollection<EventCentricCurrentStepDiscoveryEpochRegistrationReceipt> _registrationReceipts;
    private readonly ReadOnlyCollection<EventCentricCurrentStepDiscoveryEpochRegistrationReceipt> _queuedSchedule;

    internal Completed(
        IEnumerable<EventCentricCurrentStepDiscoveryEpochRegistrationReceipt> registrationReceipts,
        IEnumerable<EventCentricCurrentStepDiscoveryEpochRegistrationReceipt> queuedSchedule)
    {
        _registrationReceipts = Array.AsReadOnly(registrationReceipts.ToArray());
        _queuedSchedule = Array.AsReadOnly(queuedSchedule.ToArray());
    }

    public IReadOnlyList<EventCentricCurrentStepDiscoveryEpochRegistrationReceipt> RegistrationReceipts =>
        _registrationReceipts;

    public IReadOnlyList<EventCentricCurrentStepDiscoveryEpochRegistrationReceipt> QueuedSchedule =>
        _queuedSchedule;
}

/// <summary>Discovers and registers one finite, immutable EventCentric current-step epoch.</summary>
public sealed class EventCentricCurrentStepDiscoveryEpochRuntime
{
    private readonly DependencyIndex _dependencyIndex;
    private readonly EventCentricCurrentStepDecisionNeedProducer _producer;

    public EventCentricCurrentStepDiscoveryEpochRuntime(
        DependencyIndex dependencyIndex,
        DecisionNeedDiscoveryRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(dependencyIndex);
        ArgumentNullException.ThrowIfNull(registrar);
        _dependencyIndex = dependencyIndex;
        _producer = new EventCentricCurrentStepDecisionNeedProducer(dependencyIndex, registrar);
    }

    public EventCentricCurrentStepDiscoveryEpochResult Run(
        IEnumerable<AffectedNodeFact> facts,
        IEnumerable<EventCentricCurrentStepViewBinding> bindings)
    {
        AffectedNodeFact[] factSnapshot = SnapshotFacts(facts);
        Dictionary<ActorId, EventCentricCurrentStepViewBinding> bindingSnapshot = SnapshotBindings(bindings);
        Dictionary<ActorId, DiscoveredRoute> bestRouteByActor = DiscoverBestRoutes(factSnapshot);

        ActorId[] missingActorIds = bestRouteByActor.Keys
            .Where(actorId => !bindingSnapshot.ContainsKey(actorId))
            .OrderBy(actorId => actorId.Value, StringComparer.Ordinal)
            .ToArray();
        if (missingActorIds.Length != 0)
        {
            return new UnresolvedViews(missingActorIds);
        }

        DiscoveredRoute[] orderedRoutes = bestRouteByActor.Values.ToArray();
        Array.Sort(orderedRoutes, DiscoveredRouteRegistrationComparer.Instance);
        var receipts = new List<EventCentricCurrentStepDiscoveryEpochRegistrationReceipt>(orderedRoutes.Length);
        foreach (DiscoveredRoute route in orderedRoutes)
        {
            EventCentricCurrentStepViewBinding binding = bindingSnapshot[route.Seed.ActorId];
            EventCentricCurrentStepDecisionNeedRegistrationReceipt registration =
                _producer.RegisterPreDiscoveredCurrentStep(
                    route.Fact,
                    route.Seed,
                    binding.View,
                    binding.PlanId,
                    binding.NeedKind,
                    binding.ProblemCode,
                    binding.FirstObservedWorldRevision,
                    binding.CreatedAt,
                    binding.Deadline);
            DecisionNeed selectedNeed = SelectRegisteredNeed(registration.DecisionNeedRegistrationOutcome);
            var treatmentRank = new EventCentricTreatmentRank(
                route.Seed.RankBand,
                route.CanonicalSourceIdentity,
                route.Seed.ActorId,
                selectedNeed.Fingerprint);
            receipts.Add(new EventCentricCurrentStepDiscoveryEpochRegistrationReceipt(
                route.Fact,
                route.Seed,
                binding,
                registration,
                selectedNeed,
                treatmentRank));
        }

        EventCentricCurrentStepDiscoveryEpochRegistrationReceipt[] queuedSchedule = receipts
            .Where(IsSelectedNeedQueued)
            .ToArray();
        Array.Sort(queuedSchedule, RegistrationReceiptTreatmentRankComparer.Instance);
        return new Completed(receipts, queuedSchedule);
    }

    private static AffectedNodeFact[] SnapshotFacts(IEnumerable<AffectedNodeFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        AffectedNodeFact[] snapshot = facts.ToArray();
        foreach (AffectedNodeFact? fact in snapshot)
        {
            if (fact is null)
            {
                throw new ArgumentException("A discovery epoch cannot contain a null affected fact.", nameof(facts));
            }
        }

        return snapshot;
    }

    private static Dictionary<ActorId, EventCentricCurrentStepViewBinding> SnapshotBindings(
        IEnumerable<EventCentricCurrentStepViewBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        EventCentricCurrentStepViewBinding[] snapshot = bindings.ToArray();
        var bindingsByActor = new Dictionary<ActorId, EventCentricCurrentStepViewBinding>(snapshot.Length);
        foreach (EventCentricCurrentStepViewBinding? binding in snapshot)
        {
            if (binding is null)
            {
                throw new ArgumentException("A discovery epoch cannot contain a null Actor view binding.", nameof(bindings));
            }

            if (!bindingsByActor.TryAdd(binding.View.ActorId, binding))
            {
                throw new ArgumentException("A discovery epoch cannot contain duplicate Actor view bindings.", nameof(bindings));
            }
        }

        return bindingsByActor;
    }

    private Dictionary<ActorId, DiscoveredRoute> DiscoverBestRoutes(AffectedNodeFact[] facts)
    {
        var bestRouteByActor = new Dictionary<ActorId, DiscoveredRoute>();
        foreach (AffectedNodeFact fact in facts)
        {
            IReadOnlyList<EventCentricDiscoverySeed> seeds = _dependencyIndex.Discover(fact);
            foreach (EventCentricDiscoverySeed seed in seeds)
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
            _ => throw new InvalidOperationException("Current-step registration returned an unsupported Store outcome.")
        };
    }

    private static bool IsSelectedNeedQueued(
        EventCentricCurrentStepDiscoveryEpochRegistrationReceipt receipt)
    {
        return receipt.SelectedNeed.State == DecisionNeedState.Queued;
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
        IComparer<EventCentricCurrentStepDiscoveryEpochRegistrationReceipt>
    {
        public static RegistrationReceiptTreatmentRankComparer Instance { get; } = new();

        public int Compare(
            EventCentricCurrentStepDiscoveryEpochRegistrationReceipt? left,
            EventCentricCurrentStepDiscoveryEpochRegistrationReceipt? right)
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
