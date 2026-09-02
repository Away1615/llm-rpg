using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Actors;
using Alice.Interaction;
using Alice.Navigation;
using Alice.Npc;
using Alice.ProductRuntime;

namespace Alice.LivingTown;

public enum SleepAccessPolicy
{
    Public,
    Household,
    OwnerOnly
}

public sealed record LivingTownCurrentActivity(
    LivingTownActivityKind Kind,
    string? ActivityRef);

public sealed class LivingTownMemoryJournal
{
    private readonly List<LivingTownMemorySeed> _memories;
    private readonly HashSet<string> _memoryIds = new(StringComparer.Ordinal);

    public LivingTownMemoryJournal(ActorId actorId, IEnumerable<LivingTownMemorySeed> memories)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(memories);
        _memories = memories.ToList();
        foreach (LivingTownMemorySeed memory in _memories)
        {
            ArgumentNullException.ThrowIfNull(memory);
            if (string.IsNullOrWhiteSpace(memory.MemoryId)
                || memory.SourceEventId != memory.Emotion.SourceEventId
                || !_memoryIds.Add(memory.MemoryId))
                throw new ArgumentException("Living Town memories require unique source-bound identities.", nameof(memories));
        }
        _memories.Sort(LivingTownMemorySeedComparer.Instance);
        ActorId = actorId;
    }

    public ActorId ActorId { get; }
    public IReadOnlyList<LivingTownMemorySeed> Snapshot() =>
        new ReadOnlyCollection<LivingTownMemorySeed>(_memories.ToArray());

    public LivingTownMemoryAdmissionResult Admit(
        LivingTownMemorySeed memory,
        LivingTownMemorySignificance significance)
    {
        ArgumentNullException.ThrowIfNull(memory);
        if (memory.SourceEventId != memory.Emotion.SourceEventId)
            throw new ArgumentException("Memory emotion must bind to the exact source event.", nameof(memory));
        if (!Enum.IsDefined(significance)) throw new ArgumentOutOfRangeException(nameof(significance));
        if (!LivingTownMemoryAdmissionPolicy.IsLongTermEligible(significance))
            return new LivingTownMemoryAdmissionResult(LivingTownMemoryAdmissionStatus.RejectedInsignificant, memory);
        if (!_memoryIds.Add(memory.MemoryId))
            return new LivingTownMemoryAdmissionResult(LivingTownMemoryAdmissionStatus.Duplicate, memory);
        _memories.Add(memory);
        _memories.Sort(LivingTownMemorySeedComparer.Instance);
        return new LivingTownMemoryAdmissionResult(LivingTownMemoryAdmissionStatus.Admitted, memory);
    }

    public bool ApplyEmotion(SourceEventId sourceEventId, MemoryEmotion emotion)
    {
        ArgumentNullException.ThrowIfNull(sourceEventId);
        ArgumentNullException.ThrowIfNull(emotion);
        if (emotion.SourceEventId != sourceEventId)
            throw new ArgumentException("Memory emotion must bind to the selected source.", nameof(emotion));
        int index = _memories.FindIndex(value => value.SourceEventId == sourceEventId);
        if (index < 0) return false;
        LivingTownMemorySeed current = _memories[index];
        _memories[index] = current with { Emotion = emotion };
        return true;
    }

    public void Restore(IEnumerable<LivingTownMemorySeed> memories)
    {
        ArgumentNullException.ThrowIfNull(memories);
        LivingTownMemorySeed[] snapshot = memories.ToArray();
        if (snapshot.Any(value => value is null
            || value.SourceEventId != value.Emotion.SourceEventId)
            || snapshot.Select(value => value.MemoryId).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new InvalidDataException("Saved Living Town memories are invalid.");
        _memories.Clear();
        _memoryIds.Clear();
        foreach (LivingTownMemorySeed memory in snapshot)
        {
            _memories.Add(memory);
            _memoryIds.Add(memory.MemoryId);
        }
        _memories.Sort(LivingTownMemorySeedComparer.Instance);
    }

    public IReadOnlyList<RankedLivingTownMemory> Retrieve(
        IEnumerable<LivingTownMemoryRankEvidence> rankEvidence,
        LivingTownMemoryRankingProfile profile) =>
        LivingTownMemoryRanker.Rank(_memories, rankEvidence, profile);

    private sealed class LivingTownMemorySeedComparer : IComparer<LivingTownMemorySeed>
    {
        public static LivingTownMemorySeedComparer Instance { get; } = new();
        public int Compare(LivingTownMemorySeed? left, LivingTownMemorySeed? right) =>
            StringComparer.Ordinal.Compare(left?.MemoryId, right?.MemoryId);
    }
}

