using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Alice.Actors;
using Alice.Memory;
using Alice.Navigation;
using Alice.ProductRuntime;

namespace Alice.LivingTown;

public sealed record TownHistoryConfigurationDocument
{
    [JsonRequired, JsonPropertyName("historical_actors")]
    public TownHistoricalActorConfiguration[] HistoricalActors { get; init; } = [];

    [JsonRequired, JsonPropertyName("history_events")]
    public TownHistoryEventConfiguration[] HistoryEvents { get; init; } = [];
}

public sealed record TownHistoricalActorConfiguration
{
    [JsonRequired, JsonPropertyName("actor_id")] public string ActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("residence_place_ref")] public string ResidencePlaceRef { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("occupation_id")] public string OccupationId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
}

public sealed record TownHistoryEventConfiguration
{
    [JsonRequired, JsonPropertyName("event_id")] public string EventId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("event_kind")] public string EventKind { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("occurred_at_ticks")] public long OccurredAtTicks { get; init; }
    [JsonRequired, JsonPropertyName("location_id")] public string LocationId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("world_x")] public double WorldX { get; init; }
    [JsonRequired, JsonPropertyName("world_y")] public double WorldY { get; init; }
    [JsonRequired, JsonPropertyName("spatial_layer")] public string SpatialLayer { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("experiences")] public TownHistoryExperienceConfiguration[] Experiences { get; init; } = [];
    [JsonRequired, JsonPropertyName("visible_facts")] public TownHistoryVisibleFactConfiguration[] VisibleFacts { get; init; } = [];
    [JsonRequired, JsonPropertyName("source_references")] public TownHistorySourceReferenceConfiguration[] SourceReferences { get; init; } = [];
}

public sealed record TownHistoryExperienceConfiguration
{
    [JsonRequired, JsonPropertyName("actor_id")] public string ActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("role")] public string Role { get; init; } = string.Empty;
    [JsonPropertyName("teller_actor_id")] public string? TellerActorId { get; init; }
    [JsonPropertyName("original_event_id")] public string? OriginalEventId { get; init; }
    [JsonPropertyName("witness_world_x")] public double? WitnessWorldX { get; init; }
    [JsonPropertyName("witness_world_y")] public double? WitnessWorldY { get; init; }
    [JsonPropertyName("witness_spatial_layer")] public string? WitnessSpatialLayer { get; init; }
}

public sealed record TownHistoryVisibleFactConfiguration
{
    [JsonRequired, JsonPropertyName("fact_id")] public string FactId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("actor_id")] public string ActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("text")] public string Text { get; init; } = string.Empty;
}

public sealed record TownHistorySourceReferenceConfiguration
{
    [JsonRequired, JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("value")] public string Value { get; init; } = string.Empty;
}

public static class TownHistoryConfigurationValidator
{
    public const int InitialMemoryCount = 24;

    public static void Validate(
        TownHistoryConfigurationDocument history,
        TownWorldConfigurationDocument world,
        TownSpatialMap map,
        TownPopulationManifest population)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(population);
        HashSet<string> activeActors = population.Actors
            .Select(actor => actor.Identity.ActorId).ToHashSet(StringComparer.Ordinal);
        HashSet<string> historicalActors = Unique(history.HistoricalActors.Select(actor => actor.ActorId), "historical Actor");
        if (historicalActors.Overlaps(activeActors))
            throw new InvalidDataException("Historical and active Actor identities must be distinct.");
        HashSet<string> allActors = new(activeActors, StringComparer.Ordinal);
        allActors.UnionWith(historicalActors);

        HashSet<string> occupations = population.Occupations
            .Select(value => value.OccupationId).ToHashSet(StringComparer.Ordinal);
        HashSet<string> households = population.Households
            .Select(value => value.HouseholdId).ToHashSet(StringComparer.Ordinal);
        HashSet<string> locations = CreateLocationIds(map);
        foreach (TownHistoricalActorConfiguration actor in history.HistoricalActors)
        {
            Require(actor.ActorId, "historical Actor");
            Require(actor.Name, "historical Actor name");
            Require(actor.Status, "historical Actor status");
            if (!locations.Contains(actor.ResidencePlaceRef) || !occupations.Contains(actor.OccupationId))
                throw new InvalidDataException($"Historical Actor '{actor.ActorId}' is not map/occupation grounded.");
        }

