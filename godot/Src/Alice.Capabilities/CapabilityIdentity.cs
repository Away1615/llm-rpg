using Alice.Identity;

namespace Alice.Capabilities;

/// <summary>Canonical identity for one numeric interaction capability.</summary>
public sealed record CapabilityIdentity
{
    public CapabilityIdentity(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Capability identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}
