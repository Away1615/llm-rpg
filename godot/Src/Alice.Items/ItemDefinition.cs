using System.Collections.ObjectModel;
using Alice.Capabilities;
using Alice.Damage;

namespace Alice.Items;

/// <summary>Closed custody representation for an item definition.</summary>
public enum ItemStackability
{
    Stackable,
    Instanced
}

/// <summary>Immutable typed item definition input for pure held-item resolution.</summary>
public sealed class ItemDefinition : IEquatable<ItemDefinition>
{
    private readonly ReadOnlyCollection<CapabilityContribution> _capabilityContributions;

    public ItemDefinition(
        ItemTypeId itemTypeId,
        ItemStackability stackability,
        IEnumerable<CapabilityContribution> capabilityContributions,
        DamageContribution? damageContribution)
    {
        ArgumentNullException.ThrowIfNull(itemTypeId);
        ArgumentNullException.ThrowIfNull(capabilityContributions);
        CapabilityContribution[] snapshot = capabilityContributions.ToArray();
        foreach (CapabilityContribution contribution in snapshot)
        {
            ArgumentNullException.ThrowIfNull(contribution);
        }

        ItemTypeId = itemTypeId;
        Stackability = stackability;
        _capabilityContributions = Array.AsReadOnly(snapshot);
        DamageContribution = damageContribution;
    }

    public ItemTypeId ItemTypeId { get; }
    public ItemStackability Stackability { get; }
    public IReadOnlyList<CapabilityContribution> CapabilityContributions => _capabilityContributions;
    public DamageContribution? DamageContribution { get; }

    public bool Equals(ItemDefinition? other)
    {
        return other is not null &&
            ItemTypeId == other.ItemTypeId &&
            Stackability == other.Stackability &&
            DamageContribution == other.DamageContribution &&
            CapabilityContributions.SequenceEqual(other.CapabilityContributions);
    }

    public override bool Equals(object? obj) => Equals(obj as ItemDefinition);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ItemTypeId);
        hash.Add(Stackability);
        hash.Add(DamageContribution);
        foreach (CapabilityContribution contribution in CapabilityContributions)
        {
            hash.Add(contribution);
        }

        return hash.ToHashCode();
    }
}