/// <summary>Minimal G1 Actor owner. Later Gates add navigation and interaction without changing execution shape.</summary>
public sealed class LivingTownNpcStateOwner
{
    public LivingTownNpcStateOwner(
        LivingTownNpcInitialState initialState,
        ScheduleRuntime schedule,
        LivingTownMemoryJournal memory)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(memory);
        ActorId actorId = initialState.SharedActorState.Identity.ActorId;
        if (initialState.NpcState.ActorId != actorId
            || initialState.Profile.ActorId != actorId
            || schedule.ActorId != actorId
            || memory.ActorId != actorId)
            throw new ArgumentException("Living Town runtime components must belong to one Actor.");
        SharedActorState = initialState.SharedActorState;
        NpcState = initialState.NpcState;
        Profile = initialState.Profile;
        Position = initialState.Profile.StartingPosition;
        CurrentEmotion = initialState.Profile.CurrentEmotion;
        Schedule = schedule;
        Memory = memory;
    }

    public ActorId ActorId => SharedActorState.Identity.ActorId;
    public SharedActorState SharedActorState { get; private set; }
    public NpcState NpcState { get; }
    public LivingTownNpcProfile Profile { get; }
    public WorldPosition Position { get; private set; }
    public CurrentEmotionState CurrentEmotion { get; private set; }
    public ScheduleRuntime Schedule { get; }
    public LivingTownMemoryJournal Memory { get; }
    public IReadOnlyList<ScheduleOpportunity> OpenScheduleOpportunities => Schedule.GetOpenOpportunities();
    public LivingTownPlaceRef? RoutineDestination { get; private set; }
    public LivingTownPlaceRef? AutonomousDestination { get; private set; }
    public bool IsRoutineTravelling { get; private set; }
    public string? PreferredScheduleEntryId { get; private set; }

    public TownScheduleEntryConfiguration? CurrentScheduleEntry
    {
        get
        {
            ScheduleOpportunity? opportunity = OpenScheduleOpportunities
                .Where(value => RoutineDestination is null || value.Entry.PlaceRef == RoutineDestination)
                .OrderBy(value => value.Entry.Obligation)
                .ThenBy(value => value.Entry.StartsAtTickOfDay)
                .FirstOrDefault();
            return opportunity is null
                ? null
                : Profile.Schedule.Single(value =>
                    StringComparer.Ordinal.Equals(value.EntryId, opportunity.Entry.EntryId.Value));
        }
    }

    public bool TryPreferScheduleEntry(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        if (!Profile.Schedule.Any(value => StringComparer.Ordinal.Equals(value.EntryId, entryId))) return false;
        PreferredScheduleEntryId = entryId;
        return true;
    }

    public void RestorePosition(WorldPosition position)
    {
        Position = position;
        RoutineDestination = null;
        IsRoutineTravelling = false;
    }

    public void ApplyEmotion(CurrentEmotionState emotion)
    {
        ArgumentNullException.ThrowIfNull(emotion);
        CurrentEmotion = emotion;
    }

    public void ApplyVitals(TownActorVitalsSnapshot vitals)
    {
        SharedActorState = new SharedActorState(
            SharedActorState.Identity,
            new ActorBodyState(ActorId, new Health(vitals.HealthCurrent, vitals.HealthMaximum),
                new Satiety(vitals.Satiety), new Spirit(vitals.Spirit), vitals.Disease),
            SharedActorState.Traversal,
            SharedActorState.Inventory,
            SharedActorState.Equipment);
    }

    public void SetAutonomousDestination(LivingTownPlaceRef? destination) => AutonomousDestination = destination;

    internal void ApplyRoutinePosition(
        WorldPosition position,
        LivingTownPlaceRef destination,
        bool isTravelling)
    {
        Position = position;
        RoutineDestination = destination;
        IsRoutineTravelling = isTravelling;
    }

    internal void ClearRoutineMovement()
    {
        RoutineDestination = null;
        IsRoutineTravelling = false;
    }
}

