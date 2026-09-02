using Alice.Actors;
using Alice.Activities;
using Alice.Authority;
using Alice.Items;
using Alice.ModelRuntime;
using Alice.Npc;
using Alice.Social;
using Alice.World;

namespace Alice.Cognition;

public enum RemotePlannerHostRejectionReason
{
    InvalidInput,
    StoreEntryMismatch,
    AlreadySettled,
    NeedNotInFlight,
    StaleStoreEntry,
    ContextMismatch,
    RequestBindingMismatch,
    ResponseBindingMismatch,
    ActorViewMismatch,
    CurrentPlanMismatch,
    ResponseFailure,
    UnknownTarget,
    UnknownItem,
    InvalidProposal,
    ReviseGoalMismatch,
    RevisionOverflow,
    ResolutionTimeBeforeCreation,
    MandatoryResponseSubjectMismatch,
    StaleResponseSelection,
    ResponseRoutingConflict,
    PlanningStateMismatch,
    UnknownGoal,
    IdentityCollision
}

public abstract record RemotePlannerHostSettlementOutcome
{
    private protected RemotePlannerHostSettlementOutcome()
    {
    }
}

public sealed record RemotePlannerHostCreatePlanAccepted(NpcPlan Plan) : RemotePlannerHostSettlementOutcome;
public sealed record RemotePlannerHostRevisePlanAccepted(NpcPlan Plan) : RemotePlannerHostSettlementOutcome;
public sealed record RemotePlannerHostVerifyAccepted(NpcGoal Goal) : RemotePlannerHostSettlementOutcome;
public sealed record RemotePlannerHostNoArtifactAccepted(DecisionNeedResolutionKind ResolutionKind) : RemotePlannerHostSettlementOutcome;
public sealed record RemotePlannerHostInviteResponseAccepted(
    SemanticDialogueAct Act,
    ConversationSemanticResponseResult RoutingResult) : RemotePlannerHostSettlementOutcome;
public sealed record RemotePlannerHostInviteResponseAuthorityRejected(
    ConversationSemanticResponseResult RoutingResult) : RemotePlannerHostSettlementOutcome;
public sealed record RemotePlannerHostRejected(RemotePlannerHostRejectionReason Reason) : RemotePlannerHostSettlementOutcome;
public sealed record RemotePlannerHostPlanlessCreatePlanAccepted(
    NpcPlan Plan,
    NpcPlanningState PlanningState,
    PlanRuntime Runtime) : RemotePlannerHostSettlementOutcome;
public sealed record RemotePlannerHostPlanlessVerifyAccepted(
    NpcGoal Goal,
    NpcPlanningState PlanningState) : RemotePlannerHostSettlementOutcome;

public static class RemotePlannerHostSettlement
{
    private const string IdentityNamespace = "alice.remote_planner_host.v1";

    public static RemotePlannerHostSettlementOutcome SettlePlanlessStrategic(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorDecisionView view,
        L2PlanlessStrategicContext context,
        RemotePlannerRequest request,
        RemotePlannerResponse response,
        NpcPlanningState currentPlanning,
        SimTime resolvedAt) =>
        PlanlessStrategicRemotePlannerSettlement.Settle(
            store,
            need,
            view,
            context,
            request,
            response,
            currentPlanning,
            resolvedAt);

