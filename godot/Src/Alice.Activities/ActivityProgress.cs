namespace Alice.Activities;

/// <summary>One immutable operational progress value shared by all activity progress providers.</summary>
public sealed class ActivityProgress : IEquatable<ActivityProgress>
{
    public ActivityProgress(long completedWorkTicks, long totalWorkTicks)
    {
        if (totalWorkTicks <= 0 || completedWorkTicks < 0 || completedWorkTicks > totalWorkTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(completedWorkTicks));
        }

        CompletedWorkTicks = completedWorkTicks;
        TotalWorkTicks = totalWorkTicks;
    }

    public long CompletedWorkTicks { get; }
    public long TotalWorkTicks { get; }
    public double Fraction => (double)CompletedWorkTicks / TotalWorkTicks;

    public bool Equals(ActivityProgress? other)
    {
        return other is not null && CompletedWorkTicks == other.CompletedWorkTicks && TotalWorkTicks == other.TotalWorkTicks;
    }

    public override bool Equals(object? obj) => Equals(obj as ActivityProgress);
    public override int GetHashCode() => HashCode.Combine(CompletedWorkTicks, TotalWorkTicks);
}
