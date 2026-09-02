using System.Collections.ObjectModel;
using Alice.Damage;

namespace Alice.Interaction;

/// <summary>Typed request for collecting one currently available non-damage resource source.</summary>
public sealed record ResourceYieldActionArguments : GameActionArguments;

/// <summary>Authoritative rules and output for one non-damage resource collection source.</summary>
public sealed class ResourceYieldContract : IEquatable<ResourceYieldContract>
{
    private readonly ReadOnlyCollection<DestructionYield> _yields;

    public ResourceYieldContract(
        ContractRef contractRef,
        long version,
        InteractionRange interactionRange,
        InteractionCapabilityRequirement requirement,
        IEnumerable<DestructionYield> yields,
        int worldDropVersion,
        PickupAccessPolicy pickupAccessPolicy)
    {
        ArgumentNullException.ThrowIfNull(contractRef);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(yields);
        if (version <= 0 || worldDropVersion <= 0 || !Enum.IsDefined(pickupAccessPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        DestructionYield[] snapshot = yields.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(yields));
        }

        var itemTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (DestructionYield item in snapshot)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!itemTypes.Add(item.ItemTypeId.Value))
            {
                throw new ArgumentException("Resource yields must have unique item types.", nameof(yields));
            }
        }

        Array.Sort(snapshot, ResourceYieldComparer.Instance);
        ContractRef = contractRef;
        Version = version;
        InteractionRange = interactionRange;
        Requirement = requirement;
        _yields = Array.AsReadOnly(snapshot);
        WorldDropVersion = worldDropVersion;
        PickupAccessPolicy = pickupAccessPolicy;
    }

    public ContractRef ContractRef { get; }
    public long Version { get; }
    public InteractionRange InteractionRange { get; }
    public InteractionCapabilityRequirement Requirement { get; }
    public IReadOnlyList<DestructionYield> Yields => _yields;
    public int WorldDropVersion { get; }
    public PickupAccessPolicy PickupAccessPolicy { get; }

    public bool Equals(ResourceYieldContract? other) =>
        other is not null
        && ContractRef == other.ContractRef
        && Version == other.Version
        && InteractionRange == other.InteractionRange
        && Requirement.Equals(other.Requirement)
        && Yields.SequenceEqual(other.Yields)
        && WorldDropVersion == other.WorldDropVersion
        && PickupAccessPolicy == other.PickupAccessPolicy;

    public override bool Equals(object? obj) => Equals(obj as ResourceYieldContract);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ContractRef);
        hash.Add(Version);
        hash.Add(InteractionRange);
        hash.Add(Requirement);
        foreach (DestructionYield item in Yields)
        {
            hash.Add(item);
        }

        hash.Add(WorldDropVersion);
        hash.Add(PickupAccessPolicy);
        return hash.ToHashCode();
    }

    private sealed class ResourceYieldComparer : IComparer<DestructionYield>
    {
        public static ResourceYieldComparer Instance { get; } = new();

        public int Compare(DestructionYield? left, DestructionYield? right) =>
            StringComparer.Ordinal.Compare(left?.ItemTypeId.Value, right?.ItemTypeId.Value);
    }
}
