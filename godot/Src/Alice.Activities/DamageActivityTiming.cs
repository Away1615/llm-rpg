namespace Alice.Activities;

/// <summary>Explicit timing inputs for one timed damage action; no balance formula is implied.</summary>
public sealed class DamageActivityTiming : IEquatable<DamageActivityTiming>
{
    public DamageActivityTiming(SimDuration hitPointOffset, SimDuration totalDuration)
    {
        if (hitPointOffset.Ticks <= 0 || totalDuration.Ticks <= 0 || hitPointOffset.Ticks > totalDuration.Ticks)
        {
            throw new ArgumentOutOfRangeException(nameof(hitPointOffset));
        }

        HitPointOffset = hitPointOffset;
        TotalDuration = totalDuration;
    }

    public SimDuration HitPointOffset { get; }
    public SimDuration TotalDuration { get; }

    public bool Equals(DamageActivityTiming? other)
    {
        return other is not null && HitPointOffset == other.HitPointOffset && TotalDuration == other.TotalDuration;
    }

    public override bool Equals(object? obj) => Equals(obj as DamageActivityTiming);
    public override int GetHashCode() => HashCode.Combine(HitPointOffset, TotalDuration);
}
