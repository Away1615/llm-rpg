using Alice.Identity;

namespace Alice.Navigation;

/// <summary>Stable identity for one deterministic canonical route.</summary>
public sealed record RouteId
{
    public RouteId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Route identifier must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

/// <summary>Opaque stable reference to one place or entity anchor.</summary>
public sealed record SpatialAnchorRef
{
    public SpatialAnchorRef(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Spatial anchor reference must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}
