using Alice.Actors;
using Alice.Interaction;
using Alice.Navigation;
using Alice.Presentation;
using Godot;

namespace Alice.PlayerControl;

/// <summary>
/// A correlated terminal fact emitted upward by one Player entity navigation lifecycle.
/// </summary>
public sealed record PlayerPointNavigationTerminalFact(
    PlayerPointNavigationCorrelation Correlation,
    NavigationStatus Status);

/// <summary>A non-executing interaction handoff correlated to the completed Player navigation.</summary>
public sealed record PlayerGameActionSpecProducedFact(
    PlayerPointNavigationCorrelation Correlation,
    GameActionSpec ActionSpec,
    WorldPosition ConfirmedActorPosition);

/// <summary>
/// Composes one Player command, navigation, motion and presentation lifecycle in Godot.
/// </summary>
public sealed partial class PlayerEntity : CharacterBody2D
{
    private PlayerControlRuntime? _playerControlRuntime;
    private NavigationRuntime? _navigationRuntime;
    private OnScreenSpatialState? _onScreenSpatialState;
    private PlayerPointNavigationCorrelation? _currentCorrelation;
    private EntityNavigationTargetAssignment? _entityTargetAssignment;
    private PlayerInteractionSelection? _interactionSelection;
    private IInteractionRangeQuery? _interactionRangeQuery;
    private IActorVisibleTargetSpatialQuery? _interactionSpatialQuery;
    private RoadTravelSpeedProfile? _travelSpeedProfile;

    [Export]
    public NavigationAgent2D? NavigationAgent { get; set; }

    [Export]
    public GeometricActorMarker? Marker { get; set; }

    [Export]
    public Camera2D? Camera { get; set; }

    [Export]
    public string ActorIdentity { get; set; } = "player-preview";

    [Export]
    public float MovementSpeed { get; set; } = 160.0f;

    [Export]
    public float AgentRadius { get; set; } = 20.0f;

    [Export]
    public float PathDesiredDistance { get; set; } = 8.0f;

    [Export]
    public float TargetDesiredDistance { get; set; } = 8.0f;

    public event Action<PlayerPointNavigationTerminalFact>? PointNavigationTerminated;

    public event Action<PlayerGameActionSpecProducedFact>? GameActionSpecProduced;

    public WorldPosition ConfirmedPosition => RequireOnScreenSpatialState().ConfirmedPosition;

    public void ConfigureTravelSpeedProfile(RoadTravelSpeedProfile profile) =>
        _travelSpeedProfile = profile ?? throw new ArgumentNullException(nameof(profile));

    public override void _Ready()
    {
        ValidateComposition();

        NavigationAgent!.Radius = AgentRadius;
        NavigationAgent.PathDesiredDistance = PathDesiredDistance;
        NavigationAgent.TargetDesiredDistance = TargetDesiredDistance;

        _playerControlRuntime = new PlayerControlRuntime();
        _navigationRuntime = new NavigationRuntime(new ActorId(ActorIdentity));
        _onScreenSpatialState = new OnScreenSpatialState(
            new WorldPosition(GlobalPosition.X, GlobalPosition.Y));
    }

    public bool TryStartPointNavigation(WorldPosition target)
    {
        PlayerControlRuntime playerControlRuntime = RequirePlayerControlRuntime();
        NavigationRuntime navigationRuntime = RequireNavigationRuntime();
        NavigationAgent2D navigationAgent = RequireNavigationAgent();

        if (!GodotNavigationTargetPreparation.TryPreparePoint(
                target,
                TargetDesiredDistance,
                out GodotPreparedNavigationTarget? preparedTarget) ||
            preparedTarget is null)
        {
            return false;
        }

        CommandRevision revision = playerControlRuntime.SetCommand(new MoveToPlayerCommand(target));
        NavigationOperationId operationId = navigationRuntime.Begin(new PointNavigationTarget(target));
        ApplyNavigationTarget(target, navigationAgent, preparedTarget.TargetDesiredDistance);
        var correlation = new PlayerPointNavigationCorrelation(revision, operationId);

        _currentCorrelation = correlation;
        ClearInteractionApproach();
        GD.Print(
            $"PlayerEntity command: revision={correlation.CommandRevision.Value}, operation={correlation.NavigationOperationId.Value}, target=({target.X}, {target.Y})");
        return true;
    }

    public void ResetConfirmedPosition(WorldPosition position)
    {
        if (_currentCorrelation is not null)
            throw new InvalidOperationException("Player position cannot reset during active navigation.");
        GlobalPosition = new Vector2((float)position.X, (float)position.Y);
        _onScreenSpatialState = new OnScreenSpatialState(position);
        Velocity = Vector2.Zero;
    }

