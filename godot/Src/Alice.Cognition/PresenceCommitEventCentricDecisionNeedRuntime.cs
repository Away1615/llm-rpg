using Alice.Activities;
using Alice.Authority;
using Alice.Npc;

namespace Alice.Cognition;

/// <summary>The closed result of processing one completed Presence fulfilment attempt.</summary>
public abstract class PresenceCommitEventCentricDecisionNeedResult
{
    private protected PresenceCommitEventCentricDecisionNeedResult(
        PresenceFulfillmentResult presenceFulfillmentResult)
    {
        PresenceFulfillmentResult = presenceFulfillmentResult;
    }

    public PresenceFulfillmentResult PresenceFulfillmentResult { get; }
}

/// <summary>A completed Presence attempt that produced no committed receipt.</summary>
public sealed class NoCommittedReceipt : PresenceCommitEventCentricDecisionNeedResult
{
    internal NoCommittedReceipt(PresenceFulfillmentResult presenceFulfillmentResult)
        : base(presenceFulfillmentResult)
    {
    }
}

/// <summary>A committed Presence result and its exact EventCentric registration evidence.</summary>
public sealed class Registered : PresenceCommitEventCentricDecisionNeedResult
{
    internal Registered(
        PresenceFulfillmentResult presenceFulfillmentResult,
        AffectedNodeFact affectedNodeFact,
        EventCentricCurrentStepDecisionNeedRegistrationReceipt registrationReceipt)
        : base(presenceFulfillmentResult)
    {
        AffectedNodeFact = affectedNodeFact;
        RegistrationReceipt = registrationReceipt;
    }

    public AffectedNodeFact AffectedNodeFact { get; }
    public EventCentricCurrentStepDecisionNeedRegistrationReceipt RegistrationReceipt { get; }
}

/// <summary>Composes a completed Presence Authority result into one current-step EventCentric Need.</summary>
public sealed class PresenceCommitEventCentricDecisionNeedRuntime
{
    private readonly PresenceCommitAffectedNodeFactProjector _projector;
    private readonly EventCentricCurrentStepDecisionNeedProducer _producer;

    public PresenceCommitEventCentricDecisionNeedRuntime(
        PresenceCommitAffectedNodeFactProjector projector,
        EventCentricCurrentStepDecisionNeedProducer producer)
    {
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentNullException.ThrowIfNull(producer);
        _projector = projector;
        _producer = producer;
    }

    public PresenceCommitEventCentricDecisionNeedResult RegisterCurrentStep(
        PresenceFulfillmentResult presenceFulfillmentResult,
        ActorCognitionView view,
        PlanId planId,
        DecisionNeedKind needKind,
        DecisionProblemCode problemCode,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt,
        SimTime? deadline = null)
    {
        ArgumentNullException.ThrowIfNull(presenceFulfillmentResult);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(planId);
        ArgumentNullException.ThrowIfNull(needKind);
        ArgumentNullException.ThrowIfNull(problemCode);
        ArgumentNullException.ThrowIfNull(firstObservedWorldRevision);

        PresenceCommitReceipt? receipt = presenceFulfillmentResult.Receipt;
        if (receipt is null)
        {
            return new NoCommittedReceipt(presenceFulfillmentResult);
        }

        AffectedNodeFact fact = _projector.Project(receipt);
        EventCentricCurrentStepDecisionNeedRegistrationReceipt registrationReceipt =
            _producer.RegisterCurrentStep(
                fact,
                view,
                planId,
                needKind,
                problemCode,
                firstObservedWorldRevision,
                createdAt,
                deadline);
        return new Registered(presenceFulfillmentResult, fact, registrationReceipt);
    }
}
