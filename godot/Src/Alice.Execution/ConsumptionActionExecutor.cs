using Alice.Activities;
using Alice.Authority;
using Alice.Interaction;
using Alice.Perception;
using Alice.Validation;

namespace Alice.Execution;

/// <summary>Controller-neutral synchronous execution boundary for one Consumption attempt.</summary>
public static class ConsumptionActionExecutor
{
    public static ConsumptionActionOutcomeReceipt Execute(ConsumptionAuthorityRuntime authority, GameActionSpec action, GameActionId gameActionId, ConsumptionValidationContext context, SimTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(gameActionId);
        ArgumentNullException.ThrowIfNull(context);
        ConsumptionCommitResult result = authority.TryCommitConsumption(action, gameActionId, context);
        return ConsumptionActionOutcomeProjector.Project(action, gameActionId, result, observedAt);
    }
}
