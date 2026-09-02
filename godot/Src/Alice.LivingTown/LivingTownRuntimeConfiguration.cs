using System.Text.Json;
using System.Text.Json.Serialization;
using Alice.Items;
using Alice.ProductRuntime;

namespace Alice.LivingTown;

public sealed record LivingTownRuntimeConfigurationDocument
{
    [JsonRequired, JsonPropertyName("first_dispatch_at_ticks")] public long FirstDispatchAtTicks { get; init; }
    [JsonRequired, JsonPropertyName("npc_action_interval_ticks")] public long NpcActionIntervalTicks { get; init; }
    [JsonRequired, JsonPropertyName("ticks_per_day")] public long TicksPerDay { get; init; }
    [JsonRequired, JsonPropertyName("simulation_tick_interval_milliseconds")] public int SimulationTickIntervalMilliseconds { get; init; }
    [JsonRequired, JsonPropertyName("trace_retention_entries")] public int TraceRetentionEntries { get; init; }
    [JsonRequired, JsonPropertyName("visible_trace_entries")] public int VisibleTraceEntries { get; init; }
    [JsonRequired, JsonPropertyName("integration_validation_duration_ticks")] public long IntegrationValidationDurationTicks { get; init; }
    [JsonRequired, JsonPropertyName("trace_file_name")] public string TraceFileName { get; init; } = string.Empty;
}

public sealed record TownWorldProviderConfiguration
{
    [JsonRequired, JsonPropertyName("queue")] public ProviderQueueConfiguration Queue { get; init; } = new();
    [JsonRequired, JsonPropertyName("profiles")] public ProviderProfilesConfiguration Profiles { get; init; } = new();
}

public sealed record TownWorldConfigurationDocument
{
    [JsonRequired, JsonPropertyName("world_id")] public string WorldId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("map")] public TownSpatialMapDocument Map { get; init; } = new();
    [JsonRequired, JsonPropertyName("runtime")] public LivingTownRuntimeConfigurationDocument Runtime { get; init; } = new();
    [JsonRequired, JsonPropertyName("player")] public ProductPlayerConfiguration Player { get; init; } = new();
    [JsonRequired, JsonPropertyName("dialogue")] public ProductDialogueConfiguration Dialogue { get; init; } = new();
    [JsonRequired, JsonPropertyName("provider")] public TownWorldProviderConfiguration Provider { get; init; } = new();
    [JsonRequired, JsonPropertyName("gameplay")] public TownGameplayConfigurationDocument Gameplay { get; init; } = new();
    [JsonRequired, JsonPropertyName("history")] public TownHistoryConfigurationDocument History { get; init; } = new();
    [JsonRequired, JsonPropertyName("social")] public TownSocialConfigurationDocument Social { get; init; } = new();
    [JsonRequired, JsonPropertyName("population")] public TownPopulationManifestDocument Population { get; init; } = new();
}

public sealed class LivingTownRuntimeConfiguration
{
    internal LivingTownRuntimeConfiguration(
        LivingTownRuntimeConfigurationDocument runtime,
        ProductPlayerConfiguration player,
        ProductDialogueConfiguration dialogue,
        TownWorldProviderConfiguration provider)
    {
        FirstDispatchAtTicks = runtime.FirstDispatchAtTicks;
        NpcActionIntervalTicks = runtime.NpcActionIntervalTicks;
        TicksPerDay = runtime.TicksPerDay;
        SimulationTickIntervalMilliseconds = runtime.SimulationTickIntervalMilliseconds;
        TraceRetentionEntries = runtime.TraceRetentionEntries;
        VisibleTraceEntries = runtime.VisibleTraceEntries;
        IntegrationValidationDurationTicks = runtime.IntegrationValidationDurationTicks;
        TraceFileName = runtime.TraceFileName;
        Player = player;
        Dialogue = dialogue;
        ProviderQueue = provider.Queue;
        ProviderProfiles = provider.Profiles;
    }

    public long FirstDispatchAtTicks { get; }
    public long NpcActionIntervalTicks { get; }
    public long TicksPerDay { get; }
    public int SimulationTickIntervalMilliseconds { get; }
    public int TraceRetentionEntries { get; }
    public int VisibleTraceEntries { get; }
    public long IntegrationValidationDurationTicks { get; }
    public string TraceFileName { get; }
    public ProductPlayerConfiguration Player { get; }
    public ProductDialogueConfiguration Dialogue { get; }
    public ProviderQueueConfiguration ProviderQueue { get; }
    public ProviderProfilesConfiguration ProviderProfiles { get; }
}

