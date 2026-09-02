using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Alice.Activities;
using Alice.Navigation;

namespace Alice.LivingTown;

public sealed record TownMapPoint
{
    [JsonRequired, JsonPropertyName("x")] public int X { get; init; }
    [JsonRequired, JsonPropertyName("y")] public int Y { get; init; }
}

public sealed record TownMapRect
{
    [JsonRequired, JsonPropertyName("x")] public int X { get; init; }
    [JsonRequired, JsonPropertyName("y")] public int Y { get; init; }
    [JsonRequired, JsonPropertyName("width")] public int Width { get; init; }
    [JsonRequired, JsonPropertyName("height")] public int Height { get; init; }
}

public sealed record TownSettlementMapConfiguration
{
    [JsonRequired, JsonPropertyName("settlement_id")] public string SettlementId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("center_cell")] public TownMapPoint CenterCell { get; init; } = new();
}

public sealed record TownRoadMapConfiguration
{
    [JsonRequired, JsonPropertyName("road_id")] public string RoadId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("from_settlement_id")] public string FromSettlementId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("to_settlement_id")] public string ToSettlementId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("width_cells")] public int WidthCells { get; init; }
    [JsonRequired, JsonPropertyName("points")] public TownMapPoint[] Points { get; init; } = [];
}

public sealed record TownWaterBodyMapConfiguration
{
    [JsonRequired, JsonPropertyName("water_body_id")] public string WaterBodyId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("shape")] public string Shape { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("width_cells")] public int WidthCells { get; init; }
    [JsonRequired, JsonPropertyName("points")] public TownMapPoint[] Points { get; init; } = [];
}

public sealed record TownBottleneckMapConfiguration
{
    [JsonRequired, JsonPropertyName("bottleneck_id")] public string BottleneckId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("road_id")] public string RoadId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("bounds")] public TownMapRect Bounds { get; init; } = new();
}

public sealed record TownBuildingMapConfiguration
{
    [JsonRequired, JsonPropertyName("building_id")] public string BuildingId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("settlement_id")] public string SettlementId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("bounds")] public TownMapRect Bounds { get; init; } = new();
}

public sealed record TownRoomMapConfiguration
{
    [JsonRequired, JsonPropertyName("room_id")] public string RoomId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("building_id")] public string BuildingId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("bounds")] public TownMapRect Bounds { get; init; } = new();
}

public sealed record TownResourceRegionMapConfiguration
{
    [JsonRequired, JsonPropertyName("region_id")] public string RegionId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("settlement_id")] public string SettlementId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("resource_type")] public string ResourceType { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("bounds")] public TownMapRect Bounds { get; init; } = new();
}

public sealed record TownSpatialMapDocument
{
    [JsonRequired, JsonPropertyName("width_cells")] public int WidthCells { get; init; }
    [JsonRequired, JsonPropertyName("height_cells")] public int HeightCells { get; init; }
    [JsonRequired, JsonPropertyName("cell_size_m")] public int CellSizeMeters { get; init; }
    [JsonRequired, JsonPropertyName("off_road_speed_multiplier")] public double OffRoadSpeedMultiplier { get; init; }
    [JsonRequired, JsonPropertyName("road_speed_multiplier")] public double RoadSpeedMultiplier { get; init; }
    [JsonRequired, JsonPropertyName("settlements")] public TownSettlementMapConfiguration[] Settlements { get; init; } = [];
    [JsonRequired, JsonPropertyName("water_bodies")] public TownWaterBodyMapConfiguration[] WaterBodies { get; init; } = [];
    [JsonRequired, JsonPropertyName("roads")] public TownRoadMapConfiguration[] Roads { get; init; } = [];
    [JsonRequired, JsonPropertyName("bottlenecks")] public TownBottleneckMapConfiguration[] Bottlenecks { get; init; } = [];
    [JsonRequired, JsonPropertyName("buildings")] public TownBuildingMapConfiguration[] Buildings { get; init; } = [];
    [JsonRequired, JsonPropertyName("rooms")] public TownRoomMapConfiguration[] Rooms { get; init; } = [];
    [JsonRequired, JsonPropertyName("resource_regions")] public TownResourceRegionMapConfiguration[] ResourceRegions { get; init; } = [];
}

/// <summary>Final G2 map skeleton plus the one shared road movement/time profile.</summary>
public sealed class TownSpatialMap
{
    private readonly IReadOnlyDictionary<string, TownRoadMapConfiguration> _roads;

