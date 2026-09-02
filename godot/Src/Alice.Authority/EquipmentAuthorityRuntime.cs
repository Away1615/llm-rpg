using Alice.Actors;
using Alice.Validation;

namespace Alice.Authority;

/// <summary>Accepted immutable evidence of one hand-state replacement.</summary>
public sealed record EquipmentCommitReceipt
{
    internal EquipmentCommitReceipt(
        CommitOrigin.ActorAction origin,
        ActorId actorId,
        EquipmentChangeKind kind,
        int inventoryVersion,
        int previousEquipmentVersion,
        int currentEquipmentVersion,
        HandItemRef? previousHandItemRef,
        HandItemRef? currentHandItemRef)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ActorIdentity.ValidateActorId(actorId);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (inventoryVersion <= 0 || previousEquipmentVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inventoryVersion));
        }

        if (currentEquipmentVersion != checked(previousEquipmentVersion + 1))
        {
            throw new ArgumentOutOfRangeException(nameof(currentEquipmentVersion));
        }

        if ((kind == EquipmentChangeKind.Unequip) != (currentHandItemRef is null))
        {
            throw new ArgumentException(
                "Equipment change kind must match the committed hand state.",
                nameof(currentHandItemRef));
        }

        if (origin.ActorId != actorId)
            throw new ArgumentException("Equipment receipt origin must identify the committed Actor.", nameof(origin));
        Origin = origin;
        ActorId = actorId;
        Kind = kind;
        InventoryVersion = inventoryVersion;
        PreviousEquipmentVersion = previousEquipmentVersion;
        CurrentEquipmentVersion = currentEquipmentVersion;
        PreviousHandItemRef = previousHandItemRef;
        CurrentHandItemRef = currentHandItemRef;
    }

    public ActorId ActorId { get; }
    public CommitOrigin.ActorAction Origin { get; }
    public EquipmentChangeKind Kind { get; }
    public int InventoryVersion { get; }
    public int PreviousEquipmentVersion { get; }
    public int CurrentEquipmentVersion { get; }
    public HandItemRef? PreviousHandItemRef { get; }
    public HandItemRef? CurrentHandItemRef { get; }
}

public sealed class EquipmentCommitResult
{
    internal EquipmentCommitResult(
        EquipmentCommitReceipt? receipt,
        EquipmentValidationFailure? failure)
    {
        Receipt = receipt;
        Failure = failure;
    }

    public bool IsCommitted => Receipt is not null;
    public EquipmentCommitReceipt? Receipt { get; }
    public EquipmentValidationFailure? Failure { get; }
}

/// <summary>Single synchronous Authority owner for one Actor's equipment transition.</summary>
public sealed class EquipmentAuthorityRuntime
{
    private SharedActorState _actorState;

    public EquipmentAuthorityRuntime(SharedActorState initialActorState)
    {
        ArgumentNullException.ThrowIfNull(initialActorState);
        _actorState = initialActorState;
    }

    public SharedActorState CurrentActorState => _actorState;

    public EquipmentCommitResult TryCommit(EquipmentChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EquipmentValidationResult validation = EquipmentActionValidator.Validate(request, _actorState);
        if (!validation.IsValid || validation.ReplacementEquipment is null)
        {
            return new EquipmentCommitResult(null, validation.Failure);
        }

        EquipmentState previousEquipment = _actorState.Equipment;
        EquipmentState replacementEquipment = validation.ReplacementEquipment;
        var replacementActorState = new SharedActorState(
            _actorState.Identity,
            _actorState.Body,
            _actorState.Traversal,
            _actorState.Inventory,
            replacementEquipment);
        var receipt = new EquipmentCommitReceipt(
            request.Origin,
            request.ActorId,
            request.Kind,
            _actorState.Inventory.Version,
            previousEquipment.Version,
            replacementEquipment.Version,
            previousEquipment.HandItemRef,
            replacementEquipment.HandItemRef);

        _actorState = replacementActorState;
        return new EquipmentCommitResult(receipt, null);
    }
}
