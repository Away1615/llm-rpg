using Alice.Activities;
using Alice.Actors;
using Alice.Commitments;
using Alice.Cognition;
using Alice.Items;
using Alice.ModelRuntime;
using Alice.Npc;
using Alice.World;

namespace Alice.Cognition
{

/// <summary>Validates and atomically settles one first-plan strategic decision without a source runtime.</summary>
public static class PlanlessStrategicRemotePlannerSettlement
{
    private const string IdentityNamespace = "alice.remote_planless_strategic_host.v1";

    public static RemotePlannerHostSettlementOutcome Settle(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorDecisionView view,
        L2PlanlessStrategicContext context,
        RemotePlannerRequest request,
        RemotePlannerResponse response,
        NpcPlanningState currentPlanning,
        SimTime resolvedAt)
    {
        if (store is null || need is null || view is null || context is null
            || request is null || response is null || currentPlanning is null)
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

        if (!store.IsCurrentPlanlessStrategic(need))
        {
            return Reject(RemotePlannerHostRejectionReason.StaleStoreEntry);
        }

        if (!MatchesContext(need, view, currentPlanning, context))
        {
            return Reject(view.ActorId != need.NpcId
                ? RemotePlannerHostRejectionReason.ActorViewMismatch
                : RemotePlannerHostRejectionReason.PlanningStateMismatch);
        }

        if (!MatchesRequest(request, context))
        {
            return Reject(RemotePlannerHostRejectionReason.RequestBindingMismatch);
        }

        if (!ReferenceEquals(response.Binding, request.Binding))
        {
            return Reject(RemotePlannerHostRejectionReason.ResponseBindingMismatch);
        }

        if (response.Decision is RemotePlannerFailure)
        {
            return Reject(RemotePlannerHostRejectionReason.ResponseFailure);
        }

        return response.Decision switch
        {
            RemotePlannerPlanlessCreatePlan create => SettleCreate(need, view, currentPlanning, create.Proposal, resolvedAt),
            RemotePlannerVerify => SettleVerify(need, currentPlanning, resolvedAt),
            RemotePlannerDefer => SettleDefer(need, resolvedAt),
            _ => Reject(RemotePlannerHostRejectionReason.InvalidProposal)
        };
    }

    private static RemotePlannerHostSettlementOutcome SettleCreate(
        DecisionNeed need,
        ActorDecisionView view,
        NpcPlanningState planning,
        RemotePlannerPlanlessPlanProposal proposal,
        SimTime resolvedAt)
    {
        NpcGoal? selected = ResolveSelectedGoal(need, view, planning, proposal.GoalId);
        if (selected is null)
        {
            return Reject(RemotePlannerHostRejectionReason.UnknownGoal);
        }

        RemotePlannerHostRejectionReason? validation = ValidateSteps(view, proposal.Steps);
        if (validation is not null)
        {
            return Reject(validation.Value);
        }

        try
        {
            NpcPlan plan = new(
                new PlanId(Identity(need, "plan")),
                need.NpcId,
                selected,
                1,
                MaterializeSteps(need, proposal.Steps));
            var replacement = new NpcPlanningState(planning.ActiveGoals, plan);
            var runtime = new PlanRuntime(plan);
            need.Resolve(
                resolvedAt,
                DecisionNeedResolutionKind.CreatePlan,
                new DecisionNeedPlanResultReference(plan.PlanId));
            return new RemotePlannerHostPlanlessCreatePlanAccepted(plan, replacement, runtime);
        }
        catch (ArgumentException)
        {
            return Reject(RemotePlannerHostRejectionReason.InvalidProposal);
        }
    }

