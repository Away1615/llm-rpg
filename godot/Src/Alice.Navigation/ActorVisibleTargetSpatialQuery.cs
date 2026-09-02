using Alice.World;

namespace Alice.Navigation;

/// <summary>
/// The minimal target fact available through one actor-legitimate spatial view.
/// </summary>
public sealed record ActorVisibleTargetSpatialSnapshot(
    TargetRef TargetRef,
    TargetKind TargetKind,
    WorldPosition Position);

/// <summary>
/// Resolves only target identity, kind and position from one actor-visible view.
/// A Player view may be live perception; a future NPC view may be last known evidence.
/// Navigation cannot observe which backing view supplied the fact.
/// </summary>
public interface IActorVisibleTargetSpatialQuery
{
    bool TryResolve(
        TargetRef targetRef,
        out ActorVisibleTargetSpatialSnapshot? snapshot);
}
