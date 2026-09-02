using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Activities;
using Alice.ProductRuntime;
using Alice.NpcExecution;
using Alice.Navigation;
using Alice.Presentation;
using Alice.World;
using Godot;

namespace Alice.LivingTown;

public enum LivingTownActivityKind
{
    None,
    Travel,
    Waiting,
    Work,
    Sleep,
    Gather,
    Consumption,
    Social,
    Experience
}

public sealed record NpcRuntimeProjection(
    ActorId ActorId,
    WorldPosition Position,
    LivingTownActivityKind ActivityKind,
    string? ActivityRef,
    long Revision);

public sealed class LivingTownActivityTracker : IActorExecutionObserver
{
    public LivingTownActivityTracker(ActorId actorId)
    {
        ActorIdentity.ValidateActorId(actorId);
        ActorId = actorId;
    }

    public ActorId ActorId { get; }
    public LivingTownActivityKind ActivityKind { get; private set; }
    public string? ActivityRef { get; private set; }

    public void ObserveSelection(ActorExecutionIntent intent, SimTime now)
    {
        ArgumentNullException.ThrowIfNull(intent);
        _ = now;
        if (intent.ActorId != ActorId)
            throw new InvalidOperationException("Observed execution intent belongs to another Actor.");
        ActivityKind = Project(intent.Mode);
        ActivityRef = intent.Evidence;
    }

    public void ObserveDispatch(ActorExecutionRequest request, ActorExecutionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(receipt);
        if (request.ActorId != ActorId || receipt.ActorId != ActorId)
            throw new InvalidOperationException("Observed dispatch belongs to another Actor.");
        if (receipt.Outcome == ActorExecutionOutcome.Rejected)
        {
            ActivityKind = LivingTownActivityKind.None;
            ActivityRef = null;
        }
    }

