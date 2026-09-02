using Alice.Activities;
using Alice.Navigation;

namespace Alice.NpcExecution;

/// <summary>Owner-side correlation glue from confirmed NPC motion into one Travel Activity.</summary>
public sealed class NpcTravelActivityProjector
{
    private readonly ActivityRuntime _activity;
    private readonly CanonicalRoute _route;
    private readonly NpcTravelStartedFact _startedFact;
    private WorldPosition _lastAcceptedConfirmedPosition;
    private SimTime _lastAcceptedTime;
    private bool _closed;

    public NpcTravelActivityProjector(
        ActivityRuntime activity,
        CanonicalRoute route,
        NpcTravelStartedFact startedFact)
        : this(activity, route, CreateDirectStartHandoff(activity, route, startedFact), startedFact)
    {
    }

    public NpcTravelActivityProjector(
        ActivityRuntime activity,
        CanonicalRoute route,
        TravelProgressSourceHandoff handoff,
        NpcTravelStartedFact startedFact)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentNullException.ThrowIfNull(startedFact);
        TravelActivitySpec spec = activity.TravelSpec ??
            throw new ArgumentException("Projected Travel must retain its specification.", nameof(activity));
        if (activity.Kind != ActivityRuntimeKind.Travel ||
            activity.ProgressMode != ActivityProgressMode.Projected ||
            activity.Status != ActivityRuntimeStatus.Active ||
            activity.Progress.CompletedWorkTicks != handoff.CompletedWorkTicks ||
            activity.Progress.TotalWorkTicks != route.TotalTraversalDuration.Ticks ||
            activity.OffscreenSpatialState is not null ||
            activity.ActorId != startedFact.ActorId ||
            activity.ActivityId != startedFact.ActivityId ||
            activity.TargetRef != startedFact.TargetRef ||
            spec.RouteId != route.RouteId ||
            handoff.ActivityId != activity.ActivityId ||
            handoff.ActorId != activity.ActorId ||
            handoff.TargetRef != activity.TargetRef ||
            handoff.RouteId != route.RouteId ||
            handoff.DestinationProgressMode != ActivityProgressMode.Projected ||
            handoff.TotalWorkTicks != activity.Progress.TotalWorkTicks ||
            handoff.HandoffTime != activity.LastProgressTime ||
            startedFact.ConfirmedStartingPosition != handoff.CanonicalPosition ||
            CanonicalRouteProjection.ProjectCompletedTraversalTicks(route, startedFact.ConfirmedStartingPosition, handoff.CompletedWorkTicks) != handoff.CompletedWorkTicks)
        {
            throw new ArgumentException("Projected Travel, route and started fact must correlate exactly.");
        }

