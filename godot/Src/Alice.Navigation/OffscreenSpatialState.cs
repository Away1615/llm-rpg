using Alice.Activities;
using Alice.Actors;
using Alice.Identity;

namespace Alice.Navigation;

/// <summary>Deterministic integer progress within one canonical route segment.</summary>
public sealed record SegmentProgress
{
    public SegmentProgress(long completedTraversalTicks, long totalTraversalTicks)
    {
        if (totalTraversalTicks <= 0 || completedTraversalTicks < 0 || completedTraversalTicks > totalTraversalTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(completedTraversalTicks));
        }

        CompletedTraversalTicks = completedTraversalTicks;
        TotalTraversalTicks = totalTraversalTicks;
    }

    public long CompletedTraversalTicks { get; }
    public long TotalTraversalTicks { get; }
    public double Fraction => (double)CompletedTraversalTicks / TotalTraversalTicks;
}

/// <summary>Immutable route facts for one off-screen travelling Actor.</summary>
public sealed record OffscreenTravelState
{
    public OffscreenTravelState(
        RouteId routeId,
        int segmentIndex,
        SegmentProgress segmentProgress,
        SimTime startSimTime,
        SimTime lastProgressSimTime,
        SimTime expectedArrival)
    {
        ArgumentNullException.ThrowIfNull(routeId);
        ArgumentNullException.ThrowIfNull(segmentProgress);
        if (segmentIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentIndex));
        }

        if (lastProgressSimTime.CompareTo(startSimTime) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lastProgressSimTime));
        }

        if (expectedArrival.CompareTo(startSimTime) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedArrival));
        }

        RouteId = routeId;
        SegmentIndex = segmentIndex;
        SegmentProgress = segmentProgress;
        StartSimTime = startSimTime;
        LastProgressSimTime = lastProgressSimTime;
        ExpectedArrival = expectedArrival;
    }

    public RouteId RouteId { get; }
    public int SegmentIndex { get; }
    public SegmentProgress SegmentProgress { get; }
    public SimTime StartSimTime { get; }
    public SimTime LastProgressSimTime { get; }
    public SimTime ExpectedArrival { get; }
}

/// <summary>One Actor's immutable anchor-or-travel off-screen spatial representation.</summary>
public sealed record OffscreenSpatialState
{
    public OffscreenSpatialState(
        ActorId actorId,
        SpatialAnchorRef? currentAnchor,
        OffscreenTravelState? travelState)
    {
        NonEmptyIdentityValue.Validate(actorId.Value, nameof(actorId), "Off-screen Actor identifier must be non-empty.");
        if ((currentAnchor is null) == (travelState is null))
        {
            throw new ArgumentException("Off-screen spatial state must contain exactly one anchor-or-travel form.");
        }

        ActorId = actorId;
        CurrentAnchor = currentAnchor;
        TravelState = travelState;
    }

    public ActorId ActorId { get; }
    public SpatialAnchorRef? CurrentAnchor { get; }
    public OffscreenTravelState? TravelState { get; }
}
