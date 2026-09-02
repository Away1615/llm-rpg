using System.Collections.ObjectModel;
using Alice.Activities;

namespace Alice.Navigation;

/// <summary>One immutable timed straight segment of a canonical route.</summary>
public sealed record CanonicalRouteSegment
{
    public CanonicalRouteSegment(WorldPosition start, WorldPosition end, SimDuration traversalDuration)
    {
        ValidateFinite(start, nameof(start));
        ValidateFinite(end, nameof(end));
        if (traversalDuration.Ticks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(traversalDuration), "Canonical route segment duration must be positive.");
        }

        if (start == end)
        {
            throw new ArgumentException("Canonical route segment must have distinct endpoints.", nameof(end));
        }

        Start = start;
        End = end;
        TraversalDuration = traversalDuration;
    }

    public WorldPosition Start { get; }
    public WorldPosition End { get; }
    public SimDuration TraversalDuration { get; }

    private static void ValidateFinite(WorldPosition position, string parameterName)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Canonical route coordinates must be finite.");
        }
    }
}

/// <summary>Immutable ordered geometry and timing for one resolved route identity.</summary>
public sealed class CanonicalRoute : IEquatable<CanonicalRoute>
{
    private readonly ReadOnlyCollection<CanonicalRouteSegment> _segments;

    public CanonicalRoute(RouteId routeId, IEnumerable<CanonicalRouteSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(routeId);
        ArgumentNullException.ThrowIfNull(segments);

        CanonicalRouteSegment[] copiedSegments = segments.ToArray();
        if (copiedSegments.Length == 0)
        {
            throw new ArgumentException("Canonical route must contain at least one segment.", nameof(segments));
        }

        long totalTicks = 0;
        for (int index = 0; index < copiedSegments.Length; index++)
        {
            CanonicalRouteSegment segment = copiedSegments[index] ??
                throw new ArgumentException("Canonical route cannot contain a null segment.", nameof(segments));
            if (index > 0 && copiedSegments[index - 1].End != segment.Start)
            {
                throw new ArgumentException("Adjacent canonical route segments must be continuous.", nameof(segments));
            }

            totalTicks = checked(totalTicks + segment.TraversalDuration.Ticks);
        }

        RouteId = routeId;
        _segments = Array.AsReadOnly(copiedSegments);
        TotalTraversalDuration = new SimDuration(totalTicks);
    }

    public RouteId RouteId { get; }
    public IReadOnlyList<CanonicalRouteSegment> Segments => _segments;
    public SimDuration TotalTraversalDuration { get; }

    public bool Equals(CanonicalRoute? other)
    {
        return other is not null &&
            RouteId == other.RouteId &&
            TotalTraversalDuration == other.TotalTraversalDuration &&
            _segments.SequenceEqual(other._segments);
    }

    public override bool Equals(object? obj) => Equals(obj as CanonicalRoute);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RouteId);
        foreach (CanonicalRouteSegment segment in _segments)
        {
            hash.Add(segment);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Immutable one-route-per-identity lookup for already resolved route content.</summary>
public sealed class CanonicalRouteCatalog
{
    private readonly IReadOnlyDictionary<RouteId, CanonicalRoute> _routesById;
    private readonly ReadOnlyCollection<CanonicalRoute> _routes;

    public CanonicalRouteCatalog(IEnumerable<CanonicalRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        CanonicalRoute[] copiedRoutes = routes.ToArray();
        var routesById = new Dictionary<RouteId, CanonicalRoute>();
        foreach (CanonicalRoute route in copiedRoutes)
        {
            if (route is null)
            {
                throw new ArgumentException("Canonical route catalogue cannot contain a null route.", nameof(routes));
            }

            if (!routesById.TryAdd(route.RouteId, route))
            {
                throw new ArgumentException($"Duplicate canonical route identity '{route.RouteId.Value}'.", nameof(routes));
            }
        }

        _routes = Array.AsReadOnly(copiedRoutes);
        _routesById = new ReadOnlyDictionary<RouteId, CanonicalRoute>(routesById);
    }

    public IReadOnlyList<CanonicalRoute> Routes => _routes;

    public CanonicalRoute Resolve(RouteId routeId)
    {
        ArgumentNullException.ThrowIfNull(routeId);
        if (!_routesById.TryGetValue(routeId, out CanonicalRoute? route))
        {
            throw new KeyNotFoundException($"Canonical route '{routeId.Value}' is not present in the catalogue.");
        }

        return route;
    }
}
