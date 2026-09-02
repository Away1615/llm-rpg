using Godot;

namespace Alice.Navigation;

/// <summary>
/// Executes one collision-resolved shared movement step from the next navigation path point.
/// </summary>
public static class SharedMotionController
{
    public static MotionResult Step(
        NavigationAgent2D navigationAgent,
        CharacterBody2D body,
        float speed,
        double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(navigationAgent);
        ArgumentNullException.ThrowIfNull(body);
        ValidateSpeed(speed);
        ValidateElapsedSeconds(elapsedSeconds);

        Vector2 beforePosition = body.GlobalPosition;
        Vector2 nextPathPosition = navigationAgent.GetNextPathPosition();
        body.Velocity = CalculateDesiredVelocity(beforePosition, nextPathPosition, speed);
        body.MoveAndSlide();
        Vector2 afterPosition = body.GlobalPosition;

        return MotionResultCapture.Capture(
            new WorldPosition(beforePosition.X, beforePosition.Y),
            new WorldPosition(afterPosition.X, afterPosition.Y),
            elapsedSeconds);
    }

    public static Vector2 CalculateDesiredVelocity(
        Vector2 currentPosition,
        Vector2 nextPathPosition,
        float speed)
    {
        ValidateSpeed(speed);

        Vector2 direction = nextPathPosition - currentPosition;
        float distance = direction.Length();

        if (distance == 0.0f || speed == 0.0f)
        {
            return Vector2.Zero;
        }

        return direction / distance * speed;
    }

    internal static void ValidateSpeed(float speed)
    {
        if (!float.IsFinite(speed) || speed < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speed),
                "Speed must be finite and non-negative.");
        }
    }

    internal static void ValidateElapsedSeconds(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedSeconds),
                "Elapsed duration must be finite and positive.");
        }
    }
}
