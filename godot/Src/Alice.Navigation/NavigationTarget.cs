using Alice.World;

namespace Alice.Navigation;

/// <summary>
/// A closed current navigation-intent hierarchy.
/// </summary>
public abstract record NavigationTarget;

/// <summary>
/// A controller-neutral navigation destination expressed as a world point.
/// </summary>
public sealed record PointNavigationTarget(WorldPosition Position) : NavigationTarget;

/// <summary>
/// A stable entity destination with a target-owned mechanical stop range.
/// </summary>
public sealed record EntityNavigationTarget : NavigationTarget
{
    public EntityNavigationTarget(TargetRef targetRef, double stopRange)
    {
        ArgumentNullException.ThrowIfNull(targetRef);

        if (!double.IsFinite(stopRange) || stopRange < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stopRange),
                "Stop range must be finite and non-negative.");
        }

        TargetRef = targetRef;
        StopRange = stopRange;
    }

    public TargetRef TargetRef { get; }

    public double StopRange { get; }
}
