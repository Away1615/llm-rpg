using Alice.Items;

namespace Alice.Interaction;

/// <summary>Selects the one carried stackable item consumed by a Consumption action.</summary>
public sealed record ConsumptionActionArguments : GameActionArguments
{
    public ConsumptionActionArguments(ItemTypeId sourceItemTypeId)
    {
        ArgumentNullException.ThrowIfNull(sourceItemTypeId);
        SourceItemTypeId = sourceItemTypeId;
    }

    public ItemTypeId SourceItemTypeId { get; }
}

/// <summary>Exact current semantic contract for one fixed-unit Satiety restoration.</summary>
public sealed class ConsumptionContract : IEquatable<ConsumptionContract>
{
    public ConsumptionContract(ContractRef contractRef, long version, InteractionRange interactionRange, InteractionCapabilityRequirement requirement, ItemTypeId sourceItemTypeId, int satietyRestore)
    {
        ArgumentNullException.ThrowIfNull(contractRef);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(sourceItemTypeId);
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        if (satietyRestore <= 0) throw new ArgumentOutOfRangeException(nameof(satietyRestore));

        ContractRef = contractRef;
        Version = version;
        InteractionRange = interactionRange;
        Requirement = requirement;
        SourceItemTypeId = sourceItemTypeId;
        SatietyRestore = satietyRestore;
    }

    public ContractRef ContractRef { get; }
    public long Version { get; }
    public InteractionRange InteractionRange { get; }
    public InteractionCapabilityRequirement Requirement { get; }
    public ItemTypeId SourceItemTypeId { get; }
    public int Quantity => 1;
    public int SatietyRestore { get; }

    public bool Equals(ConsumptionContract? other) => other is not null && ContractRef == other.ContractRef && Version == other.Version && InteractionRange == other.InteractionRange && Requirement.Equals(other.Requirement) && SourceItemTypeId == other.SourceItemTypeId && SatietyRestore == other.SatietyRestore;
    public override bool Equals(object? obj) => Equals(obj as ConsumptionContract);
    public override int GetHashCode() => HashCode.Combine(ContractRef, Version, InteractionRange, Requirement, SourceItemTypeId, SatietyRestore);
}
