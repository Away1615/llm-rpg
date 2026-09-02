using Alice.Activities;
using Alice.Authority;
using Alice.Interaction;
using Alice.Validation;

namespace Alice.Perception;

/// <summary>Redacts one settled Consumption attempt into the fixed actor-visible vocabulary.</summary>
public static class ConsumptionActionOutcomeProjector
{
    public static ConsumptionActionOutcomeReceipt Project(GameActionSpec attemptedAction, GameActionId gameActionId, ConsumptionCommitResult result, SimTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(attemptedAction);
        ArgumentNullException.ThrowIfNull(gameActionId);
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsCommitted)
        {
            ConsumptionCommitReceipt receipt = result.Receipt ?? throw new ArgumentException("Committed result has no receipt.", nameof(result));
            if (result.Failure is not null || receipt.ActorId != attemptedAction.ActorId || receipt.Origin.GameActionId != gameActionId || receipt.ContractRef != attemptedAction.Binding.ContractRef)
            {
                throw new ArgumentException("Committed result cannot be correlated to the attempted action.", nameof(result));
            }

            return new ConsumptionActionOutcomeReceipt(attemptedAction, gameActionId, ConsumptionActionOutcome.Committed, null, observedAt);
        }

        if (result.Receipt is not null || result.Failure is null) throw new ArgumentException("Rejected result is internally inconsistent.", nameof(result));
        return new ConsumptionActionOutcomeReceipt(attemptedAction, gameActionId, ConsumptionActionOutcome.Rejected, MapFailure(result.Failure.Value), observedAt);
    }

    private static ConsumptionActionPerceivedFailure MapFailure(ConsumptionValidationFailure failure)
    {
        return failure switch
        {
            ConsumptionValidationFailure.ActorUnavailable or ConsumptionValidationFailure.ContractUnavailable => ConsumptionActionPerceivedFailure.InteractionUnavailable,
            ConsumptionValidationFailure.StaleContractVersion => ConsumptionActionPerceivedFailure.InteractionChanged,
            ConsumptionValidationFailure.OutOfRange => ConsumptionActionPerceivedFailure.OutOfRange,
            ConsumptionValidationFailure.BindingCapabilityMismatch or ConsumptionValidationFailure.InstrumentNotAllowed or ConsumptionValidationFailure.CapabilitySourceUnavailable or ConsumptionValidationFailure.InsufficientCapability => ConsumptionActionPerceivedFailure.MethodUnavailable,
            ConsumptionValidationFailure.SourceItemMismatch or ConsumptionValidationFailure.ItemDefinitionUnavailable or ConsumptionValidationFailure.ItemNotStackable or ConsumptionValidationFailure.InventoryQuantityUnavailable => ConsumptionActionPerceivedFailure.SourceUnavailable,
            ConsumptionValidationFailure.ActionKindMismatch or ConsumptionValidationFailure.ActorContextMismatch or ConsumptionValidationFailure.InvalidSpatialEvidence or ConsumptionValidationFailure.CheckedNumericOverflow or ConsumptionValidationFailure.DuplicateGameActionId or ConsumptionValidationFailure.CommitConstructionFailed => ConsumptionActionPerceivedFailure.AttemptFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
    }
}
