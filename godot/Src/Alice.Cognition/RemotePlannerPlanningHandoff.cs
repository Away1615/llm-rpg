using Alice.ModelRuntime;
using Alice.Npc;

namespace Alice.Cognition;

public enum RemotePlannerPlanningHandoffRejectionReason
{
    InvalidInput,
    InvocationNotTerminal,
    InvocationSettlementMissing,
    NeedNotResolved,
    NeedResolutionMismatch,
    CurrentPlanningMismatch,
    CurrentRuntimeMismatch,
    NeedContextMismatch,
    SettlementMismatch,
    GoalCollision,
    InvalidArtifact
}

public abstract record RemotePlannerPlanningHandoffResult
{
    private protected RemotePlannerPlanningHandoffResult()
    {
    }
}

public sealed record RemotePlannerPlanningHandoffCreatePlanInstalled(
    NpcPlanningState Planning,
    PlanRuntime Runtime) : RemotePlannerPlanningHandoffResult;

public sealed record RemotePlannerPlanningHandoffRevisePlanInstalled(
    NpcPlanningState Planning,
    PlanRuntime Runtime) : RemotePlannerPlanningHandoffResult;

public sealed record RemotePlannerPlanningHandoffVerifyInstalled(
    NpcPlanningState Planning,
    PlanRuntime Runtime) : RemotePlannerPlanningHandoffResult;

public sealed record RemotePlannerPlanningHandoffDeferred : RemotePlannerPlanningHandoffResult;

public sealed record RemotePlannerPlanningHandoffCancelled : RemotePlannerPlanningHandoffResult;

public sealed record RemotePlannerPlanningHandoffHostRejected(
    RemotePlannerHostRejectionReason Reason) : RemotePlannerPlanningHandoffResult;

public sealed record RemotePlannerPlanningHandoffUnavailable(
    ModelClientUnavailableReason Reason) : RemotePlannerPlanningHandoffResult;

public sealed record RemotePlannerPlanningHandoffInvocationCancelled : RemotePlannerPlanningHandoffResult;

public sealed record RemotePlannerPlanningHandoffClientFaulted : RemotePlannerPlanningHandoffResult;

public sealed record RemotePlannerPlanningHandoffRejected(
    RemotePlannerPlanningHandoffRejectionReason Reason) : RemotePlannerPlanningHandoffResult;

