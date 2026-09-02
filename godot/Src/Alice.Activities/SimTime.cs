namespace Alice.Activities;

/// <summary>Non-negative caller-owned simulation time in canonical integer quanta.</summary>
public readonly record struct SimTime : IComparable<SimTime>
{
    public SimTime(long ticks)
    {
        if (ticks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks));
        }

        Ticks = ticks;
    }

    public long Ticks { get; }

    public SimTime Add(SimDuration duration)
    {
        if (duration.Ticks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        return new SimTime(checked(Ticks + duration.Ticks));
    }

    public SimDuration Difference(SimTime earlier)
    {
        if (Ticks <= earlier.Ticks)
        {
            throw new ArgumentOutOfRangeException(nameof(earlier), "Difference must be positive and non-backward.");
        }

        return new SimDuration(checked(Ticks - earlier.Ticks));
    }

    public int CompareTo(SimTime other) => Ticks.CompareTo(other.Ticks);
}

/// <summary>Positive simulation duration in canonical integer quanta.</summary>
public readonly record struct SimDuration : IComparable<SimDuration>
{
    public SimDuration(long ticks)
    {
        if (ticks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks));
        }

        Ticks = ticks;
    }

    public long Ticks { get; }

    public int CompareTo(SimDuration other) => Ticks.CompareTo(other.Ticks);
}
