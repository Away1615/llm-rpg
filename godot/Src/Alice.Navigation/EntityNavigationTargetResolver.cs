using Alice.World;

namespace Alice.Navigation;

/// <summary>
/// Distinguishes a resolved entity target from target invalidity and stale/not-applicable work.
/// </summary>
public enum EntityNavigationTargetResolutionStatus
{
    NotApplicable,
    TargetInvalid,
    Resolved
}

/// <summary>
/// The entity target facts resolved for one current navigation operation.
/// </summary>
public sealed record EntityNavigationTargetResolution(
    TargetRef TargetRef,
    TargetKind TargetKind,
    WorldPosition Position,
    double StopRange);

/// <summary>
/// Resolves a current entity navigation target through an injected actor-visible view only.
/// </summary>
public static class EntityNavigationTargetResolver
{
    public static EntityNavigationTargetResolutionStatus Resolve(
        NavigationRuntime navigationRuntime,
        NavigationOperationId expectedOperationId,
        IActorVisibleTargetSpatialQuery spatialQuery,
        out EntityNavigationTargetResolution? resolution)
    {
        ArgumentNullException.ThrowIfNull(navigationRuntime);
        ArgumentNullException.ThrowIfNull(spatialQuery);
        resolution = null;

        if (navigationRuntime.CurrentOperation != expectedOperationId ||
            navigationRuntime.Status != NavigationStatus.Moving ||
            navigationRuntime.CurrentTarget is not EntityNavigationTarget target)
        {
            return EntityNavigationTargetResolutionStatus.NotApplicable;
        }

        return Resolve(target, spatialQuery, out resolution);
    }

    /// <summary>Resolves supplied entity intent from the caller-visible view before navigation begins.</summary>
    public static EntityNavigationTargetResolutionStatus Resolve(
        EntityNavigationTarget target,
        IActorVisibleTargetSpatialQuery spatialQuery,
        out EntityNavigationTargetResolution? resolution)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(spatialQuery);
        resolution = null;

        if (!spatialQuery.TryResolve(target.TargetRef, out ActorVisibleTargetSpatialSnapshot? snapshot) ||
            snapshot is null ||
            snapshot.TargetRef != target.TargetRef)
        {
            return EntityNavigationTargetResolutionStatus.TargetInvalid;
        }

        resolution = new EntityNavigationTargetResolution(
            snapshot.TargetRef,
            snapshot.TargetKind,
            snapshot.Position,
            target.StopRange);
        return EntityNavigationTargetResolutionStatus.Resolved;
    }
}