        HashSet<string> eventIds = Unique(history.HistoryEvents.Select(value => value.EventId), "history event");
        Dictionary<string, long> eventTimes = history.HistoryEvents.ToDictionary(
            value => value.EventId, value => value.OccurredAtTicks, StringComparer.Ordinal);
        Dictionary<string, int> activeMemoryCounts = activeActors.ToDictionary(value => value, _ => 0, StringComparer.Ordinal);
        bool hasWitness = false;
        bool hasTestimony = false;
        bool hasInterior = false;

        foreach (TownHistoryEventConfiguration historyEvent in history.HistoryEvents.OrderBy(value => value.OccurredAtTicks))
        {
            Require(historyEvent.EventKind, "history event kind");
            Require(historyEvent.LocationId, "history event location");
            Require(historyEvent.SpatialLayer, "history spatial layer");
            if (historyEvent.OccurredAtTicks < 0 || !locations.Contains(historyEvent.LocationId))
                throw new InvalidDataException($"History event '{historyEvent.EventId}' has an invalid time/location.");
            var eventPosition = new WorldPosition(historyEvent.WorldX, historyEvent.WorldY);
            if (!map.Contains(eventPosition))
                throw new InvalidDataException($"History event '{historyEvent.EventId}' is outside the map.");

            HashSet<string> experienceActors = Unique(historyEvent.Experiences.Select(value => value.ActorId), "history experience Actor");
            HashSet<string> factActors = Unique(historyEvent.VisibleFacts.Select(value => value.ActorId), "visible-fact Actor");
            _ = Unique(historyEvent.VisibleFacts.Select(value => value.FactId), "visible fact");
            if (experienceActors.Count == 0 || !experienceActors.SetEquals(factActors)
                || experienceActors.Any(actorId => !allActors.Contains(actorId)))
                throw new InvalidDataException($"History event '{historyEvent.EventId}' has inconsistent experiences/facts.");
            foreach (TownHistoryVisibleFactConfiguration fact in historyEvent.VisibleFacts)
                Require(fact.Text, "actor-visible fact text");

            HashSet<string> participantIds = historyEvent.Experiences
                .Where(value => value.Role == "Participant")
                .Select(value => value.ActorId).ToHashSet(StringComparer.Ordinal);
            foreach (TownHistoryExperienceConfiguration experience in historyEvent.Experiences)
            {
                if (experience.Role is not ("Participant" or "Witness" or "Testimony"))
                    throw new InvalidDataException($"History event '{historyEvent.EventId}' has an unknown experience role.");
                if (activeMemoryCounts.ContainsKey(experience.ActorId)) activeMemoryCounts[experience.ActorId]++;

                if (experience.Role == "Witness")
                {
                    hasWitness = true;
                    if (experience.WitnessWorldX is null || experience.WitnessWorldY is null
                        || experience.WitnessSpatialLayer is null)
                        throw new InvalidDataException("Historical witnesses require occurrence-time position/layer evidence.");
                    var presence = new TownHistoryActorPresence(
                        new ActorId(experience.ActorId),
                        new WorldPosition(experience.WitnessWorldX.Value, experience.WitnessWorldY.Value),
                        experience.WitnessSpatialLayer,
                        null);
                    if (!TownHistoryRuntime.IsWitnessEligible(eventPosition, historyEvent.SpatialLayer, presence))
                        throw new InvalidDataException($"History witness '{experience.ActorId}' is outside the admitted 8m/layer rule.");
                }

                if (experience.Role == "Testimony")
                {
                    hasTestimony = true;
                    if (experience.TellerActorId is null || experience.OriginalEventId != historyEvent.EventId
                        || !participantIds.Contains(experience.TellerActorId))
                        throw new InvalidDataException($"History testimony in '{historyEvent.EventId}' lost teller/source provenance.");
                }
                else if (experience.TellerActorId is not null || experience.OriginalEventId is not null)
                {
                    throw new InvalidDataException("Only testimony may carry teller/original-source fields.");
                }
            }

            if (historyEvent.SpatialLayer.StartsWith("interior:", StringComparison.Ordinal))
            {
                hasInterior = true;
                if (!StringComparer.Ordinal.Equals(historyEvent.SpatialLayer[9..], historyEvent.LocationId))
                    throw new InvalidDataException("Interior history layer must name its exact configured location.");
                foreach (TownHistoryExperienceConfiguration experience in historyEvent.Experiences)
                {
                    if (experience.Role == "Testimony") continue;
                    if (!HasHistoricalInteriorAccess(experience.ActorId, historyEvent.LocationId, population, history))
                        throw new InvalidDataException($"Actor '{experience.ActorId}' lacks historical interior access.");
                }
            }

            foreach (TownHistorySourceReferenceConfiguration reference in historyEvent.SourceReferences)
            {
                Require(reference.Kind, "history source-reference kind");
                Require(reference.Value, "history source-reference value");
                bool valid = reference.Kind switch
                {
                    "actor" => allActors.Contains(reference.Value),
                    "household" => households.Contains(reference.Value),
                    "occupation" => occupations.Contains(reference.Value),
                    "place" => locations.Contains(reference.Value),
                    "resource" => map.ResourceRegions.Any(value => value.RegionId == reference.Value),
                    "road" => map.Roads.Any(value => value.RoadId == reference.Value),
                    "bottleneck" => map.Bottlenecks.Any(value => value.BottleneckId == reference.Value),
                    "event" => eventIds.Contains(reference.Value)
                        && eventTimes[reference.Value] <= historyEvent.OccurredAtTicks,
                    _ => false
                };
                if (!valid) throw new InvalidDataException($"History event '{historyEvent.EventId}' has an unresolved source reference.");
            }
        }

