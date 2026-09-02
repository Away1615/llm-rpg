using Alice.Actors;
using Alice.Navigation;
using Alice.World;

namespace Alice.Activities;

/// <summary>Immutable correlation and canonical position at one active Travel source handoff.</summary>
public sealed record TravelProgressSourceHandoff
{
    public TravelProgressSourceHandoff(
        ActivityId activityId,
        ActorId actorId,
        TargetRef targetRef,
        RouteId routeId,
        ActivityProgressMode destinationProgressMode,
        SimTime handoffTime,
        long completedWorkTicks,
        long totalWorkTicks,
        WorldPosition canonicalPosition)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(targetRef);
        ArgumentNullException.ThrowIfNull(routeId);
        if (!Enum.IsDefined(destinationProgressMode))
        {
            throw new ArgumentOutOfRangeException(nameof(destinationProgressMode));
        }

        if (totalWorkTicks <= 0 || completedWorkTicks < 0 || completedWorkTicks >= totalWorkTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(completedWorkTicks));
        }

        if (!double.IsFinite(canonicalPosition.X) || !double.IsFinite(canonicalPosition.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(canonicalPosition));
        }

        ActivityId = activityId;
        ActorId = actorId;
        TargetRef = targetRef;
        RouteId = routeId;
        DestinationProgressMode = destinationProgressMode;
        HandoffTime = handoffTime;
        CompletedWorkTicks = completedWorkTicks;
        TotalWorkTicks = totalWorkTicks;
        CanonicalPosition = canonicalPosition;
    }

    public ActivityId ActivityId { get; }
    public ActorId ActorId { get; }
    public TargetRef TargetRef { get; }
    public RouteId RouteId { get; }
    public ActivityProgressMode DestinationProgressMode { get; }
    public SimTime HandoffTime { get; }
    public long CompletedWorkTicks { get; }
    public long TotalWorkTicks { get; }
    public WorldPosition CanonicalPosition { get; }
}
