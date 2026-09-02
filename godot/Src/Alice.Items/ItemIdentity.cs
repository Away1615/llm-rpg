using Alice.Identity;

namespace Alice.Items;

/// <summary>Stable typed identity for one item kind.</summary>
public sealed record ItemTypeId
{
    public ItemTypeId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Item type identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

/// <summary>Stable typed identity for one durable item instance.</summary>
public sealed record ItemInstanceId
{
    public ItemInstanceId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Item instance identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

/// <summary>One carried item entry, closed to stack or durable-instance references.</summary>
public abstract record InventoryEntry
{
    private protected InventoryEntry()
    {
    }
}

/// <summary>One carried fungible stack with the frozen per-stack quantity limit.</summary>
public sealed record StackEntry : InventoryEntry
{
    public StackEntry(ItemTypeId itemTypeId, int quantity)
    {
        ArgumentNullException.ThrowIfNull(itemTypeId);
        if (quantity < 1 || quantity > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        ItemTypeId = itemTypeId;
        Quantity = quantity;
    }

    public ItemTypeId ItemTypeId { get; }
    public int Quantity { get; }
}

/// <summary>One carried stable durable-item instance reference.</summary>
public sealed record InstanceEntry : InventoryEntry
{
    public InstanceEntry(ItemInstanceId itemInstanceId)
    {
        ArgumentNullException.ThrowIfNull(itemInstanceId);
        ItemInstanceId = itemInstanceId;
    }

    public ItemInstanceId ItemInstanceId { get; }
}