    private TownSpatialMap(TownSpatialMapDocument document)
    {
        WidthCells = document.WidthCells;
        HeightCells = document.HeightCells;
        CellSizeMeters = document.CellSizeMeters;
        Settlements = Array.AsReadOnly(document.Settlements);
        WaterBodies = Array.AsReadOnly(document.WaterBodies);
        Roads = Array.AsReadOnly(document.Roads);
        Bottlenecks = Array.AsReadOnly(document.Bottlenecks);
        Buildings = Array.AsReadOnly(document.Buildings);
        Rooms = Array.AsReadOnly(document.Rooms);
        ResourceRegions = Array.AsReadOnly(document.ResourceRegions);
        _roads = new ReadOnlyDictionary<string, TownRoadMapConfiguration>(
            document.Roads.ToDictionary(GetRoadId, StringComparer.Ordinal));
        SpeedProfile = new RoadTravelSpeedProfile(
            document.OffRoadSpeedMultiplier,
            document.RoadSpeedMultiplier,
            document.Roads.Select(CreateCorridor));
    }

    public int WidthCells { get; }
    public int HeightCells { get; }
    public int CellSizeMeters { get; }
    public double WorldWidth => WidthCells * CellSizeMeters;
    public double WorldHeight => HeightCells * CellSizeMeters;
    public IReadOnlyList<TownSettlementMapConfiguration> Settlements { get; }
    public IReadOnlyList<TownWaterBodyMapConfiguration> WaterBodies { get; }
    public IReadOnlyList<TownRoadMapConfiguration> Roads { get; }
    public IReadOnlyList<TownBottleneckMapConfiguration> Bottlenecks { get; }
    public IReadOnlyList<TownBuildingMapConfiguration> Buildings { get; }
    public IReadOnlyList<TownRoomMapConfiguration> Rooms { get; }
    public IReadOnlyList<TownResourceRegionMapConfiguration> ResourceRegions { get; }
    public RoadTravelSpeedProfile SpeedProfile { get; }

    public static TownSpatialMap Create(TownSpatialMapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.WidthCells <= 0 || document.HeightCells <= 0 || document.CellSizeMeters != 1
            || document.OffRoadSpeedMultiplier != 1.0 || document.RoadSpeedMultiplier != 1.5
            || document.Settlements.Length == 0 || document.WaterBodies.Length < 2 || document.Roads.Length < 2
            || document.Bottlenecks.Length == 0 || document.Buildings.Length == 0
            || document.Rooms.Length == 0 || document.ResourceRegions.Length == 0)
            throw new InvalidDataException("Town spatial map is incomplete.");

        HashSet<string> settlementIds = UniqueIds(document.Settlements.Select(GetSettlementId), "settlement");
        _ = UniqueIds(document.WaterBodies.Select(GetWaterBodyId), "water body");
        HashSet<string> roadIds = UniqueIds(document.Roads.Select(GetRoadId), "road");
        HashSet<string> buildingIds = UniqueIds(document.Buildings.Select(GetBuildingId), "building");
        _ = UniqueIds(document.Rooms.Select(GetRoomId), "room");
        _ = UniqueIds(document.ResourceRegions.Select(GetRegionId), "resource region");
        foreach (TownSettlementMapConfiguration settlement in document.Settlements)
            if (!ContainsPoint(settlement.CenterCell, document.WidthCells, document.HeightCells))
                throw new InvalidDataException($"Settlement '{settlement.SettlementId}' is outside the map.");
        foreach (TownWaterBodyMapConfiguration waterBody in document.WaterBodies)
        {
            bool validShape = waterBody.Shape switch
            {
                "Corridor" => waterBody.WidthCells > 0 && waterBody.Points.Length >= 2,
                "Area" => waterBody.WidthCells == 0 && waterBody.Points.Length >= 3,
                _ => false
            };
            if (!validShape || waterBody.Points.Any(point => !ContainsPoint(point, document.WidthCells, document.HeightCells)))
                throw new InvalidDataException($"Water body '{waterBody.WaterBodyId}' is incomplete or outside the map.");
        }
        foreach (TownRoadMapConfiguration road in document.Roads)
        {
            if (!settlementIds.Contains(road.FromSettlementId) || !settlementIds.Contains(road.ToSettlementId)
                || road.WidthCells <= 0 || road.Points.Length < 2
                || road.Points.Any(point => !ContainsPoint(point, document.WidthCells, document.HeightCells)))
                throw new InvalidDataException($"Road '{road.RoadId}' is incomplete.");
        }
        foreach (TownBottleneckMapConfiguration bottleneck in document.Bottlenecks)
            if (!roadIds.Contains(bottleneck.RoadId)
                || !ContainsRect(bottleneck.Bounds, document.WidthCells, document.HeightCells))
                throw new InvalidDataException("Bottleneck road is missing or outside the map.");
        foreach (TownBuildingMapConfiguration building in document.Buildings)
            if (!settlementIds.Contains(building.SettlementId)
                || !ContainsRect(building.Bounds, document.WidthCells, document.HeightCells))
                throw new InvalidDataException($"Building '{building.BuildingId}' is outside its settlement map.");
        foreach (TownRoomMapConfiguration room in document.Rooms)
        {
            TownBuildingMapConfiguration? building = document.Buildings.FirstOrDefault(value =>
                StringComparer.Ordinal.Equals(value.BuildingId, room.BuildingId));
            if (!buildingIds.Contains(room.BuildingId) || building is null || !ContainsRect(room.Bounds, building.Bounds))
                throw new InvalidDataException($"Room '{room.RoomId}' is outside its building.");
        }
        foreach (TownResourceRegionMapConfiguration region in document.ResourceRegions)
            if (!settlementIds.Contains(region.SettlementId)
                || !ContainsRect(region.Bounds, document.WidthCells, document.HeightCells))
                throw new InvalidDataException($"Resource region '{region.RegionId}' is outside its settlement map.");