        if (activeMemoryCounts.Any(pair => pair.Value != InitialMemoryCount))
            throw new InvalidDataException(
                $"Every active NPC must derive exactly {InitialMemoryCount} initial source-linked memories.");
        if (!hasWitness || !hasTestimony || !hasInterior)
            throw new InvalidDataException("Initial history must include witness, testimony and interior evidence.");
    }

    private static bool HasHistoricalInteriorAccess(
        string actorId,
        string locationId,
        TownPopulationManifest population,
        TownHistoryConfigurationDocument history)
    {
        TownNpcConfiguration? active = population.Actors.FirstOrDefault(value => value.Identity.ActorId == actorId);
        if (active is not null)
            return active.ResidencePlaceRef == locationId || active.PrivateRoomPlaceRef == locationId;
        TownHistoricalActorConfiguration? historical = history.HistoricalActors.FirstOrDefault(value => value.ActorId == actorId);
        return historical?.ResidencePlaceRef == locationId;
    }

    private static HashSet<string> CreateLocationIds(TownSpatialMap map)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        result.UnionWith(map.Settlements.Select(value => value.SettlementId));
        result.UnionWith(map.Roads.Select(value => value.RoadId));
        result.UnionWith(map.Bottlenecks.Select(value => value.BottleneckId));
        result.UnionWith(map.Buildings.Select(value => value.BuildingId));
        result.UnionWith(map.Rooms.Select(value => value.RoomId));
        result.UnionWith(map.ResourceRegions.Select(value => value.RegionId));
        return result;
    }

    private static HashSet<string> Unique(IEnumerable<string> values, string label)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in values)
        {
            Require(value, label);
            if (!result.Add(value)) throw new InvalidDataException($"Duplicate {label} identity '{value}'.");
        }
        return result;
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"Blank {label} is not allowed.");
    }
}

public sealed record TownHistoryActorPresence(
    ActorId ActorId,
    WorldPosition Position,
    string SpatialLayer,
    LivingTownMemoryJournal? Memory);

public sealed record TownHistoryProjectionResult(
    CanonicalEventAdmissionKind Kind,
    CanonicalHistoryEventRecord Event,
    int MemoriesAdmitted);