    private static RemotePlannerHostSettlementOutcome SettleVerify(
        DecisionNeed need,
        NpcPlanningState planning,
        SimTime resolvedAt)
    {
        var goalId = new GoalId(Identity(need, "verify_goal"));
        if (planning.ActiveGoals.Any(goal => goal.GoalId == goalId))
        {
            return Reject(RemotePlannerHostRejectionReason.IdentityCollision);
        }

        try
        {
            var goal = new NpcGoal(
                goalId,
                new KnowObjective(KnowledgeFactRef.ForProblemDescriptor(need.ProblemDescriptor.DescriptorHash)));
            var replacement = new NpcPlanningState([.. planning.ActiveGoals, goal], null);
            need.Resolve(
                resolvedAt,
                DecisionNeedResolutionKind.Verify,
                new DecisionNeedGoalResultReference(goal.GoalId));
            return new RemotePlannerHostPlanlessVerifyAccepted(goal, replacement);
        }
        catch (ArgumentException)
        {
            return Reject(RemotePlannerHostRejectionReason.InvalidProposal);
        }
    }

    private static RemotePlannerHostSettlementOutcome SettleDefer(DecisionNeed need, SimTime resolvedAt)
    {
        need.Resolve(resolvedAt, DecisionNeedResolutionKind.Defer, null);
        return new RemotePlannerHostNoArtifactAccepted(DecisionNeedResolutionKind.Defer);
    }

    private static bool MatchesContext(
        DecisionNeed need,
        ActorDecisionView view,
        NpcPlanningState planning,
        L2PlanlessStrategicContext context)
    {
        return need.PlanId is null
            && need.PlanStepId is null
            && need.ProblemDescriptor is PlanlessStrategicDecisionProblemDescriptor descriptor
            && context.ActorId == need.NpcId
            && context.NeedId == need.NeedId
            && context.Fingerprint == need.Fingerprint
            && context.ProblemDescriptorHash == descriptor.DescriptorHash
            && context.AttemptCount == need.AttemptCount
            && view.Equals(context.ActorView)
            && planning.Equals(context.PlanningSnapshot)
            && view.CurrentPlan is null
            && view.CurrentStep is null
            && planning.CurrentPlan is null
            && view.ActiveGoals.Count > 0
            && view.ActiveGoals.SequenceEqual(planning.ActiveGoals)
            && view.ActiveGoals.SequenceEqual(descriptor.ActiveGoals);
    }

    private static bool MatchesRequest(RemotePlannerRequest request, L2PlanlessStrategicContext context)
    {
        RemotePlannerRequestBinding binding = request.Binding;
        if (binding.Kind != RemotePlannerRequestKind.PlanlessStrategic)
        {
            return false;
        }

        RemotePlanlessStrategicRequestBinding planless = binding.PlanlessStrategicBinding;
        return binding.Role == RemotePlannerRole.PlanlessStrategicPlanner
            && binding.ActorId == context.ActorId
            && binding.NeedId == context.NeedId
            && binding.Fingerprint == context.Fingerprint
            && binding.ProblemDescriptorHash == context.ProblemDescriptorHash
            && binding.CandidateSetId == context.CandidateSetId
            && planless.SharedContextId == context.SharedContextId
            && planless.ContextId == context.ContextId
            && planless.AttemptCount == context.AttemptCount
            && request.ProtocolVersion == RemotePlanlessStrategicProtocol.ProtocolVersion
            && request.GetModelVisibleBytes().AsSpan().SequenceEqual(context.GetModelVisibleBytes())
            && request.GetToolCatalogueUtf8().AsSpan().SequenceEqual(RemotePlanlessStrategicProtocol.GetToolCatalogueUtf8());
    }

    private static NpcGoal? ResolveSelectedGoal(
        DecisionNeed need,
        ActorDecisionView view,
        NpcPlanningState planning,
        GoalId selectedGoalId)
    {
        var descriptor = (PlanlessStrategicDecisionProblemDescriptor)need.ProblemDescriptor;
        NpcGoal? descriptorGoal = ExactGoal(descriptor.ActiveGoals, selectedGoalId);
        NpcGoal? viewGoal = ExactGoal(view.ActiveGoals, selectedGoalId);
        NpcGoal? planningGoal = ExactGoal(planning.ActiveGoals, selectedGoalId);
        return descriptorGoal is not null && descriptorGoal == viewGoal && descriptorGoal == planningGoal
            ? planningGoal
            : null;
    }