internal sealed class LivingTownScheduleExecutionSource : IActorExecutionSelector
{
    private readonly LivingTownNpcStateOwner _state;
    private readonly RegionSocialGameplayRuntime _gameplay;
    private long _nextInteractionAtTicks;

    public LivingTownScheduleExecutionSource(
        LivingTownNpcStateOwner state,
        RegionSocialGameplayRuntime gameplay)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _gameplay = gameplay ?? throw new ArgumentNullException(nameof(gameplay));
        ActorId = state.ActorId;
    }

    public ActorId ActorId { get; }
    public ActorExecutionIntent Select(SimTime now)
    {
        TownScheduleEntryConfiguration? entry = _state.IsRoutineTravelling
            ? null
            : _state.CurrentScheduleEntry;
        if (entry?.ExecutionMode == "Interact" && entry.TargetRef is not null
            && now.Ticks >= _nextInteractionAtTicks)
        {
            string prefix = $"{entry.ActionFamilyId}/";
            TownGameplayActionOffer? offer = _gameplay.GetActionOffers(ActorId, entry.TargetRef, now)
                .FirstOrDefault(value => value.Validation.Available
                    && value.EntryId.StartsWith(prefix, StringComparison.Ordinal));
            if (offer is not null)
            {
                _nextInteractionAtTicks = checked(now.Ticks
                    + _gameplay.GetInteractionDurationTicks(entry.TargetRef));
                var action = new GameActionSpec(ActorId, offer.Selection.Binding, offer.Selection.Arguments);
                return new ActorExecutionIntent(
                    ActorId,
                    ActorExecutionMode.Interact,
                    new InteractExecutionPayload(ActorId, action),
                    $"schedule/{entry.EntryId}/{entry.Purpose}",
                    AutonomousNpcCognitionRoute.L0);
            }
        }

        string reason = entry is null
            ? "living-town/routine/wait"
            : $"schedule/{entry.EntryId}/{entry.Purpose}/at/{entry.PlaceRef}";
        return ActorExecutionIntent.Wait(ActorId, reason);
    }
}

public sealed class LivingTownNpcRuntime
{
    private LivingTownPlaceRef? _routeDestination;
    private IReadOnlyList<WorldPosition> _routeWaypoints = Array.Empty<WorldPosition>();
    private int _routeWaypointIndex;

    internal LivingTownNpcRuntime(
        LivingTownNpcStateOwner stateOwner,
        UnifiedActorExecutionDispatcher dispatcher,
        AutonomousNpc schedulerNpc,
        LivingTownActivityTracker activityTracker)
    {
        ArgumentNullException.ThrowIfNull(stateOwner);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(schedulerNpc);
        ArgumentNullException.ThrowIfNull(activityTracker);
        if (stateOwner.ActorId != dispatcher.ActorId
            || stateOwner.ActorId != schedulerNpc.ActorId
            || stateOwner.ActorId != activityTracker.ActorId)
            throw new ArgumentException("Living Town runtime adapters must belong to one Actor.");
        State = stateOwner;
        Dispatcher = dispatcher;
        SchedulerNpc = schedulerNpc;
        ActivityTracker = activityTracker;
    }