/// <summary>One product history network plus actor-local memory projection.</summary>
public sealed class TownHistoryRuntime
{
    public const double WitnessRadiusWorldUnits = 2.0;
    private readonly CanonicalEventStore _eventStore;
    private readonly ActorExperienceIndex _experienceIndex = new();
    private readonly TownSpatialMap _map;
    private readonly HashSet<string> _activeActorIds;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<LivingTownMemorySeed>> _initialMemories;

    private TownHistoryRuntime(
        CanonicalEventStore eventStore,
        TownSpatialMap map,
        HashSet<string> activeActorIds,
        IReadOnlyDictionary<string, IReadOnlyList<LivingTownMemorySeed>> initialMemories)
    {
        _eventStore = eventStore;
        _map = map;
        _activeActorIds = activeActorIds;
        _initialMemories = initialMemories;
    }

    public CanonicalEventStore EventStore => _eventStore;
    public IReadOnlyList<CanonicalHistoryEventRecord> Events => _eventStore.GetHistoryInsertionOrderSnapshot();
    public IReadOnlyList<ActorExperienceReference> Experiences => _experienceIndex.GetInsertionOrderSnapshot();
    public bool ContainsSource(string sourceEventId) =>
        _eventStore.Contains(new DecisionMemorySourceId(sourceEventId));

