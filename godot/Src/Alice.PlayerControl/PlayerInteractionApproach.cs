using Alice.Actors;
using Alice.Interaction;
using Alice.Navigation;

namespace Alice.PlayerControl;

/// <summary>Pure Player interaction orchestration around the typed range and spatial boundaries.</summary>
public static class PlayerInteractionApproach
{
    public static bool TryBegin(
        PlayerControlRuntime playerControlRuntime,
        NavigationRuntime navigationRuntime,
        PlayerInteractionSelection selection,
        IInteractionRangeQuery rangeQuery,
        out PlayerPointNavigationCorrelation? correlation)
    {
        ArgumentNullException.ThrowIfNull(playerControlRuntime);
        ArgumentNullException.ThrowIfNull(navigationRuntime);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(rangeQuery);
        correlation = null;

        if (!TryPrepareTarget(selection.Binding, rangeQuery, out EntityNavigationTarget? target) || target is null)
        {
            return false;
        }

        CommandRevision revision = playerControlRuntime.SetCommand(new InteractWithPlayerCommand(selection));
        NavigationOperationId operationId = navigationRuntime.Begin(target);
        correlation = new PlayerPointNavigationCorrelation(revision, operationId);
        return true;
    }

    /// <summary>Builds interaction navigation intent without advancing either runtime owner.</summary>
    public static bool TryPrepareTarget(
        InteractionBinding binding,
        IInteractionRangeQuery rangeQuery,
        out EntityNavigationTarget? target)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(rangeQuery);
        target = null;

        if (!rangeQuery.TryResolve(binding, out InteractionRange range))
        {
            return false;
        }

        target = new EntityNavigationTarget(binding.ContractRef.TargetRef, range.Value);
        return true;
    }

    public static bool TryProduceSpec(
        PlayerControlRuntime playerControlRuntime,
        PlayerPointNavigationCorrelation correlation,
        PlayerInteractionSelection selectedInteraction,
        IInteractionRangeQuery rangeQuery,
        IActorVisibleTargetSpatialQuery spatialQuery,
        OnScreenSpatialState onScreenSpatialState,
        ActorId actorId,
        out GameActionSpec? actionSpec)
    {
        ArgumentNullException.ThrowIfNull(playerControlRuntime);
        ArgumentNullException.ThrowIfNull(correlation);
        ArgumentNullException.ThrowIfNull(selectedInteraction);
        ArgumentNullException.ThrowIfNull(rangeQuery);
        ArgumentNullException.ThrowIfNull(spatialQuery);
        ArgumentNullException.ThrowIfNull(onScreenSpatialState);
        ArgumentNullException.ThrowIfNull(actorId);
        actionSpec = null;

        if (!playerControlRuntime.IsCurrent(correlation.CommandRevision) ||
            playerControlRuntime.CurrentCommand is not InteractWithPlayerCommand command ||
            command.Selection != selectedInteraction ||
            !rangeQuery.TryResolve(selectedInteraction.Binding, out InteractionRange range) ||
            !spatialQuery.TryResolve(selectedInteraction.Binding.ContractRef.TargetRef, out ActorVisibleTargetSpatialSnapshot? target) ||
            target is null)
        {
            return false;
        }

        double x = onScreenSpatialState.ConfirmedPosition.X - target.Position.X;
        double y = onScreenSpatialState.ConfirmedPosition.Y - target.Position.Y;
        if ((x * x) + (y * y) > range.Value * range.Value)
        {
            return false;
        }

        actionSpec = new GameActionSpec(
            actorId,
            selectedInteraction.Binding,
            selectedInteraction.Arguments);
        return true;
    }
}