    private static NpcGoal? ExactGoal(IReadOnlyList<NpcGoal> goals, GoalId goalId)
    {
        NpcGoal[] matches = goals.Where(goal => goal.GoalId == goalId).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static RemotePlannerHostRejectionReason? ValidateSteps(
        ActorDecisionView view,
        IReadOnlyList<RemotePlannerPlanStepProposal> steps)
    {
        if (steps.Count == 0)
        {
            return RemotePlannerHostRejectionReason.InvalidProposal;
        }

        foreach (RemotePlannerPlanStepProposal step in steps)
        {
            if (step is null || !MatchesActor(view.ActorId, step.DesiredResult))
            {
                return RemotePlannerHostRejectionReason.InvalidProposal;
            }

            if (!IsKnownNonTargetObjective(view, step.Objective)
                || !IsKnownNonTargetResult(view, step.DesiredResult))
            {
                return step.Objective is AcquireItemObjective || step.DesiredResult is InventoryAtLeast
                    ? RemotePlannerHostRejectionReason.UnknownItem
                    : RemotePlannerHostRejectionReason.InvalidProposal;
            }

            if (!IsKnownTarget(view, step.Target)
                || step.Objective is ReachTargetObjective reach && !IsKnownTarget(view, reach.TargetRef)
                || step.DesiredResult is InteractionTargetReached reached && !IsKnownTarget(view, reached.TargetRef)
                || step.DesiredResult is TargetTerminal terminal && !IsKnownTarget(view, terminal.TargetRef))
            {
                return RemotePlannerHostRejectionReason.UnknownTarget;
            }

            try
            {
                _ = new PlanStep(new PlanStepId("validation"), step.Objective, null, step.Target, step.DesiredResult);
            }
            catch (ArgumentException)
            {
                return RemotePlannerHostRejectionReason.InvalidProposal;
            }
        }

        return null;
    }

    private static bool IsKnownNonTargetObjective(ActorDecisionView view, GoalObjective objective) => objective switch
    {
        AcquireItemObjective acquire => IsKnownItem(view, acquire.ItemTypeId),
        MaintainBodyObjective => true,
        ReachTargetObjective => true,
        FulfillCommitmentObjective fulfill => view.ActiveGoals.Any(
            goal => goal.Objective is FulfillCommitmentObjective visible
                && visible.CommitmentId == fulfill.CommitmentId),
        ExperienceObjective experience => view.ActiveGoals.Any(
            goal => goal.Objective is ExperienceObjective visible
                && visible.ExperienceId == experience.ExperienceId),
        _ => false
    };

    private static bool IsKnownNonTargetResult(ActorDecisionView view, ResultPredicate result) => result switch
    {
        InventoryAtLeast inventory => IsKnownItem(view, inventory.ItemTypeId),
        BodyStateWithin => true,
        InteractionTargetReached => true,
        TargetTerminal => true,
        CommitmentStatusMatches commitment => view.ActiveGoals.Any(
            goal => goal.Objective is FulfillCommitmentObjective visible
                && visible.CommitmentId == commitment.CommitmentId),
        ExperienceCompleted experience => view.ActiveGoals.Any(
            goal => goal.Objective is ExperienceObjective visible
                && visible.ExperienceId == experience.ExperienceId),
        _ => false
    };

    private static bool MatchesActor(ActorId actorId, ResultPredicate result) => result switch
    {
        InventoryAtLeast inventory => inventory.ActorId == actorId,
        BodyStateWithin body => body.ActorId == actorId,
        InteractionTargetReached reached => reached.ActorId == actorId,
        TargetTerminal terminal => terminal.ActorId == actorId,
        CommitmentStatusMatches commitment => commitment.Debtor == actorId,
        ExperienceCompleted experience => experience.ActorId == actorId,
        _ => false
    };

    private static bool IsKnownTarget(ActorDecisionView view, TargetRef? target) =>
        target is null || view.Knowledge.KnownTargets.TryResolve(target, out _);

    private static bool IsKnownItem(ActorDecisionView view, ItemTypeId itemTypeId)
    {
        if (view.Self.Inventory.Entries.Any(
            entry => entry is StackEntry stack && stack.ItemTypeId == itemTypeId))
        {
            return true;
        }

        if (view.ActiveGoals.Any(
            goal => goal.Objective is AcquireItemObjective acquire && acquire.ItemTypeId == itemTypeId))
        {
            return true;
        }

        return view.Knowledge.KnownOpportunities.DamageOpportunities.Any(
                opportunity => opportunity.BelievedYields.Any(value => value.ItemTypeId == itemTypeId))
            || view.Knowledge.KnownOpportunities.ConsumptionOpportunities.Any(
                opportunity => opportunity.SourceItemTypeId == itemTypeId)
            || view.Knowledge.KnownOpportunities.PickupOpportunities.Any(
                opportunity => opportunity.BelievedItems.Any(value => value.ItemTypeId == itemTypeId));
    }

    private static IEnumerable<PlanStep> MaterializeSteps(
        DecisionNeed need,
        IReadOnlyList<RemotePlannerPlanStepProposal> steps)
    {
        for (int index = 0; index < steps.Count; index++)
        {
            RemotePlannerPlanStepProposal step = steps[index];
            yield return new PlanStep(
                new PlanStepId(Identity(need, "create_step_" + index)),
                step.Objective,
                null,
                step.Target,
                step.DesiredResult);
        }
    }

    private static string Identity(DecisionNeed need, string suffix) =>
        IdentityNamespace + ":" + need.NeedId.Value + ":" + need.AttemptCount + ":" + suffix;

    private static RemotePlannerHostRejected Reject(RemotePlannerHostRejectionReason reason) => new(reason);
}
}

namespace Alice.ModelRuntime
{
public sealed partial class RemotePlannerInvocationSession
{
    public ValueTask<RemotePlannerInvocationResult> PollAndSettlePlanlessStrategicAsync(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorDecisionView view,
        L2PlanlessStrategicContext context,
        NpcPlanningState currentPlanning,
        SimTime resolvedAt)
    {
        lock (_sync)
        {
            if (_terminalResult is not null)
            {
                return ValueTask.FromResult(_terminalResult);
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(need);
            ArgumentNullException.ThrowIfNull(view);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(currentPlanning);
            ObserveCancellation();
            if (_terminalResult is not null)
            {
                return ValueTask.FromResult(_terminalResult);
            }

            if (!_operation.IsCompleted)
            {
                return ValueTask.FromResult(RemotePlannerInvocationResult.InFlight());
            }

            if (!TryClaimSettlement())
            {
                return ValueTask.FromResult(StoreTerminal(RemotePlannerInvocationResult.Cancelled()));
            }

            ModelClientResult<RemotePlannerResponse> clientResult;
            try
            {
                clientResult = _operation.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                return ValueTask.FromResult(StoreTerminal(RemotePlannerInvocationResult.ClientFaulted()));
            }

            if (clientResult.Status == ModelClientResultStatus.Unavailable)
            {
                return ValueTask.FromResult(StoreTerminal(RemotePlannerInvocationResult.Unavailable(
                    clientResult.Mode,
                    clientResult.UnavailableReason!.Value)));
            }

            RemotePlannerHostSettlementOutcome settlement = RemotePlannerHostSettlement.SettlePlanlessStrategic(
                store,
                need,
                view,
                context,
                _request,
                clientResult.Output!,
                currentPlanning,
                resolvedAt);
            return ValueTask.FromResult(StoreTerminal(RemotePlannerInvocationResult.Settled(
                clientResult.Mode,
                clientResult.ExecutionEvidence!,
                settlement,
                _request.Binding)));
        }
    }
}
}
