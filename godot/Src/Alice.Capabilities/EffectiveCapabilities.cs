using System.Collections.ObjectModel;
using Alice.Items;

namespace Alice.Capabilities;

/// <summary>One non-negative numeric interaction capability contribution.</summary>
public sealed record CapabilityContribution
{
    public CapabilityContribution(CapabilityIdentity capabilityIdentity, int value)
    {
        ArgumentNullException.ThrowIfNull(capabilityIdentity);
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        CapabilityIdentity = capabilityIdentity;
        Value = value;
    }

    public CapabilityIdentity CapabilityIdentity { get; }
    public int Value { get; }
}

/// <summary>One signed temporary numeric interaction capability adjustment.</summary>
public sealed record TemporaryCapabilityModifier
{
    public TemporaryCapabilityModifier(CapabilityIdentity capabilityIdentity, int delta)
    {
        ArgumentNullException.ThrowIfNull(capabilityIdentity);
        CapabilityIdentity = capabilityIdentity;
        Delta = delta;
    }

    public CapabilityIdentity CapabilityIdentity { get; }
    public int Delta { get; }
}

/// <summary>Immutable resolved numeric interaction capability values.</summary>
public sealed class EffectiveCapabilities : IEquatable<EffectiveCapabilities>
{
    private readonly ReadOnlyCollection<CapabilityContribution> _values;

    private EffectiveCapabilities(IEnumerable<CapabilityContribution> values)
    {
        _values = Array.AsReadOnly(values.ToArray());
    }

    public IReadOnlyList<CapabilityContribution> Values => _values;

    /// <summary>Resolves intrinsic, held-item and temporary numeric inputs into clamped values.</summary>
    public static EffectiveCapabilities Resolve(
        IEnumerable<CapabilityContribution> intrinsicContributions,
        ItemDefinition? heldItemDefinition,
        IEnumerable<TemporaryCapabilityModifier> temporaryModifiers)
    {
        ArgumentNullException.ThrowIfNull(intrinsicContributions);
        ArgumentNullException.ThrowIfNull(temporaryModifiers);

        var inputs = new List<NumericInput>();
        AddContributions(inputs, intrinsicContributions);
        if (heldItemDefinition is not null)
        {
            AddContributions(inputs, heldItemDefinition.CapabilityContributions);
        }

        AddModifiers(inputs, temporaryModifiers);
        inputs.Sort(NumericInputComparer.Instance);

        var values = new List<CapabilityContribution>();
        int index = 0;
        while (index < inputs.Count)
        {
            CapabilityIdentity identity = inputs[index].CapabilityIdentity;
            long total = 0;
            do
            {
                total = checked(total + inputs[index].Delta);
                index++;
            }
            while (index < inputs.Count && inputs[index].CapabilityIdentity == identity);

            if (total > int.MaxValue)
            {
                throw new OverflowException("Effective capability exceeds Int32 range.");
            }

            values.Add(new CapabilityContribution(identity, total <= 0 ? 0 : (int)total));
        }

        return new EffectiveCapabilities(values);
    }

    /// <summary>Returns zero for an identity absent from the resolved collection.</summary>
    public int GetValue(CapabilityIdentity capabilityIdentity)
    {
        ArgumentNullException.ThrowIfNull(capabilityIdentity);
        foreach (CapabilityContribution value in Values)
        {
            if (value.CapabilityIdentity == capabilityIdentity)
            {
                return value.Value;
            }
        }

        return 0;
    }

    public bool Equals(EffectiveCapabilities? other)
    {
        return other is not null && Values.SequenceEqual(other.Values);
    }

    public override bool Equals(object? obj) => Equals(obj as EffectiveCapabilities);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (CapabilityContribution value in Values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    private static void AddContributions(List<NumericInput> inputs, IEnumerable<CapabilityContribution> contributions)
    {
        foreach (CapabilityContribution contribution in contributions)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            inputs.Add(new NumericInput(contribution.CapabilityIdentity, contribution.Value));
        }
    }

    private static void AddModifiers(List<NumericInput> inputs, IEnumerable<TemporaryCapabilityModifier> modifiers)
    {
        foreach (TemporaryCapabilityModifier modifier in modifiers)
        {
            ArgumentNullException.ThrowIfNull(modifier);
            inputs.Add(new NumericInput(modifier.CapabilityIdentity, modifier.Delta));
        }
    }

    private sealed record NumericInput(CapabilityIdentity CapabilityIdentity, int Delta);

    private sealed class NumericInputComparer : IComparer<NumericInput>
    {
        public static NumericInputComparer Instance { get; } = new();

        public int Compare(NumericInput? left, NumericInput? right)
        {
            return StringComparer.Ordinal.Compare(left?.CapabilityIdentity.Value, right?.CapabilityIdentity.Value);
        }
    }
}
