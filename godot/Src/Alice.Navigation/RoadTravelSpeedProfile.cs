using System.Collections.ObjectModel;
using Godot;

namespace Alice.Navigation;

public sealed record RoadTravelCorridor
{
    public RoadTravelCorridor(string roadId, IEnumerable<WorldPosition> points, double halfWidth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roadId);
        ArgumentNullException.ThrowIfNull(points);
        WorldPosition[] copiedPoints = points.ToArray();
        if (copiedPoints.Length < 2 || !double.IsFinite(halfWidth) || halfWidth <= 0)
            throw new ArgumentException("A road corridor needs at least two points and a positive width.");
        RoadId = roadId;
        Points = Array.AsReadOnly(copiedPoints);
        HalfWidth = halfWidth;
    }

    public string RoadId { get; }
    public IReadOnlyList<WorldPosition> Points { get; }
    public double HalfWidth { get; }
}

/// <summary>Shared road/off-road speed semantics consumed by Player, NPC and route timing.</summary>
public sealed class RoadTravelSpeedProfile
{
    private readonly ReadOnlyCollection<RoadTravelCorridor> _roads;

    public RoadTravelSpeedProfile(
        double offRoadMultiplier,
        double roadMultiplier,
        IEnumerable<RoadTravelCorridor> roads)
    {
        ArgumentNullException.ThrowIfNull(roads);
        if (!double.IsFinite(offRoadMultiplier) || offRoadMultiplier <= 0
            || !double.IsFinite(roadMultiplier) || roadMultiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(offRoadMultiplier));
        OffRoadMultiplier = offRoadMultiplier;
        RoadMultiplier = roadMultiplier;
        _roads = Array.AsReadOnly(roads.ToArray());
    }

    public double OffRoadMultiplier { get; }
    public double RoadMultiplier { get; }
    public IReadOnlyList<RoadTravelCorridor> Roads => _roads;

    public double ResolveMultiplier(WorldPosition position)
    {
        foreach (RoadTravelCorridor road in _roads)
        {
            for (int index = 1; index < road.Points.Count; index++)
            {
                if (DistanceToSegment(position, road.Points[index - 1], road.Points[index]) <= road.HalfWidth)
                    return RoadMultiplier;
            }
        }
        return OffRoadMultiplier;
    }

    public float ResolveSpeed(float baseSpeed, WorldPosition position)
    {
        if (!float.IsFinite(baseSpeed) || baseSpeed < 0)
            throw new ArgumentOutOfRangeException(nameof(baseSpeed));
        double speed = baseSpeed * ResolveMultiplier(position);
        if (speed > float.MaxValue) throw new OverflowException("Resolved movement speed exceeds Godot float range.");
        return (float)speed;
    }