        _activity = activity;
        _route = route;
        _startedFact = startedFact;
        _lastAcceptedConfirmedPosition = startedFact.ConfirmedStartingPosition;
        _lastAcceptedTime = handoff.HandoffTime;
    }

    public WorldPosition LastAcceptedConfirmedPosition => _lastAcceptedConfirmedPosition;

    public bool TryApplyMotion(NpcTravelMotionFact fact, SimTime time)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (_closed ||
            !Correlates(fact.ActorId, fact.ActivityId, fact.NavigationOperationId, fact.TargetRef) ||
            fact.PreviousConfirmedPosition != _lastAcceptedConfirmedPosition ||
            time.CompareTo(_lastAcceptedTime) < 0 ||
            _activity.Kind != ActivityRuntimeKind.Travel ||
            _activity.ProgressMode != ActivityProgressMode.Projected ||
            _activity.Status != ActivityRuntimeStatus.Active)
        {
            return false;
        }

        long completedTicks = CanonicalRouteProjection.ProjectCompletedTraversalTicks(
            _route,
            fact.ResultingConfirmedPosition,
            _activity.Progress.CompletedWorkTicks);
        _activity.ReportProjectedProgress(completedTicks, time);
        _lastAcceptedConfirmedPosition = fact.ResultingConfirmedPosition;
        _lastAcceptedTime = time;
        return true;
    }

    public bool TryApplyArrived(
        NpcTravelTerminalFact fact,
        TravelCompletionSpatialSnapshot completionSnapshot,
        SimTime time,
        out TravelActivityResult? result)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(completionSnapshot);
        result = null;
        bool endpointReady = _activity.Status == ActivityRuntimeStatus.AwaitingSettlement &&
            _activity.Progress.CompletedWorkTicks == _activity.Progress.TotalWorkTicks;
        if (_closed ||
            !Correlates(fact.ActorId, fact.ActivityId, fact.NavigationOperationId, fact.TargetRef) ||
            fact.Status != NavigationStatus.Arrived ||
            fact.ConfirmedActorPosition != _lastAcceptedConfirmedPosition ||
            completionSnapshot.TargetRef != _startedFact.TargetRef ||
            time.CompareTo(_lastAcceptedTime) < 0 ||
            _activity.Kind != ActivityRuntimeKind.Travel ||
            _activity.ProgressMode != ActivityProgressMode.Projected ||
            (_activity.Status != ActivityRuntimeStatus.Active && !endpointReady))
        {
            return false;
        }

        if (!endpointReady)
        {
            _activity.ReportProjectedProgress(_route.TotalTraversalDuration.Ticks, time);
        }

        result = _activity.CheckProjectedTravelCompletion(completionSnapshot, fact.ConfirmedActorPosition);
        _lastAcceptedConfirmedPosition = fact.ConfirmedActorPosition;
        _lastAcceptedTime = time;
        return true;
    }

    public bool TryReleaseProjection(
        NpcTravelProjectionReleasedFact fact,
        SimTime handoffTime,
        out TravelProgressSourceHandoff? handoff)
    {
        ArgumentNullException.ThrowIfNull(fact);
        handoff = null;
        if (_closed ||
            !Correlates(fact.ActorId, fact.ActivityId, fact.NavigationOperationId, fact.TargetRef) ||
            fact.ConfirmedFinalPosition != _lastAcceptedConfirmedPosition ||
            handoffTime != _lastAcceptedTime ||
            handoffTime != _activity.LastProgressTime ||
            _activity.Status != ActivityRuntimeStatus.Active ||
            _activity.ProgressMode != ActivityProgressMode.Projected)
        {
            return false;
        }

        TravelProgressSourceHandoff released = _activity.SwitchTravelToOffscreen(
            fact.ConfirmedFinalPosition,
            handoffTime);
        _closed = true;
        handoff = released;
        return true;
    }

    private static TravelProgressSourceHandoff CreateDirectStartHandoff(
        ActivityRuntime activity,
        CanonicalRoute route,
        NpcTravelStartedFact startedFact)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(startedFact);
        if (activity.Progress.CompletedWorkTicks != 0 ||
            startedFact.ConfirmedStartingPosition != route.Segments[0].Start)
        {
            throw new ArgumentException("Direct projected Travel must start at zero route Progress.");
        }

        return new TravelProgressSourceHandoff(
            activity.ActivityId,
            activity.ActorId,
            activity.TargetRef,
            route.RouteId,
            ActivityProgressMode.Projected,
            activity.LastProgressTime,
            activity.Progress.CompletedWorkTicks,
            activity.Progress.TotalWorkTicks,
            startedFact.ConfirmedStartingPosition);
    }

    private bool Correlates(
        Alice.Actors.ActorId actorId,
        ActivityId activityId,
        NavigationOperationId operationId,
        Alice.World.TargetRef targetRef)
    {
        return actorId == _startedFact.ActorId &&
            activityId == _startedFact.ActivityId &&
            operationId == _startedFact.NavigationOperationId &&
            targetRef == _startedFact.TargetRef;
    }
}
