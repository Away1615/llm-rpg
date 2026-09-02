using Alice.Actors;
using Alice.Activities;
using Alice.Navigation;
using Alice.Presentation;
using Alice.World;
using Godot;

namespace Alice.NpcExecution;

/// <summary>A current NPC travel terminal fact correlated to caller-owned activity and navigation work.</summary>
public sealed record NpcTravelTerminalFact
{
    public NpcTravelTerminalFact(ActorId actorId, ActivityId activityId, NavigationOperationId navigationOperationId, TargetRef targetRef, NavigationStatus status, WorldPosition confirmedActorPosition)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(targetRef);
        if (navigationOperationId.Value <= 0 || status is not NavigationStatus.Arrived and not NavigationStatus.TargetInvalid and not NavigationStatus.NoPath and not NavigationStatus.Cancelled || !double.IsFinite(confirmedActorPosition.X) || !double.IsFinite(confirmedActorPosition.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(navigationOperationId));
        }

        ActorId = actorId;
        ActivityId = activityId;
        NavigationOperationId = navigationOperationId;
        TargetRef = targetRef;
        Status = status;
        ConfirmedActorPosition = confirmedActorPosition;
    }

    public ActorId ActorId { get; }
    public ActivityId ActivityId { get; }
    public NavigationOperationId NavigationOperationId { get; }
    public TargetRef TargetRef { get; }
    public NavigationStatus Status { get; }
    public WorldPosition ConfirmedActorPosition { get; }
}

/// <summary>Authored NPC composition consuming the shared entity-navigation and motion boundary.</summary>
public sealed partial class NpcEntity : CharacterBody2D
{
    private NavigationRuntime? _navigationRuntime;
    private OnScreenSpatialState? _onScreenSpatialState;
    private NpcTravelToRequest? _request;
    private IActorVisibleTargetSpatialQuery? _lastKnownTargetQuery;
    private EntityNavigationTargetAssignment? _targetAssignment;
    private uint _activeCollisionLayer;
    private uint _activeCollisionMask;
    private RoadTravelSpeedProfile? _travelSpeedProfile;
    private Vector2? _standingProjectionTarget;

    [Export] public NavigationAgent2D? NavigationAgent { get; set; }
    [Export] public GeometricActorMarker? Marker { get; set; }
    [Export] public string ActorIdentity { get; set; } = "npc-preview";
    [Export] public string DisplayName { get; set; } = "NPC";
    [Export] public float MovementSpeed { get; set; } = 140.0f;
    [Export] public float AgentRadius { get; set; } = 20.0f;
    [Export] public float PathDesiredDistance { get; set; } = 8.0f;
    [Export] public float TargetDesiredDistance { get; set; } = 8.0f;
    [Export] public bool StartProjectionActive { get; set; } = true;

    public bool IsProjectionActive => _onScreenSpatialState is not null;
    public ActorId DomainActorId => new(ActorIdentity);

    public event Action<NpcTravelTerminalFact>? TravelTerminated;

    public event Action<NpcTravelMotionFact>? TravelMotionProduced;

    public void ConfigureTravelSpeedProfile(RoadTravelSpeedProfile profile) =>
        _travelSpeedProfile = profile ?? throw new ArgumentNullException(nameof(profile));

    /// <summary>Projects caller-owned semantic presentation only; it does not choose or execute work.</summary>
    public void ApplyActivityPresentation(string? activityLabel)
    {
        if (activityLabel is not null && string.IsNullOrWhiteSpace(activityLabel))
            throw new ArgumentException("Activity label must be null or non-blank.", nameof(activityLabel));
        Marker!.SetActivity(activityLabel);
    }

    public void ApplyCognitionPresentation(string? route, Color color) =>
        Marker!.SetCognition(route, color);

    public void ConfigureMarker(Color fillColor) => Marker!.Configure(fillColor);
    public void SetHighlighted(bool highlighted) => Marker!.SetHighlighted(highlighted);
    public void SetActivityVisible(bool visible) => Marker!.SetActivityVisible(visible);

    public override void _Ready()
    {
        if (NavigationAgent is null || Marker is null || string.IsNullOrWhiteSpace(ActorIdentity))
        {
            throw new InvalidOperationException("NpcEntity requires navigation, geometric marker and actor identity references.");
        }

        NavigationAgent.Radius = AgentRadius;
        NavigationAgent.PathDesiredDistance = PathDesiredDistance;
        NavigationAgent.TargetDesiredDistance = TargetDesiredDistance;
        _navigationRuntime = new NavigationRuntime(new ActorId(ActorIdentity));
        _activeCollisionLayer = CollisionLayer;
        _activeCollisionMask = CollisionMask;
        if (StartProjectionActive)
        {
            _onScreenSpatialState = new OnScreenSpatialState(new WorldPosition(GlobalPosition.X, GlobalPosition.Y));
        }
        else
        {
            SetProjectionInactive();
        }
    }

