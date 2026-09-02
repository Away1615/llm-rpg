using System.Collections.ObjectModel;
using Alice.Identity;

namespace Alice.Items;

public enum AssetContainerOwnerKind
{
    Actor,
    Shop,
    Warehouse
}

public sealed record AssetContainerOwnerId
{
    public AssetContainerOwnerId(AssetContainerOwnerKind kind, string value)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        NonEmptyIdentityValue.Validate(value, nameof(value), "Asset-container owner identity must be non-empty.");
        Kind = kind;
        Value = value;
    }

    public AssetContainerOwnerKind Kind { get; }
    public string Value { get; }
}

/// <summary>Shared fungible identity for Coin and stackable resources.</summary>
public sealed record FungibleAssetId
{
    public FungibleAssetId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Fungible asset identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public sealed record FungibleAssetBalance
{
    public FungibleAssetBalance(FungibleAssetId assetId, long quantity)
    {
        ArgumentNullException.ThrowIfNull(assetId);
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        AssetId = assetId;
        Quantity = quantity;
    }

    public FungibleAssetId AssetId { get; }
    public long Quantity { get; }
}

/// <summary>
/// One owner-neutral asset container. Fungible balances cover Coin and ordinary resources;
/// durable tools remain explicit item-instance identities.
/// </summary>
public sealed class AssetContainerState
{
    private readonly ReadOnlyCollection<FungibleAssetBalance> _balances;
    private readonly ReadOnlyCollection<ItemInstanceId> _itemInstances;

    public AssetContainerState(
        AssetContainerOwnerId ownerId,
        IEnumerable<FungibleAssetBalance> balances,
        IEnumerable<ItemInstanceId> itemInstances,
        long revision)
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(balances);
        ArgumentNullException.ThrowIfNull(itemInstances);
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        FungibleAssetBalance[] balanceSnapshot = balances.ToArray();
        ItemInstanceId[] instanceSnapshot = itemInstances.ToArray();
        if (balanceSnapshot.Any(IsNullBalance)
            || balanceSnapshot.Select(GetAssetId).Distinct().Count() != balanceSnapshot.Length)
            throw new ArgumentException("Fungible asset balances must have unique identities.", nameof(balances));
        if (instanceSnapshot.Any(IsNullInstance)
            || instanceSnapshot.Distinct().Count() != instanceSnapshot.Length)
            throw new ArgumentException("Item-instance identities must be unique within one container.", nameof(itemInstances));
        Array.Sort(balanceSnapshot, FungibleBalanceComparer.Instance);
        Array.Sort(instanceSnapshot, ItemInstanceComparer.Instance);
        OwnerId = ownerId;
        _balances = Array.AsReadOnly(balanceSnapshot);
        _itemInstances = Array.AsReadOnly(instanceSnapshot);
        Revision = revision;
    }

    public AssetContainerOwnerId OwnerId { get; }
    public IReadOnlyList<FungibleAssetBalance> Balances => _balances;
    public IReadOnlyList<ItemInstanceId> ItemInstances => _itemInstances;
    public long Revision { get; }

    private static bool IsNullBalance(FungibleAssetBalance? balance) => balance is null;
    private static bool IsNullInstance(ItemInstanceId? itemInstanceId) => itemInstanceId is null;
    private static FungibleAssetId GetAssetId(FungibleAssetBalance balance) => balance.AssetId;

    private sealed class FungibleBalanceComparer : IComparer<FungibleAssetBalance>
    {
        public static FungibleBalanceComparer Instance { get; } = new();
        public int Compare(FungibleAssetBalance? left, FungibleAssetBalance? right) =>
            StringComparer.Ordinal.Compare(left?.AssetId.Value, right?.AssetId.Value);
    }

    private sealed class ItemInstanceComparer : IComparer<ItemInstanceId>
    {
        public static ItemInstanceComparer Instance { get; } = new();
        public int Compare(ItemInstanceId? left, ItemInstanceId? right) =>
            StringComparer.Ordinal.Compare(left?.Value, right?.Value);
    }
}

public sealed class AssetContainerRegistry
{
    private readonly ReadOnlyDictionary<AssetContainerOwnerId, AssetContainerState> _containers;

    public AssetContainerRegistry(IEnumerable<AssetContainerState> containers)
    {
        ArgumentNullException.ThrowIfNull(containers);
        var byOwner = new Dictionary<AssetContainerOwnerId, AssetContainerState>();
        foreach (AssetContainerState container in containers)
        {
            ArgumentNullException.ThrowIfNull(container);
            if (!byOwner.TryAdd(container.OwnerId, container))
                throw new ArgumentException("Asset-container owners must be unique.", nameof(containers));
        }
        _containers = new ReadOnlyDictionary<AssetContainerOwnerId, AssetContainerState>(byOwner);
    }

    public IReadOnlyDictionary<AssetContainerOwnerId, AssetContainerState> Containers => _containers;
}
