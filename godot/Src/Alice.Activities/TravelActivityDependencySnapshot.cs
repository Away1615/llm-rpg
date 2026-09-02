using Alice.Navigation;
using Alice.World;

namespace Alice.Activities;

/// <summary>Immutable Travel startup correlation evidence, never fresh completion truth.</summary>
public sealed record TravelActivityDependencySnapshot : IActivityDependencySnapshot
{
    public TravelActivityDependencySnapshot(
        RouteId routeId,
        TargetRef targetRef,
        WorldPosition observedTargetPosition)
    {
        ArgumentNullException.ThrowIfNull(routeId);
        ArgumentNullException.ThrowIfNull(targetRef);
        ValidateFinite(observedTargetPosition, nameof(observedTargetPosition));
        RouteId = routeId;
        TargetRef = targetRef;
        ObservedTargetPosition = observedTargetPosition;
    }

    public RouteId RouteId { get; }
    public TargetRef TargetRef { get; }
    public WorldPosition ObservedTargetPosition { get; }

    private static void ValidateFinite(WorldPosition position, string parameterName)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Observed target position must be finite.");
        }
    }
}