    public static RemotePlannerHostSettlementOutcome Settle(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorCognitionView view,
        L2PlanningContext context,
        RemotePlannerRequest request,
        RemotePlannerResponse response,
        NpcPlan currentPlan,
        SimTime resolvedAt)
    {
        if (store is null || need is null || view is null || context is null || request is null || response is null || currentPlan is null)
        {
            return Reject(RemotePlannerHostRejectionReason.InvalidInput);
        }

        if (resolvedAt.Ticks < need.CreatedAt.Ticks)
        {
            return Reject(RemotePlannerHostRejectionReason.ResolutionTimeBeforeCreation);
        }

        if (store.Lookup(need.NeedId) is not FoundDecisionNeed found || !ReferenceEquals(found.Need, need))
        {
            return Reject(RemotePlannerHostRejectionReason.StoreEntryMismatch);
        }

        if (need.State == DecisionNeedState.Resolved)
        {
            return Reject(RemotePlannerHostRejectionReason.AlreadySettled);
        }

        if (need.State != DecisionNeedState.InFlight)
        {
            return Reject(RemotePlannerHostRejectionReason.NeedNotInFlight);
        }

        if (need.NpcId != view.ActorId)
        {
            return Reject(RemotePlannerHostRejectionReason.ActorViewMismatch);
        }

        bool contextMatches = MatchesNeedAndContext(need, context);
        if (!contextMatches || !MatchesCurrentPlan(need, view, currentPlan, context.SourcePlanBinding))
        {
            return Reject(!contextMatches
                ? RemotePlannerHostRejectionReason.ContextMismatch
                : RemotePlannerHostRejectionReason.CurrentPlanMismatch);
        }

        if (!MatchesRequestContext(request, context))
        {
            return Reject(RemotePlannerHostRejectionReason.RequestBindingMismatch);
        }

        if (response.Binding != request.Binding)
        {
            return Reject(RemotePlannerHostRejectionReason.ResponseBindingMismatch);
        }

        if (response.Decision is RemotePlannerFailure)
        {
            return Reject(RemotePlannerHostRejectionReason.ResponseFailure);
        }

        DecisionNeedRevalidationOutcome revalidation = store.RevalidateStoreCurrent(need.NeedId);
        if (revalidation is StaleSuperseded)
        {
            return Reject(RemotePlannerHostRejectionReason.StaleStoreEntry);
        }

        return SettleDecision(need, view, response.Decision, currentPlan, resolvedAt);
    }

    public static RemotePlannerHostSettlementOutcome SettleInviteResponse(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorDecisionView view,
        L2InviteResponseContext context,
        RoutineSemanticResponseContext responseContext,
        RemotePlannerRequest request,
        RemotePlannerResponse response,
        InvitationAcceptanceAuthorityRuntime invitationAcceptanceAuthority,
        SimTime resolvedAt)
    {
        if (store is null || need is null || view is null || context is null || responseContext is null
            || request is null || response is null || invitationAcceptanceAuthority is null)
        {
            return Reject(RemotePlannerHostRejectionReason.InvalidInput);
        }

        if (resolvedAt.Ticks < need.CreatedAt.Ticks)
        {
            return Reject(RemotePlannerHostRejectionReason.ResolutionTimeBeforeCreation);
        }

        if (store.Lookup(need.NeedId) is not FoundDecisionNeed found || !ReferenceEquals(found.Need, need))
        {
            return Reject(RemotePlannerHostRejectionReason.StoreEntryMismatch);
        }

        if (need.State == DecisionNeedState.Resolved)
        {
            return Reject(RemotePlannerHostRejectionReason.AlreadySettled);
        }

        if (need.State != DecisionNeedState.InFlight)
        {
            return Reject(RemotePlannerHostRejectionReason.NeedNotInFlight);
        }

        if (!store.IsCurrentMandatoryResponse(need))
        {
            return Reject(RemotePlannerHostRejectionReason.StaleStoreEntry);
        }

        if (view.ActorId != need.NpcId || !view.Equals(context.ActorView)
            || context.ActorId != need.NpcId
            || context.NeedId != need.NeedId
            || context.Fingerprint != need.Fingerprint
            || context.ProblemDescriptorHash != need.ProblemDescriptor.DescriptorHash
            || context.AttemptCount != need.AttemptCount)
        {
            return Reject(RemotePlannerHostRejectionReason.ContextMismatch);
        }

        if (!need.MatchesMandatoryResponseContext(responseContext)
            || !MatchesSubject(context.SubjectBinding, responseContext))
        {
            return Reject(RemotePlannerHostRejectionReason.MandatoryResponseSubjectMismatch);
        }

        if (!IsLiveSelection(responseContext))
        {
            return Reject(RemotePlannerHostRejectionReason.StaleResponseSelection);
        }

        if (!MatchesInviteRequestContext(request, context))
        {
            return Reject(RemotePlannerHostRejectionReason.RequestBindingMismatch);
        }

        if (!ReferenceEquals(response.Binding, request.Binding))
        {
            return Reject(RemotePlannerHostRejectionReason.ResponseBindingMismatch);
        }

        if (response.Decision is not RemotePlannerInviteResponse inviteResponse)
        {
            return Reject(RemotePlannerHostRejectionReason.ResponseFailure);
        }

        SemanticDialogueAct act = MaterializeInviteResponseAct(need, responseContext, inviteResponse.ResponseKind);
        RoutineSemanticResponseCandidateSet candidates;
        try
        {
            candidates = new RoutineSemanticResponseCandidateSet(responseContext.Selection, [act]);
        }
        catch (ArgumentException)
        {
            return Reject(RemotePlannerHostRejectionReason.ResponseRoutingConflict);
        }

        ConversationSemanticResponseResult routing = ConversationSemanticResponseRuntime.Route(
            candidates,
            invitationAcceptanceAuthority);
        if (routing.Outcome is ConversationSemanticResponseOutcome.OrdinaryReplyRecorded
            or ConversationSemanticResponseOutcome.InvitationAccepted)
        {
            need.Resolve(
                resolvedAt,
                DecisionNeedResolutionKind.Respond,
                new DecisionNeedSemanticActResultReference(act.ActId));
            return new RemotePlannerHostInviteResponseAccepted(act, routing);
        }

        if (routing.Outcome == ConversationSemanticResponseOutcome.InvitationRejected)
        {
            need.ReturnMandatoryResponseToQueued();
            return new RemotePlannerHostInviteResponseAuthorityRejected(routing);
        }

        return Reject(RemotePlannerHostRejectionReason.ResponseRoutingConflict);
    }