    /// <summary>Starts one selected binding approach using only caller-supplied typed views.</summary>
    public bool TryStartInteraction(
        PlayerInteractionSelection selection,
        IInteractionRangeQuery rangeQuery,
        IActorVisibleTargetSpatialQuery spatialQuery)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(rangeQuery);
        ArgumentNullException.ThrowIfNull(spatialQuery);

        PlayerControlRuntime playerControlRuntime = RequirePlayerControlRuntime();
        NavigationRuntime navigationRuntime = RequireNavigationRuntime();
        NavigationAgent2D navigationAgent = RequireNavigationAgent();
        if (!PlayerInteractionApproach.TryPrepareTarget(selection.Binding, rangeQuery, out EntityNavigationTarget? target) ||
            target is null ||
            !GodotNavigationTargetPreparation.TryPrepareEntity(target, spatialQuery, out GodotPreparedNavigationTarget? preparedTarget) ||
            preparedTarget is null)
        {
            return false;
        }

        CommandRevision revision = playerControlRuntime.SetCommand(new InteractWithPlayerCommand(selection));
        NavigationOperationId operationId = navigationRuntime.Begin(target);
        ApplyNavigationTarget(
            new WorldPosition(preparedTarget.TargetPosition.X, preparedTarget.TargetPosition.Y),
            navigationAgent,
            preparedTarget.TargetDesiredDistance);
        var correlation = new PlayerPointNavigationCorrelation(revision, operationId);
        var assignment = new EntityNavigationTargetAssignment(
            operationId,
            preparedTarget.TargetPosition,
            preparedTarget.TargetDesiredDistance);