/// <summary>The sole unversioned product-world input and its closed cross-reference boundary.</summary>
public sealed class TownWorldConfiguration
{
    private TownWorldConfiguration(
        string worldId,
        TownSpatialMap map,
        TownPopulationManifest population,
        TownGameplayConfigurationDocument gameplay,
        TownHistoryConfigurationDocument history,
        TownSocialConfigurationDocument social,
        LivingTownRuntimeConfiguration runtime,
        DurableStateRegistry durableStateRegistry)
    {
        WorldId = worldId;
        Map = map;
        Population = population;
        Gameplay = gameplay;
        History = history;
        Social = social;
        Runtime = runtime;
        DurableStateRegistry = durableStateRegistry;
    }

    public string WorldId { get; }
    public TownSpatialMap Map { get; }
    public TownPopulationManifest Population { get; }
    public TownGameplayConfigurationDocument Gameplay { get; }
    public TownHistoryConfigurationDocument History { get; }
    public TownSocialConfigurationDocument Social { get; }
    public LivingTownRuntimeConfiguration Runtime { get; }
    public DurableStateRegistry DurableStateRegistry { get; }

    public static TownWorldConfiguration Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = File.ReadAllBytes(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        TownWorldConfigurationDocument document = JsonSerializer.Deserialize<TownWorldConfigurationDocument>(bytes, options)
            ?? throw new InvalidDataException("Town world configuration must contain one JSON object.");
        return Create(document);
    }

    public static TownWorldConfiguration Create(TownWorldConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateRuntime(document.Runtime);
        ValidateProviderQueue(document.Provider.Queue);
        ValidateProviderProfiles(document.Provider.Profiles);
        ValidatePlayer(document.Player);
        TownSpatialMap map = TownSpatialMap.Create(document.Map);
        TownPopulationManifest population = TownPopulationManifest.Create(document.Population);
        ValidateCrossReferences(document, map, population);
        TownHistoryConfigurationValidator.Validate(document.History, document, map, population);
        TownSocialConfigurationValidator.Validate(document.Social, document, population);
        var runtime = new LivingTownRuntimeConfiguration(
            document.Runtime,
            document.Player,
            document.Dialogue,
            document.Provider);
        return new TownWorldConfiguration(
            document.WorldId,
            map,
            population,
            document.Gameplay,
            document.History,
            document.Social,
            runtime,
            CreateDurableRegistry(document.WorldId, population, document.Player.ActorId, document.Gameplay));
    }