    private static bool MatchesSubject(
        InviteResponseDecisionSubjectBinding subject,
        RoutineSemanticResponseContext context)
    {
        ConversationResponseSelection selection = context.Selection;
        return subject.ActorId == context.RespondingActor
            && subject.SessionId == selection.Session.SessionId
            && subject.OpportunityId == selection.Opportunity.OpportunityId
            && subject.SourceActId == selection.SourceAct.ActId;
    }

    private static bool IsLiveSelection(RoutineSemanticResponseContext context)
    {
        ConversationResponseSelection selection = context.Selection;
        return selection.Opportunity.SessionId == selection.Session.SessionId
            && selection.Opportunity.SourceActId == selection.SourceAct.ActId
            && ReferenceEquals(selection.SourceTurn.Act, selection.SourceAct)
            && selection.Session.Transcript.Count(turn => ReferenceEquals(turn, selection.SourceTurn)) == 1
            && selection.Session.PendingResponseOpportunities.Count(
                opportunity => ReferenceEquals(opportunity, selection.Opportunity)) == 1;
    }

    private static bool MatchesInviteRequestContext(
        RemotePlannerRequest request,
        L2InviteResponseContext context)
    {
        RemotePlannerRequestBinding binding = request.Binding;
        if (binding.Kind != RemotePlannerRequestKind.InviteResponse)
        {
            return false;
        }

        RemoteInviteResponseRequestBinding inviteBinding = binding.InviteResponseBinding;
        return binding.Role == RemotePlannerRole.InviteResponder
            && binding.ActorId == context.ActorId
            && binding.NeedId == context.NeedId
            && binding.Fingerprint == context.Fingerprint
            && binding.ProblemDescriptorHash == context.ProblemDescriptorHash
            && binding.CandidateSetId == context.CandidateSetId
            && inviteBinding.SharedContextId == context.SharedContextId
            && inviteBinding.ContextId == context.ContextId
            && inviteBinding.AttemptCount == context.AttemptCount
            && inviteBinding.Subject == context.SubjectBinding
            && request.ProtocolVersion == RemoteInviteResponseProtocol.ProtocolVersion
            && request.GetModelVisibleBytes().AsSpan().SequenceEqual(context.GetModelVisibleBytes())
            && request.GetToolCatalogueUtf8().AsSpan().SequenceEqual(RemoteInviteResponseProtocol.GetToolCatalogueUtf8());
    }

    private static SemanticDialogueAct MaterializeInviteResponseAct(
        DecisionNeed need,
        RoutineSemanticResponseContext context,
        RemoteInviteResponseKind responseKind)
    {
        SemanticDialogueActKind actKind = responseKind switch
        {
            RemoteInviteResponseKind.Accept => SemanticDialogueActKind.Accept,
            RemoteInviteResponseKind.Decline => SemanticDialogueActKind.Decline,
            RemoteInviteResponseKind.Clarify => SemanticDialogueActKind.Clarify,
            RemoteInviteResponseKind.CounterOffer => SemanticDialogueActKind.CounterOffer,
            _ => throw new ArgumentOutOfRangeException(nameof(responseKind))
        };
        string kindToken = responseKind switch
        {
            RemoteInviteResponseKind.Accept => "accept",
            RemoteInviteResponseKind.Decline => "decline",
            RemoteInviteResponseKind.Clarify => "clarify",
            RemoteInviteResponseKind.CounterOffer => "counter_offer",
            _ => throw new ArgumentOutOfRangeException(nameof(responseKind))
        };
        string actId = string.Concat(
            "alice.remote_invite_response.v1:",
            need.NeedId.Value.Length,
            ":",
            need.NeedId.Value,
            ":",
            need.AttemptCount,
            ":",
            kindToken);
        return new SemanticDialogueAct(
            new SemanticDialogueActId(actId),
            actKind,
            context.RespondingActor,
            [context.OriginalSpeaker],
            context.TopicRef,
            [],
            null);
    }

