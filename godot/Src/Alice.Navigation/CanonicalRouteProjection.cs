namespace Alice.Navigation;

/// <summary>Projects confirmed positions onto monotonic work on one canonical route.</summary>
public static class CanonicalRouteProjection
{
    public static long ProjectCompletedTraversalTicks(
        CanonicalRoute route,
        WorldPosition confirmedPosition,
        long previousCompletedTraversalTicks)
    {
        ArgumentNullException.ThrowIfNull(route);
        ValidateFinite(confirmedPosition);
        long totalTicks = route.TotalTraversalDuration.Ticks;
        if (previousCompletedTraversalTicks < 0 || previousCompletedTraversalTicks > totalTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(previousCompletedTraversalTicks));
        }

        if (previousCompletedTraversalTicks == totalTicks)
        {
            return totalTicks;
        }

        if (previousCompletedTraversalTicks == 0 && confirmedPosition == route.Segments[0].Start)
        {
            return 0;
        }

        if (confirmedPosition == route.Segments[route.Segments.Count - 1].End)
        {
            return totalTicks;
        }

        long bestTicks = previousCompletedTraversalTicks;
        double bestDistanceSquared = double.PositiveInfinity;
        long ticksBeforeSegment = 0;
        foreach (CanonicalRouteSegment segment in route.Segments)
        {
            long segmentEndTicks = checked(ticksBeforeSegment + segment.TraversalDuration.Ticks);
            if (segmentEndTicks < previousCompletedTraversalTicks)
            {
                ticksBeforeSegment = segmentEndTicks;
                continue;
            }

            double minimumFraction = previousCompletedTraversalTicks <= ticksBeforeSegment
                ? 0.0
                : (double)(previousCompletedTraversalTicks - ticksBeforeSegment) / segment.TraversalDuration.Ticks;
            ProjectionCandidate candidate = ProjectToSegment(segment, confirmedPosition, minimumFraction);
            long segmentTicks = candidate.Fraction >= 1.0
                ? segment.TraversalDuration.Ticks
                : (long)Math.Floor(candidate.Fraction * segment.TraversalDuration.Ticks);
            long candidateTicks = checked(ticksBeforeSegment + segmentTicks);
            if (candidateTicks < previousCompletedTraversalTicks)
            {
                candidateTicks = previousCompletedTraversalTicks;
            }

            if (candidate.DistanceSquared < bestDistanceSquared ||
                (candidate.DistanceSquared == bestDistanceSquared && candidateTicks < bestTicks))
            {
                bestDistanceSquared = candidate.DistanceSquared;
                bestTicks = candidateTicks;
            }

            ticksBeforeSegment = segmentEndTicks;
        }

        if (!double.IsFinite(bestDistanceSquared) || bestTicks < previousCompletedTraversalTicks || bestTicks > totalTicks)
        {
            throw new OverflowException("Canonical route projection must remain finite and in range.");
        }

        return bestTicks;
    }

    private static ProjectionCandidate ProjectToSegment(
        CanonicalRouteSegment segment,
        WorldPosition position,
        double minimumFraction)
    {
        double segmentX = segment.End.X - segment.Start.X;
        double segmentY = segment.End.Y - segment.Start.Y;
        double offsetX = position.X - segment.Start.X;
        double offsetY = position.Y - segment.Start.Y;
        double lengthSquared = (segmentX * segmentX) + (segmentY * segmentY);
        double dot = (offsetX * segmentX) + (offsetY * segmentY);
        if (!double.IsFinite(segmentX) || !double.IsFinite(segmentY) ||
            !double.IsFinite(offsetX) || !double.IsFinite(offsetY) ||
            !double.IsFinite(lengthSquared) || lengthSquared <= 0.0 || !double.IsFinite(dot))
        {
            throw new OverflowException("Canonical route projection geometry must remain finite.");
        }

        double fraction = Math.Clamp(dot / lengthSquared, minimumFraction, 1.0);
        double projectedX = segment.Start.X + (segmentX * fraction);
        double projectedY = segment.Start.Y + (segmentY * fraction);
        double distanceX = position.X - projectedX;
        double distanceY = position.Y - projectedY;
        double distanceSquared = (distanceX * distanceX) + (distanceY * distanceY);
        if (!double.IsFinite(fraction) || !double.IsFinite(projectedX) || !double.IsFinite(projectedY) ||
            !double.IsFinite(distanceSquared))
        {
            throw new OverflowException("Canonical route projection result must remain finite.");
        }

        return new ProjectionCandidate(fraction, distanceSquared);
    }

    private static void ValidateFinite(WorldPosition position)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }
    }

    private readonly record struct ProjectionCandidate(double Fraction, double DistanceSquared);
}