    private static void ValidateRuntime(LivingTownRuntimeConfigurationDocument runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (runtime.FirstDispatchAtTicks < 0
            || runtime.NpcActionIntervalTicks <= 0
            || runtime.TicksPerDay <= 0
            || runtime.SimulationTickIntervalMilliseconds <= 0
            || runtime.TraceRetentionEntries <= 0
            || runtime.VisibleTraceEntries <= 0
            || runtime.VisibleTraceEntries > runtime.TraceRetentionEntries
            || runtime.IntegrationValidationDurationTicks <= 0
            || string.IsNullOrWhiteSpace(runtime.TraceFileName)
            || runtime.TraceFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Living Town runtime configuration is invalid or incomplete.");
    }

    private static void ValidateCrossReferences(
        TownWorldConfigurationDocument document,
        TownSpatialMap map,
        TownPopulationManifest population)
    {
        if (string.IsNullOrWhiteSpace(document.WorldId))
            throw new InvalidDataException("Town world identity is required.");
        HashSet<string> actorIds = population.Actors.Select(GetActorId).ToHashSet(StringComparer.Ordinal);
        if (!actorIds.Add(document.Player.ActorId)
            || !StringComparer.Ordinal.Equals(document.Dialogue.PlayerActorId, document.Player.ActorId)
            || !actorIds.Contains(document.Dialogue.NpcActorId))
            throw new InvalidDataException("Player and dialogue Actor references must close over one Town world.");
        ValidateDialogue(document.Dialogue);
        RegionSocialGameplayRuntime.ValidateConfiguration(document.Gameplay);

        HashSet<string> gameplayRegionIds = document.Gameplay.Regions.Select(value => value.RegionId)
            .Concat(document.Gameplay.FarmPlots.Select(value => value.PlotId))
            .ToHashSet(StringComparer.Ordinal);

        if (!map.Contains(new Alice.Navigation.WorldPosition(document.Player.StartWorldX, document.Player.StartWorldY))
            || population.Places.Any(place => !map.ContainsPlaceId(place.PlaceRef))
            || population.Actors.Any(actor => !map.Contains(new Alice.Navigation.WorldPosition(actor.StartWorldX, actor.StartWorldY)))
            || document.Gameplay.Regions.Any(value => !map.ContainsPlaceId(value.RegionId))
            || document.Gameplay.FarmPlots.Any(value => !map.ContainsPlaceId(value.PlotId))
            || map.ResourceRegions.Any(value => !gameplayRegionIds.Contains(value.RegionId)))
            throw new InvalidDataException("Player, NPC and place references must fit the final spatial map.");

        var itemInstances = new HashSet<string>(document.Player.Tools.Select(value => value.ItemInstanceId), StringComparer.Ordinal);
        foreach (TownNpcConfiguration actor in population.Actors)
        {
            foreach (TownItemInstanceConfiguration instance in actor.Inventory.Instances)
            {
                if (!itemInstances.Add(instance.ItemInstanceId))
                    throw new InvalidDataException("Item-instance identities must be unique across the Town world.");
            }
            HashSet<string> fungibleAssets = actor.Inventory.Stacks.Select(GetStackItemTypeId).ToHashSet(StringComparer.Ordinal);
            if (actor.Currency.Any(value => !fungibleAssets.Add(value.CurrencyId)))
                throw new InvalidDataException("Coin and resource balances share one fungible identity namespace per Actor.");
        }
        ValidateProductionReferences(document, population, actorIds);
        ValidatePopulationTopology(document, map, population, actorIds);
    }

    private static void ValidateProductionReferences(
        TownWorldConfigurationDocument document,
        TownPopulationManifest population,
        HashSet<string> actorIds)
    {
        HashSet<string> assets = document.Gameplay.AssetDefinitions.Select(value => value.AssetId)
            .ToHashSet(StringComparer.Ordinal);
        IEnumerable<string> actorAssets = document.Player.Tools.Select(value => value.ToolTypeId)
            .Concat(document.Player.FungibleAssets.Select(value => value.AssetId))
            .Concat(population.Actors.SelectMany(value => value.Inventory.Instances.Select(item => item.ItemTypeId)))
            .Concat(population.Actors.SelectMany(value => value.Inventory.Stacks.Select(item => item.ItemTypeId)))
            .Concat(population.Actors.SelectMany(value => value.Currency.Select(item => item.CurrencyId)));
        HashSet<string> places = population.Places.Select(value => value.PlaceRef).ToHashSet(StringComparer.Ordinal);
        HashSet<(AssetContainerOwnerKind Kind, string Id)> containers = document.Gameplay.Containers
            .Select(value => (Enum.Parse<AssetContainerOwnerKind>(value.OwnerKind, false), value.OwnerId))
            .ToHashSet();
        bool HasCapability(IEnumerable<TownCapabilityConfiguration> capabilities, string capabilityId, int minimum) =>
            capabilities.Any(value => StringComparer.Ordinal.Equals(value.CapabilityId, capabilityId)
                && value.Value >= minimum);
        bool missingRecipeExecutor = document.Gameplay.Recipes.Any(recipe =>
            !HasCapability(document.Player.Capabilities, recipe.RequiredCapabilityId, recipe.MinimumCapability)
            && !population.Actors.Any(actor => HasCapability(
                actor.Capabilities, recipe.RequiredCapabilityId, recipe.MinimumCapability)));
        bool missingFarmExecutor = document.Gameplay.FarmPlots.Any(plot =>
            !HasCapability(document.Player.Capabilities, plot.RequiredCapabilityId, plot.MinimumCapability)
            && !population.Actors.Any(actor => HasCapability(
                actor.Capabilities, plot.RequiredCapabilityId, plot.MinimumCapability)));
        if (actorAssets.Any(value => !assets.Contains(value))
            || document.Gameplay.Recipes.Any(value => value.PlaceId is not null && !places.Contains(value.PlaceId))
            || missingRecipeExecutor || missingFarmExecutor
            || document.Gameplay.Services.Any(value => !places.Contains(value.PlaceId)
                || value.ProviderActorIds.Any(actor => !actorIds.Contains(actor))
                || !containers.Contains((Enum.Parse<AssetContainerOwnerKind>(value.ProviderOwnerKind, false),
                    value.ProviderContainerId))))
            throw new InvalidDataException("Gameplay production references do not close over the Town world.");
    }

    private static void ValidatePopulationTopology(
        TownWorldConfigurationDocument document,
        TownSpatialMap map,
        TownPopulationManifest population,
        HashSet<string> actorIds)
    {
        HashSet<string> settlementIds = map.Settlements.Select(value => value.SettlementId).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, TownBuildingMapConfiguration> buildings = map.Buildings.ToDictionary(value => value.BuildingId, StringComparer.Ordinal);
        Dictionary<string, TownRoomMapConfiguration> rooms = map.Rooms.ToDictionary(value => value.RoomId, StringComparer.Ordinal);
        Dictionary<string, TownNpcConfiguration> actors = population.Actors.ToDictionary(GetActorId, StringComparer.Ordinal);
        Dictionary<string, TownHouseholdConfiguration> households = population.Households.ToDictionary(value => value.HouseholdId, StringComparer.Ordinal);
        Dictionary<string, TownOccupationConfiguration> occupations = population.Occupations.ToDictionary(value => value.OccupationId, StringComparer.Ordinal);
        Dictionary<string, TownSleepFacilityConfiguration> sleepFacilities = population.SleepFacilities.ToDictionary(value => value.PlaceRef, StringComparer.Ordinal);
        Dictionary<string, TownGameplayPlaceConfiguration> accessPlaces = document.Gameplay.Places.ToDictionary(value => value.PlaceId, StringComparer.Ordinal);
        var ownedPrivateRooms = new HashSet<string>(StringComparer.Ordinal);

        foreach (TownNpcConfiguration actor in population.Actors)
        {
            if (!settlementIds.Contains(actor.SettlementId)
                || actor.ResidencePlaceRef is null || !buildings.TryGetValue(actor.ResidencePlaceRef, out TownBuildingMapConfiguration? residence)
                || residence.Kind != "House" || residence.SettlementId != actor.SettlementId
                || actor.PrivateRoomPlaceRef is null || !rooms.TryGetValue(actor.PrivateRoomPlaceRef, out TownRoomMapConfiguration? room)
                || room.Kind != "Private" || room.BuildingId != residence.BuildingId || !ownedPrivateRooms.Add(room.RoomId)
                || actor.HouseholdId is null || !households.ContainsKey(actor.HouseholdId)
                || actor.OccupationId is null || !occupations.ContainsKey(actor.OccupationId)
                || actor.WorkplacePlaceRef is null
                || !sleepFacilities.TryGetValue(room.RoomId, out TownSleepFacilityConfiguration? facility)
                || facility.AccessPolicy != "OwnerOnly"
                || facility.RequiredAccessRef is null || !actor.AccessRefs.Contains(facility.RequiredAccessRef, StringComparer.Ordinal))
                throw new InvalidDataException($"Actor '{actor.Identity.ActorId}' lacks a usable settlement, home, room, schedule or occupation projection.");

            if (!accessPlaces.TryGetValue(residence.BuildingId, out TownGameplayPlaceConfiguration? sharedAccess)
                || sharedAccess.AccessKind != "ResidentShared"
                || !sharedAccess.ResidentActorIds.Contains(actor.Identity.ActorId, StringComparer.Ordinal)
                || !accessPlaces.TryGetValue(room.RoomId, out TownGameplayPlaceConfiguration? privateAccess)
                || privateAccess.AccessKind != "PrivateRoom"
                || privateAccess.ResidentActorIds.Length != 1
                || privateAccess.ResidentActorIds[0] != actor.Identity.ActorId)
                throw new InvalidDataException($"Actor '{actor.Identity.ActorId}' lacks configured household or private-room access.");
        }

        foreach (TownHouseholdConfiguration household in population.Households)
        {
            if (!settlementIds.Contains(household.SettlementId)
                || !accessPlaces.TryGetValue(household.ResidencePlaceRef, out TownGameplayPlaceConfiguration? access)
                || !household.MemberActorIds.ToHashSet(StringComparer.Ordinal).SetEquals(access.ResidentActorIds))
                throw new InvalidDataException($"Household '{household.HouseholdId}' access does not match its admitted members.");
        }

        foreach (TownGameplayPlaceConfiguration place in document.Gameplay.Places)
        {
            if (!Enum.TryParse(place.AccessKind, false, out TownPlaceAccessKind _)
                || place.ResidentActorIds.Any(value => !actorIds.Contains(value)))
                throw new InvalidDataException($"Gameplay place '{place.PlaceId}' has invalid access references.");
        }
        foreach (TownGameplayInvitationConfiguration invitation in document.Gameplay.Invitations)
        {
            if (!accessPlaces.ContainsKey(invitation.PlaceId)
                || !actorIds.Contains(invitation.InviterActorId)
                || invitation.InviteeActorId != document.Player.ActorId && !actorIds.Contains(invitation.InviteeActorId))
                throw new InvalidDataException($"Invitation '{invitation.InvitationId}' has unresolved references.");
        }

        ValidateOccupationDependencies(document.Gameplay, population.Occupations, settlementIds);
        ValidateMerchantReferences(document.Gameplay, actors, occupations, actorIds);
        ValidateResourceWorkers(document.Gameplay, population.Occupations, actors);
    }

    private static void ValidateOccupationDependencies(
        TownGameplayConfigurationDocument gameplay,
        IReadOnlyList<TownOccupationConfiguration> occupations,
        HashSet<string> settlementIds)
    {
        Dictionary<string, TownOccupationConfiguration> occupationById = occupations.ToDictionary(value => value.OccupationId, StringComparer.Ordinal);
        Dictionary<string, TownGameplayRegionConfiguration> regionById = gameplay.Regions.ToDictionary(value => value.RegionId, StringComparer.Ordinal);
        Dictionary<string, TownGameplayContainerConfiguration> containerById = gameplay.Containers.ToDictionary(value => value.OwnerId, StringComparer.Ordinal);
        Dictionary<string, TownGameplayListingConfiguration> listingById = gameplay.Listings.ToDictionary(value => value.ListingId, StringComparer.Ordinal);
        Dictionary<string, TownGameplayRestockConfiguration> restockById = gameplay.Restocks.ToDictionary(value => value.RestockId, StringComparer.Ordinal);
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        AddSourceIds(sourceIds, regionById.Keys);
        AddSourceIds(sourceIds, occupationById.Keys);
        AddSourceIds(sourceIds, containerById.Keys);
        AddSourceIds(sourceIds, listingById.Keys);
        AddSourceIds(sourceIds, restockById.Keys);
        var crossSettlementDependents = new HashSet<string>(StringComparer.Ordinal);

        foreach (TownOccupationConfiguration occupation in occupations)
        {
            if (!settlementIds.Contains(occupation.SettlementId))
                throw new InvalidDataException($"Occupation '{occupation.OccupationId}' has an unknown settlement.");
            foreach (TownOccupationInputConfiguration input in occupation.Inputs)
            {
                foreach (string sourceId in input.SourceIds)
                {
                    if (!sourceIds.Contains(sourceId))
                        throw new InvalidDataException($"Occupation input '{occupation.OccupationId}/{input.AssetId}' has unresolved source '{sourceId}'.");
                    if (occupationById.TryGetValue(sourceId, out TownOccupationConfiguration? sourceOccupation)
                        && sourceOccupation.SettlementId != occupation.SettlementId)
                        crossSettlementDependents.Add(occupation.SettlementId);
                }
                bool assetProvided = input.SourceIds.Any(sourceId =>
                    SourceProvidesAsset(sourceId, input.AssetId, occupationById, regionById, containerById, listingById, restockById));
                if (!assetProvided)
                    throw new InvalidDataException($"Occupation input '{occupation.OccupationId}/{input.AssetId}' has no matching configured source.");
            }
        }
        if (settlementIds.Count > 1 && !crossSettlementDependents.SetEquals(settlementIds))
            throw new InvalidDataException("Every settlement requires at least one cross-settlement occupation dependency.");
    }

    private static void ValidateMerchantReferences(
        TownGameplayConfigurationDocument gameplay,
        Dictionary<string, TownNpcConfiguration> actors,
        Dictionary<string, TownOccupationConfiguration> occupations,
        HashSet<string> actorIds)
    {
        Dictionary<string, TownGameplayShopConfiguration> shops = gameplay.Shops.ToDictionary(value => value.ShopId, StringComparer.Ordinal);
        Dictionary<string, TownGameplayListingConfiguration> listings = gameplay.Listings.ToDictionary(value => value.ListingId, StringComparer.Ordinal);
        Dictionary<string, TownGameplayRestockConfiguration> restocks = gameplay.Restocks.ToDictionary(value => value.RestockId, StringComparer.Ordinal);
        HashSet<string> containers = gameplay.Containers.Select(value => value.OwnerId).ToHashSet(StringComparer.Ordinal);
        foreach (TownGameplayListingConfiguration listing in gameplay.Listings)
        {
            if (!shops.ContainsKey(listing.ShopId) || listing.Quantity <= 0 || listing.CoinPrice <= 0)
                throw new InvalidDataException($"Listing '{listing.ListingId}' is invalid.");
        }
        foreach (TownGameplayStockTargetConfiguration target in gameplay.StockTargets)
        {
            if (!shops.ContainsKey(target.ShopId))
                throw new InvalidDataException($"Stock target '{target.StockTargetId}' has no business.");
        }
        foreach (TownGameplayShopConfiguration shop in gameplay.Shops)
        {
            string[] shopListings = gameplay.Listings.Where(value => value.ShopId == shop.ShopId).Select(value => value.ListingId).ToArray();
            if (!containers.Contains(shop.ContainerId) || shop.ManagerActorIds.Length == 0 || shopListings.Length == 0
                || shop.ManagerActorIds.Any(value => !actorIds.Contains(value)))
                throw new InvalidDataException($"Shop '{shop.ShopId}' is incomplete.");
            foreach (string managerId in shop.ManagerActorIds)
            {
                TownNpcConfiguration manager = actors[managerId];
                TownOccupationConfiguration occupation = occupations[manager.OccupationId!];
                if (shopListings.Any(value => !occupation.ListingIds.Contains(value, StringComparer.Ordinal)))
                    throw new InvalidDataException($"Shop manager '{managerId}' is outside the configured merchant listing graph.");
            }
        }
        foreach (TownGameplayRestockConfiguration restock in gameplay.Restocks)
        {
            if (!actorIds.Contains(restock.MerchantActorId)
                || !containers.Contains(restock.SourceContainerId) || !containers.Contains(restock.ShopContainerId)
                || restock.Quantity <= 0)
                throw new InvalidDataException($"Restock '{restock.RestockId}' is incomplete.");
            TownNpcConfiguration merchant = actors[restock.MerchantActorId];
            if (!occupations[merchant.OccupationId!].RestockIds.Contains(restock.RestockId, StringComparer.Ordinal))
                throw new InvalidDataException($"Restock '{restock.RestockId}' is outside its merchant occupation.");
        }
        foreach (TownOccupationConfiguration occupation in occupations.Values)
        {
            if (occupation.ListingIds.Any(value => !listings.ContainsKey(value))
                || occupation.RestockIds.Any(value => !restocks.ContainsKey(value)))
                throw new InvalidDataException($"Merchant occupation '{occupation.OccupationId}' has unresolved listing or restock references.");
        }
    }

    private static void ValidateResourceWorkers(
        TownGameplayConfigurationDocument gameplay,
        IReadOnlyList<TownOccupationConfiguration> occupations,
        Dictionary<string, TownNpcConfiguration> actors)
    {
        Dictionary<string, TownGameplayRegionConfiguration> regions = gameplay.Regions.ToDictionary(value => value.RegionId, StringComparer.Ordinal);
        Dictionary<string, TownOccupationConfiguration> occupationById = occupations.ToDictionary(value => value.OccupationId, StringComparer.Ordinal);
        var coveredRegions = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownOccupationConfiguration occupation in occupations)
        {
            if (!regions.TryGetValue(occupation.WorkplacePlaceRef, out TownGameplayRegionConfiguration? region)) continue;
            if (!occupation.OutputAssetIds.Contains(region.OutputAssetId, StringComparer.Ordinal))
                throw new InvalidDataException($"Resource occupation '{occupation.OccupationId}' does not expose its region output.");
            coveredRegions.Add(region.RegionId);
            foreach (string workerId in occupation.WorkerActorIds)
            {
                TownNpcConfiguration worker = actors[workerId];
                TownScheduleEntryConfiguration? gathering = worker.Schedule.SingleOrDefault(value =>
                    value.ExecutionMode == "Interact" && value.ActionFamilyId == "region-operation" && value.TargetRef == region.RegionId);
                TownItemInstanceConfiguration? tool = string.IsNullOrWhiteSpace(region.RequiredToolTypeId)
                    ? null
                    : worker.Inventory.Instances.SingleOrDefault(value => value.ItemTypeId == region.RequiredToolTypeId);
                bool missingTool = !string.IsNullOrWhiteSpace(region.RequiredToolTypeId)
                    && (tool is null || worker.Inventory.EquippedHandInstanceId != tool.ItemInstanceId);
                if (gathering is null || missingTool
                    || !worker.Capabilities.Any(value => value.CapabilityId == region.RequiredCapabilityId
                        && value.Value >= region.MinimumCapability))
                    throw new InvalidDataException($"Resource worker '{workerId}' lacks the shared region-operation schedule, tool or capability.");
            }
        }
        if (!coveredRegions.SetEquals(regions.Keys))
            throw new InvalidDataException("Every configured resource region requires one ordinary resource occupation.");
        foreach (TownNpcConfiguration actor in actors.Values)
        {
            foreach (TownScheduleEntryConfiguration gathering in actor.Schedule.Where(value => value.ActionFamilyId == "region-operation"))
            {
                if (gathering.TargetRef is null || !regions.ContainsKey(gathering.TargetRef)
                    || actor.OccupationId is null || !occupationById.TryGetValue(actor.OccupationId, out TownOccupationConfiguration? occupation)
                    || occupation.WorkplacePlaceRef != gathering.TargetRef)
                    throw new InvalidDataException($"Gathering schedule '{gathering.EntryId}' is outside its resource occupation.");
            }
        }
    }