    public void Restore(LivingTownActivityKind kind, string? activityRef)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ActivityKind = kind;
        ActivityRef = activityRef;
    }

    public static LivingTownActivityKind Project(ActorExecutionMode mode)
    {
        return mode switch
        {
            ActorExecutionMode.Navigate => LivingTownActivityKind.Travel,
            ActorExecutionMode.Interact => LivingTownActivityKind.Gather,
            ActorExecutionMode.Communicate => LivingTownActivityKind.Social,
            ActorExecutionMode.Wait => LivingTownActivityKind.Waiting,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

}

public interface INpcEntityProjectionPort
{
    ActorId ActorId { get; }
    bool IsProjected { get; }
    bool ApplyProjection(NpcRuntimeProjection projection);
    bool ReleaseProjection(NpcRuntimeProjection projection);
    void ApplyCognitionPresentation(LivingTownCognitionRoute route, bool visible);
}

public sealed class ActorSceneRegistry
{
    private readonly Dictionary<ActorId, INpcEntityProjectionPort> _ports = [];

    public IReadOnlyList<ActorId> ActorIds
    {
        get
        {
            ActorId[] values = _ports.Keys.ToArray();
            Array.Sort(values, ActorIdComparer.Instance);
            return Array.AsReadOnly(values);
        }
    }

    public void Register(INpcEntityProjectionPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        ActorIdentity.ValidateActorId(port.ActorId);
        if (!_ports.TryAdd(port.ActorId, port)) throw new ArgumentException("Actor scene port is already registered.", nameof(port));
    }

    public void Unregister(ActorId actorId, INpcEntityProjectionPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        if (!_ports.TryGetValue(actorId, out INpcEntityProjectionPort? current) || !ReferenceEquals(current, port))
            throw new InvalidOperationException("Only the exact registered Actor scene port may be removed.");
        _ports.Remove(actorId);
    }

    public bool TryResolve(ActorId actorId, out INpcEntityProjectionPort? port) => _ports.TryGetValue(actorId, out port);

    private sealed class ActorIdComparer : IComparer<ActorId>
    {
        public static ActorIdComparer Instance { get; } = new();
        public int Compare(ActorId left, ActorId right) => StringComparer.Ordinal.Compare(left.Value, right.Value);
    }
}

public sealed class TownPlaceRegistry
{
    private readonly IReadOnlyDictionary<LivingTownPlaceRef, WorldPosition> _positions;
    private readonly IReadOnlyDictionary<LivingTownPlaceRef, TownMapRect> _bounds;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> _visitorOrdinals;

    public TownPlaceRegistry(TownPopulationManifest manifest, TownSpatialMap map)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(map);
        var positions = new Dictionary<LivingTownPlaceRef, WorldPosition>();
        foreach (TownPlaceConfiguration place in manifest.Places)
        {
            positions.Add(new LivingTownPlaceRef(place.PlaceRef), new WorldPosition(place.WorldX, place.WorldY));
        }
        _positions = new ReadOnlyDictionary<LivingTownPlaceRef, WorldPosition>(positions);
        var bounds = new Dictionary<LivingTownPlaceRef, TownMapRect>();
        foreach (TownBuildingMapConfiguration place in map.Buildings)
            bounds[new LivingTownPlaceRef(place.BuildingId)] = place.Bounds;
        foreach (TownRoomMapConfiguration place in map.Rooms)
            bounds[new LivingTownPlaceRef(place.RoomId)] = place.Bounds;
        foreach (TownResourceRegionMapConfiguration place in map.ResourceRegions)
            bounds[new LivingTownPlaceRef(place.RegionId)] = place.Bounds;
        _bounds = new ReadOnlyDictionary<LivingTownPlaceRef, TownMapRect>(bounds);

        TownNpcConfiguration[] actors = manifest.Actors
            .OrderBy(value => value.Identity.ActorId, StringComparer.Ordinal)
            .ToArray();
        var visitorOrdinals = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);
        foreach (TownPlaceConfiguration place in manifest.Places)
        {
            string[] visitors = actors
                .OrderByDescending(actor => Visits(actor, place.PlaceRef))
                .ThenBy(actor => actor.Identity.ActorId, StringComparer.Ordinal)
                .Select(actor => actor.Identity.ActorId)
                .ToArray();
            visitorOrdinals[place.PlaceRef] = new ReadOnlyDictionary<string, int>(visitors
                .Select((actorId, index) => (actorId, index))
                .ToDictionary(value => value.actorId, value => value.index, StringComparer.Ordinal));
        }
        _visitorOrdinals = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>(visitorOrdinals);
        TownId = manifest.TownId;
    }

    public TownId TownId { get; }
    public bool TryResolve(LivingTownPlaceRef placeRef, out WorldPosition position)
    {
        ArgumentNullException.ThrowIfNull(placeRef);
        return _positions.TryGetValue(placeRef, out position);
    }

    public bool TryResolveForActor(
        LivingTownPlaceRef placeRef,
        ActorId actorId,
        out WorldPosition position)
    {
        ArgumentNullException.ThrowIfNull(placeRef);
        ActorIdentity.ValidateActorId(actorId);
        if (!_positions.TryGetValue(placeRef, out WorldPosition center))
        {
            position = default;
            return false;
        }
        int ordinal = _visitorOrdinals[placeRef.Value][actorId.Value];
        if (!_bounds.TryGetValue(placeRef, out TownMapRect? bounds))
        {
            position = OffsetAround(center, ordinal, 1.05);
            return true;
        }

        const double inset = 0.6;
        const double spacing = 1.05;
        int columns = Math.Max(1, (int)Math.Floor((bounds.Width - inset * 2) / spacing) + 1);
        int rows = Math.Max(1, (int)Math.Floor((bounds.Height - inset * 2) / spacing) + 1);
        int slot = ordinal % checked(columns * rows);
        double x = bounds.X + inset + slot % columns * spacing;
        double y = bounds.Y + inset + slot / columns * spacing;
        position = new WorldPosition(
            Math.Min(bounds.X + bounds.Width - inset, x),
            Math.Min(bounds.Y + bounds.Height - inset, y));
        return true;
    }

    public LivingTownPlaceRef? ResolveContaining(WorldPosition position)
    {
        foreach ((LivingTownPlaceRef placeRef, TownMapRect bounds) in _bounds
                     .OrderBy(value => value.Value.Width * value.Value.Height))
            if (position.X >= bounds.X && position.X <= bounds.X + bounds.Width
                && position.Y >= bounds.Y && position.Y <= bounds.Y + bounds.Height)
                return placeRef;
        return _positions
            .Where(value => Distance(value.Value, position) <= 1.6)
            .OrderBy(value => Distance(value.Value, position))
            .Select(value => value.Key)
            .FirstOrDefault();
    }

    private static bool Visits(TownNpcConfiguration actor, string placeRef) =>
        StringComparer.Ordinal.Equals(actor.ResidencePlaceRef, placeRef)
        || StringComparer.Ordinal.Equals(actor.PrivateRoomPlaceRef, placeRef)
        || StringComparer.Ordinal.Equals(actor.WorkplacePlaceRef, placeRef)
        || actor.Schedule.Any(entry => StringComparer.Ordinal.Equals(entry.PlaceRef, placeRef));

    private static WorldPosition OffsetAround(WorldPosition center, int ordinal, double spacing)
    {
        int column = ordinal % 5 - 2;
        int row = ordinal / 5 % 5 - 2;
        return new WorldPosition(center.X + column * spacing, center.Y + row * spacing);
    }

    private static double Distance(WorldPosition left, WorldPosition right)
    {
        double x = left.X - right.X;
        double y = left.Y - right.Y;
        return Math.Sqrt(x * x + y * y);
    }
}

