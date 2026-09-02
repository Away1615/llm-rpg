using Alice.Activities;
using Alice.Npc;

namespace Alice.Cognition;

/// <summary>The complete public result of one EventCentric current-step registration.</summary>
public sealed record EventCentricCurrentStepDecisionNeedRegistrationReceipt
{
    public EventCentricCurrentStepDecisionNeedRegistrationReceipt(
        EventCentricRankBand eventCentricRankBand,
        DecisionNeedRegistrationOutcome decisionNeedRegistrationOutcome)
    {
        ArgumentNullException.ThrowIfNull(decisionNeedRegistrationOutcome);
        if (!Enum.IsDefined(eventCentricRankBand))
        {
            throw new ArgumentOutOfRangeException(nameof(eventCentricRankBand));
        }

        EventCentricRankBand = eventCentricRankBand;
        DecisionNeedRegistrationOutcome = decisionNeedRegistrationOutcome;
    }

    public EventCentricRankBand EventCentricRankBand { get; }
    public DecisionNeedRegistrationOutcome DecisionNeedRegistrationOutcome { get; }
}

/// <summary>Registers one exact one-hop EventCentric discovery for a supplied current-step Actor.</summary>
public sealed class EventCentricCurrentStepDecisionNeedProducer
{
    private readonly DependencyIndex _dependencyIndex;
    private readonly DecisionNeedDiscoveryRegistrar _registrar;

    public EventCentricCurrentStepDecisionNeedProducer(
        DependencyIndex dependencyIndex,
        DecisionNeedDiscoveryRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(dependencyIndex);
        ArgumentNullException.ThrowIfNull(registrar);
        _dependencyIndex = dependencyIndex;
        _registrar = registrar;
    }

    public EventCentricCurrentStepDecisionNeedRegistrationReceipt RegisterCurrentStep(
        AffectedNodeFact fact,
        ActorCognitionView view,
        PlanId planId,
        DecisionNeedKind needKind,
        DecisionProblemCode problemCode,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt,
        SimTime? deadline = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        EventCentricDiscoverySeed seed = FindExactActorSeed(
            _dependencyIndex.Discover(fact),
            view.ActorId);
        return RegisterPreDiscoveredCurrentStep(
            fact,
            seed,
            view,
            planId,
            needKind,
            problemCode,
            firstObservedWorldRevision,
            createdAt,
            deadline);
    }

    internal EventCentricCurrentStepDecisionNeedRegistrationReceipt RegisterPreDiscoveredCurrentStep(
        AffectedNodeFact fact,
        EventCentricDiscoverySeed seed,
        ActorCognitionView view,
        PlanId planId,
        DecisionNeedKind needKind,
        DecisionProblemCode problemCode,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt,
        SimTime? deadline = null)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(view);
        if (fact.SourceKind != seed.SourceKind
            || !StringComparer.Ordinal.Equals(fact.SourceId, seed.SourceId))
        {
            throw new ArgumentException(
                "The discovered seed must retain the affected fact's exact source identity.",
                nameof(seed));
        }

        if (seed.ActorId != view.ActorId)
        {
            throw new ArgumentException(
                "The discovered seed must identify the supplied Actor cognition view.",
                nameof(seed));
        }

        DecisionNeedDiscoveryTrace trace = CreateTrace(fact, seed);
        DecisionNeedRegistrationOutcome outcome = _registrar.RegisterCurrentStep(
            view,
            planId,
            needKind,
            problemCode,
            trace,
            firstObservedWorldRevision,
            createdAt,
            deadline);
        return new EventCentricCurrentStepDecisionNeedRegistrationReceipt(seed.RankBand, outcome);
    }

    private static EventCentricDiscoverySeed FindExactActorSeed(
        IReadOnlyList<EventCentricDiscoverySeed> seeds,
        Alice.Actors.ActorId actorId)
    {
        foreach (EventCentricDiscoverySeed seed in seeds)
        {
            if (seed.ActorId == actorId)
            {
                return seed;
            }
        }

        throw new InvalidOperationException(
            "The supplied Actor is not an exact one-hop dependency result for the affected fact.");
    }

    internal static DecisionNeedDiscoveryTrace CreateTrace(
        AffectedNodeFact fact,
        EventCentricDiscoverySeed seed)
    {
        return new DecisionNeedDiscoveryTrace(
            DecisionNeedDiscoveryRoute.EventCentric,
            new DecisionNeedDiscoverySourceId(CreateCanonicalSourceIdentity(seed.SourceKind, seed.SourceId)),
            [
                new DecisionNeedDiscoveryNodeId(CreateAffectedNodeCanonicalIdentity(fact.AffectedNode)),
                new DecisionNeedDiscoveryNodeId(string.Concat("dependent_actor/", seed.ActorId.Value))
            ]);
    }

    internal static string CreateCanonicalSourceIdentity(
        DependencySourceKind sourceKind,
        string sourceId)
    {
        string sourcePrefix = sourceKind switch
        {
            DependencySourceKind.Event => "event/",
            DependencySourceKind.Pressure => "pressure/",
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind))
        };
        return string.Concat(sourcePrefix, sourceId);
    }

    internal static string CreateAffectedNodeCanonicalIdentity(AffectedNode affectedNode)
    {
        ArgumentNullException.ThrowIfNull(affectedNode);
        if (affectedNode.PlaceRef is { } placeRef)
        {
            return string.Concat("affected/place/", placeRef.Value);
        }

        if (affectedNode.ResourceRef is { } resourceRef)
        {
            return string.Concat("affected/resource/", resourceRef.Value);
        }

        if (affectedNode.CommitmentId is { } commitmentId)
        {
            return string.Concat("affected/commitment/", commitmentId.Value);
        }

        if (affectedNode.ActorId is { } actorId)
        {
            return string.Concat("affected/actor/", actorId.Value);
        }

        if (affectedNode.DutyRef is { } dutyRef)
        {
            return string.Concat("affected/duty/", dutyRef.Value);
        }

        throw new ArgumentException("The affected node has no supported typed identity.", nameof(affectedNode));
    }
}