    private static bool SourceProvidesAsset(
        string sourceId,
        string assetId,
        Dictionary<string, TownOccupationConfiguration> occupations,
        Dictionary<string, TownGameplayRegionConfiguration> regions,
        Dictionary<string, TownGameplayContainerConfiguration> containers,
        Dictionary<string, TownGameplayListingConfiguration> listings,
        Dictionary<string, TownGameplayRestockConfiguration> restocks) =>
        occupations.TryGetValue(sourceId, out TownOccupationConfiguration? occupation)
            && occupation.OutputAssetIds.Contains(assetId, StringComparer.Ordinal)
        || regions.TryGetValue(sourceId, out TownGameplayRegionConfiguration? region) && region.OutputAssetId == assetId
        || containers.TryGetValue(sourceId, out TownGameplayContainerConfiguration? container)
            && container.Balances.Any(value => value.AssetId == assetId)
        || listings.TryGetValue(sourceId, out TownGameplayListingConfiguration? listing) && listing.AssetId == assetId
        || restocks.TryGetValue(sourceId, out TownGameplayRestockConfiguration? restock) && restock.AssetId == assetId;

    private static void AddSourceIds(HashSet<string> destination, IEnumerable<string> values)
    {
        foreach (string value in values)
            if (!destination.Add(value)) throw new InvalidDataException($"Dependency source identity '{value}' is ambiguous.");
    }

