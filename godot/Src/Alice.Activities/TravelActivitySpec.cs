using Alice.Actors;
using Alice.Interaction;
using Alice.Navigation;
using Alice.World;

namespace Alice.Activities;

/// <summary>Controller-neutral operational start facts for one entity-target Travel activity.</summary>
public sealed record TravelActivitySpec
{
    public TravelActivitySpec(
        ActorId actorId,
        TargetRef targetRef,
        InteractionRange interactionRange,
        RouteId routeId)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(targetRef);
        ArgumentNullException.ThrowIfNull(routeId);
        ActorId = actorId;
        TargetRef = targetRef;
        InteractionRange = interactionRange;
        RouteId = routeId;
    }

    public ActorId ActorId { get; }
    public TargetRef TargetRef { get; }
    public InteractionRange InteractionRange { get; }
    public RouteId RouteId { get; }
}