    private static RemotePlannerHostSettlementOutcome SettleDecision(
        DecisionNeed need,
        ActorCognitionView view,
        RemotePlannerDecision decision,
        NpcPlan currentPlan,
        SimTime resolvedAt)
    {
        switch (decision)
        {
            case RemotePlannerCreatePlan create:
                return SettleCreate(need, view, create.Proposal, resolvedAt);
            case RemotePlannerRevisePlan revise:
                return SettleRevise(need, view, revise.Proposal, currentPlan, resolvedAt);
            case RemotePlannerVerify:
                return SettleVerify(need, resolvedAt);
            case RemotePlannerDefer:
                need.Resolve(resolvedAt, DecisionNeedResolutionKind.Defer, null);
                return new RemotePlannerHostNoArtifactAccepted(DecisionNeedResolutionKind.Defer);
            case RemotePlannerCancel:
                need.Resolve(resolvedAt, DecisionNeedResolutionKind.Cancel, null);
                return new RemotePlannerHostNoArtifactAccepted(DecisionNeedResolutionKind.Cancel);
            default:
                return Reject(RemotePlannerHostRejectionReason.InvalidProposal);
        }
    }

    private static RemotePlannerHostSettlementOutcome SettleCreate(DecisionNeed need, ActorCognitionView view, RemotePlannerPlanProposal proposal, SimTime resolvedAt)
    {
        RemotePlannerHostRejectionReason? validation = ValidateProposal(view, proposal);
        if (validation is not null)
        {
            return Reject(validation.Value);
        }

        try
        {
            NpcGoal goal = new(CreateGoalId(need), proposal.GoalObjective);
            NpcPlan plan = new(CreatePlanId(need), need.NpcId, goal, 1, MaterializeSteps(need, proposal, "create"));
            need.Resolve(resolvedAt, DecisionNeedResolutionKind.CreatePlan, new DecisionNeedPlanResultReference(plan.PlanId));
            return new RemotePlannerHostCreatePlanAccepted(plan);
        }
        catch (ArgumentException)
        {
            return Reject(RemotePlannerHostRejectionReason.InvalidProposal);
        }
    }

    private static RemotePlannerHostSettlementOutcome SettleRevise(DecisionNeed need, ActorCognitionView view, RemotePlannerPlanProposal proposal, NpcPlan currentPlan, SimTime resolvedAt)
    {
        if (proposal.GoalObjective != currentPlan.Goal.Objective)
        {
            return Reject(RemotePlannerHostRejectionReason.ReviseGoalMismatch);
        }

        RemotePlannerHostRejectionReason? validation = ValidateProposal(view, proposal);
        if (validation is not null)
        {
            return Reject(validation.Value);
        }

        int revision;
        try
        {
            revision = checked(currentPlan.Revision + 1);
        }
        catch (OverflowException)
        {
            return Reject(RemotePlannerHostRejectionReason.RevisionOverflow);
        }

        try
        {
            NpcPlan plan = new(currentPlan.PlanId, need.NpcId, currentPlan.Goal, revision, MaterializeSteps(need, proposal, "revise"));
            need.Resolve(resolvedAt, DecisionNeedResolutionKind.RevisePlan, new DecisionNeedPlanResultReference(plan.PlanId));
            return new RemotePlannerHostRevisePlanAccepted(plan);
        }
        catch (ArgumentException)
        {
            return Reject(RemotePlannerHostRejectionReason.InvalidProposal);
        }
    }

    private static RemotePlannerHostSettlementOutcome SettleVerify(DecisionNeed need, SimTime resolvedAt)
    {
        NpcGoal goal = new(CreateVerifyGoalId(need), new KnowObjective(KnowledgeFactRef.ForProblemDescriptor(need.ProblemDescriptor.DescriptorHash)));
        need.Resolve(resolvedAt, DecisionNeedResolutionKind.Verify, new DecisionNeedGoalResultReference(goal.GoalId));
        return new RemotePlannerHostVerifyAccepted(goal);
    }