        HashSet<string> reached = [document.Settlements[0].SettlementId];
        for (int pass = 0; pass < document.Settlements.Length; pass++)
        {
            foreach (TownRoadMapConfiguration road in document.Roads)
            {
                if (reached.Contains(road.FromSettlementId)) reached.Add(road.ToSettlementId);
                if (reached.Contains(road.ToSettlementId)) reached.Add(road.FromSettlementId);
            }
        }
        if (reached.Count != document.Settlements.Length)
            throw new InvalidDataException("All settlements must be road-reachable.");
        return new TownSpatialMap(document);
    }

    public CanonicalRoute CreateRoadRoute(string roadId, double baseWorldUnitsPerTick)
    {
        if (!_roads.TryGetValue(roadId, out TownRoadMapConfiguration? road))
            throw new KeyNotFoundException($"Road '{roadId}' is absent.");
        var segments = new List<CanonicalRouteSegment>();
        for (int index = 1; index < road.Points.Length; index++)
        {
            WorldPosition start = ToWorld(road.Points[index - 1]);
            WorldPosition end = ToWorld(road.Points[index]);
            long ticks = SpeedProfile.EstimateTraversalTicks(
                RoadTravelSpeedProfile.Distance(start, end), baseWorldUnitsPerTick, SpeedProfile.RoadMultiplier);
            segments.Add(new CanonicalRouteSegment(start, end, new SimDuration(ticks)));
        }
        return new CanonicalRoute(new RouteId($"road/{roadId}"), segments);
    }

    public long EstimateOffRoadTicks(WorldPosition start, WorldPosition end, double baseWorldUnitsPerTick) =>
        SpeedProfile.EstimateTraversalTicks(
            RoadTravelSpeedProfile.Distance(start, end), baseWorldUnitsPerTick, SpeedProfile.OffRoadMultiplier);

    public WorldPosition ToWorld(TownMapPoint point) => new(point.X * CellSizeMeters, point.Y * CellSizeMeters);

    public bool Contains(WorldPosition position) =>
        position.X >= 0 && position.Y >= 0 && position.X <= WorldWidth && position.Y <= WorldHeight;

    public bool ContainsPlaceId(string placeId)
    {
        foreach (TownBuildingMapConfiguration building in Buildings)
            if (StringComparer.Ordinal.Equals(building.BuildingId, placeId)) return true;
        foreach (TownRoomMapConfiguration room in Rooms)
            if (StringComparer.Ordinal.Equals(room.RoomId, placeId)) return true;
        foreach (TownResourceRegionMapConfiguration region in ResourceRegions)
            if (StringComparer.Ordinal.Equals(region.RegionId, placeId)) return true;
        return false;
    }

    private RoadTravelCorridor CreateCorridor(TownRoadMapConfiguration road) =>
        new(road.RoadId, road.Points.Select(ToWorld), road.WidthCells * CellSizeMeters / 2.0);

    private static HashSet<string> UniqueIds(IEnumerable<string> values, string kind)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in values)
            if (string.IsNullOrWhiteSpace(value) || !ids.Add(value))
                throw new InvalidDataException($"Duplicate or blank {kind} identity.");
        return ids;
    }

    private static bool ContainsPoint(TownMapPoint point, int width, int height) =>
        point.X >= 0 && point.Y >= 0 && point.X <= width && point.Y <= height;

    private static bool ContainsRect(TownMapRect bounds, int width, int height) =>
        bounds.X >= 0 && bounds.Y >= 0 && bounds.Width > 0 && bounds.Height > 0
        && bounds.X + bounds.Width <= width && bounds.Y + bounds.Height <= height;

    private static bool ContainsRect(TownMapRect inner, TownMapRect outer) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Width > 0 && inner.Height > 0
        && inner.X + inner.Width <= outer.X + outer.Width
        && inner.Y + inner.Height <= outer.Y + outer.Height;

    private static string GetSettlementId(TownSettlementMapConfiguration value) => value.SettlementId;
    private static string GetWaterBodyId(TownWaterBodyMapConfiguration value) => value.WaterBodyId;
    private static string GetRoadId(TownRoadMapConfiguration value) => value.RoadId;
    private static string GetBuildingId(TownBuildingMapConfiguration value) => value.BuildingId;
    private static string GetRoomId(TownRoomMapConfiguration value) => value.RoomId;
    private static string GetRegionId(TownResourceRegionMapConfiguration value) => value.RegionId;
}