/// <summary>Host-only atomic bridge from a settled Remote Planner result to one NPC planning snapshot.</summary>
public static class RemotePlannerPlanningHandoff
{
    public static RemotePlannerPlanningHandoffResult HandOff(
        DecisionNeed need,
        RemotePlannerInvocationResult invocation,
        NpcPlanningState currentPlanning,
        PlanRuntime currentRuntime)
    {
        if (need is null || invocation is null || currentPlanning is null || currentRuntime is null)
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.InvalidInput);
        }

        if (!invocation.IsTerminal)
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.InvocationNotTerminal);
        }

        switch (invocation.State)
        {
            case RemotePlannerInvocationState.Unavailable:
                return invocation.UnavailableReason is ModelClientUnavailableReason unavailableReason
                    ? new RemotePlannerPlanningHandoffUnavailable(unavailableReason)
                    : Reject(RemotePlannerPlanningHandoffRejectionReason.InvalidInput);
            case RemotePlannerInvocationState.Cancelled:
                return new RemotePlannerPlanningHandoffInvocationCancelled();
            case RemotePlannerInvocationState.ClientFaulted:
                return new RemotePlannerPlanningHandoffClientFaulted();
            case RemotePlannerInvocationState.Settled:
                break;
            default:
                return Reject(RemotePlannerPlanningHandoffRejectionReason.InvalidInput);
        }

        if (invocation.Settlement is null)
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.InvocationSettlementMissing);
        }

        return HandOffSettled(
            need,
            invocation.Settlement,
            invocation.RequestBinding!,
            currentPlanning,
            currentRuntime);
    }

    /// <summary>Atomic handoff for a Host settlement already owned by an outer queue/session.</summary>
    public static RemotePlannerPlanningHandoffResult HandOffSettled(
        DecisionNeed need,
        RemotePlannerHostSettlementOutcome settlement,
        RemotePlannerRequestBinding requestBinding,
        NpcPlanningState currentPlanning,
        PlanRuntime currentRuntime)
    {
        if (need is null || settlement is null || requestBinding is null || currentPlanning is null || currentRuntime is null)
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.InvalidInput);
        }

        if (settlement is RemotePlannerHostRejected hostRejected)
        {
            return new RemotePlannerPlanningHandoffHostRejected(hostRejected.Reason);
        }

        if (need.State != DecisionNeedState.Resolved || need.ResolvedAt is null || need.ResolutionKind is null)
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.NeedNotResolved);
        }

        if (!MatchesNeedBinding(need, requestBinding))
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.NeedContextMismatch);
        }

        if (!MatchesCurrentPlanning(currentPlanning, requestBinding.SourcePlanBinding))
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.CurrentPlanningMismatch);
        }

        if (!MatchesCurrentRuntime(currentRuntime, requestBinding.SourcePlanBinding))
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.CurrentRuntimeMismatch);
        }

        return settlement switch
        {
            RemotePlannerHostCreatePlanAccepted create => InstallCreate(need, currentPlanning, currentRuntime, create.Plan),
            RemotePlannerHostRevisePlanAccepted revise => InstallRevise(need, currentPlanning, currentRuntime, revise.Plan),
            RemotePlannerHostVerifyAccepted verify => InstallVerify(need, currentPlanning, currentRuntime, verify.Goal),
            RemotePlannerHostNoArtifactAccepted noArtifact => PreserveNoArtifact(need, noArtifact),
            _ => Reject(RemotePlannerPlanningHandoffRejectionReason.SettlementMismatch)
        };
    }

    private static RemotePlannerPlanningHandoffResult InstallCreate(
        DecisionNeed need,
        NpcPlanningState currentPlanning,
        PlanRuntime currentRuntime,
        NpcPlan plan)
    {
        if (!MatchesPlanResolution(need, DecisionNeedResolutionKind.CreatePlan, plan) || plan.ActorId != need.NpcId)
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.NeedResolutionMismatch);
        }

        if (HasGoalId(currentPlanning, plan.Goal.GoalId))
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.GoalCollision);
        }

        try
        {
            var planning = new NpcPlanningState(currentPlanning.ActiveGoals.Append(plan.Goal), plan);
            var runtime = new PlanRuntime(plan);
            var result = new RemotePlannerPlanningHandoffCreatePlanInstalled(planning, runtime);
            currentRuntime.Supersede();
            return result;
        }
        catch (ArgumentException)
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.InvalidArtifact);
        }
    }

    private static RemotePlannerPlanningHandoffResult InstallRevise(
        DecisionNeed need,
        NpcPlanningState currentPlanning,
        PlanRuntime currentRuntime,
        NpcPlan plan)
    {
        NpcPlan currentPlan = currentPlanning.CurrentPlan!;
        if (!MatchesPlanResolution(need, DecisionNeedResolutionKind.RevisePlan, plan) ||
            plan.PlanId != currentPlan.PlanId || plan.ActorId != currentPlan.ActorId ||
            plan.Goal != currentPlan.Goal || plan.Revision <= currentPlan.Revision)
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.NeedResolutionMismatch);
        }

        try
        {
            var planning = new NpcPlanningState(currentPlanning.ActiveGoals, plan);
            var runtime = new PlanRuntime(plan);
            var result = new RemotePlannerPlanningHandoffRevisePlanInstalled(planning, runtime);
            currentRuntime.Supersede();
            return result;
        }
        catch (ArgumentException)
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.InvalidArtifact);
        }
    }

    private static RemotePlannerPlanningHandoffResult InstallVerify(
        DecisionNeed need,
        NpcPlanningState currentPlanning,
        PlanRuntime currentRuntime,
        NpcGoal goal)
    {
        if (!MatchesGoalResolution(need, goal) || goal.Objective is not KnowObjective)
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.NeedResolutionMismatch);
        }

        if (HasGoalId(currentPlanning, goal.GoalId))
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.GoalCollision);
        }

        try
        {
            var planning = new NpcPlanningState(currentPlanning.ActiveGoals.Append(goal), currentPlanning.CurrentPlan);
            return new RemotePlannerPlanningHandoffVerifyInstalled(planning, currentRuntime);
        }
        catch (ArgumentException)
        {
            return Reject(RemotePlannerPlanningHandoffRejectionReason.InvalidArtifact);
        }
    }

    private static RemotePlannerPlanningHandoffResult PreserveNoArtifact(
        DecisionNeed need,
        RemotePlannerHostNoArtifactAccepted settlement)
    {
        if (settlement.ResolutionKind is DecisionNeedResolutionKind.Defer && MatchesNoArtifactResolution(need, settlement.ResolutionKind))
        {
            return new RemotePlannerPlanningHandoffDeferred();
        }

        if (settlement.ResolutionKind is DecisionNeedResolutionKind.Cancel && MatchesNoArtifactResolution(need, settlement.ResolutionKind))
        {
            return new RemotePlannerPlanningHandoffCancelled();
        }

        return Reject(RemotePlannerPlanningHandoffRejectionReason.NeedResolutionMismatch);
    }

    private static bool MatchesNeedBinding(DecisionNeed need, RemotePlannerRequestBinding binding)
    {
        if (binding.SourcePlanBinding is not L2SourcePlanBinding source)
        {
            return false;
        }

        NpcPlan plan = source.Plan;
        PlanStep? step = plan.Steps.FirstOrDefault(
            candidate => candidate.PlanStepId == source.CurrentPlanStepId);
        return binding.Role == RemotePlannerRole.StrategicPlanner &&
            binding.ActorId == need.NpcId &&
            binding.NeedId == need.NeedId &&
            binding.Fingerprint == need.Fingerprint &&
            binding.ProblemDescriptorHash == need.ProblemDescriptor.DescriptorHash &&
            plan.ActorId == need.NpcId &&
            plan.PlanId == need.PlanId &&
            source.CurrentPlanStepId == need.PlanStepId &&
            step is not null &&
            need.ProblemDescriptor is CurrentStepDecisionProblemDescriptor descriptor &&
            descriptor.ActorId == plan.ActorId &&
            descriptor.CurrentGoalId == plan.Goal.GoalId &&
            descriptor.CurrentGoalObjective == plan.Goal.Objective &&
            descriptor.PlanStepId == step.PlanStepId &&
            descriptor.StepObjective == step.Objective &&
            descriptor.Target == step.Target &&
            descriptor.DesiredResult == step.DesiredResult;
    }

    private static bool MatchesCurrentPlanning(
        NpcPlanningState planning,
        L2SourcePlanBinding source)
    {
        return planning.CurrentPlan is NpcPlan plan && plan.Equals(source.Plan);
    }

    private static bool MatchesCurrentRuntime(
        PlanRuntime runtime,
        L2SourcePlanBinding source)
    {
        NpcPlan plan = source.Plan;
        int currentIndex = runtime.CurrentStepIndex;
        if (!runtime.OwnsPlan(plan) ||
            currentIndex < 0 || currentIndex >= plan.Steps.Count ||
            currentIndex >= runtime.Steps.Count ||
            runtime.Steps.Count != plan.Steps.Count ||
            plan.Steps[currentIndex].PlanStepId != source.CurrentPlanStepId ||
            runtime.Steps[currentIndex].PlanStepId != source.CurrentPlanStepId)
        {
            return false;
        }

        return runtime.Status switch
        {
            PlanRuntimeStatus.Active => runtime.Steps[currentIndex].Status == PlanStepRuntimeStatus.InProgress,
            PlanRuntimeStatus.Suspended => runtime.Steps[currentIndex].Status == PlanStepRuntimeStatus.Interrupted,
            _ => false
        };
    }

    private static bool MatchesPlanResolution(DecisionNeed need, DecisionNeedResolutionKind kind, NpcPlan plan)
    {
        return need.ResolutionKind == kind &&
            need.ResultingRef is DecisionNeedPlanResultReference reference &&
            reference.PlanId == plan.PlanId;
    }

    private static bool MatchesGoalResolution(DecisionNeed need, NpcGoal goal)
    {
        return need.ResolutionKind == DecisionNeedResolutionKind.Verify &&
            need.ResultingRef is DecisionNeedGoalResultReference reference &&
            reference.GoalId == goal.GoalId;
    }

    private static bool MatchesNoArtifactResolution(DecisionNeed need, DecisionNeedResolutionKind kind)
    {
        return need.ResolutionKind == kind && need.ResultingRef is null;
    }

    private static bool HasGoalId(NpcPlanningState planning, GoalId goalId)
    {
        return planning.ActiveGoals.Any(goal => goal.GoalId == goalId);
    }

    private static RemotePlannerPlanningHandoffRejected Reject(RemotePlannerPlanningHandoffRejectionReason reason)
    {
        return new RemotePlannerPlanningHandoffRejected(reason);
    }
}