    private static RemotePlannerHostRejectionReason? ValidateProposal(ActorCognitionView view, RemotePlannerPlanProposal proposal)
    {
        if (proposal is null || !IsKnownObjectiveItem(view, proposal.GoalObjective))
        {
            return RemotePlannerHostRejectionReason.UnknownItem;
        }

        foreach (RemotePlannerPlanStepProposal step in proposal.Steps)
        {
            if (step is null || !IsKnownObjectiveItem(view, step.Objective) || !MatchesActor(view.ActorId, step.DesiredResult))
            {
                return RemotePlannerHostRejectionReason.InvalidProposal;
            }

            if (!IsKnownResultItem(view, step.DesiredResult))
            {
                return RemotePlannerHostRejectionReason.UnknownItem;
            }

            if (!AreKnownTargets(view, step))
            {
                return RemotePlannerHostRejectionReason.UnknownTarget;
            }
        }

        return null;
    }

    private static bool MatchesNeedAndContext(DecisionNeed need, L2PlanningContext context)
    {
        return context.ActorId == need.NpcId &&
            context.NeedId == need.NeedId &&
            context.Fingerprint == need.Fingerprint &&
            context.ProblemDescriptorHash == need.ProblemDescriptor.DescriptorHash &&
            context.SourcePlanBinding.Plan.ActorId == need.NpcId &&
            context.SourcePlanBinding.Plan.PlanId == need.PlanId &&
            context.SourcePlanBinding.CurrentPlanStepId == need.PlanStepId;
    }

    private static bool MatchesCurrentPlan(
        DecisionNeed need,
        ActorCognitionView view,
        NpcPlan currentPlan,
        L2SourcePlanBinding sourceBinding)
    {
        PlanStep? sourceStep = sourceBinding.Plan.Steps.FirstOrDefault(
            step => step.PlanStepId == sourceBinding.CurrentPlanStepId);
        return need.PlanId is not null &&
            need.PlanStepId is not null &&
            sourceBinding.Plan.Equals(currentPlan) &&
            sourceBinding.CurrentPlanStepId == need.PlanStepId &&
            currentPlan.ActorId == need.NpcId &&
            currentPlan.PlanId == need.PlanId &&
            view.CurrentPlan.Goal == currentPlan.Goal &&
            view.CurrentPlan.Steps.SequenceEqual(currentPlan.Steps) &&
            sourceStep is not null &&
            view.CurrentStep == sourceStep &&
            need.ProblemDescriptor is CurrentStepDecisionProblemDescriptor descriptor &&
            descriptor.ActorId == currentPlan.ActorId &&
            descriptor.CurrentGoalId == currentPlan.Goal.GoalId &&
            descriptor.CurrentGoalObjective == currentPlan.Goal.Objective &&
            descriptor.PlanStepId == sourceStep.PlanStepId &&
            descriptor.StepObjective == sourceStep.Objective &&
            descriptor.Target == sourceStep.Target &&
            descriptor.DesiredResult == sourceStep.DesiredResult;
    }

    private static bool MatchesRequestContext(RemotePlannerRequest request, L2PlanningContext context)
    {
        RemotePlannerRequestBinding binding = request.Binding;
        return binding.Role == RemotePlannerRole.StrategicPlanner &&
            binding.ActorId == context.ActorId &&
            binding.NeedId == context.NeedId &&
            binding.Fingerprint == context.Fingerprint &&
            binding.ProblemDescriptorHash == context.ProblemDescriptorHash &&
            binding.CandidateSetId == context.CandidateSetId &&
            binding.SharedContextId == context.SharedContextId &&
            binding.ContextId == context.ContextId &&
            binding.SourcePlanBinding == context.SourcePlanBinding &&
            request.GetModelVisibleBytes().AsSpan().SequenceEqual(context.GetModelVisibleBytes());
    }

    private static IEnumerable<PlanStep> MaterializeSteps(DecisionNeed need, RemotePlannerPlanProposal proposal, string kind)
    {
        for (int index = 0; index < proposal.Steps.Count; index++)
        {
            RemotePlannerPlanStepProposal step = proposal.Steps[index];
            yield return new PlanStep(CreateStepId(need, kind, index), step.Objective, null, step.Target, step.DesiredResult);
        }
    }