    private static DurableStateRegistry CreateDurableRegistry(
        string worldId,
        TownPopulationManifest population,
        string playerActorId,
        TownGameplayConfigurationDocument gameplay)
    {
        var registrations = new List<DurableAggregateRegistration>
        {
            Registration($"world/{worldId}/clock", DurableAggregateKind.WorldClock, DurableStateOwnerKind.World, worldId, 0),
            Registration($"world/{worldId}/history", DurableAggregateKind.CanonicalHistory, DurableStateOwnerKind.World, worldId, 10),
            Registration($"world/{worldId}/public-events", DurableAggregateKind.PublicEventState, DurableStateOwnerKind.World, worldId, 30),
            Registration($"world/{worldId}/conversations", DurableAggregateKind.ConversationState, DurableStateOwnerKind.World, worldId, 80),
            Registration($"world/{worldId}/commitments", DurableAggregateKind.CommitmentState, DurableStateOwnerKind.World, worldId, 90)
        };
        foreach (TownPlaceConfiguration place in population.Places)
            registrations.Add(Registration($"place/{place.PlaceRef}/state", DurableAggregateKind.PlaceState, DurableStateOwnerKind.Place, place.PlaceRef, 20));
        foreach (TownGameplayRegionConfiguration region in gameplay.Regions)
            registrations.Add(Registration($"region/{region.RegionId}/state", DurableAggregateKind.RegionState, DurableStateOwnerKind.Region, region.RegionId, 25));
        foreach (TownGameplayFarmPlotConfiguration plot in gameplay.FarmPlots)
            registrations.Add(Registration($"region/{plot.PlotId}/state", DurableAggregateKind.RegionState, DurableStateOwnerKind.Region, plot.PlotId, 25));
        foreach (TownNpcConfiguration actor in population.Actors)
            AddActorRegistrations(registrations, actor.Identity.ActorId);
        AddActorRegistrations(registrations, playerActorId);
        return new DurableStateRegistry(registrations);
    }

