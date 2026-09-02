using Alice.Capabilities;

namespace Alice.Interaction;

/// <summary>One authoritative minimum numeric capability requirement for an interaction contract.</summary>
public sealed class InteractionCapabilityRequirement : IEquatable<InteractionCapabilityRequirement>
{
    public InteractionCapabilityRequirement(CapabilityIdentity capabilityIdentity, int minimumValue)
    {
        ArgumentNullException.ThrowIfNull(capabilityIdentity);
        if (minimumValue <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumValue));
        }

        CapabilityIdentity = capabilityIdentity;
        MinimumValue = minimumValue;
    }

    public CapabilityIdentity CapabilityIdentity { get; }
    public int MinimumValue { get; }

    public bool Equals(InteractionCapabilityRequirement? other)
    {
        return other is not null &&
            CapabilityIdentity == other.CapabilityIdentity &&
            MinimumValue == other.MinimumValue;
    }

    public override bool Equals(object? obj) => Equals(obj as InteractionCapabilityRequirement);

    public override int GetHashCode() => HashCode.Combine(CapabilityIdentity, MinimumValue);
}