    public LiveNavigationRoute SelectLiveRoute(
        WorldPosition start,
        WorldPosition destination,
        double baseUnitsPerSecond)
    {
        if (!double.IsFinite(baseUnitsPerSecond) || baseUnitsPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseUnitsPerSecond));
        double bestElapsed = Distance(start, destination) / (baseUnitsPerSecond * OffRoadMultiplier);
        string? bestRoadId = null;
        WorldPosition[] bestWaypoints = [destination];
        foreach (RoadTravelCorridor road in _roads)
        {
            EvaluateCandidate(road, false, start, destination, baseUnitsPerSecond, ref bestElapsed, ref bestRoadId, ref bestWaypoints);
            EvaluateCandidate(road, true, start, destination, baseUnitsPerSecond, ref bestElapsed, ref bestRoadId, ref bestWaypoints);
        }
        return new LiveNavigationRoute(bestRoadId, bestWaypoints, bestElapsed);
    }

    public long EstimateTraversalTicks(double distance, double baseUnitsPerTick, double multiplier)
    {
        if (!double.IsFinite(distance) || distance < 0
            || !double.IsFinite(baseUnitsPerTick) || baseUnitsPerTick <= 0
            || !double.IsFinite(multiplier) || multiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(distance));
        return Math.Max(1, checked((long)Math.Ceiling(distance / (baseUnitsPerTick * multiplier))));
    }

    private static double DistanceToSegment(WorldPosition point, WorldPosition start, WorldPosition end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared == 0) return Distance(point, start);
        double fraction = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
        fraction = Math.Clamp(fraction, 0, 1);
        return Distance(point, new WorldPosition(start.X + dx * fraction, start.Y + dy * fraction));
    }

    internal static double Distance(WorldPosition left, WorldPosition right)
    {
        double dx = right.X - left.X;
        double dy = right.Y - left.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void EvaluateCandidate(
        RoadTravelCorridor road,
        bool reverse,
        WorldPosition start,
        WorldPosition destination,
        double baseUnitsPerSecond,
        ref double bestElapsed,
        ref string? bestRoadId,
        ref WorldPosition[] bestWaypoints)
    {
        WorldPosition first = reverse ? road.Points[^1] : road.Points[0];
        WorldPosition last = reverse ? road.Points[0] : road.Points[^1];
        double elapsed = Distance(start, first) / (baseUnitsPerSecond * OffRoadMultiplier)
            + Distance(last, destination) / (baseUnitsPerSecond * OffRoadMultiplier);
        for (int index = 1; index < road.Points.Count; index++)
            elapsed += Distance(road.Points[index - 1], road.Points[index]) / (baseUnitsPerSecond * RoadMultiplier);
        if (elapsed >= bestElapsed) return;

        var waypoints = new List<WorldPosition>();
        if (reverse)
        {
            for (int index = road.Points.Count - 1; index >= 0; index--) waypoints.Add(road.Points[index]);
        }
        else
        {
            foreach (WorldPosition point in road.Points) waypoints.Add(point);
        }
        if (waypoints[^1] != destination) waypoints.Add(destination);
        bestElapsed = elapsed;
        bestRoadId = road.RoadId;
        bestWaypoints = waypoints.ToArray();
    }
}

/// <summary>One direct target or one selected road followed by the final target.</summary>
public sealed record LiveNavigationRoute
{
    public LiveNavigationRoute(string? selectedRoadId, IEnumerable<WorldPosition> waypoints, double estimatedElapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        WorldPosition[] copied = waypoints.ToArray();
        if (copied.Length == 0 || !double.IsFinite(estimatedElapsedSeconds) || estimatedElapsedSeconds < 0)
            throw new ArgumentException("A live navigation route needs waypoints and finite elapsed cost.");
        SelectedRoadId = selectedRoadId;
        Waypoints = Array.AsReadOnly(copied);
        EstimatedElapsedSeconds = estimatedElapsedSeconds;
    }

    public string? SelectedRoadId { get; }
    public IReadOnlyList<WorldPosition> Waypoints { get; }
    public double EstimatedElapsedSeconds { get; }
    public LiveNavigationWaypointCursor CreateCursor() => new(Waypoints);
}

/// <summary>Thin Godot adapter that advances one selected route without owning gameplay state.</summary>
public sealed class LiveNavigationWaypointCursor
{
    private readonly IReadOnlyList<WorldPosition> _waypoints;
    private int _index;
    private float _targetDesiredDistance;

    internal LiveNavigationWaypointCursor(IReadOnlyList<WorldPosition> waypoints) => _waypoints = waypoints;

    public bool HasIntermediateTarget => _index < _waypoints.Count - 1;

    public void ApplyInitial(NavigationAgent2D navigationAgent, float targetDesiredDistance)
    {
        ArgumentNullException.ThrowIfNull(navigationAgent);
        _targetDesiredDistance = targetDesiredDistance;
        ApplyCurrent(navigationAgent);
    }

    public bool TryAdvanceReachedWaypoint(NavigationAgent2D navigationAgent)
    {
        ArgumentNullException.ThrowIfNull(navigationAgent);
        if (!HasIntermediateTarget || !navigationAgent.IsNavigationFinished() || !navigationAgent.IsTargetReached())
            return false;
        _index++;
        ApplyCurrent(navigationAgent);
        return true;
    }

    private void ApplyCurrent(NavigationAgent2D navigationAgent)
    {
        if (!GodotNavigationTargetPreparation.TryPreparePoint(
                _waypoints[_index], _targetDesiredDistance, out GodotPreparedNavigationTarget? target)
            || target is null)
            throw new InvalidOperationException("Selected live navigation waypoint is not Godot-compatible.");
        GodotNavigationTargetPreparation.Apply(target, navigationAgent);
    }
}
