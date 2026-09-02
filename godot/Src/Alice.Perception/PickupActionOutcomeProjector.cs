using Alice.Activities;
using Alice.Authority;
using Alice.Interaction;
using Alice.Validation;

namespace Alice.Perception;

/// <summary>Redacts one settled Pickup attempt into the fixed actor-visible vocabulary.</summary>
public static class PickupActionOutcomeProjector
{
    public static PickupActionOutcomeReceipt Project(GameActionSpec attemptedAction, GameActionId gameActionId, PickupCommitResult result, SimTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(attemptedAction);
        ArgumentNullException.ThrowIfNull(gameActionId);
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsCommitted)
        {
            PickupCommitReceipt receipt = result.Receipt ?? throw new ArgumentException("Committed result has no receipt.", nameof(result));
            if (result.Failure is not null || receipt.ActorId != attemptedAction.ActorId || receipt.Origin.GameActionId != gameActionId || receipt.ContractRef != attemptedAction.Binding.ContractRef)
            {
                throw new ArgumentException("Committed result cannot be correlated to the attempted action.", nameof(result));
            }

            return new PickupActionOutcomeReceipt(attemptedAction, gameActionId, PickupActionOutcome.Committed, null, observedAt);
        }

        if (result.Receipt is not null || result.Failure is null)
        {
            throw new ArgumentException("Rejected result is internally inconsistent.", nameof(result));
        }

        return new PickupActionOutcomeReceipt(attemptedAction, gameActionId, PickupActionOutcome.Rejected, MapFailure(result.Failure.Value), observedAt);
    }

    private static PickupActionPerceivedFailure MapFailure(PickupValidationFailure failure)
    {
        return failure switch
        {
            PickupValidationFailure.ActorUnavailable or PickupValidationFailure.DropUnavailable or PickupValidationFailure.ContractUnavailable => PickupActionPerceivedFailure.InteractionUnavailable,
            PickupValidationFailure.DropIdentityMismatch or PickupValidationFailure.StaleContractVersion or PickupValidationFailure.ClaimantMismatch => PickupActionPerceivedFailure.InteractionChanged,
            PickupValidationFailure.OutOfRange => PickupActionPerceivedFailure.OutOfRange,
            PickupValidationFailure.BindingCapabilityMismatch or PickupValidationFailure.InstrumentNotAllowed or PickupValidationFailure.CapabilitySourceUnavailable or PickupValidationFailure.InsufficientCapability => PickupActionPerceivedFailure.MethodUnavailable,
            PickupValidationFailure.InventoryFull => PickupActionPerceivedFailure.CapacityUnavailable,
            PickupValidationFailure.ItemDefinitionUnavailable or PickupValidationFailure.ItemNotStackable => PickupActionPerceivedFailure.SourceUnavailable,
            PickupValidationFailure.ActionKindMismatch or PickupValidationFailure.ActorContextMismatch or PickupValidationFailure.CheckedNumericOverflow => PickupActionPerceivedFailure.AttemptFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
    }
}
