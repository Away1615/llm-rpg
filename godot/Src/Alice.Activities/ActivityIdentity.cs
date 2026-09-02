using Alice.Identity;

namespace Alice.Activities;

/// <summary>Caller-owned identity for one shared activity; no runtime ownership is implied.</summary>
public sealed record ActivityId
{
    public ActivityId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Activity identifier must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}