    public ActorId ActorId => State.ActorId;
    public LivingTownNpcStateOwner State { get; }
    public UnifiedActorExecutionDispatcher Dispatcher { get; }
    public AutonomousNpc SchedulerNpc { get; }
    public LivingTownActivityTracker ActivityTracker { get; }
    public IReadOnlyList<ActorExecutionMode> RegisteredModes => Dispatcher.RegisteredModes;

    public LivingTownCurrentActivity GetCurrentActivity()
    {
        TownScheduleEntryConfiguration? scheduleEntry = State.CurrentScheduleEntry;
        LivingTownActivityKind kind = State.IsRoutineTravelling
            ? LivingTownActivityKind.Travel
            : scheduleEntry is null
                ? ActivityTracker.ActivityKind
                : Enum.Parse<SchedulePurpose>(scheduleEntry.Purpose, false) switch
                {
                    SchedulePurpose.Work => LivingTownActivityKind.Work,
                    SchedulePurpose.Sleep => LivingTownActivityKind.Sleep,
                    SchedulePurpose.Meal => LivingTownActivityKind.Consumption,
                    SchedulePurpose.Social => LivingTownActivityKind.Social,
                    _ => LivingTownActivityKind.Waiting
                };
        string? activityRef = State.IsRoutineTravelling
            ? State.RoutineDestination?.Value
            : scheduleEntry?.PlaceRef ?? (kind == LivingTownActivityKind.None ? null : ActivityTracker.ActivityRef);
        return new LivingTownCurrentActivity(kind, activityRef);
    }

    internal void AdvanceSchedule(SimTime now) => State.Schedule.Advance(now);

    internal void AdvanceRoutineMovement(
        SimTime now,
        TownPlaceRegistry places,
        TownSpatialMap map,
        double baseUnitsPerTick)
    {
        ScheduleEntry? entry = State.Schedule.ResolveTravelEntry(now, State.PreferredScheduleEntryId);
        LivingTownPlaceRef? destination = entry?.PlaceRef ?? State.AutonomousDestination;
        if (destination is null || !places.TryResolveForActor(destination, ActorId, out WorldPosition destinationPosition))
        {
            ClearRoute();
            return;
        }

        if (_routeDestination != destination || _routeWaypoints.Count == 0)
        {
            LiveNavigationRoute route = map.SpeedProfile.SelectLiveRoute(State.Position, destinationPosition, baseUnitsPerTick);
            _routeDestination = destination;
            _routeWaypoints = route.Waypoints;
            _routeWaypointIndex = 0;
        }

        WorldPosition position = State.Position;
        double remaining = baseUnitsPerTick * map.SpeedProfile.ResolveMultiplier(position);
        while (remaining > 0 && _routeWaypointIndex < _routeWaypoints.Count)
        {
            WorldPosition waypoint = _routeWaypoints[_routeWaypointIndex];
            double distance = Distance(position, waypoint);
            if (distance <= remaining)
            {
                position = waypoint;
                remaining -= distance;
                _routeWaypointIndex++;
                continue;
            }

            double fraction = remaining / distance;
            position = new WorldPosition(
                position.X + (waypoint.X - position.X) * fraction,
                position.Y + (waypoint.Y - position.Y) * fraction);
            remaining = 0;
        }

        bool travelling = _routeWaypointIndex < _routeWaypoints.Count;
        State.ApplyRoutinePosition(position, destination, travelling);
    }

    private void ClearRoute()
    {
        _routeDestination = null;
        _routeWaypoints = Array.Empty<WorldPosition>();
        _routeWaypointIndex = 0;
        State.ClearRoutineMovement();
    }

