using Alice.Interaction;

namespace Alice.Navigation;

/// <summary>Pure finite Euclidean interaction-range arithmetic shared by Travel and Plan evidence.</summary>
public static class InteractionTargetReachability
{
    public static bool IsWithinRange(
        WorldPosition actorPosition,
        WorldPosition targetPosition,
        InteractionRange interactionRange)
    {
        ValidateFinite(actorPosition, nameof(actorPosition));
        ValidateFinite(targetPosition, nameof(targetPosition));
        double horizontal = actorPosition.X - targetPosition.X;
        double vertical = actorPosition.Y - targetPosition.Y;
        double distance = Math.Sqrt((horizontal * horizontal) + (vertical * vertical));
        if (!double.IsFinite(distance))
        {
            throw new ArgumentException("Interaction distance must be finite.");
        }

        return distance <= interactionRange.Value;
    }

    private static void ValidateFinite(WorldPosition position, string parameterName)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Interaction positions must be finite.");
        }
    }
}