    private static void AddActorRegistrations(
        ICollection<DurableAggregateRegistration> registrations,
        string actorId)
    {
        registrations.Add(Registration($"actor/{actorId}/state", DurableAggregateKind.ActorState, DurableStateOwnerKind.Actor, actorId, 40));
        registrations.Add(Registration($"actor/{actorId}/assets", DurableAggregateKind.AssetContainer, DurableStateOwnerKind.Actor, actorId, 50));
        registrations.Add(Registration($"actor/{actorId}/relationships", DurableAggregateKind.RelationshipState, DurableStateOwnerKind.Actor, actorId, 60));
    }

    private static DurableAggregateRegistration Registration(
        string aggregateId,
        DurableAggregateKind kind,
        DurableStateOwnerKind ownerKind,
        string ownerId,
        int restoreOrder) =>
        new(new DurableAggregateId(aggregateId), kind, new DurableStateOwnerId(ownerKind, ownerId), restoreOrder);

    private static string GetActorId(TownNpcConfiguration actor) => actor.Identity.ActorId;
    private static string GetStackItemTypeId(TownStackConfiguration stack) => stack.ItemTypeId;

    private static void ValidatePlayer(ProductPlayerConfiguration player)
    {
        ArgumentNullException.ThrowIfNull(player);
        string[] identities = [player.ActorId, player.Name];
        if (identities.Any(string.IsNullOrWhiteSpace)
            || player.Age < 0
            || player.HealthMaximum <= 0
            || player.HealthCurrent < 0
            || player.HealthCurrent > player.HealthMaximum
            || player.Satiety is < 0 or > 100
            || player.Spirit is < 0 or > 100
            || !double.IsFinite(player.StartWorldX)
            || !double.IsFinite(player.StartWorldY)
            || player.Tools.Length == 0
            || player.Tools.Any(value => string.IsNullOrWhiteSpace(value.ItemInstanceId) || string.IsNullOrWhiteSpace(value.ToolTypeId))
            || player.FungibleAssets.Any(value => string.IsNullOrWhiteSpace(value.AssetId) || value.Quantity < 0)
            || player.Capabilities.Any(value => string.IsNullOrWhiteSpace(value.CapabilityId) || value.Value < 0)
            || player.EquippedToolInstanceId is not null
            && !player.Tools.Any(value => StringComparer.Ordinal.Equals(value.ItemInstanceId, player.EquippedToolInstanceId)))
            throw new InvalidDataException("Living Town player configuration is invalid.");
    }

