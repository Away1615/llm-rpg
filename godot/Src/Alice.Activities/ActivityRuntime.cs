using Alice.Actors;
using Alice.Authority;
using Alice.Interaction;
using Alice.Navigation;
using Alice.Validation;
using Alice.World;

namespace Alice.Activities;

/// <summary>Single synchronous owner of one closed Damage or Travel operational lifecycle.</summary>
public sealed class ActivityRuntime
{
    private readonly DamageActivityTiming? _damageTiming;
    private readonly CanonicalRoute? _canonicalRoute;
    private readonly GameActionId? _gameActionId;
    private readonly GameActionSpec? _action;
    private readonly IActivityDependencySnapshot _dependencySnapshot;
    private readonly TravelActivitySpec? _travelSpec;
    private ActivityProgress _progress;
    private SimTime _startTime;
    private SimTime _lastProgressTime;
    private SimTime _expectedEnd;
    private SimTime? _nextWakeAt;
    private ActivityRuntimeStatus _status;
    private ActivityProgressMode _progressMode;
    private DamageCommitReceipt? _damageCommitReceipt;
    private OffscreenSpatialState? _offscreenSpatialState;
    private TravelActivityResult? _travelActivityResult;

    public ActivityRuntime(
        ActivityId activityId,
        GameActionId gameActionId,
        GameActionSpec action,
        SimTime startTime,
        DamageActivityTiming timing,
        ActivityProgressMode initialProgressMode,
        ActivityDependencySnapshot dependencySnapshot,
        DamageValidationContext startupContext)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(gameActionId);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(dependencySnapshot);
        ArgumentNullException.ThrowIfNull(startupContext);
        if (activityId.Value == gameActionId.Value)
        {
            throw new ArgumentException("Game action identity must remain distinct from Activity identity.", nameof(gameActionId));
        }
        if (action.Arguments is not DamageActionArguments)
        {
            throw new ArgumentException("ActivityRuntime requires Damage action arguments.", nameof(action));
        }
        if (!Enum.IsDefined(initialProgressMode))
        {
            throw new ArgumentOutOfRangeException(nameof(initialProgressMode));
        }

        EnsureStartupCorrelation(action, dependencySnapshot, startupContext);