/// <summary>Shared water/bridge passability used to build the Godot navigation map.</summary>
public static class TownWaterPassability
{
    public static bool IsBlocked(
        TownSpatialMap map,
        WorldPosition position,
        double actorClearance,
        double samplingMargin = 0)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (actorClearance < 0 || !double.IsFinite(actorClearance)
            || samplingMargin < 0 || !double.IsFinite(samplingMargin))
            throw new ArgumentOutOfRangeException(nameof(actorClearance));
        if (IsBridgePassage(map, position, actorClearance + samplingMargin)) return false;
        foreach (TownWaterBodyMapConfiguration waterBody in map.WaterBodies)
        {
            WorldPosition[] points = waterBody.Points.Select(map.ToWorld).ToArray();
            if (waterBody.Shape == "Area")
            {
                if (ContainsPoint(points, position)
                    || DistanceToClosedPolyline(points, position) <= actorClearance + samplingMargin) return true;
                continue;
            }
            double radius = waterBody.WidthCells * map.CellSizeMeters / 2.0 + actorClearance + samplingMargin;
            if (DistanceToPolyline(points, position) <= radius) return true;
        }
        return false;
    }

    private static bool IsBridgePassage(
        TownSpatialMap map,
        WorldPosition position,
        double actorClearance)
    {
        foreach (TownBottleneckMapConfiguration bridge in map.Bottlenecks)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(bridge.Kind, "Bridge")) continue;
            double left = bridge.Bounds.X * map.CellSizeMeters - actorClearance;
            double top = bridge.Bounds.Y * map.CellSizeMeters - actorClearance;
            double right = (bridge.Bounds.X + bridge.Bounds.Width) * map.CellSizeMeters + actorClearance;
            double bottom = (bridge.Bounds.Y + bridge.Bounds.Height) * map.CellSizeMeters + actorClearance;
            if (position.X >= left && position.X <= right
                && position.Y >= top && position.Y <= bottom) return true;
        }
        return false;
    }

    private static double DistanceToClosedPolyline(
        IReadOnlyList<WorldPosition> points,
        WorldPosition position)
    {
        double distance = DistanceToPolyline(points, position);
        return Math.Min(distance, DistanceToSegment(points[^1], points[0], position));
    }

    private static double DistanceToPolyline(
        IReadOnlyList<WorldPosition> points,
        WorldPosition position)
    {
        double distance = double.PositiveInfinity;
        for (int index = 1; index < points.Count; index++)
            distance = Math.Min(distance, DistanceToSegment(points[index - 1], points[index], position));
        return distance;
    }

    private static double DistanceToSegment(
        WorldPosition start,
        WorldPosition end,
        WorldPosition position)
    {
        double x = end.X - start.X;
        double y = end.Y - start.Y;
        double lengthSquared = x * x + y * y;
        if (lengthSquared == 0) return Distance(start, position);
        double projection = Math.Clamp(
            ((position.X - start.X) * x + (position.Y - start.Y) * y) / lengthSquared,
            0,
            1);
        return Distance(
            new WorldPosition(start.X + projection * x, start.Y + projection * y),
            position);
    }

    private static bool ContainsPoint(
        IReadOnlyList<WorldPosition> polygon,
        WorldPosition point)
    {
        bool inside = false;
        for (int current = 0, previous = polygon.Count - 1; current < polygon.Count; previous = current++)
        {
            WorldPosition a = polygon[current];
            WorldPosition b = polygon[previous];
            bool crosses = (a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X;
            if (crosses) inside = !inside;
        }
        return inside;
    }

    private static double Distance(WorldPosition left, WorldPosition right)
    {
        double x = left.X - right.X;
        double y = left.Y - right.Y;
        return Math.Sqrt(x * x + y * y);
    }
}
