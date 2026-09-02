using Alice.Items;

namespace Alice.Actors;

/// <summary>One hand reference to an entry already carried by the same Actor.</summary>
public abstract record HandItemRef
{
    private protected HandItemRef()
    {
    }
}

/// <summary>Hand reference to a carried fungible stack by item type.</summary>
public sealed record StackHandItemRef : HandItemRef
{
    public StackHandItemRef(ItemTypeId itemTypeId)
    {
        ArgumentNullException.ThrowIfNull(itemTypeId);
        ItemTypeId = itemTypeId;
    }

    public ItemTypeId ItemTypeId { get; }
}

/// <summary>Hand reference to a carried durable item by stable instance identity.</summary>
public sealed record InstanceHandItemRef : HandItemRef
{
    public InstanceHandItemRef(ItemInstanceId itemInstanceId)
    {
        ArgumentNullException.ThrowIfNull(itemInstanceId);
        ItemInstanceId = itemInstanceId;
    }

    public ItemInstanceId ItemInstanceId { get; }
}

/// <summary>Immutable one-hand equipment state validated against current carried custody.</summary>
public sealed record EquipmentState
{
    public EquipmentState(ActorId actorId, HandItemRef? handItemRef, int version, InventoryState inventoryState)
    {
        ArgumentNullException.ThrowIfNull(inventoryState);
        ActorIdentity.ValidateActorId(actorId);

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        if (inventoryState.ActorId != actorId)
        {
            throw new ArgumentException("Equipment and inventory must belong to the same actor.", nameof(inventoryState));
        }

        if (!IsCarried(handItemRef, inventoryState.Entries))
        {
            throw new ArgumentException("Hand item reference must identify a carried entry.", nameof(handItemRef));
        }

        ActorId = actorId;
        HandItemRef = handItemRef;
        Version = version;
    }

    public ActorId ActorId { get; }
    public HandItemRef? HandItemRef { get; }
    public int Version { get; }

    private static bool IsCarried(HandItemRef? handItemRef, IReadOnlyList<InventoryEntry> entries)
    {
        if (handItemRef is null)
        {
            return true;
        }

        foreach (InventoryEntry entry in entries)
        {
            if (handItemRef is StackHandItemRef stackReference && entry is StackEntry stackEntry &&
                stackReference.ItemTypeId == stackEntry.ItemTypeId)
            {
                return true;
            }

            if (handItemRef is InstanceHandItemRef instanceReference && entry is InstanceEntry instanceEntry &&
                instanceReference.ItemInstanceId == instanceEntry.ItemInstanceId)
            {
                return true;
            }
        }

        return false;
    }
}