public sealed record TownInteractionBinding(TargetRef TargetRef, LivingTownPlaceRef PlaceRef, WorldPosition Position);

public sealed class TownInteractionRegistry
{
    private readonly IReadOnlyDictionary<TargetRef, TownInteractionBinding> _bindings;

    public TownInteractionRegistry(TownPopulationManifest manifest, TownPlaceRegistry places)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(places);
        var bindings = new Dictionary<TargetRef, TownInteractionBinding>();
        foreach (TownSleepFacilityConfiguration facility in manifest.SleepFacilities)
        {
            var placeRef = new LivingTownPlaceRef(facility.PlaceRef);
            if (!places.TryResolve(placeRef, out WorldPosition position))
                throw new ArgumentException("Sleep facility place is absent from the Town registry.", nameof(manifest));
            var targetRef = new TargetRef(facility.TargetRef);
            bindings.Add(targetRef, new TownInteractionBinding(targetRef, placeRef, position));
        }
        _bindings = new ReadOnlyDictionary<TargetRef, TownInteractionBinding>(bindings);
    }

    public bool TryResolve(TargetRef targetRef, out TownInteractionBinding? binding)
    {
        ArgumentNullException.ThrowIfNull(targetRef);
        return _bindings.TryGetValue(targetRef, out binding);
    }
}

/// <summary>Reads domain state and sends immutable projections to registered scene ports only.</summary>
public sealed class NpcProjectionCoordinator
{
    private readonly LivingTownPopulationRuntime _runtime;
    private readonly ActorSceneRegistry _scenes;
    private readonly Dictionary<ActorId, long> _revisions = [];

    public NpcProjectionCoordinator(LivingTownPopulationRuntime runtime, ActorSceneRegistry scenes)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(scenes);
        _runtime = runtime;
        _scenes = scenes;
    }

    public NpcRuntimeProjection Snapshot(ActorId actorId)
    {
        LivingTownNpcRuntime npc = _runtime.GetNpc(actorId);
        LivingTownCurrentActivity activity = npc.GetCurrentActivity();
        long revision = _revisions.TryGetValue(actorId, out long current) ? current : 0;
        return new NpcRuntimeProjection(
            actorId, npc.State.Position, activity.Kind, activity.ActivityRef, revision);
    }

    public bool Project(ActorId actorId)
    {
        if (!_scenes.TryResolve(actorId, out INpcEntityProjectionPort? port) || port is null) return false;
        NpcRuntimeProjection projection = NextSnapshot(actorId);
        return port.ApplyProjection(projection);
    }

    public bool Release(ActorId actorId)
    {
        if (!_scenes.TryResolve(actorId, out INpcEntityProjectionPort? port) || port is null) return false;
        NpcRuntimeProjection projection = NextSnapshot(actorId);
        return port.ReleaseProjection(projection);
    }

    private NpcRuntimeProjection NextSnapshot(ActorId actorId)
    {
        long revision = checked((_revisions.TryGetValue(actorId, out long current) ? current : 0) + 1);
        _revisions[actorId] = revision;
        NpcRuntimeProjection snapshot = Snapshot(actorId);
        return snapshot with { Revision = revision };
    }
}

