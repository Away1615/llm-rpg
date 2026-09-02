namespace Alice.Navigation;

/// <summary>
/// An immutable two-dimensional mechanical delta or rate.
/// </summary>
public readonly record struct MotionVector(double X, double Y);

/// <summary>
/// The confirmed mechanical outcome of one completed movement step.
/// </summary>
public sealed record MotionResult(
    MotionVector ActualDisplacement,
    MotionVector ActualVelocity);

/// <summary>
/// Derives one mechanical motion result from confirmed positions and elapsed physics time.
/// </summary>
public static class MotionResultCapture
{
    public static MotionResult Capture(
        WorldPosition before,
        WorldPosition after,
        double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedSeconds),
                "Elapsed duration must be finite and positive.");
        }

        var displacement = new MotionVector(
            after.X - before.X,
            after.Y - before.Y);
        var velocity = new MotionVector(
            displacement.X / elapsedSeconds,
            displacement.Y / elapsedSeconds);

        return new MotionResult(displacement, velocity);
    }
}
