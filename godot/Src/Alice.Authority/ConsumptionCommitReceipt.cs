using Alice.Actors;
using Alice.Interaction;
using Alice.Items;
using Alice.Validation;

namespace Alice.Authority;

/// <summary>Immutable audit fact emitted only after one Inventory and Body Consumption commit settles.</summary>
public sealed class ConsumptionCommitReceipt
{
    public ConsumptionCommitReceipt(CommitOrigin.ActorAction origin, ContractRef contractRef, long contractVersion, ItemTypeId sourceItemTypeId, int previousInventoryVersion, int currentInventoryVersion, int previousSatiety, int currentSatiety)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(contractRef);
        ArgumentNullException.ThrowIfNull(sourceItemTypeId);
        if (contractVersion <= 0 || previousInventoryVersion <= 0 || currentInventoryVersion <= 0) throw new ArgumentOutOfRangeException(nameof(contractVersion));
        if (previousSatiety is < 0 or > 100 || currentSatiety is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(previousSatiety));

        Origin = origin;
        ContractRef = contractRef;
        ContractVersion = contractVersion;
        SourceItemTypeId = sourceItemTypeId;
        PreviousInventoryVersion = previousInventoryVersion;
        CurrentInventoryVersion = currentInventoryVersion;
        PreviousSatiety = previousSatiety;
        CurrentSatiety = currentSatiety;
    }

    public CommitOrigin.ActorAction Origin { get; }
    public ActorId ActorId => Origin.ActorId;
    public ContractRef ContractRef { get; }
    public long ContractVersion { get; }
    public ItemTypeId SourceItemTypeId { get; }
    public int Quantity => 1;
    public int PreviousInventoryVersion { get; }
    public int CurrentInventoryVersion { get; }
    public int PreviousSatiety { get; }
    public int CurrentSatiety { get; }
}

/// <summary>One Consumption Authority attempt outcome with no public raw failure data.</summary>
public sealed class ConsumptionCommitResult
{
    internal ConsumptionCommitResult(ConsumptionCommitReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        Receipt = receipt;
    }

    internal ConsumptionCommitResult(ConsumptionValidationFailure failure)
    {
        if (!Enum.IsDefined(failure)) throw new ArgumentOutOfRangeException(nameof(failure));
        Failure = failure;
    }

    public ConsumptionCommitReceipt? Receipt { get; }
    public bool IsCommitted => Receipt is not null;
    internal ConsumptionValidationFailure? Failure { get; }
}
