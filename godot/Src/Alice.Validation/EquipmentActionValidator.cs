using Alice.Actors;

namespace Alice.Validation;

public enum EquipmentValidationFailure
{
    ActorMismatch,
    StaleInventoryVersion,
    StaleEquipmentVersion,
    HandItemNotCarried,
    CheckedNumericOverflow
}

/// <summary>Fail-closed result of validating one requested hand-state replacement.</summary>
public sealed class EquipmentValidationResult
{
    private EquipmentValidationResult(
        EquipmentState? replacementEquipment,
        EquipmentValidationFailure? failure)
    {
        ReplacementEquipment = replacementEquipment;
        Failure = failure;
    }

    public bool IsValid => ReplacementEquipment is not null;
    public EquipmentValidationFailure? Failure { get; }
    internal EquipmentState? ReplacementEquipment { get; }

    internal static EquipmentValidationResult Accepted(EquipmentState replacementEquipment)
    {
        ArgumentNullException.ThrowIfNull(replacementEquipment);
        return new EquipmentValidationResult(replacementEquipment, null);
    }

    internal static EquipmentValidationResult Rejected(EquipmentValidationFailure failure)
    {
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        return new EquipmentValidationResult(null, failure);
    }
}

/// <summary>Validates actor ownership, both expected versions and carried custody.</summary>
public static class EquipmentActionValidator
{
    public static EquipmentValidationResult Validate(
        EquipmentChangeRequest request,
        SharedActorState actorState)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actorState);

        if (request.ActorId != actorState.Identity.ActorId)
        {
            return EquipmentValidationResult.Rejected(EquipmentValidationFailure.ActorMismatch);
        }

        if (request.ExpectedInventoryVersion != actorState.Inventory.Version)
        {
            return EquipmentValidationResult.Rejected(EquipmentValidationFailure.StaleInventoryVersion);
        }

        if (request.ExpectedEquipmentVersion != actorState.Equipment.Version)
        {
            return EquipmentValidationResult.Rejected(EquipmentValidationFailure.StaleEquipmentVersion);
        }

        int replacementVersion;
        try
        {
            replacementVersion = checked(actorState.Equipment.Version + 1);
        }
        catch (OverflowException)
        {
            return EquipmentValidationResult.Rejected(EquipmentValidationFailure.CheckedNumericOverflow);
        }

        try
        {
            var replacement = new EquipmentState(
                request.ActorId,
                request.HandItemRef,
                replacementVersion,
                actorState.Inventory);
            return EquipmentValidationResult.Accepted(replacement);
        }
        catch (ArgumentException)
        {
            return EquipmentValidationResult.Rejected(EquipmentValidationFailure.HandItemNotCarried);
        }
    }
}