    private static void ValidateDialogue(ProductDialogueConfiguration dialogue)
    {
        ArgumentNullException.ThrowIfNull(dialogue);
        if (string.IsNullOrWhiteSpace(dialogue.SurfaceProfilePath)
            || dialogue.SurfaceProfilePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0
            || string.IsNullOrWhiteSpace(dialogue.PlayerActorId)
            || string.IsNullOrWhiteSpace(dialogue.NpcActorId)
            || string.IsNullOrWhiteSpace(dialogue.DefaultTopicRef)
            || dialogue.MaxSessionTurns is <= 0
            || dialogue.MaxSessionElapsedTicks is <= 0)
            throw new InvalidDataException("Living Town dialogue configuration is invalid.");
    }

    private static void ValidateProviderQueue(ProviderQueueConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.RetryBackoffMilliseconds);
        ArgumentNullException.ThrowIfNull(configuration.RetryableFailureCodes);
        if (configuration.AdmittedCapacity <= 0
            || configuration.MaxInFlight <= 0
            || configuration.MaxInFlight > configuration.AdmittedCapacity
            || configuration.MaxContextTokens <= 0
            || configuration.MaxOutputTokens <= 0
            || configuration.RetryBackoffMilliseconds.Any(IsNegative)
            || configuration.RetryableFailureCodes.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Living Town Provider queue configuration is invalid.");
    }

