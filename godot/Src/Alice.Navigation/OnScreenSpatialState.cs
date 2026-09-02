namespace Alice.Navigation;

/// <summary>
/// Owns the confirmed on-screen spatial facts for one active Actor.
/// </summary>
public sealed class OnScreenSpatialState
{
    public OnScreenSpatialState(WorldPosition spawnPosition)
    {
        ValidateFinite(spawnPosition);
        ConfirmedPosition = spawnPosition;
        ActualVelocity = new MotionVector(0.0, 0.0);
    }

    public WorldPosition ConfirmedPosition { get; private set; }

    public MotionVector ActualVelocity { get; private set; }

    /// <summary>
    /// Incorporates one accepted collision-resolved motion fact.
    /// </summary>
    public void ApplyMotionResult(MotionResult motionResult)
    {
        ArgumentNullException.ThrowIfNull(motionResult);
        ValidateFinite(motionResult.ActualDisplacement);
        ValidateFinite(motionResult.ActualVelocity);

        ConfirmedPosition = new WorldPosition(
            ConfirmedPosition.X + motionResult.ActualDisplacement.X,
            ConfirmedPosition.Y + motionResult.ActualDisplacement.Y);
        ActualVelocity = motionResult.ActualVelocity;
    }

    private static void ValidateFinite(WorldPosition position)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                "World position coordinates must be finite.");
        }
    }

    private static void ValidateFinite(MotionVector vector)
    {
        if (!double.IsFinite(vector.X) || !double.IsFinite(vector.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(vector),
                "Motion vector coordinates must be finite.");
        }
    }
}
