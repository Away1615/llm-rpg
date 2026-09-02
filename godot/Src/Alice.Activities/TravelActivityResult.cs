using Alice.Actors;
using Alice.Navigation;
using Alice.World;

namespace Alice.Activities;

/// <summary>Fresh exact-target position supplied only at the Travel completion boundary.</summary>
public sealed record TravelCompletionSpatialSnapshot
{
    public TravelCompletionSpatialSnapshot(TargetRef targetRef, WorldPosition currentTargetPosition)
    {
        ArgumentNullException.ThrowIfNull(targetRef);
        ValidateFinite(currentTargetPosition, nameof(currentTargetPosition));
        TargetRef = targetRef;
        CurrentTargetPosition = currentTargetPosition;
    }

    public TargetRef TargetRef { get; }
    public WorldPosition CurrentTargetPosition { get; }

    private static void ValidateFinite(WorldPosition position, string parameterName)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Current target position must be finite.");
        }
    }
}

public enum TravelActivityResultKind
{
    Reached,
    PredicateUnsatisfied
}

/// <summary>Bounded terminal fact from one explicit fresh Travel completion check.</summary>
public sealed record TravelActivityResult
{
    public TravelActivityResult(
        ActivityId activityId,
        ActorId actorId,
        TargetRef targetRef,
        TravelActivityResultKind kind,
        WorldPosition actorPosition)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(targetRef);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!double.IsFinite(actorPosition.X) || !double.IsFinite(actorPosition.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(actorPosition));
        }

        ActivityId = activityId;
        ActorId = actorId;
        TargetRef = targetRef;
        Kind = kind;
        ActorPosition = actorPosition;
    }

    public ActivityId ActivityId { get; }
    public ActorId ActorId { get; }
    public TargetRef TargetRef { get; }
    public TravelActivityResultKind Kind { get; }
    public WorldPosition ActorPosition { get; }
}