    public static TownHistoryRuntime Create(
        TownHistoryConfigurationDocument configuration,
        TownSpatialMap map,
        TownPopulationManifest population,
        CanonicalEventStore eventStore)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(eventStore);
        HashSet<string> activeActorIds = population.Actors
            .Select(value => value.Identity.ActorId).ToHashSet(StringComparer.Ordinal);
        var memoryLists = activeActorIds.ToDictionary(
            actorId => actorId,
            _ => new List<LivingTownMemorySeed>(),
            StringComparer.Ordinal);
        var runtime = new TownHistoryRuntime(
            eventStore,
            map,
            activeActorIds,
            new ReadOnlyDictionary<string, IReadOnlyList<LivingTownMemorySeed>>(
                memoryLists.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<LivingTownMemorySeed>)pair.Value,
                    StringComparer.Ordinal)));

        foreach (TownHistoryEventConfiguration historyEvent in configuration.HistoryEvents
                     .OrderBy(value => value.OccurredAtTicks).ThenBy(value => value.EventId, StringComparer.Ordinal))
        {
            CanonicalHistoryEventRecord record = CreateRecord(historyEvent);
            CanonicalHistoryEventAdmissionResult admission = eventStore.Append(record);
            if (admission.Kind == CanonicalEventAdmissionKind.IdentityConflict)
                throw new InvalidDataException($"Canonical history event '{historyEvent.EventId}' conflicts with an existing source.");
            foreach (CanonicalHistoryExperience experience in record.Experiences)
            {
                ActorExperienceReference reference = CreateExperienceReference(record.SourceId, experience);
                ActorExperienceAdmissionResult experienceAdmission = runtime._experienceIndex.Append(reference);
                if (experienceAdmission.Kind == ActorExperienceAdmissionKind.RoleConflict)
                    throw new InvalidDataException("Canonical history experience role conflicts with its source.");
                if (!activeActorIds.Contains(experience.ActorId.Value)) continue;
                string text = record.ActorVisibleFacts.Single(value => value.ActorId == experience.ActorId).Text;
                memoryLists[experience.ActorId.Value].Add(CreateMemory(record, experience.ActorId, text));
            }
        }

        foreach (List<LivingTownMemorySeed> memories in memoryLists.Values)
            memories.Sort((left, right) => StringComparer.Ordinal.Compare(left.MemoryId, right.MemoryId));
        return runtime;
    }

    public IReadOnlyList<LivingTownMemorySeed> GetInitialMemories(ActorId actorId)
    {
        ActorIdentity.ValidateActorId(actorId);
        if (!_initialMemories.TryGetValue(actorId.Value, out IReadOnlyList<LivingTownMemorySeed>? memories))
            throw new KeyNotFoundException($"Actor '{actorId.Value}' has no initial history projection.");
        return new ReadOnlyCollection<LivingTownMemorySeed>(memories.ToArray());
    }

    public void RestoreDurableEvents(
        IEnumerable<CanonicalHistoryEventRecord> events,
        IEnumerable<LivingTownNpcRuntime> npcs)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(npcs);
        Dictionary<string, LivingTownMemoryJournal> journals = npcs.ToDictionary(
            value => value.ActorId.Value,
            value => value.State.Memory,
            StringComparer.Ordinal);
        foreach (CanonicalHistoryEventRecord record in events)
        {
            CanonicalHistoryEventAdmissionResult admission = _eventStore.Append(record);
            if (admission.Kind == CanonicalEventAdmissionKind.IdentityConflict)
                throw new InvalidDataException($"Saved history event '{record.SourceId.Value}' conflicts with the configured world.");
            foreach (CanonicalHistoryExperience experience in record.Experiences)
            {
                _ = _experienceIndex.Append(CreateExperienceReference(record.SourceId, experience));
                if (!journals.TryGetValue(experience.ActorId.Value, out LivingTownMemoryJournal? journal)) continue;
                string text = record.ActorVisibleFacts.Single(value => value.ActorId == experience.ActorId).Text;
                _ = journal.Admit(
                    CreateMemory(record, experience.ActorId, text),
                    LivingTownMemorySignificance.OtherSignificant);
            }
        }
    }

    public TownHistoryProjectionResult RecordRuntimeEvent(
        string sourceEventId,
        string eventKind,
        long occurredAtTicks,
        string locationId,
        WorldPosition position,
        string spatialLayer,
        IEnumerable<ActorId> participantActorIds,
        string actorVisibleText,
        IEnumerable<CanonicalHistorySourceReference> sourceReferences,
        IEnumerable<TownHistoryActorPresence> presences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(spatialLayer);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorVisibleText);
        ArgumentNullException.ThrowIfNull(participantActorIds);
        ArgumentNullException.ThrowIfNull(sourceReferences);
        ArgumentNullException.ThrowIfNull(presences);

        TownHistoryActorPresence[] presenceSnapshot = presences.ToArray();
        ActorId[] participants = participantActorIds.Distinct().ToArray();
        if (participants.Length == 0)
            throw new ArgumentException("A runtime event requires at least one participant.", nameof(participantActorIds));

        var experiences = participants.Select(value => new CanonicalHistoryExperience(
            value,
            CanonicalHistoryExperienceRole.Participant,
            null,
            null)).ToList();
        foreach (TownHistoryActorPresence presence in presenceSnapshot)
        {
            if (participants.Contains(presence.ActorId)
                || !IsWitnessEligible(position, spatialLayer, presence)) continue;
            experiences.Add(new CanonicalHistoryExperience(
                presence.ActorId,
                CanonicalHistoryExperienceRole.Witness,
                null,
                null));
        }

        CanonicalHistoryActorVisibleFact[] facts = experiences
            .Select(value => new CanonicalHistoryActorVisibleFact(value.ActorId, actorVisibleText)).ToArray();
        var record = new CanonicalHistoryEventRecord(
            new DecisionMemorySourceId(sourceEventId),
            eventKind,
            occurredAtTicks,
            locationId,
            position,
            spatialLayer,
            experiences,
            facts,
            sourceReferences);
        CanonicalHistoryEventAdmissionResult admission = _eventStore.Append(record);
        if (admission.Kind == CanonicalEventAdmissionKind.IdentityConflict)
            throw new InvalidOperationException($"Runtime event '{sourceEventId}' conflicts with canonical history.");

        int admitted = 0;
        foreach (CanonicalHistoryExperience experience in record.Experiences)
        {
            _ = _experienceIndex.Append(CreateExperienceReference(record.SourceId, experience));
            TownHistoryActorPresence? presence = presenceSnapshot.FirstOrDefault(value => value.ActorId == experience.ActorId);
            if (presence?.Memory is null || !_activeActorIds.Contains(experience.ActorId.Value)) continue;
            LivingTownMemoryAdmissionResult result = presence.Memory.Admit(
                CreateMemory(record, experience.ActorId, actorVisibleText),
                LivingTownMemorySignificance.OtherSignificant);
            if (result.Status == LivingTownMemoryAdmissionStatus.Admitted) admitted++;
        }
        return new TownHistoryProjectionResult(admission.Kind, admission.Record, admitted);
    }

    public TownHistoryProjectionResult ProjectAcceptedAction(
        ActorExecutionReceipt receipt,
        WorldPosition actorPosition,
        string actorLayer,
        IEnumerable<TownHistoryActorPresence> presences)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorLayer);
        ArgumentNullException.ThrowIfNull(presences);
        if (receipt.Outcome != ActorExecutionOutcome.Completed)
            throw new ArgumentException("Only accepted execution receipts become canonical action events.", nameof(receipt));

        string family = receipt.Result is AuthorityCommitExecutionResult authority
            ? authority.ActionFamily
            : receipt.Mode.ToString();
        return RecordRuntimeEvent(
            $"execution/{receipt.ExecutionId.Value}",
            $"action/{family}",
            receipt.SourceTime.Ticks,
            actorPosition,
            actorLayer,
            [receipt.ActorId],
            $"{receipt.ActorId.Value} completed {family}.",
            [new CanonicalHistorySourceReference("receipt", receipt.ExecutionId.Value)],
            presences);
    }

    public TownHistoryProjectionResult RecordRuntimeEvent(
        string sourceEventId,
        string eventKind,
        long occurredAtTicks,
        WorldPosition position,
        string spatialLayer,
        IEnumerable<ActorId> participants,
        string visibleText,
        IEnumerable<CanonicalHistorySourceReference> sourceReferences,
        IEnumerable<TownHistoryActorPresence> presences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(spatialLayer);
        ArgumentException.ThrowIfNullOrWhiteSpace(visibleText);
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(sourceReferences);
        ArgumentNullException.ThrowIfNull(presences);
        TownHistoryActorPresence[] presenceSnapshot = presences.ToArray();
        ActorId[] participantSnapshot = participants.Distinct().ToArray();
        if (participantSnapshot.Length == 0)
            throw new ArgumentException("A runtime history event requires at least one participant.", nameof(participants));
        var experiences = participantSnapshot.Select(value =>
            new CanonicalHistoryExperience(value, CanonicalHistoryExperienceRole.Participant, null, null)).ToList();
        foreach (TownHistoryActorPresence presence in presenceSnapshot)
        {
            if (participantSnapshot.Contains(presence.ActorId)
                || !IsWitnessEligible(position, spatialLayer, presence)) continue;
            experiences.Add(new CanonicalHistoryExperience(
                presence.ActorId,
                CanonicalHistoryExperienceRole.Witness,
                null,
                null));
        }
        CanonicalHistoryActorVisibleFact[] facts = experiences.Select(value =>
            new CanonicalHistoryActorVisibleFact(value.ActorId, visibleText)).ToArray();
        var record = new CanonicalHistoryEventRecord(
            new DecisionMemorySourceId(sourceEventId),
            eventKind,
            occurredAtTicks,
            ResolveLocationId(position),
            position,
            spatialLayer,
            experiences,
            facts,
            sourceReferences);
        CanonicalHistoryEventAdmissionResult admission = _eventStore.Append(record);
        if (admission.Kind == CanonicalEventAdmissionKind.IdentityConflict)
            throw new InvalidOperationException($"Runtime event '{sourceEventId}' conflicts with canonical history.");

        int admitted = 0;
        foreach (CanonicalHistoryExperience experience in record.Experiences)
        {
            _ = _experienceIndex.Append(CreateExperienceReference(record.SourceId, experience));
            TownHistoryActorPresence? presence = presenceSnapshot.FirstOrDefault(value => value.ActorId == experience.ActorId);
            if (presence?.Memory is null || !_activeActorIds.Contains(experience.ActorId.Value)) continue;
            LivingTownMemoryAdmissionResult result = presence.Memory.Admit(
                CreateMemory(record, experience.ActorId, visibleText),
                LivingTownMemorySignificance.OtherSignificant);
            if (result.Status == LivingTownMemoryAdmissionStatus.Admitted) admitted++;
        }
        return new TownHistoryProjectionResult(admission.Kind, record, admitted);
    }

    public static bool IsWitnessEligible(
        WorldPosition eventPosition,
        string eventLayer,
        TownHistoryActorPresence presence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventLayer);
        ArgumentNullException.ThrowIfNull(presence);
        if (!StringComparer.Ordinal.Equals(eventLayer, presence.SpatialLayer)) return false;
        double dx = presence.Position.X - eventPosition.X;
        double dy = presence.Position.Y - eventPosition.Y;
        return dx * dx + dy * dy <= WitnessRadiusWorldUnits * WitnessRadiusWorldUnits;
    }

    private string ResolveLocationId(WorldPosition position)
    {
        foreach (TownRoomMapConfiguration room in _map.Rooms)
            if (Contains(room.Bounds, position, _map.CellSizeMeters)) return room.RoomId;
        foreach (TownBuildingMapConfiguration building in _map.Buildings)
            if (Contains(building.Bounds, position, _map.CellSizeMeters)) return building.BuildingId;
        foreach (TownResourceRegionMapConfiguration region in _map.ResourceRegions)
            if (Contains(region.Bounds, position, _map.CellSizeMeters)) return region.RegionId;
        return _map.Settlements.OrderBy(value => DistanceSquared(_map.ToWorld(value.CenterCell), position))
            .First().SettlementId;
    }

    private static bool Contains(TownMapRect bounds, WorldPosition position, int cellSize) =>
        position.X >= bounds.X * cellSize && position.Y >= bounds.Y * cellSize
        && position.X <= (bounds.X + bounds.Width) * cellSize
        && position.Y <= (bounds.Y + bounds.Height) * cellSize;

    private static double DistanceSquared(WorldPosition left, WorldPosition right)
    {
        double dx = left.X - right.X;
        double dy = left.Y - right.Y;
        return dx * dx + dy * dy;
    }

    private static CanonicalHistoryEventRecord CreateRecord(TownHistoryEventConfiguration source)
    {
        var sourceId = new DecisionMemorySourceId(source.EventId);
        return new CanonicalHistoryEventRecord(
            sourceId,
            source.EventKind,
            source.OccurredAtTicks,
            source.LocationId,
            new WorldPosition(source.WorldX, source.WorldY),
            source.SpatialLayer,
            source.Experiences.Select(value => new CanonicalHistoryExperience(
                new ActorId(value.ActorId),
                ParseRole(value.Role),
                value.TellerActorId is null ? null : new ActorId(value.TellerActorId),
                value.OriginalEventId is null ? null : new DecisionMemorySourceId(value.OriginalEventId))),
            source.VisibleFacts.Select(value =>
                new CanonicalHistoryActorVisibleFact(new ActorId(value.ActorId), value.Text)),
            source.SourceReferences.Select(value =>
                new CanonicalHistorySourceReference(value.Kind, value.Value)));
    }

    private static CanonicalHistoryExperienceRole ParseRole(string role) => role switch
    {
        "Participant" => CanonicalHistoryExperienceRole.Participant,
        "Witness" => CanonicalHistoryExperienceRole.Witness,
        "Testimony" => CanonicalHistoryExperienceRole.Testimony,
        _ => throw new InvalidDataException($"Unknown canonical history experience role '{role}'.")
    };

    private static ActorExperienceReference CreateExperienceReference(
        DecisionMemorySourceId sourceId,
        CanonicalHistoryExperience experience) =>
        new(experience.ActorId, sourceId, experience.Role switch
        {
            CanonicalHistoryExperienceRole.Participant => ActorExperienceRole.Participant,
            CanonicalHistoryExperienceRole.Witness => ActorExperienceRole.Witness,
            CanonicalHistoryExperienceRole.Testimony => ActorExperienceRole.Testimony,
            _ => throw new ArgumentOutOfRangeException(nameof(experience))
        });

    private static LivingTownMemorySeed CreateMemory(
        CanonicalHistoryEventRecord historyEvent,
        ActorId actorId,
        string visibleText)
    {
        var sourceId = new SourceEventId(historyEvent.SourceId.Value);
        return new LivingTownMemorySeed(
            $"memory/{actorId.Value}/{historyEvent.SourceId.Value}",
            sourceId,
            historyEvent.OccurredAtTicks,
            visibleText,
            new MemoryEmotion(LivingTownEmotionKind.Neutral, 0, 0.2, sourceId, historyEvent.OccurredAtTicks));
    }
}
