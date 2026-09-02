using Godot;

namespace Alice.Navigation;

/// <summary>
/// Correlates one completed shared motion step with its navigation operation.
/// </summary>
public sealed record NavigationMotionStepResult(
    NavigationOperationId NavigationOperationId,
    MotionResult MotionResult);

/// <summary>
/// Projects one current point-navigation operation into the Godot motion boundary.
/// </summary>
public static class GodotPointNavigationOperationAdapter
{
    public static bool TryResolveTarget(
        NavigationRuntime navigationRuntime,
        NavigationOperationId expectedOperationId,
        out Vector2 targetPosition)
    {
        ArgumentNullException.ThrowIfNull(navigationRuntime);
        targetPosition = Vector2.Zero;

        if (navigationRuntime.CurrentOperation != expectedOperationId ||
            navigationRuntime.Status != NavigationStatus.Moving ||
            navigationRuntime.CurrentTarget is null)
        {
            return false;
        }

        if (navigationRuntime.CurrentTarget is not PointNavigationTarget pointTarget)
        {
            return false;
        }

        return GodotNavigationTargetPreparation.TryPreparePoint(
            pointTarget.Position,
            0.0f,
            out GodotPreparedNavigationTarget? preparedTarget) &&
            AssignResolvedTarget(preparedTarget, out targetPosition);
    }

    /// <summary>
    /// Applies a current operation target once when that operation starts.
    /// </summary>
    public static bool TryApplyTarget(
        NavigationRuntime navigationRuntime,
        NavigationOperationId expectedOperationId,
        NavigationAgent2D navigationAgent,
        float targetDesiredDistance)
    {
        ArgumentNullException.ThrowIfNull(navigationRuntime);
        ArgumentNullException.ThrowIfNull(navigationAgent);
        if (!TryResolveTarget(
                navigationRuntime,
                expectedOperationId,
                out Vector2 targetPosition) ||
            !GodotNavigationTargetPreparation.TryPreparePoint(
                new WorldPosition(targetPosition.X, targetPosition.Y),
                targetDesiredDistance,
                out GodotPreparedNavigationTarget? preparedTarget) ||
            preparedTarget is null)
        {
            return false;
        }

        GodotNavigationTargetPreparation.Apply(preparedTarget, navigationAgent);
        return true;
    }

    /// <summary>
    /// Classifies a completed, synchronised navigation path without changing runtime ownership.
    /// </summary>
    public static bool TryClassifyTerminal(
        NavigationRuntime navigationRuntime,
        NavigationOperationId expectedOperationId,
        NavigationAgent2D navigationAgent,
        out NavigationStatus? terminalStatus)
    {
        ArgumentNullException.ThrowIfNull(navigationRuntime);
        ArgumentNullException.ThrowIfNull(navigationAgent);
        terminalStatus = null;

        if (!TryResolveTarget(navigationRuntime, expectedOperationId, out _) ||
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

    public static bool TryStep(
        NavigationRuntime navigationRuntime,
        NavigationOperationId expectedOperationId,
        NavigationAgent2D navigationAgent,
        CharacterBody2D body,
        float speed,
        double elapsedSeconds,
        out NavigationMotionStepResult? stepResult)
    {
        ArgumentNullException.ThrowIfNull(navigationRuntime);
        ArgumentNullException.ThrowIfNull(navigationAgent);
        ArgumentNullException.ThrowIfNull(body);
        SharedMotionController.ValidateSpeed(speed);
        SharedMotionController.ValidateElapsedSeconds(elapsedSeconds);
        stepResult = null;

        if (!TryResolveTarget(navigationRuntime, expectedOperationId, out _) ||
            NavigationServer2D.MapGetIterationId(navigationAgent.GetNavigationMap()) == 0 ||
            navigationAgent.IsNavigationFinished())
        {
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

    private static bool AssignResolvedTarget(
        GodotPreparedNavigationTarget? preparedTarget,
        out Vector2 targetPosition)
    {
        targetPosition = Vector2.Zero;
        if (preparedTarget is null)
        {
            return false;
        }

        targetPosition = preparedTarget.TargetPosition;
        return true;
    }
}