    private static bool AreKnownTargets(ActorCognitionView view, RemotePlannerPlanStepProposal step)
    {
        return IsKnownTarget(view, step.Target) && step.DesiredResult switch
        {
            InteractionTargetReached reached => IsKnownTarget(view, reached.TargetRef),
            TargetTerminal terminal => IsKnownTarget(view, terminal.TargetRef),
            _ => true
        };
    }

    private static bool IsKnownTarget(ActorCognitionView view, TargetRef? target) => target is null || view.Knowledge.KnownTargets.TryResolve(target, out _);

    private static bool IsKnownObjectiveItem(ActorCognitionView view, GoalObjective objective) => objective switch
    {
        AcquireItemObjective acquire => IsKnownItem(view, acquire.ItemTypeId),
        MaintainBodyObjective => true,
        _ => false
    };

    private static bool IsKnownResultItem(ActorCognitionView view, ResultPredicate result) => result switch
    {
        InventoryAtLeast inventory => IsKnownItem(view, inventory.ItemTypeId),
        BodyStateWithin or InteractionTargetReached or TargetTerminal => true,
        _ => false
    };

    private static bool MatchesActor(ActorId actorId, ResultPredicate result) => result switch
    {
        InventoryAtLeast inventory => inventory.ActorId == actorId,
        BodyStateWithin body => body.ActorId == actorId,
        InteractionTargetReached reached => reached.ActorId == actorId,
        TargetTerminal terminal => terminal.ActorId == actorId,
        _ => false
    };

    private static bool IsKnownItem(ActorCognitionView view, ItemTypeId itemTypeId)
    {
        foreach (InventoryEntry entry in view.Self.Inventory.Entries)
        {
            if (entry is StackEntry stack && stack.ItemTypeId == itemTypeId) return true;
        }

        foreach (NpcGoal goal in view.ActiveGoals)
        {
            if (goal.Objective is AcquireItemObjective acquire && acquire.ItemTypeId == itemTypeId) return true;
        }

        if (HasPlanItem(view.CurrentPlan.Goal.Objective, itemTypeId)) return true;
        foreach (PlanStep step in view.CurrentPlan.Steps)
        {
            if (HasPlanItem(step.Objective, itemTypeId) || HasResultItem(step.DesiredResult, itemTypeId)) return true;
        }

        foreach (KnownDamageOpportunity opportunity in view.Knowledge.KnownOpportunities.DamageOpportunities)
        {
            if (opportunity.BelievedYields.Any(yieldValue => yieldValue.ItemTypeId == itemTypeId)) return true;
        }

        foreach (KnownConsumptionOpportunity opportunity in view.Knowledge.KnownOpportunities.ConsumptionOpportunities)
        {
            if (opportunity.SourceItemTypeId == itemTypeId) return true;
        }

        foreach (KnownPickupOpportunity opportunity in view.Knowledge.KnownOpportunities.PickupOpportunities)
        {
            if (opportunity.BelievedItems.Any(yieldValue => yieldValue.ItemTypeId == itemTypeId)) return true;
        }

        foreach (KnownResourceYieldOpportunity opportunity in view.Knowledge.KnownOpportunities.ResourceYieldOpportunities)
        {
            if (opportunity.BelievedYields.Any(yieldValue => yieldValue.ItemTypeId == itemTypeId)) return true;
        }

        return false;
    }

    private static bool HasPlanItem(GoalObjective objective, ItemTypeId itemTypeId) => objective is AcquireItemObjective acquire && acquire.ItemTypeId == itemTypeId;
    private static bool HasResultItem(ResultPredicate result, ItemTypeId itemTypeId) => result is InventoryAtLeast inventory && inventory.ItemTypeId == itemTypeId;
    private static GoalId CreateGoalId(DecisionNeed need) => new(Identity(need, "goal"));
    private static PlanId CreatePlanId(DecisionNeed need) => new(Identity(need, "plan"));
    private static GoalId CreateVerifyGoalId(DecisionNeed need) => new(Identity(need, "verify_goal"));
    private static PlanStepId CreateStepId(DecisionNeed need, string kind, int index) => new(Identity(need, kind + "_step_" + index));
    private static string Identity(DecisionNeed need, string suffix) => IdentityNamespace + ":" + need.NeedId.Value + ":" + suffix;
    private static RemotePlannerHostRejected Reject(RemotePlannerHostRejectionReason reason) => new(reason);
}
