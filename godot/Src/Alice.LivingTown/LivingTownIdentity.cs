using Alice.Identity;

namespace Alice.LivingTown;

public sealed record TownId
{
    public TownId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Town identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public sealed record TownPopulationManifestId
{
    public TownPopulationManifestId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Population manifest identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public sealed record LivingTownPlaceRef
{
    public LivingTownPlaceRef(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Place reference must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public sealed record SourceEventId
{
    public SourceEventId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Source event identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public enum LivingTownEmotionKind
{
    Neutral,
    Joy,
    Sadness,
    Anger,
    Fear,
    Surprise,
    Disgust,
    Trust
}

public enum SchedulePurpose
{
    Sleep,
    Work,
    Meal,
    Social,
    Free
}

public enum ScheduleObligation
{
    Hard,
    Soft,
    None
}
