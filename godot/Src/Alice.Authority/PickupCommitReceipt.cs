using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Damage;
using Alice.Interaction;
using Alice.World;

namespace Alice.Authority;

public sealed class PickupCommitReceipt
{
    private readonly ReadOnlyCollection<DestructionYield> _transferredItems;
    public PickupCommitReceipt(CommitOrigin.ActorAction origin, ContractRef contractRef, long contractVersion, WorldDropId worldDropId, int worldDropVersion, int previousInventoryVersion, int currentInventoryVersion, IEnumerable<DestructionYield> transferredItems)
    {
        ArgumentNullException.ThrowIfNull(origin); ArgumentNullException.ThrowIfNull(contractRef); ArgumentNullException.ThrowIfNull(worldDropId); ArgumentNullException.ThrowIfNull(transferredItems);
        if (contractVersion <= 0 || worldDropVersion <= 0 || previousInventoryVersion <= 0 || currentInventoryVersion <= 0) throw new ArgumentOutOfRangeException(nameof(contractVersion));
        DestructionYield[] items = transferredItems.ToArray(); foreach (DestructionYield item in items) ArgumentNullException.ThrowIfNull(item); Array.Sort(items, YieldComparer.Instance);
        Origin = origin; ContractRef = contractRef; ContractVersion = contractVersion; WorldDropId = worldDropId; WorldDropVersion = worldDropVersion; PreviousInventoryVersion = previousInventoryVersion; CurrentInventoryVersion = currentInventoryVersion; _transferredItems = Array.AsReadOnly(items);
    }
    public CommitOrigin.ActorAction Origin { get; }
    public ActorId ActorId => Origin.ActorId;
    public ContractRef ContractRef { get; }
    public long ContractVersion { get; }
    public WorldDropId WorldDropId { get; }
    public int WorldDropVersion { get; }
    public int PreviousInventoryVersion { get; }
    public int CurrentInventoryVersion { get; }
    public IReadOnlyList<DestructionYield> TransferredItems => _transferredItems;
    private sealed class YieldComparer : IComparer<DestructionYield> { public static YieldComparer Instance { get; } = new(); public int Compare(DestructionYield? left, DestructionYield? right) => StringComparer.Ordinal.Compare(left?.ItemTypeId.Value, right?.ItemTypeId.Value); }
}

public sealed class PickupCommitResult
{
    internal PickupCommitResult(PickupCommitReceipt? receipt, Alice.Validation.PickupValidationFailure? failure) { Receipt = receipt; Failure = failure; }
    public PickupCommitReceipt? Receipt { get; }
    public bool IsCommitted => Receipt is not null;
    internal Alice.Validation.PickupValidationFailure? Failure { get; }
}
