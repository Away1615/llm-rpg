using Alice.Activities;

namespace Alice.Navigation;

/// <summary>Pure construction, lazy progression and position derivation for off-screen route state.</summary>
public static class OffscreenSpatialProgression
{
    public static OffscreenSpatialState ReconstituteTravel(
        Alice.Actors.ActorId actorId,
        CanonicalRoute route,
        long completedRouteTicks,
        SimTime handoffTime)
    {
        Alice.Actors.ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(route);
        long totalTicks = route.TotalTraversalDuration.Ticks;
        if (completedRouteTicks < 0 || completedRouteTicks >= totalTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(completedRouteTicks));
        }

        long epochTicks = checked(handoffTime.Ticks - completedRouteTicks);
        if (epochTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handoffTime), "Travel handoff cannot produce a negative route epoch.");
        }

        long remainingTicks = checked(totalTicks - completedRouteTicks);
        var epoch = new SimTime(epochTicks);
        SimTime expectedArrival = new(checked(handoffTime.Ticks + remainingTicks));
        int segmentIndex = 0;
        long segmentCompletedTicks = completedRouteTicks;
        while (segmentCompletedTicks >= route.Segments[segmentIndex].TraversalDuration.Ticks)
        {
            segmentCompletedTicks = checked(segmentCompletedTicks - route.Segments[segmentIndex].TraversalDuration.Ticks);
            segmentIndex++;
        }

        CanonicalRouteSegment segment = route.Segments[segmentIndex];
        var travel = new OffscreenTravelState(
            route.RouteId,
            segmentIndex,
            new SegmentProgress(segmentCompletedTicks, segment.TraversalDuration.Ticks),
            epoch,
            handoffTime,
            expectedArrival);
        return new OffscreenSpatialState(actorId, null, travel);
    }

    public static OffscreenSpatialState StartTravel(
        OffscreenSpatialState anchoredState,
        CanonicalRoute route,
        SimTime startSimTime)
    {
        ArgumentNullException.ThrowIfNull(anchoredState);
        ArgumentNullException.ThrowIfNull(route);
        if (anchoredState.CurrentAnchor is null || anchoredState.TravelState is not null)
        {
            throw new InvalidOperationException("Only an anchored off-screen state can start travel.");
        }

        CanonicalRouteSegment firstSegment = route.Segments[0];
        var travelState = new OffscreenTravelState(
            route.RouteId,
            0,
            new SegmentProgress(0, firstSegment.TraversalDuration.Ticks),
            startSimTime,
            startSimTime,
            startSimTime.Add(route.TotalTraversalDuration));
        return new OffscreenSpatialState(anchoredState.ActorId, null, travelState);
    }

    public static OffscreenSpatialState AdvanceTo(
        OffscreenSpatialState state,
        CanonicalRoute route,
        SimTime requestedSimTime)
    {
        OffscreenTravelState travel = ValidateTravellingState(state, route);
        if (requestedSimTime.CompareTo(travel.LastProgressSimTime) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedSimTime), "Off-screen route progression cannot move backward in simulation time.");
        }

        if (requestedSimTime == travel.LastProgressSimTime || IsAtRouteEndpoint(travel, route))
        {
            return state;
        }

        long elapsedTicks = checked(requestedSimTime.Ticks - travel.LastProgressSimTime.Ticks);
        long unusedTicks = elapsedTicks;
        int segmentIndex = travel.SegmentIndex;
        long completedTicks = travel.SegmentProgress.CompletedTraversalTicks;

        while (true)
        {
            CanonicalRouteSegment segment = route.Segments[segmentIndex];
            long remainingSegmentTicks = checked(segment.TraversalDuration.Ticks - completedTicks);
            if (unusedTicks < remainingSegmentTicks)
            {
                completedTicks = checked(completedTicks + unusedTicks);
                return WithTravelState(
                    state,
                    travel,
                    segmentIndex,
                    new SegmentProgress(completedTicks, segment.TraversalDuration.Ticks),
                    requestedSimTime);
            }

            unusedTicks = checked(unusedTicks - remainingSegmentTicks);
            if (segmentIndex == route.Segments.Count - 1)
            {
                long consumedTicks = checked(elapsedTicks - unusedTicks);
                var exhaustionTime = new SimTime(checked(travel.LastProgressSimTime.Ticks + consumedTicks));
                return WithTravelState(
                    state,
                    travel,
                    segmentIndex,
                    new SegmentProgress(segment.TraversalDuration.Ticks, segment.TraversalDuration.Ticks),
                    exhaustionTime);
            }

            segmentIndex++;
            completedTicks = 0;
            if (unusedTicks == 0)
            {
                CanonicalRouteSegment nextSegment = route.Segments[segmentIndex];
                return WithTravelState(
                    state,
                    travel,
                    segmentIndex,
                    new SegmentProgress(0, nextSegment.TraversalDuration.Ticks),
                    requestedSimTime);
            }
        }
    }

    public static WorldPosition DerivePosition(OffscreenSpatialState state, CanonicalRoute route)
    {
        OffscreenTravelState travel = ValidateTravellingState(state, route);
        CanonicalRouteSegment segment = route.Segments[travel.SegmentIndex];
        long completedTicks = travel.SegmentProgress.CompletedTraversalTicks;
        if (completedTicks == 0)
        {
            return segment.Start;
        }

        if (completedTicks == travel.SegmentProgress.TotalTraversalTicks)
        {
            return segment.End;
        }

        double fraction = travel.SegmentProgress.Fraction;
        double remainingFraction = 1.0 - fraction;
        var position = new WorldPosition(
            segment.Start.X * remainingFraction + segment.End.X * fraction,
            segment.Start.Y * remainingFraction + segment.End.Y * fraction);
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            throw new OverflowException("Derived canonical route position must remain finite.");
        }

        return position;
    }

    private static OffscreenTravelState ValidateTravellingState(
        OffscreenSpatialState state,
        CanonicalRoute route)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(route);
        OffscreenTravelState travel = state.TravelState ??
            throw new InvalidOperationException("An anchored off-screen state has no route progress.");
        if (travel.RouteId != route.RouteId)
        {
            throw new ArgumentException("Resolved canonical route does not match the travelling state.", nameof(route));
        }

        if (travel.SegmentIndex < 0 || travel.SegmentIndex >= route.Segments.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Travelling state has an invalid route segment index.");
        }

        CanonicalRouteSegment segment = route.Segments[travel.SegmentIndex];
        if (travel.SegmentProgress.TotalTraversalTicks != segment.TraversalDuration.Ticks)
        {
            throw new ArgumentException("Segment progress total does not match the canonical route segment duration.", nameof(state));
        }

        if (travel.SegmentIndex < route.Segments.Count - 1 &&
            travel.SegmentProgress.CompletedTraversalTicks == travel.SegmentProgress.TotalTraversalTicks)
        {
            throw new ArgumentException("A completed non-final segment must be represented by the next segment at zero progress.", nameof(state));
        }

        long elapsedRouteTicks = CalculateElapsedRouteTicks(travel, route);
        long elapsedSimTimeTicks = checked(travel.LastProgressSimTime.Ticks - travel.StartSimTime.Ticks);
        if (elapsedRouteTicks != elapsedSimTimeTicks)
        {
            throw new ArgumentException("Route segment progress must match elapsed simulation time.", nameof(state));
        }

        SimTime expectedArrival = travel.StartSimTime.Add(route.TotalTraversalDuration);
        if (travel.ExpectedArrival != expectedArrival)
        {
            throw new ArgumentException("Expected arrival must equal checked start time plus canonical route duration.", nameof(state));
        }

        return travel;
    }

    private static long CalculateElapsedRouteTicks(OffscreenTravelState travel, CanonicalRoute route)
    {
        long elapsedTicks = 0;
        for (int index = 0; index < travel.SegmentIndex; index++)
        {
            elapsedTicks = checked(elapsedTicks + route.Segments[index].TraversalDuration.Ticks);
        }

        return checked(elapsedTicks + travel.SegmentProgress.CompletedTraversalTicks);
    }

    private static bool IsAtRouteEndpoint(OffscreenTravelState travel, CanonicalRoute route)
    {
        return travel.SegmentIndex == route.Segments.Count - 1 &&
            travel.SegmentProgress.CompletedTraversalTicks == travel.SegmentProgress.TotalTraversalTicks;
    }

    private static OffscreenSpatialState WithTravelState(
        OffscreenSpatialState state,
        OffscreenTravelState previous,
        int segmentIndex,
        SegmentProgress segmentProgress,
        SimTime lastProgressSimTime)
    {
        var travel = new OffscreenTravelState(
            previous.RouteId,
            segmentIndex,
            segmentProgress,
            previous.StartSimTime,
            lastProgressSimTime,
            previous.ExpectedArrival);
        return new OffscreenSpatialState(state.ActorId, null, travel);
    }
}