    /// <summary>Applies a non-travel domain projection without selecting or executing semantic work.</summary>
    public bool TryApplyStandingProjection(ActorId actorId, WorldPosition position)
    {
        NavigationRuntime navigationRuntime = RequireNavigationRuntime();
        if (actorId != navigationRuntime.ActorId || _request is not null || _targetAssignment is not null || _lastKnownTargetQuery is not null)
            return false;
        float x = (float)position.X;
        float y = (float)position.Y;
        if (!float.IsFinite(x) || !float.IsFinite(y)) return false;
        Vector2 target = new(x, y);
        bool firstProjection = _onScreenSpatialState is null || !Visible;
        if (firstProjection) GlobalPosition = target;
        _standingProjectionTarget = target;
        _onScreenSpatialState = new OnScreenSpatialState(position);
        CollisionLayer = _activeCollisionLayer;
        CollisionMask = _activeCollisionMask;
        Visible = true;
        SetPhysicsProcess(true);
        return true;
    }

    /// <summary>Releases a standing projection without producing an activity terminal or Authority consequence.</summary>
    public bool TryReleaseStandingProjection(ActorId actorId, out WorldPosition confirmedPosition)
    {
        NavigationRuntime navigationRuntime = RequireNavigationRuntime();
        OnScreenSpatialState? state = _onScreenSpatialState;
        if (actorId != navigationRuntime.ActorId || state is null || _request is not null || _targetAssignment is not null || _lastKnownTargetQuery is not null)
        {
            confirmedPosition = default;
            return false;
        }
        confirmedPosition = state.ConfirmedPosition;
        _onScreenSpatialState = null;
        _standingProjectionTarget = null;
        SetProjectionInactive();
        return true;
    }

    /// <summary>Starts travel using only the caller's copied actor-visible target query.</summary>
    public bool TryStartTravel(NpcTravelToRequest request, IActorVisibleTargetSpatialQuery lastKnownTargetQuery)
    {
        return TryStartTravel(request, lastKnownTargetQuery, out _);
    }

