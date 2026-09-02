namespace Alice.Actors;

/// <summary>Immutable current and maximum health facts for one Actor.</summary>
public sealed record Health
{
    public Health(int current, int maximum)
    {
        if (maximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum));
        }

        if (current < 0 || current > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(current));
        }

        Current = current;
        Maximum = maximum;
    }

    public int Current { get; }
    public int Maximum { get; }
}

/// <summary>Immutable satiety on the independently frozen 0 through 100 scale.</summary>
public sealed record Satiety
{
    public Satiety(int value)
    {
        ValidatePercent(value);
        Value = value;
    }

    public int Value { get; }

    internal static void ValidatePercent(int value)
    {
        if (value < 0 || value > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}

/// <summary>Immutable spirit on the independently frozen 0 through 100 scale.</summary>
public sealed record Spirit
{
    public Spirit(int value)
    {
        Satiety.ValidatePercent(value);
        Value = value;
    }

    public int Value { get; }
}

/// <summary>The closed generic disease catalogue.</summary>
public enum Disease
{
    Healthy,
    Ill,
    SevereIllness,
    Dead
}

/// <summary>Controller-neutral immutable body facts for one Actor.</summary>
public sealed record ActorBodyState
{
    public ActorBodyState(ActorId actorId, Health health, Satiety satiety, Spirit spirit, Disease disease)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(satiety);
        ArgumentNullException.ThrowIfNull(spirit);

        ActorId = actorId;
        Health = health;
        Satiety = satiety;
        Spirit = spirit;
        Disease = disease;
    }

    public ActorId ActorId { get; }
    public Health Health { get; }
    public Satiety Satiety { get; }
    public Spirit Spirit { get; }
    public Disease Disease { get; }
}
