using Alice.Npc;
using Alice.ProductRuntime;
using Alice.Social;

namespace Alice.Cognition;

public enum DecisionNeedResolutionKind
{
    CreatePlan,
    RevisePlan,
    Verify,
    Defer,
    Cancel,
    Respond,
    ExecuteAction
}

public abstract record DecisionNeedResultReference
{
    private protected DecisionNeedResultReference()
    {
    }
}

public sealed record DecisionNeedPlanResultReference : DecisionNeedResultReference
{
    public DecisionNeedPlanResultReference(PlanId planId)
    {
        ArgumentNullException.ThrowIfNull(planId);
        PlanId = planId;
    }

    public PlanId PlanId { get; }
}

public sealed record DecisionNeedGoalResultReference : DecisionNeedResultReference
{
    public DecisionNeedGoalResultReference(GoalId goalId)
    {
        ArgumentNullException.ThrowIfNull(goalId);
        GoalId = goalId;
    }

    public GoalId GoalId { get; }
}

public sealed record DecisionNeedSemanticActResultReference : DecisionNeedResultReference
{
    public DecisionNeedSemanticActResultReference(SemanticDialogueActId actId)
    {
        ArgumentNullException.ThrowIfNull(actId);
        ActId = actId;
    }

    public SemanticDialogueActId ActId { get; }
}

public sealed record DecisionNeedExecutionResultReference : DecisionNeedResultReference
{
    public DecisionNeedExecutionResultReference(ActorExecutionId executionId)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        ExecutionId = executionId;
    }

    public ActorExecutionId ExecutionId { get; }
}