        ActivityId = activityId;
        Kind = ActivityRuntimeKind.Damage;
        _gameActionId = gameActionId;
        ActorId = action.ActorId;
        _action = action;
        TargetRef = action.Binding.ContractRef.TargetRef;
        _startTime = startTime;
        _lastProgressTime = startTime;
        _expectedEnd = startTime.Add(timing.TotalDuration);
        _damageTiming = timing;
        _progress = new ActivityProgress(0, timing.TotalDuration.Ticks);
        _status = ActivityRuntimeStatus.Active;
        _progressMode = initialProgressMode;
        _dependencySnapshot = dependencySnapshot;
        _nextWakeAt = initialProgressMode == ActivityProgressMode.OffscreenSimTime
            ? startTime.Add(timing.HitPointOffset)
            : null;
    }

    private ActivityRuntime(
        ActivityId activityId,
        TravelActivitySpec spec,
        SimTime startTime,
        TravelActivityDependencySnapshot dependencySnapshot,
        OffscreenSpatialState anchoredState,
        CanonicalRoute route)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(dependencySnapshot);
        ArgumentNullException.ThrowIfNull(anchoredState);
        ArgumentNullException.ThrowIfNull(route);
        EnsureTravelStartupCorrelation(spec, dependencySnapshot, anchoredState, route);

        OffscreenSpatialState travellingState = OffscreenSpatialProgression.StartTravel(anchoredState, route, startTime);
        OffscreenTravelState travel = travellingState.TravelState ??
            throw new InvalidOperationException("Travel startup did not produce off-screen route state.");

        ActivityId = activityId;
        Kind = ActivityRuntimeKind.Travel;
        ActorId = spec.ActorId;
        TargetRef = spec.TargetRef;
        _startTime = startTime;
        _lastProgressTime = startTime;
        _expectedEnd = travel.ExpectedArrival;
        _progress = new ActivityProgress(0, route.TotalTraversalDuration.Ticks);
        _status = ActivityRuntimeStatus.Active;
        _progressMode = ActivityProgressMode.OffscreenSimTime;
        _dependencySnapshot = dependencySnapshot;
        _travelSpec = spec;
        _canonicalRoute = route;
        _offscreenSpatialState = travellingState;
        _nextWakeAt = travel.ExpectedArrival;
    }

    private ActivityRuntime(
        ActivityId activityId,
        TravelActivitySpec spec,
        SimTime startTime,
        TravelActivityDependencySnapshot dependencySnapshot,
        CanonicalRoute route,
        WorldPosition confirmedStartingPosition)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(dependencySnapshot);
        ArgumentNullException.ThrowIfNull(route);
        EnsureProjectedTravelStartupCorrelation(spec, dependencySnapshot, route, confirmedStartingPosition);

        ActivityId = activityId;
        Kind = ActivityRuntimeKind.Travel;
        ActorId = spec.ActorId;
        TargetRef = spec.TargetRef;
        _startTime = startTime;
        _lastProgressTime = startTime;
        _expectedEnd = startTime.Add(route.TotalTraversalDuration);
        _progress = new ActivityProgress(0, route.TotalTraversalDuration.Ticks);
        _status = ActivityRuntimeStatus.Active;
        _progressMode = ActivityProgressMode.Projected;
        _dependencySnapshot = dependencySnapshot;
        _travelSpec = spec;
        _canonicalRoute = route;
        _nextWakeAt = null;
    }

    public static ActivityRuntime StartTravel(
        ActivityId activityId,
        TravelActivitySpec spec,
        SimTime startTime,
        TravelActivityDependencySnapshot dependencySnapshot,
        OffscreenSpatialState anchoredState,
        CanonicalRoute route)
    {
        return new ActivityRuntime(activityId, spec, startTime, dependencySnapshot, anchoredState, route);
    }

    public static ActivityRuntime StartProjectedTravel(
        ActivityId activityId,
        TravelActivitySpec spec,
        SimTime startTime,
        TravelActivityDependencySnapshot dependencySnapshot,
        CanonicalRoute route,
        WorldPosition confirmedStartingPosition)
    {
        return new ActivityRuntime(activityId, spec, startTime, dependencySnapshot, route, confirmedStartingPosition);
    }

    public ActivityId ActivityId { get; }
    public ActivityRuntimeKind Kind { get; }
    public GameActionId? GameActionId => _gameActionId;
    public ActorId ActorId { get; }
    public GameActionSpec? Action => _action;
    public TargetRef TargetRef { get; }
    public SimTime StartTime => _startTime;
    public SimTime LastProgressTime => _lastProgressTime;
    public SimTime ExpectedEnd => _expectedEnd;
    public SimTime? NextWakeAt => _nextWakeAt;
    public ActivityProgress Progress => _progress;
    public ActivityRuntimeStatus Status => _status;
    public ActivityProgressMode ProgressMode => _progressMode;
    public IActivityDependencySnapshot DependencySnapshot => _dependencySnapshot;
    public DamageCommitReceipt? DamageCommitReceipt => _damageCommitReceipt;
    public TravelActivitySpec? TravelSpec => _travelSpec;
    public OffscreenSpatialState? OffscreenSpatialState => _offscreenSpatialState;
    public TravelActivityResult? TravelResult => _travelActivityResult;

    public TravelProgressSourceHandoff SwitchTravelToProjected(SimTime handoffTime)
    {
        EnsureTravelKind();
        if (_status != ActivityRuntimeStatus.Active ||
            _progressMode != ActivityProgressMode.OffscreenSimTime ||
            _progress.CompletedWorkTicks >= _progress.TotalWorkTicks ||
            handoffTime != _lastProgressTime)
        {
            throw new InvalidOperationException("Only active off-screen Travel at its exact progress time can project.");
        }

        CanonicalRoute route = _canonicalRoute ?? throw new InvalidOperationException("Travel activity has no canonical route.");
        OffscreenSpatialState spatialState = _offscreenSpatialState ?? throw new InvalidOperationException("Travel activity has no off-screen spatial state.");
        OffscreenTravelState travel = spatialState.TravelState ?? throw new InvalidOperationException("Travel activity has no route state.");
        if (travel.LastProgressSimTime != handoffTime)
        {
            throw new InvalidOperationException("Off-screen state is not advanced to the exact handoff time.");
        }

        WorldPosition canonicalPosition = OffscreenSpatialProgression.DerivePosition(spatialState, route);
        TravelProgressSourceHandoff handoff = CreateTravelHandoff(
            ActivityProgressMode.Projected,
            handoffTime,
            canonicalPosition);

        _offscreenSpatialState = null;
        _progressMode = ActivityProgressMode.Projected;
        _nextWakeAt = null;
        return handoff;
    }

    public TravelProgressSourceHandoff SwitchTravelToOffscreen(
        WorldPosition confirmedPosition,
        SimTime handoffTime)
    {
        EnsureTravelKind();
        if (_status != ActivityRuntimeStatus.Active ||
            _progressMode != ActivityProgressMode.Projected ||
            _progress.CompletedWorkTicks >= _progress.TotalWorkTicks ||
            handoffTime != _lastProgressTime ||
            _offscreenSpatialState is not null)
        {
            throw new InvalidOperationException("Only active projected Travel at its exact progress time can return off-screen.");
        }

        CanonicalRoute route = _canonicalRoute ?? throw new InvalidOperationException("Travel activity has no canonical route.");
        long projectedTicks = CanonicalRouteProjection.ProjectCompletedTraversalTicks(
            route,
            confirmedPosition,
            _progress.CompletedWorkTicks);
        if (projectedTicks != _progress.CompletedWorkTicks)
        {
            throw new ArgumentException("Confirmed handoff position must map to the already accepted Travel Progress.", nameof(confirmedPosition));
        }

        OffscreenSpatialState reconstituted = OffscreenSpatialProgression.ReconstituteTravel(
            ActorId,
            route,
            _progress.CompletedWorkTicks,
            handoffTime);
        OffscreenTravelState travel = reconstituted.TravelState ?? throw new InvalidOperationException("Reconstituted Travel has no route state.");
        WorldPosition canonicalPosition = OffscreenSpatialProgression.DerivePosition(reconstituted, route);
        TravelProgressSourceHandoff handoff = CreateTravelHandoff(
            ActivityProgressMode.OffscreenSimTime,
            handoffTime,
            canonicalPosition);

        _offscreenSpatialState = reconstituted;
        _progressMode = ActivityProgressMode.OffscreenSimTime;
        _expectedEnd = travel.ExpectedArrival;
        _nextWakeAt = travel.ExpectedArrival;
        return handoff;
    }

    public void AdvanceOffscreenTo(SimTime time)
    {
        EnsureActiveMode(ActivityProgressMode.OffscreenSimTime);
        EnsureNotBackward(time);
        if (time == _lastProgressTime)
        {
            return;
        }

        if (Kind == ActivityRuntimeKind.Travel)
        {
            AdvanceTravelOffscreenTo(time);
            return;
        }

        long elapsed = checked(time.Ticks - _lastProgressTime.Ticks);
        AdvanceDamageByOffscreenElapsed(time, elapsed);
    }

    public void ReportProjectedProgress(long completedWorkTicks, SimTime time)
    {
        EnsureActiveMode(ActivityProgressMode.Projected);
        EnsureNotBackward(time);
        if (completedWorkTicks < _progress.CompletedWorkTicks || completedWorkTicks > _progress.TotalWorkTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(completedWorkTicks));
        }

        if (completedWorkTicks == _progress.CompletedWorkTicks)
        {
            return;
        }

        if (Kind == ActivityRuntimeKind.Travel)
        {
            _progress = new ActivityProgress(completedWorkTicks, _progress.TotalWorkTicks);
            _lastProgressTime = time;
            if (completedWorkTicks == _progress.TotalWorkTicks)
            {
                _status = ActivityRuntimeStatus.AwaitingSettlement;
                _nextWakeAt = null;
            }

            return;
        }

        long boundary = NextBoundaryWorkTicks();
        if (completedWorkTicks >= boundary)
        {
            _progress = new ActivityProgress(boundary, _progress.TotalWorkTicks);
            _lastProgressTime = time;
            if (_damageCommitReceipt is null)
            {
                _status = ActivityRuntimeStatus.AwaitingSettlement;
                _nextWakeAt = null;
            }
            else
            {
                _status = ActivityRuntimeStatus.Completed;
                _nextWakeAt = null;
            }

            return;
        }

        _progress = new ActivityProgress(completedWorkTicks, _progress.TotalWorkTicks);
        _lastProgressTime = time;
    }

    public DamageCommitResult SettleHit(DamageAuthorityRuntime authorityRuntime, DamageValidationContext currentContext)
    {
        EnsureDamageKind();
        if (_status != ActivityRuntimeStatus.AwaitingSettlement)
        {
            throw new InvalidOperationException("Only a hit-ready activity can settle.");
        }

        ArgumentNullException.ThrowIfNull(authorityRuntime);
        ArgumentNullException.ThrowIfNull(currentContext);
        GameActionSpec action = _action ?? throw new InvalidOperationException("Damage activity has no action.");
        GameActionId gameActionId = _gameActionId ?? throw new InvalidOperationException("Damage activity has no action identity.");
        DamageActivityTiming timing = _damageTiming ?? throw new InvalidOperationException("Damage activity has no timing.");
        DamageCommitResult result = authorityRuntime.TryCommitDamage(action, gameActionId, currentContext);
        if (!result.IsCommitted || result.Receipt is null)
        {
            _status = ActivityRuntimeStatus.Rejected;
            _nextWakeAt = null;
            return result;
        }

        _damageCommitReceipt = result.Receipt;
        if (timing.HitPointOffset.Ticks == timing.TotalDuration.Ticks)
        {
            _status = ActivityRuntimeStatus.Completed;
            _nextWakeAt = null;
            return result;
        }

        _status = ActivityRuntimeStatus.Active;
        _nextWakeAt = _progressMode == ActivityProgressMode.OffscreenSimTime
            ? _lastProgressTime.Add(new SimDuration(timing.TotalDuration.Ticks - _progress.CompletedWorkTicks))
            : null;
        return result;
    }

    public TravelActivityResult CheckTravelCompletion(TravelCompletionSpatialSnapshot? snapshot)
    {
        EnsureTravelKind();
        if (_progressMode != ActivityProgressMode.OffscreenSimTime)
        {
            throw new InvalidOperationException("Off-screen Travel completion requires the off-screen progress source.");
        }

        if (_status != ActivityRuntimeStatus.AwaitingSettlement)
        {
            throw new InvalidOperationException("Only endpoint-ready Travel can check completion.");
        }

        if (snapshot is not null && snapshot.TargetRef != TargetRef)
        {
            throw new ArgumentException("Travel completion evidence must name the exact target.", nameof(snapshot));
        }

        CanonicalRoute route = _canonicalRoute ?? throw new InvalidOperationException("Travel activity has no canonical route.");
        OffscreenSpatialState spatialState = _offscreenSpatialState ?? throw new InvalidOperationException("Travel activity has no off-screen spatial state.");
        TravelActivitySpec spec = _travelSpec ?? throw new InvalidOperationException("Travel activity has no specification.");
        WorldPosition actorPosition = OffscreenSpatialProgression.DerivePosition(spatialState, route);
        bool reached = snapshot is not null && InteractionTargetReachability.IsWithinRange(actorPosition, snapshot.CurrentTargetPosition, spec.InteractionRange);
        var result = new TravelActivityResult(
            ActivityId,
            ActorId,
            TargetRef,
            reached ? TravelActivityResultKind.Reached : TravelActivityResultKind.PredicateUnsatisfied,
            actorPosition);

        _travelActivityResult = result;
        _status = reached ? ActivityRuntimeStatus.Completed : ActivityRuntimeStatus.Rejected;
        _nextWakeAt = null;
        return result;
    }

    public TravelActivityResult CheckProjectedTravelCompletion(
        TravelCompletionSpatialSnapshot? snapshot,
        WorldPosition confirmedActorPosition)
    {
        EnsureTravelKind();
        if (_progressMode != ActivityProgressMode.Projected || _status != ActivityRuntimeStatus.AwaitingSettlement)
        {
            throw new InvalidOperationException("Only endpoint-ready projected Travel can check completion.");
        }

        if (!double.IsFinite(confirmedActorPosition.X) || !double.IsFinite(confirmedActorPosition.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(confirmedActorPosition));
        }

        if (snapshot is not null && snapshot.TargetRef != TargetRef)
        {
            throw new ArgumentException("Travel completion evidence must name the exact target.", nameof(snapshot));
        }

        TravelActivitySpec spec = _travelSpec ?? throw new InvalidOperationException("Travel activity has no specification.");
        bool reached = snapshot is not null && InteractionTargetReachability.IsWithinRange(
            confirmedActorPosition,
            snapshot.CurrentTargetPosition,
            spec.InteractionRange);
        var result = new TravelActivityResult(
            ActivityId,
            ActorId,
            TargetRef,
            reached ? TravelActivityResultKind.Reached : TravelActivityResultKind.PredicateUnsatisfied,
            confirmedActorPosition);

        _travelActivityResult = result;
        _status = reached ? ActivityRuntimeStatus.Completed : ActivityRuntimeStatus.Rejected;
        _nextWakeAt = null;
        return result;
    }

    public void Cancel()
    {
        if (_status is not ActivityRuntimeStatus.Active and not ActivityRuntimeStatus.AwaitingSettlement and not ActivityRuntimeStatus.Suspended)
        {
            throw new InvalidOperationException("Only a non-terminal activity can cancel.");
        }

        _status = ActivityRuntimeStatus.Cancelled;
        _nextWakeAt = null;
    }

    public void Suspend()
    {
        if (_status != ActivityRuntimeStatus.Active)
        {
            throw new InvalidOperationException("Only an active activity can suspend.");
        }

        _status = ActivityRuntimeStatus.Suspended;
        _nextWakeAt = null;
    }

    public void Resume(SimTime time)
    {
        if (_status != ActivityRuntimeStatus.Suspended)
        {
            throw new InvalidOperationException("Only a suspended activity can resume.");
        }

        EnsureNotBackward(time);
        if (Kind == ActivityRuntimeKind.Travel)
        {
            ResumeTravel(time);
            return;
        }

        _lastProgressTime = time;
        _status = ActivityRuntimeStatus.Active;
        _nextWakeAt = _progressMode == ActivityProgressMode.OffscreenSimTime ? NextWakeAtFromCurrentProgress() : null;
    }

    public void SwitchProgressMode(ActivityProgressMode progressMode, SimTime time)
    {
        EnsureDamageKind();
        if (_status != ActivityRuntimeStatus.Active)
        {
            throw new InvalidOperationException("Only an active activity can switch progress source.");
        }

        if (!Enum.IsDefined(progressMode) || progressMode == _progressMode)
        {
            throw new ArgumentOutOfRangeException(nameof(progressMode));
        }

        EnsureNotBackward(time);
        if (_progressMode == ActivityProgressMode.OffscreenSimTime && time != _lastProgressTime)
        {
            throw new InvalidOperationException("Off-screen progress must advance to the exact handoff time first.");
        }

        _progressMode = progressMode;
        _lastProgressTime = time;
        _nextWakeAt = progressMode == ActivityProgressMode.OffscreenSimTime ? NextWakeAtFromCurrentProgress() : null;
    }

    private void AdvanceTravelOffscreenTo(SimTime requestedTime)
    {
        CanonicalRoute route = _canonicalRoute ?? throw new InvalidOperationException("Travel activity has no canonical route.");
        OffscreenSpatialState currentState = _offscreenSpatialState ?? throw new InvalidOperationException("Travel activity has no off-screen spatial state.");
        OffscreenSpatialState advancedState = OffscreenSpatialProgression.AdvanceTo(currentState, route, requestedTime);
        OffscreenTravelState travel = advancedState.TravelState ?? throw new InvalidOperationException("Travel progression lost route state.");
        long completedTicks = checked(travel.LastProgressSimTime.Ticks - travel.StartSimTime.Ticks);
        var progress = new ActivityProgress(completedTicks, route.TotalTraversalDuration.Ticks);
        bool endpointReached = completedTicks == route.TotalTraversalDuration.Ticks;

        _offscreenSpatialState = advancedState;
        _progress = progress;
        _lastProgressTime = travel.LastProgressSimTime;
        _nextWakeAt = endpointReached ? null : travel.ExpectedArrival;
        _status = endpointReached ? ActivityRuntimeStatus.AwaitingSettlement : ActivityRuntimeStatus.Active;
    }

    private void ResumeTravel(SimTime time)
    {
        long suspendedTicks = checked(time.Ticks - _lastProgressTime.Ticks);
        SimTime shiftedActivityStart = ShiftTime(_startTime, suspendedTicks);
        SimTime shiftedActivityLast = ShiftTime(_lastProgressTime, suspendedTicks);
        SimTime shiftedExpectedEnd = ShiftTime(_expectedEnd, suspendedTicks);
        if (_progressMode == ActivityProgressMode.Projected)
        {
            _startTime = shiftedActivityStart;
            _lastProgressTime = shiftedActivityLast;
            _expectedEnd = shiftedExpectedEnd;
            _status = ActivityRuntimeStatus.Active;
            _nextWakeAt = null;
            return;
        }

        OffscreenSpatialState spatialState = _offscreenSpatialState ?? throw new InvalidOperationException("Travel activity has no off-screen spatial state.");
        OffscreenTravelState travel = spatialState.TravelState ?? throw new InvalidOperationException("Travel activity has no route state.");
        SimTime shiftedStart = ShiftTime(travel.StartSimTime, suspendedTicks);
        SimTime shiftedLast = ShiftTime(travel.LastProgressSimTime, suspendedTicks);
        SimTime shiftedArrival = ShiftTime(travel.ExpectedArrival, suspendedTicks);
        var shiftedTravel = new OffscreenTravelState(
            travel.RouteId,
            travel.SegmentIndex,
            travel.SegmentProgress,
            shiftedStart,
            shiftedLast,
            shiftedArrival);
        var shiftedSpatialState = new OffscreenSpatialState(spatialState.ActorId, null, shiftedTravel);
        _offscreenSpatialState = shiftedSpatialState;
        _startTime = shiftedActivityStart;
        _lastProgressTime = shiftedLast;
        _expectedEnd = shiftedExpectedEnd;
        _status = ActivityRuntimeStatus.Active;
        _nextWakeAt = shiftedArrival;
    }

    private static SimTime ShiftTime(SimTime time, long ticks)
    {
        return new SimTime(checked(time.Ticks + ticks));
    }

    private TravelProgressSourceHandoff CreateTravelHandoff(
        ActivityProgressMode destinationMode,
        SimTime handoffTime,
        WorldPosition canonicalPosition)
    {
        TravelActivitySpec spec = _travelSpec ?? throw new InvalidOperationException("Travel activity has no specification.");
        return new TravelProgressSourceHandoff(
            ActivityId,
            ActorId,
            TargetRef,
            spec.RouteId,
            destinationMode,
            handoffTime,
            _progress.CompletedWorkTicks,
            _progress.TotalWorkTicks,
            canonicalPosition);
    }

    private void AdvanceDamageByOffscreenElapsed(SimTime requestedTime, long elapsed)
    {
        long boundary = NextBoundaryWorkTicks();
        long remaining = checked(boundary - _progress.CompletedWorkTicks);
        if (elapsed >= remaining)
        {
            _progress = new ActivityProgress(boundary, _progress.TotalWorkTicks);
            _lastProgressTime = _lastProgressTime.Add(new SimDuration(remaining));
            _nextWakeAt = null;
            _status = _damageCommitReceipt is null ? ActivityRuntimeStatus.AwaitingSettlement : ActivityRuntimeStatus.Completed;
            return;
        }

        long completed = checked(_progress.CompletedWorkTicks + elapsed);
        _progress = new ActivityProgress(completed, _progress.TotalWorkTicks);
        _lastProgressTime = requestedTime;
        _nextWakeAt = NextWakeAtFromCurrentProgress();
    }

    private SimTime NextWakeAtFromCurrentProgress()
    {
        long remaining = checked(NextBoundaryWorkTicks() - _progress.CompletedWorkTicks);
        return _lastProgressTime.Add(new SimDuration(remaining));
    }

    private long NextBoundaryWorkTicks()
    {
        DamageActivityTiming timing = _damageTiming ?? throw new InvalidOperationException("Damage activity has no timing.");
        return _damageCommitReceipt is null ? timing.HitPointOffset.Ticks : timing.TotalDuration.Ticks;
    }

    private void EnsureActiveMode(ActivityProgressMode expectedMode)
    {
        if (_status != ActivityRuntimeStatus.Active || _progressMode != expectedMode)
        {
            throw new InvalidOperationException("Activity is not active for this progress provider.");
        }
    }

    private void EnsureNotBackward(SimTime time)
    {
        if (time.CompareTo(_lastProgressTime) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(time), "Activity progress time cannot move backward.");
        }
    }

    private void EnsureDamageKind()
    {
        if (Kind != ActivityRuntimeKind.Damage)
        {
            throw new InvalidOperationException("Operation is available only for Damage activities.");
        }
    }

    private void EnsureTravelKind()
    {
        if (Kind != ActivityRuntimeKind.Travel)
        {
            throw new InvalidOperationException("Operation is available only for Travel activities.");
        }
    }

    private static void EnsureStartupCorrelation(
        GameActionSpec action,
        ActivityDependencySnapshot dependencySnapshot,
        DamageValidationContext startupContext)
    {
        if (action.ActorId != startupContext.ActorState.Identity.ActorId ||
            dependencySnapshot.ExpectedContractVersion != action.Binding.ExpectedVersion ||
            dependencySnapshot.StartingInventoryVersion != startupContext.ActorState.Inventory.Version ||
            dependencySnapshot.StartingEquipmentVersion != startupContext.ActorState.Equipment.Version ||
            (action.Binding.InstrumentRef is null) != (dependencySnapshot.SelectedInstrumentVersion is null))
        {
            throw new ArgumentException("Activity startup evidence does not correlate to the selected action.");
        }
    }

    private static void EnsureTravelStartupCorrelation(
        TravelActivitySpec spec,
        TravelActivityDependencySnapshot dependencySnapshot,
        OffscreenSpatialState anchoredState,
        CanonicalRoute route)
    {
        EnsureTravelInputCorrelation(spec, dependencySnapshot, route);
        if (anchoredState.CurrentAnchor is null || anchoredState.TravelState is not null)
        {
            throw new ArgumentException("Travel must start from one anchored off-screen state.", nameof(anchoredState));
        }

        if (anchoredState.ActorId != spec.ActorId)
        {
            throw new ArgumentException("Travel Actor must match the anchored off-screen state.", nameof(anchoredState));
        }

    }

    private static void EnsureProjectedTravelStartupCorrelation(
        TravelActivitySpec spec,
        TravelActivityDependencySnapshot dependencySnapshot,
        CanonicalRoute route,
        WorldPosition confirmedStartingPosition)
    {
        EnsureTravelInputCorrelation(spec, dependencySnapshot, route);
        if (!double.IsFinite(confirmedStartingPosition.X) || !double.IsFinite(confirmedStartingPosition.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(confirmedStartingPosition));
        }

        if (confirmedStartingPosition != route.Segments[0].Start)
        {
            throw new ArgumentException("Projected Travel must start at the canonical route start.", nameof(confirmedStartingPosition));
        }
    }

    private static void EnsureTravelInputCorrelation(
        TravelActivitySpec spec,
        TravelActivityDependencySnapshot dependencySnapshot,
        CanonicalRoute route)
    {
        if (spec.RouteId != dependencySnapshot.RouteId || spec.RouteId != route.RouteId)
        {
            throw new ArgumentException("Travel specification, dependency evidence and route must share one RouteId.");
        }

        if (spec.TargetRef != dependencySnapshot.TargetRef)
        {
            throw new ArgumentException("Travel specification and dependency evidence must share one TargetRef.");
        }

        WorldPosition finalEndpoint = route.Segments[route.Segments.Count - 1].End;
        if (!InteractionTargetReachability.IsWithinRange(finalEndpoint, dependencySnapshot.ObservedTargetPosition, spec.InteractionRange))
        {
            throw new ArgumentException("Canonical route endpoint must correlate to the startup target position.", nameof(route));
        }
    }
}