/// <summary>Caller-owned visibility selection. It changes projection only, never simulation semantics.</summary>
public sealed class TownLodCoordinator
{
    private readonly NpcProjectionCoordinator _projection;
    private readonly HashSet<ActorId> _projected = [];

    public TownLodCoordinator(NpcProjectionCoordinator projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        _projection = projection;
    }

    public IReadOnlyList<ActorId> ProjectedActorIds
    {
        get
        {
            ActorId[] values = _projected.ToArray();
            Array.Sort(values, ActorIdComparer.Instance);
            return Array.AsReadOnly(values);
        }
    }

    public void SetProjectedActors(IEnumerable<ActorId> actorIds)
    {
        ArgumentNullException.ThrowIfNull(actorIds);
        var requested = new HashSet<ActorId>(actorIds);
        var removalList = new List<ActorId>();
        foreach (ActorId actorId in _projected)
        {
            if (!requested.Contains(actorId)) removalList.Add(actorId);
        }
        var additionList = new List<ActorId>();
        foreach (ActorId actorId in requested)
        {
            if (!_projected.Contains(actorId)) additionList.Add(actorId);
        }
        ActorId[] removals = removalList.ToArray();
        ActorId[] additions = additionList.ToArray();
        Array.Sort(removals, ActorIdComparer.Instance);
        Array.Sort(additions, ActorIdComparer.Instance);
        foreach (ActorId actorId in removals)
        {
            if (!_projection.Release(actorId)) throw new InvalidOperationException($"Failed to release Actor {actorId.Value} projection.");
            _projected.Remove(actorId);
        }
        foreach (ActorId actorId in additions)
        {
            if (!_projection.Project(actorId)) throw new InvalidOperationException($"Failed to project Actor {actorId.Value}.");
            _projected.Add(actorId);
        }
    }

    private sealed class ActorIdComparer : IComparer<ActorId>
    {
        public static ActorIdComparer Instance { get; } = new();
        public int Compare(ActorId left, ActorId right) => StringComparer.Ordinal.Compare(left.Value, right.Value);
    }
}

/// <summary>Godot adapter; NpcEntity remains a movement/display port and receives no domain writer.</summary>
public sealed class NpcEntitySceneProjectionPort : INpcEntityProjectionPort
{
    private readonly NpcEntity _entity;

    public NpcEntitySceneProjectionPort(ActorId actorId, NpcEntity entity)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(entity);
        if (!StringComparer.Ordinal.Equals(actorId.Value, entity.ActorIdentity))
            throw new ArgumentException("NpcEntity ActorIdentity must exact-match the scene registry ActorId.", nameof(entity));
        ActorId = actorId;
        _entity = entity;
    }

    public ActorId ActorId { get; }
    public bool IsProjected => _entity.IsProjectionActive;
    public bool ApplyProjection(NpcRuntimeProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (projection.ActorId != ActorId || !_entity.TryApplyStandingProjection(ActorId, projection.Position)) return false;
        LivingTownCharacterProjection presentation = LivingTownPresentationProjector.Project(projection.ActivityKind);
        string activity = presentation.ActivityLabel ?? "Idle";
        _entity.ApplyActivityPresentation($"{_entity.DisplayName} · {activity}");
        return true;
    }

    public bool ReleaseProjection(NpcRuntimeProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return projection.ActorId == ActorId && _entity.TryReleaseStandingProjection(ActorId, out _);
    }

    public void ApplyCognitionPresentation(LivingTownCognitionRoute route, bool visible)
    {
        string? label = visible && route != LivingTownCognitionRoute.None ? route.ToString() : null;
        Color color = route switch
        {
            LivingTownCognitionRoute.L0 => new Color("93c47d"),
            LivingTownCognitionRoute.L1 => new Color("ffd966"),
            LivingTownCognitionRoute.L2 => new Color("c27ba0"),
            _ => Colors.White
        };
        _entity.ApplyCognitionPresentation(label, color);
    }
}
