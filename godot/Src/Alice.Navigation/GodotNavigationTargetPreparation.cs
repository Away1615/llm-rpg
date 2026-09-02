using Godot;

namespace Alice.Navigation;

/// <summary>Validated Godot target facts that can be assigned without further target queries.</summary>
public sealed record GodotPreparedNavigationTarget(Vector2 TargetPosition, float TargetDesiredDistance);

/// <summary>Prepares actor-visible navigation targets before their runtime lifecycle is committed.</summary>
public static class GodotNavigationTargetPreparation
{
    public static bool TryPreparePoint(
        WorldPosition target,
        float targetDesiredDistance,
        out GodotPreparedNavigationTarget? preparedTarget)
    {
        preparedTarget = null;

        if (!TryConvertPosition(target, out Vector2 targetPosition) ||
            !float.IsFinite(targetDesiredDistance) ||
            targetDesiredDistance < 0.0f)
        {
            return false;
        }

        preparedTarget = new GodotPreparedNavigationTarget(targetPosition, targetDesiredDistance);
        return true;
    }

    public static bool TryPrepareEntity(
        EntityNavigationTarget target,
        IActorVisibleTargetSpatialQuery spatialQuery,
        out GodotPreparedNavigationTarget? preparedTarget)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(spatialQuery);
        preparedTarget = null;

        EntityNavigationTargetResolutionStatus status = EntityNavigationTargetResolver.Resolve(
            target,
            spatialQuery,
            out EntityNavigationTargetResolution? resolution);
        if (status != EntityNavigationTargetResolutionStatus.Resolved || resolution is null)
        {
            return false;
        }

        return TryPrepareEntityResolution(resolution, out preparedTarget);
    }

    public static bool TryPrepareEntityResolution(
        EntityNavigationTargetResolution resolution,
        out GodotPreparedNavigationTarget? preparedTarget)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        preparedTarget = null;

        if (!TryConvertPosition(resolution.Position, out Vector2 targetPosition) ||
            !double.IsFinite(resolution.StopRange) ||
            resolution.StopRange < 0.0 ||
            resolution.StopRange > float.MaxValue)
        {
            return false;
        }

        preparedTarget = new GodotPreparedNavigationTarget(targetPosition, (float)resolution.StopRange);
        return true;
    }

    public static void Apply(GodotPreparedNavigationTarget preparedTarget, NavigationAgent2D navigationAgent)
    {
        ArgumentNullException.ThrowIfNull(preparedTarget);
        ArgumentNullException.ThrowIfNull(navigationAgent);
        navigationAgent.TargetPosition = preparedTarget.TargetPosition;
        navigationAgent.TargetDesiredDistance = preparedTarget.TargetDesiredDistance;
    }

    private static bool TryConvertPosition(WorldPosition position, out Vector2 targetPosition)
    {
        targetPosition = Vector2.Zero;

        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            return false;
        }

        float x = (float)position.X;
        float y = (float)position.Y;
        if (!float.IsFinite(x) || !float.IsFinite(y))
        {
            return false;
        }

        targetPosition = new Vector2(x, y);
        return true;
    }
}
