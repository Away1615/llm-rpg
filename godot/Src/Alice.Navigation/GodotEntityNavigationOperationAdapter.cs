using Godot;

namespace Alice.Navigation;

/// <summary>
/// The most recent Godot target values assigned for one entity navigation operation.
/// </summary>
public sealed record EntityNavigationTargetAssignment(
    NavigationOperationId NavigationOperationId,
    Vector2 TargetPosition,
    float TargetDesiredDistance);

/// <summary>
/// Projects actor-visible entity navigation facts into the shared Godot motion boundary.
/// </summary>
public static class GodotEntityNavigationOperationAdapter
{
    public static bool TryApplyTarget(
        NavigationRuntime navigationRuntime,
        NavigationOperationId expectedOperationId,
        IActorVisibleTargetSpatialQuery spatialQuery,
        NavigationAgent2D navigationAgent,
        out EntityNavigationTargetAssignment? assignment)
    {
        ArgumentNullException.ThrowIfNull(navigationRuntime);
        ArgumentNullException.ThrowIfNull(spatialQuery);
        ArgumentNullException.ThrowIfNull(navigationAgent);
        assignment = null;

        EntityNavigationTargetResolutionStatus resolutionStatus =
            EntityNavigationTargetResolver.Resolve(
                navigationRuntime,
                expectedOperationId,
                spatialQuery,
                out EntityNavigationTargetResolution? resolution);
        if (resolutionStatus != EntityNavigationTargetResolutionStatus.Resolved ||
            resolution is null ||
            !TryCreateAssignment(expectedOperationId, resolution, out EntityNavigationTargetAssignment? resolvedAssignment) ||
            resolvedAssignment is null)
        {
            return false;
        }

        navigationAgent.TargetPosition = resolvedAssignment.TargetPosition;
        navigationAgent.TargetDesiredDistance = resolvedAssignment.TargetDesiredDistance;
        assignment = resolvedAssignment;
        return true;
    }

    /// <summary>
    /// Refreshes a current entity target only when actor-visible facts changed.
    /// </summary>
    public static EntityNavigationTargetResolutionStatus RefreshTarget(
        NavigationRuntime navigationRuntime,
        NavigationOperationId expectedOperationId,
        IActorVisibleTargetSpatialQuery spatialQuery,
        NavigationAgent2D navigationAgent,
        EntityNavigationTargetAssignment assignedTarget,
        out EntityNavigationTargetAssignment? refreshedAssignment)
    {
        ArgumentNullException.ThrowIfNull(navigationRuntime);
        ArgumentNullException.ThrowIfNull(spatialQuery);
        ArgumentNullException.ThrowIfNull(navigationAgent);
        ArgumentNullException.ThrowIfNull(assignedTarget);
        refreshedAssignment = null;

        EntityNavigationTargetResolutionStatus resolutionStatus =
            EntityNavigationTargetResolver.Resolve(
                navigationRuntime,
                expectedOperationId,
                spatialQuery,
                out EntityNavigationTargetResolution? resolution);
        if (resolutionStatus != EntityNavigationTargetResolutionStatus.Resolved ||
            resolution is null)
        {
            return resolutionStatus;
        }

        if (assignedTarget.NavigationOperationId != expectedOperationId ||
            !TryCreateAssignment(expectedOperationId, resolution, out EntityNavigationTargetAssignment? resolvedAssignment) ||
            resolvedAssignment is null)
        {
            return EntityNavigationTargetResolutionStatus.NotApplicable;
        }

        if (assignedTarget.TargetPosition != resolvedAssignment.TargetPosition)
        {
            navigationAgent.TargetPosition = resolvedAssignment.TargetPosition;
        }

        if (assignedTarget.TargetDesiredDistance != resolvedAssignment.TargetDesiredDistance)
        {
            navigationAgent.TargetDesiredDistance = resolvedAssignment.TargetDesiredDistance;
        }

        refreshedAssignment = resolvedAssignment;
        return EntityNavigationTargetResolutionStatus.Resolved;
    }

