using Alice.Identity;

namespace Alice.Interaction;

/// <summary>Correlation identity for one Authority commit attempt; it is not binding identity.</summary>
public sealed record GameActionId
{
    public GameActionId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Game action identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}