    private static bool IsNegative(int value) => value < 0;

    private static void ValidateProviderProfiles(ProviderProfilesConfiguration profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ValidateProfile(profiles.LocalReasoner, false);
        ValidateProfile(profiles.RemotePlanner, true);
    }

    private static void ValidateProfile(ProviderProfileConfiguration profile, bool remote)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.ProfileId)
            || string.IsNullOrWhiteSpace(profile.ModelId)
            || !Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme is not ("http" or "https")
            || remote && endpoint.Scheme != "https"
            || profile.TimeoutMilliseconds <= 0
            || profile.MaxResponseBodyBytes <= 0)
            throw new InvalidDataException("Living Town Provider profile is invalid.");

        if (remote)
        {
            if (string.IsNullOrWhiteSpace(profile.CredentialEnvironmentVariable)
                || profile.TransportProtocol is not ("openai_chat_completions" or "deepseek_anthropic_messages")
                || profile.TransportProtocol == "deepseek_anthropic_messages"
                && profile.ThinkingEffort is not ("high" or "max"))
                throw new InvalidDataException("Living Town Remote Planner profile is invalid.");
        }
        else if (profile.TransportProtocol != "openai_chat_completions"
            || profile.CredentialEnvironmentVariable is not null
            || profile.ThinkingEffort is not null)
            throw new InvalidDataException("Living Town Local Reasoner profile is invalid.");
    }
}
