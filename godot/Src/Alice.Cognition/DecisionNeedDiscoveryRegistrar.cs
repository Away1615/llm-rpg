using Alice.Activities;
using Alice.Npc;
using Alice.Social;

namespace Alice.Cognition;

public sealed class DecisionNeedDiscoveryRegistrar
{
    private readonly DecisionNeedStore _store;

    public DecisionNeedDiscoveryRegistrar(DecisionNeedStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public DecisionNeedRegistrationOutcome RegisterCurrentStep(
        ActorCognitionView view,
        PlanId planId,
        DecisionNeedKind needKind,
        DecisionProblemCode problemCode,
        DecisionNeedDiscoveryTrace discoveryTrace,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt,
        SimTime? deadline = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(planId);
        ArgumentNullException.ThrowIfNull(needKind);
        ArgumentNullException.ThrowIfNull(problemCode);
        ArgumentNullException.ThrowIfNull(discoveryTrace);
        ArgumentNullException.ThrowIfNull(firstObservedWorldRevision);
        if (planId != view.SourcePlanId)
        {
            throw new ArgumentException(
                "The supplied PlanId must identify the ActorCognitionView source plan.",
                nameof(planId));
        }

        EnsureSupportedRoute(discoveryTrace.Route);

        CurrentStepDecisionProblemDescriptor descriptor =
            DecisionProblemDescriptorBuilder.CreateCurrentStep(view, problemCode);
        return _store.Register(
            view.ActorId,
            planId,
            view.CurrentStep.PlanStepId,
            needKind,
            descriptor,
            discoveryTrace,
            firstObservedWorldRevision,
            createdAt,
            deadline);
    }

    public DecisionNeedRegistrationOutcome RegisterPlanlessStrategic(
        ActorDecisionView view,
        DecisionNeedKind needKind,
        DecisionProblemCode problemCode,
        DecisionNeedDiscoveryTrace discoveryTrace,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt,
        SimTime? deadline = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(needKind);
        ArgumentNullException.ThrowIfNull(problemCode);
        ArgumentNullException.ThrowIfNull(discoveryTrace);
        ArgumentNullException.ThrowIfNull(firstObservedWorldRevision);
        EnsureSupportedRoute(discoveryTrace.Route);
        if (view.CurrentPlan is not null || view.CurrentStep is not null)
        {
            throw new ArgumentException(
                "Only an exact planless Actor decision view may register a planless strategic Need.",
                nameof(view));
        }

        if (view.ActiveGoals.Count == 0)
        {
            throw new ArgumentException(
                "A planless strategic Need requires at least one actor-visible active Goal.",
                nameof(view));
        }

        PlanlessStrategicDecisionProblemDescriptor descriptor =
            DecisionProblemDescriptorBuilder.CreatePlanlessStrategic(view, problemCode);
        return _store.RegisterPlanlessStrategic(
            view.ActorId,
            needKind,
            descriptor,
            discoveryTrace,
            firstObservedWorldRevision,
            createdAt,
            deadline);
    }

    public DecisionNeedRegistrationOutcome RegisterMandatoryInviteResponse(
        RoutineSemanticResponseContext context,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(firstObservedWorldRevision);
        EnsureExactMandatoryInviteContext(context);
        ConversationResponseSelection selection = context.Selection;
        DialogueInvitePayload invitePayload = selection.SourceAct.InvitePayload!;

        var descriptor = new InviteResponseDecisionProblemDescriptor(
            context.RespondingActor,
            new DecisionProblemCode("mandatory_invite_response"),
            context.OriginalSpeaker,
            invitePayload.GatheringRef,
            invitePayload.ExpectedGatheringRevision,
            invitePayload.BelievedAuthorizationRef,
            context.TopicRef,
            context.ClaimReferences);
        MandatoryResponseDecisionSubject subject = MandatoryResponseDecisionSubject.Create(context);
        var discoveryTrace = new DecisionNeedDiscoveryTrace(
            DecisionNeedDiscoveryRoute.MandatoryResponse,
            new DecisionNeedDiscoverySourceId(subject.CanonicalValue),
            []);
        return _store.RegisterMandatoryResponse(
            context,
            descriptor,
            discoveryTrace,
            firstObservedWorldRevision,
            createdAt);
    }

    public MandatoryResponseDecisionOwnershipInspection InspectMandatoryInviteResponse(
        RoutineSemanticResponseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureExactMandatoryInviteContext(context);
        return _store.InspectMandatoryResponse(context);
    }

    private static void EnsureExactMandatoryInviteContext(RoutineSemanticResponseContext context)
    {
        ConversationResponseSelection selection = context.Selection;
        if (context.SourceActKind != SemanticDialogueActKind.Invite
            || selection.SourceAct.InvitePayload is null
            || selection.Metadata.IsMandatoryInviteResponse is false
            || !ReferenceEquals(selection.SourceTurn.Act, selection.SourceAct)
            || selection.Session.Transcript.Count(turn => ReferenceEquals(turn, selection.SourceTurn)) != 1
            || selection.Session.PendingResponseOpportunities.Count(
                opportunity => ReferenceEquals(opportunity, selection.Opportunity)) != 1
            || selection.Opportunity.SessionId != selection.Session.SessionId
            || selection.Opportunity.SourceActId != selection.SourceAct.ActId
            || selection.Opportunity.Recipient != context.RespondingActor
            || selection.Opportunity.OriginalSpeaker != context.OriginalSpeaker
            || selection.SourceAct.InvitePayload.InvitedActorId != context.RespondingActor)
        {
            throw new ArgumentException("Only the exact selected pending Invite may be retained or inspected.", nameof(context));
        }
    }

    private static void EnsureSupportedRoute(DecisionNeedDiscoveryRoute route)
    {
        if (route is not DecisionNeedDiscoveryRoute.AgentCentric and not DecisionNeedDiscoveryRoute.EventCentric)
        {
            throw new ArgumentException("Only AgentCentric and EventCentric discovery routes may register a strategic Need.", nameof(route));
        }
    }
}
