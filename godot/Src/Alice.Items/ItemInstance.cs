namespace Alice.Items;

/// <summary>Immutable state snapshot for one stable item instance.</summary>
public sealed record ItemInstance
{
    public ItemInstance(ItemInstanceId itemInstanceId, ItemTypeId itemTypeId, int? durability, int version)
    {
        ArgumentNullException.ThrowIfNull(itemInstanceId);
        ArgumentNullException.ThrowIfNull(itemTypeId);
        if (durability is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durability));
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        ItemInstanceId = itemInstanceId;
        ItemTypeId = itemTypeId;
        Durability = durability;
        Version = version;
    }

    public ItemInstanceId ItemInstanceId { get; }
    public ItemTypeId ItemTypeId { get; }
    public int? Durability { get; }
    public int Version { get; }
}
