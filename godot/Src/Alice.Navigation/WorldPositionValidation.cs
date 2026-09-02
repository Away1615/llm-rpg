namespace Alice.Navigation;

/// <summary>Shared finite-coordinate validation for the many typed boundaries that accept a WorldPosition.</summary>
internal static class WorldPositionValidation
{
    public static bool IsFinite(WorldPosition position) => double.IsFinite(position.X) && double.IsFinite(position.Y);

    public static void Validate(WorldPosition position, string paramName, string? message = null)
    {
        if (!IsFinite(position))
        {
            throw message is null
                ? new ArgumentOutOfRangeException(paramName)
                : new ArgumentOutOfRangeException(paramName, message);
        }
    }
}
