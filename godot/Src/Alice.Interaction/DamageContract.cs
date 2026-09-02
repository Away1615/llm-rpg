using System.Collections.ObjectModel;
using Alice.Damage;

namespace Alice.Interaction;

/// <summary>Authoritative immutable target-side rules for one damage interaction contract.</summary>
public sealed class DamageContract : IEquatable<DamageContract>
{
    private readonly ReadOnlyCollection<DamageType> _vulnerabilities;
    private readonly ReadOnlyCollection<DamageType> _resistances;
    private readonly ReadOnlyCollection<DamageType> _immunities;

    public DamageContract(
        ContractRef contractRef,
        long version,
        InteractionRange interactionRange,
        InteractionCapabilityRequirement requirement,
        IEnumerable<DamageType> vulnerabilities,
        IEnumerable<DamageType> resistances,
        IEnumerable<DamageType> immunities)
    {
        ArgumentNullException.ThrowIfNull(contractRef);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(vulnerabilities);
        ArgumentNullException.ThrowIfNull(resistances);
        ArgumentNullException.ThrowIfNull(immunities);
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        DamageType[] vulnerabilitySnapshot = Canonicalize(vulnerabilities, nameof(vulnerabilities));
        DamageType[] resistanceSnapshot = Canonicalize(resistances, nameof(resistances));
        DamageType[] immunitySnapshot = Canonicalize(immunities, nameof(immunities));
        RejectCrossSetConflicts(vulnerabilitySnapshot, resistanceSnapshot, immunitySnapshot);

        ContractRef = contractRef;
        Version = version;
        InteractionRange = interactionRange;
        Requirement = requirement;
        _vulnerabilities = Array.AsReadOnly(vulnerabilitySnapshot);
        _resistances = Array.AsReadOnly(resistanceSnapshot);
        _immunities = Array.AsReadOnly(immunitySnapshot);
    }

    public ContractRef ContractRef { get; }
    public long Version { get; }
    public InteractionRange InteractionRange { get; }
    public InteractionCapabilityRequirement Requirement { get; }
    public IReadOnlyList<DamageType> Vulnerabilities => _vulnerabilities;
    public IReadOnlyList<DamageType> Resistances => _resistances;
    public IReadOnlyList<DamageType> Immunities => _immunities;

    public bool Equals(DamageContract? other)
    {
        return other is not null &&
            ContractRef == other.ContractRef &&
            Version == other.Version &&
            InteractionRange == other.InteractionRange &&
            Requirement.Equals(other.Requirement) &&
            Vulnerabilities.SequenceEqual(other.Vulnerabilities) &&
            Resistances.SequenceEqual(other.Resistances) &&
            Immunities.SequenceEqual(other.Immunities);
    }

    public override bool Equals(object? obj) => Equals(obj as DamageContract);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ContractRef);
        hash.Add(Version);
        hash.Add(InteractionRange);
        hash.Add(Requirement);
        AddSequenceHash(ref hash, Vulnerabilities);
        AddSequenceHash(ref hash, Resistances);
        AddSequenceHash(ref hash, Immunities);
        return hash.ToHashCode();
    }

    private static DamageType[] Canonicalize(IEnumerable<DamageType> input, string parameterName)
    {
        DamageType[] snapshot = input.ToArray();
        var values = new HashSet<DamageType>();
        foreach (DamageType damageType in snapshot)
        {
            if (!Enum.IsDefined(damageType))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            if (!values.Add(damageType))
            {
                throw new ArgumentException("Damage response types must be unique.", parameterName);
            }
        }

        Array.Sort(snapshot);
        return snapshot;
    }

    private static void RejectCrossSetConflicts(
        IEnumerable<DamageType> vulnerabilities,
        IEnumerable<DamageType> resistances,
        IEnumerable<DamageType> immunities)
    {
        var seen = new HashSet<DamageType>();
        foreach (DamageType damageType in vulnerabilities.Concat(resistances).Concat(immunities))
        {
            if (!seen.Add(damageType))
            {
                throw new ArgumentException("Damage response types cannot conflict across response sets.");
            }
        }
    }

    private static void AddSequenceHash(ref HashCode hash, IEnumerable<DamageType> values)
    {
        foreach (DamageType value in values)
        {
            hash.Add(value);
        }
    }
}