    /// <summary>Starts travel and returns the confirmed current-operation start correlation.</summary>
    public bool TryStartTravel(
        NpcTravelToRequest request,
        IActorVisibleTargetSpatialQuery lastKnownTargetQuery,
        out NpcTravelStartedFact? startedFact)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lastKnownTargetQuery);
        startedFact = null;

        NavigationRuntime navigationRuntime = RequireNavigationRuntime();
        NavigationAgent2D navigationAgent = RequireNavigationAgent();
        if (_onScreenSpatialState is null)
        {
            return false;
        }

        var target = new EntityNavigationTarget(request.TargetRef, request.StopRange);
        if (!GodotNavigationTargetPreparation.TryPrepareEntity(
                target,
                lastKnownTargetQuery,
                out GodotPreparedNavigationTarget? preparedTarget) ||
            preparedTarget is null)
        {
            return false;
        }

        NavigationOperationId operationId = navigationRuntime.Begin(target);
        ApplyNavigationTarget(
            new WorldPosition(preparedTarget.TargetPosition.X, preparedTarget.TargetPosition.Y),
            navigationAgent,
            preparedTarget.TargetDesiredDistance);
        var assignment = new EntityNavigationTargetAssignment(
            operationId,
            preparedTarget.TargetPosition,
            preparedTarget.TargetDesiredDistance);

        _request = request;
        _lastKnownTargetQuery = lastKnownTargetQuery;
        _targetAssignment = assignment;
        startedFact = new NpcTravelStartedFact(
            navigationRuntime.ActorId,
            request.ActivityId,
            operationId,
            request.TargetRef,
            RequireOnScreenSpatialState().ConfirmedPosition);
        return true;
    }

    /// <summary>Activates this authored projection at one exact canonical Travel handoff.</summary>
    public bool TryActivateTravelProjection(
        TravelProgressSourceHandoff handoff,
        NpcTravelToRequest request,
        IActorVisibleTargetSpatialQuery lastKnownTargetQuery,
        out NpcTravelStartedFact? startedFact)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lastKnownTargetQuery);
        startedFact = null;
        NavigationRuntime navigationRuntime = RequireNavigationRuntime();
        NavigationAgent2D navigationAgent = RequireNavigationAgent();
        if (_onScreenSpatialState is not null || _request is not null || _targetAssignment is not null ||
            _lastKnownTargetQuery is not null || handoff.DestinationProgressMode != ActivityProgressMode.Projected ||
            handoff.ActorId != navigationRuntime.ActorId || handoff.ActivityId != request.ActivityId ||
            handoff.TargetRef != request.TargetRef)
        {
            return false;
        }

        float projectedX = (float)handoff.CanonicalPosition.X;
        float projectedY = (float)handoff.CanonicalPosition.Y;
        if (!float.IsFinite(projectedX) || !float.IsFinite(projectedY))
        {
            return false;
        }

        var target = new EntityNavigationTarget(request.TargetRef, request.StopRange);
        if (!GodotNavigationTargetPreparation.TryPrepareEntity(
                target,
                lastKnownTargetQuery,
                out GodotPreparedNavigationTarget? preparedTarget) ||
            preparedTarget is null)
        {
            return false;
        }

        var spatialState = new OnScreenSpatialState(handoff.CanonicalPosition);
        NavigationOperationId operationId = navigationRuntime.Begin(target);
        _onScreenSpatialState = spatialState;
        ApplyNavigationTarget(
            new WorldPosition(preparedTarget.TargetPosition.X, preparedTarget.TargetPosition.Y),
            navigationAgent,
            preparedTarget.TargetDesiredDistance);
        var assignment = new EntityNavigationTargetAssignment(
            operationId,
            preparedTarget.TargetPosition,
            preparedTarget.TargetDesiredDistance);

        GlobalPosition = new Vector2(projectedX, projectedY);
        _request = request;
        _lastKnownTargetQuery = lastKnownTargetQuery;
        _targetAssignment = assignment;
        CollisionLayer = _activeCollisionLayer;
        CollisionMask = _activeCollisionMask;
        Visible = true;
        SetPhysicsProcess(true);
        startedFact = new NpcTravelStartedFact(
            navigationRuntime.ActorId,
            request.ActivityId,
            operationId,
            request.TargetRef,
            spatialState.ConfirmedPosition);
        return true;
    }

    /// <summary>Stops one exact current projection without emitting a semantic terminal event.</summary>
    public bool TryReleaseTravelProjection(
        NpcTravelStartedFact startedFact,
        out NpcTravelProjectionReleasedFact? releasedFact)
    {
        ArgumentNullException.ThrowIfNull(startedFact);
        releasedFact = null;
        NavigationRuntime navigationRuntime = RequireNavigationRuntime();
        OnScreenSpatialState? spatialState = _onScreenSpatialState;
        NpcTravelToRequest? request = _request;
        EntityNavigationTargetAssignment? assignment = _targetAssignment;
        if (spatialState is null || request is null || assignment is null || _lastKnownTargetQuery is null ||
            startedFact.ActorId != navigationRuntime.ActorId ||
            startedFact.ActivityId != request.ActivityId ||
            startedFact.NavigationOperationId != assignment.NavigationOperationId ||
            startedFact.TargetRef != request.TargetRef ||
            navigationRuntime.CurrentOperation != startedFact.NavigationOperationId ||
            navigationRuntime.Status != NavigationStatus.Moving)
        {
            return false;
        }

        var fact = new NpcTravelProjectionReleasedFact(
            startedFact.ActorId,
            startedFact.ActivityId,
            startedFact.NavigationOperationId,
            startedFact.TargetRef,
            spatialState.ConfirmedPosition);
        if (!navigationRuntime.TryReportTerminalOutcome(startedFact.NavigationOperationId, NavigationStatus.Cancelled))
        {
            return false;
        }

        Velocity = Vector2.Zero;
        _request = null;
        _targetAssignment = null;
        _lastKnownTargetQuery = null;
        _onScreenSpatialState = null;
        SetProjectionInactive();
        releasedFact = fact;
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_request is null || _targetAssignment is null || _lastKnownTargetQuery is null)
        {
            AdvanceStandingProjection(delta);
            return;
        }

        NavigationRuntime navigationRuntime = RequireNavigationRuntime();
        NavigationOperationId operationId = navigationRuntime.CurrentOperation ?? throw new InvalidOperationException("NPC travel has no operation.");
        NavigationAgent2D navigationAgent = RequireNavigationAgent();
        if (GodotEntityNavigationOperationAdapter.TryClassifyTerminal(
                navigationRuntime, operationId, _lastKnownTargetQuery, navigationAgent, out NavigationStatus? terminalStatus) &&
            terminalStatus is NavigationStatus status)
        {
            Complete(operationId, status);
            return;
        }

        if (!GodotEntityNavigationOperationAdapter.TryStep(
                navigationRuntime, operationId, _lastKnownTargetQuery, navigationAgent, this,
                _targetAssignment, ResolveMovementSpeed(), delta, out EntityNavigationTargetAssignment? refreshedAssignment,
                out NavigationMotionStepResult? stepResult, out NavigationStatus? stepTerminalStatus))
        {
            if (stepTerminalStatus is NavigationStatus terminal)
            {
                Complete(operationId, terminal);
            }

            return;
        }

        _targetAssignment = refreshedAssignment;
        if (stepResult is not null)
        {
            OnScreenSpatialState spatialState = RequireOnScreenSpatialState();
            WorldPosition previousPosition = spatialState.ConfirmedPosition;
            spatialState.ApplyMotionResult(stepResult.MotionResult);
            NpcTravelToRequest request = _request ?? throw new InvalidOperationException("NPC travel request is required.");
            TravelMotionProduced?.Invoke(new NpcTravelMotionFact(
                navigationRuntime.ActorId,
                request.ActivityId,
                operationId,
                request.TargetRef,
                previousPosition,
                stepResult.MotionResult,
                spatialState.ConfirmedPosition));
        }
    }

    private void Complete(NavigationOperationId operationId, NavigationStatus status)
    {
        NpcTravelToRequest request = _request ?? throw new InvalidOperationException("NPC travel request is required.");
        NavigationRuntime navigationRuntime = RequireNavigationRuntime();
        if (!navigationRuntime.TryReportTerminalOutcome(operationId, status))
        {
            return;
        }

        Velocity = Vector2.Zero;
        _request = null;
        _targetAssignment = null;
        _lastKnownTargetQuery = null;
        TravelTerminated?.Invoke(new NpcTravelTerminalFact(
            navigationRuntime.ActorId,
            request.ActivityId,
            operationId,
            request.TargetRef,
            status,
            RequireOnScreenSpatialState().ConfirmedPosition));
    }

    private void SetProjectionInactive()
    {
        _standingProjectionTarget = null;
        Velocity = Vector2.Zero;
        CollisionLayer = 0;
        CollisionMask = 0;
        Visible = false;
        SetPhysicsProcess(false);
    }

    private void AdvanceStandingProjection(double delta)
    {
        if (_standingProjectionTarget is not Vector2 target) return;
        if (GlobalPosition.DistanceTo(target) <= Math.Max(TargetDesiredDistance, 0.05f))
        {
            Velocity = Vector2.Zero;
            return;
        }
        NavigationAgent2D navigationAgent = RequireNavigationAgent();
        if (!navigationAgent.TargetPosition.IsEqualApprox(target))
            navigationAgent.TargetPosition = target;
        if (NavigationServer2D.MapGetIterationId(navigationAgent.GetNavigationMap()) == 0
            || navigationAgent.IsNavigationFinished())
        {
            Velocity = Vector2.Zero;
            return;
        }
        var position = new WorldPosition(GlobalPosition.X, GlobalPosition.Y);
        float speed = _travelSpeedProfile?.ResolveSpeed(MovementSpeed, position) ?? MovementSpeed;
        _ = SharedMotionController.Step(navigationAgent, this, speed, delta);
    }

    private NavigationRuntime RequireNavigationRuntime() => _navigationRuntime ?? throw new InvalidOperationException("NpcEntity must initialize first.");
    private NavigationAgent2D RequireNavigationAgent() => NavigationAgent ?? throw new InvalidOperationException("NPC navigation agent is required.");
    private OnScreenSpatialState RequireOnScreenSpatialState() => _onScreenSpatialState ?? throw new InvalidOperationException("NpcEntity must initialize first.");
    private float ResolveMovementSpeed() =>
        _travelSpeedProfile?.ResolveSpeed(MovementSpeed, RequireOnScreenSpatialState().ConfirmedPosition) ?? MovementSpeed;

    private static void ApplyNavigationTarget(
        WorldPosition destination,
        NavigationAgent2D navigationAgent,
        float targetDesiredDistance)
    {
        GodotNavigationTargetPreparation.Apply(
            new GodotPreparedNavigationTarget(
                new Vector2((float)destination.X, (float)destination.Y),
                targetDesiredDistance),
            navigationAgent);
    }
}
