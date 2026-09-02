using Alice.Identity;

namespace Alice.World;

/// <summary>
/// A stable identity for one targetable world entity.
/// </summary>
public sealed record TargetRef
{
    public TargetRef(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Target reference must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

/// <summary>
/// The currently targetable world kinds required by shared navigation.
/// </summary>
public enum TargetKind
{
    Tree,
    Npc,
    ResourceNode,
    PointOfInterest
}