    /// <summary>
    /// Classifies a terminal entity operation mechanically without mutating runtime status.
    /// </summary>
    public static bool TryClassifyTerminal(
        NavigationRuntime navigationRuntime,
        NavigationOperationId expectedOperationId,
        IActorVisibleTargetSpatialQuery spatialQuery,
        NavigationAgent2D navigationAgent,
        out NavigationStatus? terminalStatus)
    {
        ArgumentNullException.ThrowIfNull(navigationRuntime);
        ArgumentNullException.ThrowIfNull(spatialQuery);
        ArgumentNullException.ThrowIfNull(navigationAgent);
        terminalStatus = null;

        EntityNavigationTargetResolutionStatus resolutionStatus =
            EntityNavigationTargetResolver.Resolve(
                navigationRuntime,
                expectedOperationId,
                spatialQuery,
                out EntityNavigationTargetResolution? resolution);
        if (resolutionStatus == EntityNavigationTargetResolutionStatus.TargetInvalid)
        {
            terminalStatus = NavigationStatus.TargetInvalid;
            return true;
        }

        if (resolutionStatus != EntityNavigationTargetResolutionStatus.Resolved ||
            resolution is null ||
            !TryCreateAssignment(expectedOperationId, resolution, out _) ||
            NavigationServer2D.MapGetIterationId(navigationAgent.GetNavigationMap()) == 0 ||
            !navigationAgent.IsNavigationFinished())
        {
            return false;
        }

        terminalStatus = navigationAgent.IsTargetReached()
            ? NavigationStatus.Arrived
            : NavigationStatus.NoPath;
        return true;
    }

    /// <summary>
    /// Refreshes and executes at most one shared path-querying motion step.
    /// </summary>
    public static bool TryStep(
        NavigationRuntime navigationRuntime,
        NavigationOperationId expectedOperationId,
        IActorVisibleTargetSpatialQuery spatialQuery,
        NavigationAgent2D navigationAgent,
        CharacterBody2D body,
        EntityNavigationTargetAssignment assignedTarget,
        float speed,
        double elapsedSeconds,
        out EntityNavigationTargetAssignment? refreshedAssignment,
        out NavigationMotionStepResult? stepResult,
        out NavigationStatus? terminalStatus)
    {
        ArgumentNullException.ThrowIfNull(navigationRuntime);
        ArgumentNullException.ThrowIfNull(spatialQuery);
        ArgumentNullException.ThrowIfNull(navigationAgent);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(assignedTarget);
        SharedMotionController.ValidateSpeed(speed);
        SharedMotionController.ValidateElapsedSeconds(elapsedSeconds);
        refreshedAssignment = null;
        stepResult = null;
        terminalStatus = null;

        EntityNavigationTargetResolutionStatus refreshStatus = RefreshTarget(
            navigationRuntime,
            expectedOperationId,
            spatialQuery,
            navigationAgent,
            assignedTarget,
            out EntityNavigationTargetAssignment? currentAssignment);
        if (refreshStatus == EntityNavigationTargetResolutionStatus.TargetInvalid)
        {
            terminalStatus = NavigationStatus.TargetInvalid;
            return false;
        }

        if (refreshStatus != EntityNavigationTargetResolutionStatus.Resolved ||
            currentAssignment is null ||
            NavigationServer2D.MapGetIterationId(navigationAgent.GetNavigationMap()) == 0)
        {
            return false;
        }

        refreshedAssignment = currentAssignment;
        if (navigationAgent.IsNavigationFinished())
        {
            terminalStatus = navigationAgent.IsTargetReached()
                ? NavigationStatus.Arrived
                : NavigationStatus.NoPath;
            return false;
        }

        MotionResult motionResult = SharedMotionController.Step(
            navigationAgent,
            body,
            speed,
            elapsedSeconds);
        stepResult = new NavigationMotionStepResult(expectedOperationId, motionResult);
        return true;
    }

    private static bool TryCreateAssignment(
        NavigationOperationId operationId,
        EntityNavigationTargetResolution resolution,
        out EntityNavigationTargetAssignment? assignment)
    {
        assignment = null;

        if (!GodotNavigationTargetPreparation.TryPrepareEntityResolution(
                resolution,
                out GodotPreparedNavigationTarget? preparedTarget) ||
            preparedTarget is null)
        {
            return false;
        }

        assignment = new EntityNavigationTargetAssignment(
            operationId,
            preparedTarget.TargetPosition,
            preparedTarget.TargetDesiredDistance);
        return true;
    }

}
