namespace Alice.Actors;

/// <summary>
/// A typed identity for one world Actor, independent of its controller.
/// </summary>
public readonly record struct ActorId(string Value);

/// <summary>
/// The current traversal mode shared by Player- and NPC-controlled Actors.
/// </summary>
public enum MovementMode
{
    Land,
    Swimming
}

/// <summary>
/// Persistent traversal state associated with one world Actor.
/// </summary>
public sealed record ActorTraversalState
{
    public ActorTraversalState(ActorId actorId, MovementMode movementMode)
    {
        ActorIdentity.ValidateActorId(actorId);

        ActorId = actorId;
        MovementMode = movementMode;
    }

    public ActorId ActorId { get; }
    public MovementMode MovementMode { get; }
}
