using Alice.Authority;

namespace Alice.Actors;

public enum EquipmentChangeKind
{
    Equip,
    Unequip
}

/// <summary>Typed optimistic-concurrency request to replace one Actor's hand state.</summary>
public sealed record EquipmentChangeRequest
{
    public EquipmentChangeRequest(
        CommitOrigin.ActorAction origin,
        ActorId actorId,
        int expectedInventoryVersion,
        int expectedEquipmentVersion,
        HandItemRef? handItemRef)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ActorIdentity.ValidateActorId(actorId);
        if (origin.ActorId != actorId)
            throw new ArgumentException("Equipment commit origin must identify the request Actor.", nameof(origin));
        if (expectedInventoryVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedInventoryVersion));
        }

        if (expectedEquipmentVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedEquipmentVersion));
        }

        Origin = origin;
        ActorId = actorId;
        ExpectedInventoryVersion = expectedInventoryVersion;
        ExpectedEquipmentVersion = expectedEquipmentVersion;
        HandItemRef = handItemRef;
    }

    public ActorId ActorId { get; }
    public CommitOrigin.ActorAction Origin { get; }
    public int ExpectedInventoryVersion { get; }
    public int ExpectedEquipmentVersion { get; }
    public HandItemRef? HandItemRef { get; }
    public EquipmentChangeKind Kind => HandItemRef is null
        ? EquipmentChangeKind.Unequip
        : EquipmentChangeKind.Equip;
}