    private static double Distance(WorldPosition left, WorldPosition right)
    {
        double dx = right.X - left.X;
        double dy = right.Y - left.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public sealed class LivingTownPopulationRuntime : IDisposable
{
    private readonly ReadOnlyCollection<LivingTownNpcRuntime> _npcs;
    private readonly AutonomousNpcScheduler _scheduler;
    private readonly LivingTownPublicEventRuntime _publicEvents;
    private readonly TownPlaceRegistry? _places;
    private readonly TownSpatialMap? _map;
    private readonly double _baseMovementUnitsPerTick;

    internal LivingTownPopulationRuntime(
        TownPopulationManifest manifest,
        LivingTownRuntimeConfiguration configuration,
        IEnumerable<LivingTownNpcRuntime> npcs,
        TownSpatialMap? map)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(npcs);
        LivingTownNpcRuntime[] snapshot = npcs.ToArray();
        Array.Sort(snapshot, LivingTownNpcRuntimeComparer.Instance);
        if (snapshot.Length != manifest.Actors.Count)
            throw new ArgumentException("Runtime roster must cover the exact manifest population.", nameof(npcs));
        _npcs = Array.AsReadOnly(snapshot);
        _scheduler = new AutonomousNpcScheduler(GetSchedulerNpcs(snapshot), configuration.NpcActionIntervalTicks);
        _publicEvents = new LivingTownPublicEventRuntime(manifest, configuration.TicksPerDay);
        _places = map is null ? null : new TownPlaceRegistry(manifest, map);
        _map = map;
        _baseMovementUnitsPerTick = 5.0 * configuration.SimulationTickIntervalMilliseconds / 1000.0;
        ManifestId = manifest.ManifestId;
    }

    public TownPopulationManifestId ManifestId { get; }
    public IReadOnlyList<LivingTownNpcRuntime> Npcs => _npcs;
    public LivingTownPublicEventRuntime PublicEvents => _publicEvents;

    public LivingTownPlaceRef? ResolvePlace(WorldPosition position) => _places?.ResolveContaining(position);

    public IReadOnlyList<LivingTownNpcDurableState> CaptureDurableState() =>
        new ReadOnlyCollection<LivingTownNpcDurableState>(_npcs.Select(npc =>
            new LivingTownNpcDurableState(
                npc.ActorId.Value,
                npc.State.Position.X,
                npc.State.Position.Y,
                npc.State.CurrentEmotion.Kind,
                npc.State.CurrentEmotion.Valence,
                npc.State.CurrentEmotion.Intensity,
                npc.State.CurrentEmotion.SourceEventId?.Value,
                npc.State.Memory.Snapshot(),
                npc.ActivityTracker.ActivityKind,
                npc.ActivityTracker.ActivityRef,
                npc.SchedulerNpc.NextDispatchAt.Ticks,
                npc.SchedulerNpc.DispatchSequence)).ToArray());

    public void RestoreDurableState(
        IEnumerable<LivingTownNpcDurableState> states,
        SimTime settledAt)
    {
        ArgumentNullException.ThrowIfNull(states);
        Dictionary<string, LivingTownNpcDurableState> byActor = states.ToDictionary(
            value => value.ActorId, StringComparer.Ordinal);
        if (byActor.Count != _npcs.Count) throw new InvalidDataException("Saved NPC roster is incomplete.");
        _publicEvents.Advance(settledAt);
        foreach (LivingTownNpcRuntime npc in _npcs)
        {
            LivingTownNpcDurableState state = byActor[npc.ActorId.Value];
            npc.State.RestorePosition(new WorldPosition(state.WorldX, state.WorldY));
            npc.State.ApplyEmotion(new CurrentEmotionState(
                state.EmotionKind,
                state.EmotionValence,
                state.EmotionIntensity,
                state.EmotionSourceEventId is null ? null : new SourceEventId(state.EmotionSourceEventId)));
            npc.State.Memory.Restore(state.Memories);
            npc.ActivityTracker.Restore(state.ActivityKind, state.ActivityRef);
            npc.SchedulerNpc.RestoreDispatchState(new SimTime(state.NextDispatchAtTicks), state.DispatchSequence);
            npc.AdvanceSchedule(settledAt);
        }
    }

    public ActorExecutionBatch Advance(SimTime now)
    {
        _publicEvents.Advance(now);
        foreach (LivingTownNpcRuntime npc in _npcs)
        {
            npc.AdvanceSchedule(now);
            if (_places is not null && _map is not null)
                npc.AdvanceRoutineMovement(now, _places, _map, _baseMovementUnitsPerTick);
        }
        return _scheduler.Advance(now);
    }

    public ActorExecutionBatch Advance(
        SimTime now,
        DateTimeOffset wallTime,
        CancellationToken cancellationToken)
    {
        _ = wallTime;
        cancellationToken.ThrowIfCancellationRequested();
        return Advance(now);
    }

    public LivingTownNpcRuntime GetNpc(ActorId actorId)
    {
        ActorIdentity.ValidateActorId(actorId);
        foreach (LivingTownNpcRuntime npc in _npcs)
        {
            if (npc.ActorId == actorId) return npc;
        }
        throw new KeyNotFoundException($"Actor {actorId.Value} is not in the Living Town roster.");
    }

    public void Dispose()
    {
    }

    private static AutonomousNpc[] GetSchedulerNpcs(IEnumerable<LivingTownNpcRuntime> npcs)
    {
        var result = new List<AutonomousNpc>();
        foreach (LivingTownNpcRuntime npc in npcs) result.Add(npc.SchedulerNpc);
        return result.ToArray();
    }

    private sealed class LivingTownNpcRuntimeComparer : IComparer<LivingTownNpcRuntime>
    {
        public static LivingTownNpcRuntimeComparer Instance { get; } = new();
        public int Compare(LivingTownNpcRuntime? left, LivingTownNpcRuntime? right) =>
            StringComparer.Ordinal.Compare(left?.ActorId.Value, right?.ActorId.Value);
    }
}

public sealed record LivingTownNpcDurableState(
    string ActorId,
    double WorldX,
    double WorldY,
    LivingTownEmotionKind EmotionKind,
    double EmotionValence,
    double EmotionIntensity,
    string? EmotionSourceEventId,
    IReadOnlyList<LivingTownMemorySeed> Memories,
    LivingTownActivityKind ActivityKind,
    string? ActivityRef,
    long NextDispatchAtTicks,
    long DispatchSequence);

public sealed class NpcRuntimeFactory
{
    private readonly NpcInitialStateBuilder _stateBuilder = new();