        _currentCorrelation = correlation;
        _entityTargetAssignment = assignment;
        _interactionSelection = selection;
        _interactionRangeQuery = rangeQuery;
        _interactionSpatialQuery = spatialQuery;
        GD.Print($"PlayerEntity interaction: revision={correlation.CommandRevision.Value}, operation={correlation.NavigationOperationId.Value}");
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_currentCorrelation is not PlayerPointNavigationCorrelation correlation)
        {
            return;
        }

        NavigationRuntime navigationRuntime = RequireNavigationRuntime();
        NavigationAgent2D navigationAgent = RequireNavigationAgent();

        if (_entityTargetAssignment is EntityNavigationTargetAssignment entityAssignment)
        {
            ProcessEntityNavigation(correlation, entityAssignment, delta);
            return;
        }

        if (GodotPointNavigationOperationAdapter.TryClassifyTerminal(
                navigationRuntime,
                correlation.NavigationOperationId,
                navigationAgent,
                out NavigationStatus? terminalStatus) && terminalStatus is NavigationStatus status)
        {
            CompleteCurrentNavigation(correlation, status);
            return;
        }

        if (!GodotPointNavigationOperationAdapter.TryStep(
                navigationRuntime,
                correlation.NavigationOperationId,
                navigationAgent,
                this,
                ResolveMovementSpeed(),
                delta,
                out NavigationMotionStepResult? stepResult) ||
            stepResult is null)
        {
            return;
        }

        RequireOnScreenSpatialState().ApplyMotionResult(stepResult.MotionResult);
        ApplyMotionPresentation(stepResult.MotionResult);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mouse ||
            mouse.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown))
        {
            return;
        }

        float direction = mouse.ButtonIndex == MouseButton.WheelUp ? 1.0f : -1.0f;
        float pixelsPerMeter = Mathf.Clamp(
            Camera!.Zoom.X + direction * GeometricPresentationScale.CameraZoomStepPixelsPerMeter,
            GeometricPresentationScale.MinimumCameraPixelsPerMeter,
            GeometricPresentationScale.DefaultCameraPixelsPerMeter);
        Camera.Zoom = Vector2.One * pixelsPerMeter;
        GetViewport().SetInputAsHandled();
    }

    private void ProcessEntityNavigation(
        PlayerPointNavigationCorrelation correlation,
        EntityNavigationTargetAssignment assignment,
        double delta)
    {
        NavigationRuntime navigationRuntime = RequireNavigationRuntime();
        NavigationAgent2D navigationAgent = RequireNavigationAgent();
        IActorVisibleTargetSpatialQuery spatialQuery = _interactionSpatialQuery ?? throw new InvalidOperationException("Interaction spatial query is required.");

        if (GodotEntityNavigationOperationAdapter.TryClassifyTerminal(
                navigationRuntime, correlation.NavigationOperationId, spatialQuery, navigationAgent,
                out NavigationStatus? terminalStatus) && terminalStatus is NavigationStatus status)
        {
            CompleteCurrentNavigation(correlation, status);
            return;
        }

        if (!GodotEntityNavigationOperationAdapter.TryStep(
                navigationRuntime, correlation.NavigationOperationId, spatialQuery, navigationAgent, this,
                assignment, ResolveMovementSpeed(), delta,
                out EntityNavigationTargetAssignment? refreshedAssignment,
                out NavigationMotionStepResult? stepResult,
                out NavigationStatus? stepTerminalStatus))
        {
            if (stepTerminalStatus is NavigationStatus terminal)
            {
                CompleteCurrentNavigation(correlation, terminal);
            }

            return;
        }

        _entityTargetAssignment = refreshedAssignment;
        if (stepResult is null)
        {
            return;
        }

        RequireOnScreenSpatialState().ApplyMotionResult(stepResult.MotionResult);
        ApplyMotionPresentation(stepResult.MotionResult);
    }

    private void CompleteCurrentNavigation(
        PlayerPointNavigationCorrelation correlation,
        NavigationStatus terminalStatus)
    {
        if (!PlayerPointNavigationCorrelationBridge.TryReportTerminalOutcome(
                RequirePlayerControlRuntime(),
                RequireNavigationRuntime(),
                correlation,
                terminalStatus))
        {
            return;
        }

        PlayerInteractionSelection? interactionSelection = _interactionSelection;
        IInteractionRangeQuery? interactionRangeQuery = _interactionRangeQuery;
        IActorVisibleTargetSpatialQuery? interactionSpatialQuery = _interactionSpatialQuery;
        Velocity = Vector2.Zero;
        _currentCorrelation = null;

        var terminalFact = new PlayerPointNavigationTerminalFact(correlation, terminalStatus);
        GD.Print(
            $"PlayerEntity terminal: revision={correlation.CommandRevision.Value}, operation={correlation.NavigationOperationId.Value}, status={terminalStatus}");
        PointNavigationTerminated?.Invoke(terminalFact);

        if (terminalStatus == NavigationStatus.Arrived &&
            interactionSelection is not null &&
            interactionRangeQuery is not null &&
            interactionSpatialQuery is not null &&
            PlayerInteractionApproach.TryProduceSpec(
                RequirePlayerControlRuntime(), correlation, interactionSelection, interactionRangeQuery,
                interactionSpatialQuery, RequireOnScreenSpatialState(), RequireNavigationRuntime().ActorId,
                out GameActionSpec? actionSpec) && actionSpec is not null)
        {
            GD.Print("PlayerEntity action spec produced, not executed.");
            GameActionSpecProduced?.Invoke(new PlayerGameActionSpecProducedFact(
                correlation,
                actionSpec,
                RequireOnScreenSpatialState().ConfirmedPosition));
        }

        ClearInteractionApproach();
    }

    private void ClearInteractionApproach()
    {
        _entityTargetAssignment = null;
        _interactionSelection = null;
        _interactionRangeQuery = null;
        _interactionSpatialQuery = null;
    }

    private void ApplyMotionPresentation(MotionResult motion)
    {
        Marker!.SetFacing(new MotionVectorLike(
            motion.ActualVelocity.X,
            motion.ActualVelocity.Y));
    }

    private void ValidateComposition()
    {
        if (NavigationAgent is null || Marker is null || Camera is null)
        {
            throw new InvalidOperationException(
                "PlayerEntity requires explicit navigation, geometric marker and camera references.");
        }

        if (string.IsNullOrWhiteSpace(ActorIdentity))
        {
            throw new ArgumentException("Actor identity must be non-empty.", nameof(ActorIdentity));
        }

        ValidateFiniteNonNegative(MovementSpeed, nameof(MovementSpeed));
        ValidateFiniteNonNegative(AgentRadius, nameof(AgentRadius));
        ValidateFiniteNonNegative(PathDesiredDistance, nameof(PathDesiredDistance));
        ValidateFiniteNonNegative(TargetDesiredDistance, nameof(TargetDesiredDistance));
    }

    private static void ValidateFiniteNonNegative(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Movement configuration must be finite and non-negative.");
        }
    }

    private NavigationAgent2D RequireNavigationAgent()
    {
        return NavigationAgent ?? throw new InvalidOperationException(
            "PlayerEntity must initialize with a NavigationAgent.");
    }

    private PlayerControlRuntime RequirePlayerControlRuntime()
    {
        return _playerControlRuntime ?? throw new InvalidOperationException(
            "PlayerEntity must initialize before it can receive point navigation.");
    }

    private NavigationRuntime RequireNavigationRuntime()
    {
        return _navigationRuntime ?? throw new InvalidOperationException(
            "PlayerEntity must initialize before it can receive point navigation.");
    }

    private OnScreenSpatialState RequireOnScreenSpatialState()
    {
        return _onScreenSpatialState ?? throw new InvalidOperationException(
            "PlayerEntity must initialize before it can capture motion facts.");
    }

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