    public LivingTownPopulationRuntime Create(
        TownPopulationManifest manifest,
        LivingTownRuntimeConfiguration configuration,
        RegionSocialGameplayRuntime gameplay,
        TownHistoryRuntime? history = null,
        TownSpatialMap? map = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(gameplay);
        var calendar = new TownCalendar(configuration.TicksPerDay);
        var runtimes = new List<LivingTownNpcRuntime>();
        foreach (TownNpcConfiguration actorConfiguration in manifest.Actors)
        {
            LivingTownNpcInitialState initial = _stateBuilder.Build(actorConfiguration, manifest);
            RoutineSchedule routine = RoutineSchedule.FromConfiguration(
                initial.Profile.ActorId,
                actorConfiguration.Schedule,
                configuration.TicksPerDay);
            var schedule = new ScheduleRuntime(routine, calendar);
            IEnumerable<LivingTownMemorySeed> initialMemories = history is null
                ? initial.Profile.Memories
                : history.GetInitialMemories(initial.Profile.ActorId);
            var memory = new LivingTownMemoryJournal(initial.Profile.ActorId, initialMemories);
            var owner = new LivingTownNpcStateOwner(initial, schedule, memory);
            var source = new LivingTownScheduleExecutionSource(owner, gameplay);
            TownGameplayActorExecutor executor = gameplay.CreateExecutor(owner.ActorId);
            var activityTracker = new LivingTownActivityTracker(owner.ActorId);
            var dispatcher = new UnifiedActorExecutionDispatcher(source, executor, activityTracker);
            var npc = new AutonomousNpc(dispatcher, new SimTime(configuration.FirstDispatchAtTicks));
            runtimes.Add(new LivingTownNpcRuntime(owner, dispatcher, npc, activityTracker));
        }
        return new LivingTownPopulationRuntime(manifest, configuration, runtimes, map);
    }
}
