using System.Text.Json.Serialization;
using Alice.Activities;
using Alice.Actors;
using Alice.Authority;
using Alice.Capabilities;
using Alice.Interaction;
using Alice.Items;
using Alice.Navigation;
using Alice.Perception;
using Alice.PlayerControl;
using Alice.World;

namespace Alice.ProductRuntime;

public enum TownPlaceAccessKind
{
    Public,
    ResidentShared,
    PrivateRoom
}

public enum TownGameplayAssetStorageKind
{
    Fungible,
    Instance
}

public enum TownFarmPlotStage
{
    Empty,
    Growing,
    Harvestable
}

public enum TownGameplayExchangeKind
{
    ShopToActor,
    ActorToShop,
    ExternalToShop,
    ShopToExternal
}

public sealed record TownGameplayAssetDefinitionConfiguration
{
    [JsonRequired, JsonPropertyName("asset_id")] public string AssetId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("storage_kind")] public string StorageKind { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("maximum_durability")] public int? MaximumDurability { get; init; }
}

public sealed record TownGameplayAssetAmountConfiguration
{
    [JsonRequired, JsonPropertyName("asset_id")] public string AssetId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("quantity")] public long Quantity { get; init; }
}

public sealed record TownGameplayConsumableConfiguration
{
    [JsonRequired, JsonPropertyName("asset_id")] public string AssetId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("satiety_restore")] public int SatietyRestore { get; init; }
    [JsonRequired, JsonPropertyName("treats_diseases")] public string[] TreatsDiseases { get; init; } = [];
}

public sealed record TownGameplayFarmPlotConfiguration
{
    [JsonRequired, JsonPropertyName("plot_id")] public string PlotId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("initial_stage")] public string InitialStage { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("initial_growth_remaining_ticks")] public long? InitialGrowthRemainingTicks { get; init; }
    [JsonRequired, JsonPropertyName("growth_ticks")] public long GrowthTicks { get; init; }
    [JsonRequired, JsonPropertyName("seed_asset_id")] public string SeedAssetId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("seed_quantity")] public long SeedQuantity { get; init; }
    [JsonRequired, JsonPropertyName("output_asset_id")] public string OutputAssetId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("yield_quantity")] public long YieldQuantity { get; init; }
    [JsonRequired, JsonPropertyName("plant_tool_type_id")] public string PlantToolTypeId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("plant_durability_cost")] public int PlantDurabilityCost { get; init; }
    [JsonRequired, JsonPropertyName("harvest_tool_type_id")] public string HarvestToolTypeId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("harvest_durability_cost")] public int HarvestDurabilityCost { get; init; }
    [JsonRequired, JsonPropertyName("required_capability_id")] public string RequiredCapabilityId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("minimum_capability")] public int MinimumCapability { get; init; }
    [JsonRequired, JsonPropertyName("operation_ticks")] public long OperationTicks { get; init; }
    [JsonRequired, JsonPropertyName("interaction_range")] public double InteractionRange { get; init; }
    [JsonRequired, JsonPropertyName("world_x")] public double WorldX { get; init; }
    [JsonRequired, JsonPropertyName("world_y")] public double WorldY { get; init; }
}

public sealed record TownGameplayRecipeConfiguration
{
    [JsonRequired, JsonPropertyName("recipe_id")] public string RecipeId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("place_id")] public string? PlaceId { get; init; }
    [JsonRequired, JsonPropertyName("required_capability_id")] public string RequiredCapabilityId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("minimum_capability")] public int MinimumCapability { get; init; }
    [JsonRequired, JsonPropertyName("required_asset_id")] public string? RequiredAssetId { get; init; }
    [JsonRequired, JsonPropertyName("inputs")] public TownGameplayAssetAmountConfiguration[] Inputs { get; init; } = [];
    [JsonRequired, JsonPropertyName("outputs")] public TownGameplayAssetAmountConfiguration[] Outputs { get; init; } = [];
}

public sealed record TownGameplayServiceConfiguration
{
    [JsonRequired, JsonPropertyName("service_id")] public string ServiceId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("place_id")] public string PlaceId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("provider_actor_ids")] public string[] ProviderActorIds { get; init; } = [];
    [JsonRequired, JsonPropertyName("provider_owner_kind")] public string ProviderOwnerKind { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("provider_container_id")] public string ProviderContainerId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("coin_fee")] public long CoinFee { get; init; }
    [JsonRequired, JsonPropertyName("customer_inputs")] public TownGameplayAssetAmountConfiguration[] CustomerInputs { get; init; } = [];
    [JsonRequired, JsonPropertyName("provider_inputs")] public TownGameplayAssetAmountConfiguration[] ProviderInputs { get; init; } = [];
    [JsonRequired, JsonPropertyName("customer_outputs")] public TownGameplayAssetAmountConfiguration[] CustomerOutputs { get; init; } = [];
    [JsonRequired, JsonPropertyName("target_item_type_ids")] public string[] TargetItemTypeIds { get; init; } = [];
    [JsonRequired, JsonPropertyName("durability_restore")] public int DurabilityRestore { get; init; }
}

public sealed record TownGameplayRegionConfiguration
{
    [JsonRequired, JsonPropertyName("region_id")] public string RegionId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("output_asset_id")] public string OutputAssetId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("yield_quantity")] public long YieldQuantity { get; init; }
    [JsonRequired, JsonPropertyName("capacity")] public long Capacity { get; init; }
    [JsonRequired, JsonPropertyName("refresh_ticks")] public long RefreshTicks { get; init; }
    [JsonRequired, JsonPropertyName("replenish_quantity")] public long ReplenishQuantity { get; init; }
    [JsonRequired, JsonPropertyName("operation_ticks")] public long OperationTicks { get; init; }
    [JsonRequired, JsonPropertyName("interaction_range")] public double InteractionRange { get; init; }
    [JsonRequired, JsonPropertyName("required_tool_type_id")] public string RequiredToolTypeId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("required_capability_id")] public string RequiredCapabilityId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("minimum_capability")] public int MinimumCapability { get; init; }
    [JsonRequired, JsonPropertyName("durability_cost")] public int DurabilityCost { get; init; }
    [JsonRequired, JsonPropertyName("world_x")] public double WorldX { get; init; }
    [JsonRequired, JsonPropertyName("world_y")] public double WorldY { get; init; }
}

public sealed record TownGameplayPlaceConfiguration
{
    [JsonRequired, JsonPropertyName("place_id")] public string PlaceId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("access_kind")] public string AccessKind { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("resident_actor_ids")] public string[] ResidentActorIds { get; init; } = [];
}

public sealed record TownGameplayInvitationConfiguration
{
    [JsonRequired, JsonPropertyName("invitation_id")] public string InvitationId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("place_id")] public string PlaceId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("inviter_actor_id")] public string InviterActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("invitee_actor_id")] public string InviteeActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("active")] public bool Active { get; init; }
}

public sealed record TownGameplayAssetBalanceConfiguration
{
    [JsonRequired, JsonPropertyName("asset_id")] public string AssetId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("quantity")] public long Quantity { get; init; }
}

public sealed record TownGameplayContainerConfiguration
{
    [JsonRequired, JsonPropertyName("owner_kind")] public string OwnerKind { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("owner_id")] public string OwnerId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("balances")] public TownGameplayAssetBalanceConfiguration[] Balances { get; init; } = [];
}

public sealed record TownGameplayShopConfiguration
{
    [JsonRequired, JsonPropertyName("shop_id")] public string ShopId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("place_id")] public string PlaceId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("container_id")] public string ContainerId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("opens_at_tick_of_day")] public long OpensAtTickOfDay { get; init; }
    [JsonRequired, JsonPropertyName("closes_at_tick_of_day")] public long ClosesAtTickOfDay { get; init; }
    [JsonRequired, JsonPropertyName("manager_actor_ids")] public string[] ManagerActorIds { get; init; } = [];
    [JsonRequired, JsonPropertyName("world_x")] public double WorldX { get; init; }
    [JsonRequired, JsonPropertyName("world_y")] public double WorldY { get; init; }
}

public sealed record TownGameplayListingConfiguration
{
    [JsonRequired, JsonPropertyName("listing_id")] public string ListingId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("shop_id")] public string ShopId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("asset_id")] public string AssetId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("quantity")] public long Quantity { get; init; }
    [JsonRequired, JsonPropertyName("coin_price")] public long CoinPrice { get; init; }
    [JsonRequired, JsonPropertyName("exchange_kind")] public string ExchangeKind { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("cooldown_group_id")] public string? CooldownGroupId { get; init; }
    [JsonRequired, JsonPropertyName("cooldown_ticks")] public long CooldownTicks { get; init; }
}

public sealed record TownGameplayStockTargetConfiguration
{
    [JsonRequired, JsonPropertyName("stock_target_id")] public string StockTargetId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("shop_id")] public string ShopId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("asset_id")] public string AssetId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("target_quantity")] public long TargetQuantity { get; init; }
}

public sealed record TownGameplayRestockConfiguration
{
    [JsonRequired, JsonPropertyName("restock_id")] public string RestockId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("merchant_actor_id")] public string MerchantActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("source_container_id")] public string SourceContainerId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("shop_container_id")] public string ShopContainerId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("asset_id")] public string AssetId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("quantity")] public long Quantity { get; init; }
}

public sealed record TownGameplayConfigurationDocument
{
    [JsonRequired, JsonPropertyName("asset_definitions")] public TownGameplayAssetDefinitionConfiguration[] AssetDefinitions { get; init; } = [];
    [JsonRequired, JsonPropertyName("regions")] public TownGameplayRegionConfiguration[] Regions { get; init; } = [];
    [JsonRequired, JsonPropertyName("farm_plots")] public TownGameplayFarmPlotConfiguration[] FarmPlots { get; init; } = [];
    [JsonRequired, JsonPropertyName("consumables")] public TownGameplayConsumableConfiguration[] Consumables { get; init; } = [];
    [JsonRequired, JsonPropertyName("places")] public TownGameplayPlaceConfiguration[] Places { get; init; } = [];
    [JsonRequired, JsonPropertyName("invitations")] public TownGameplayInvitationConfiguration[] Invitations { get; init; } = [];
    [JsonRequired, JsonPropertyName("containers")] public TownGameplayContainerConfiguration[] Containers { get; init; } = [];
    [JsonRequired, JsonPropertyName("shops")] public TownGameplayShopConfiguration[] Shops { get; init; } = [];
    [JsonRequired, JsonPropertyName("listings")] public TownGameplayListingConfiguration[] Listings { get; init; } = [];
    [JsonRequired, JsonPropertyName("stock_targets")] public TownGameplayStockTargetConfiguration[] StockTargets { get; init; } = [];
    [JsonRequired, JsonPropertyName("restocks")] public TownGameplayRestockConfiguration[] Restocks { get; init; } = [];
    [JsonRequired, JsonPropertyName("recipes")] public TownGameplayRecipeConfiguration[] Recipes { get; init; } = [];
    [JsonRequired, JsonPropertyName("services")] public TownGameplayServiceConfiguration[] Services { get; init; } = [];
}

public sealed record ProductPlayerToolConfiguration
{
    [JsonRequired, JsonPropertyName("item_instance_id")] public string ItemInstanceId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("tool_type_id")] public string ToolTypeId { get; init; } = string.Empty;
}

public sealed record GameplayValidationResult(bool Available, string? Reason)
{
    public static GameplayValidationResult Accepted() => new(true, null);
    public static GameplayValidationResult Rejected(string reason) => new(false, reason);
}

public sealed record TownGameplayActionOffer(
    string EntryId,
    string Label,
    PlayerInteractionSelection Selection,
    GameplayValidationResult Validation);

public sealed record TownRegionSnapshot(string RegionId, long Stock, long Capacity, long? RefreshAtTicks, long Revision);

public sealed record TownGameplayRegionDurableState(
    string RegionId,
    long Stock,
    long? RefreshAtTicks,
    long Revision);

public sealed record TownFarmPlotSnapshot(
    string PlotId,
    TownFarmPlotStage Stage,
    long? GrowingUntilTicks,
    long Revision);

public sealed record TownGameplayFarmPlotDurableState(
    string PlotId,
    TownFarmPlotStage Stage,
    long? GrowingUntilTicks,
    long Revision);

public sealed record TownGameplayAssetBalanceDurableState(string AssetId, long Quantity);

public sealed record TownGameplayContainerDurableState(
    AssetContainerOwnerKind OwnerKind,
    string OwnerId,
    IReadOnlyList<TownGameplayAssetBalanceDurableState> Balances,
    IReadOnlyList<string> ItemInstanceIds,
    long Revision);

public sealed record TownGameplayActorDurableState(
    string ActorId,
    string? EquippedInstanceId,
    long EquipmentRevision,
    int HealthCurrent,
    int HealthMaximum,
    int Satiety,
    int Spirit,
    Disease Disease,
    long LastNeedsAtTicks);

public sealed record TownActorVitalsSnapshot(
    int HealthCurrent,
    int HealthMaximum,
    int Satiety,
    int Spirit,
    Disease Disease);

public sealed record TownBodyRuleCommitReceipt(
    CommitOrigin.WorldRule Origin,
    ActorId ActorId,
    TownActorVitalsSnapshot Previous,
    TownActorVitalsSnapshot Current);

public sealed record TownGameplayPlaceExceptionDurableState(string PlaceId, bool Closed);

public sealed record TownGameplayExchangeCooldownDurableState(string GroupId, long NextAvailableAtTicks);

public sealed record TownBusinessStockTargetSnapshot(
    string StockTargetId,
    string ShopId,
    string AssetId,
    long CurrentQuantity,
    long TargetQuantity);

public sealed record TownGameplayItemInstanceDurableState(
    string ItemInstanceId,
    string ItemTypeId,
    int? Durability,
    int? MaximumDurability,
    int Version,
    AssetContainerOwnerKind OwnerKind,
    string OwnerId);

public sealed record TownGameplayDurableState(
    IReadOnlyList<TownGameplayRegionDurableState> Regions,
    IReadOnlyList<TownGameplayFarmPlotDurableState> FarmPlots,
    IReadOnlyList<TownGameplayContainerDurableState> Containers,
    IReadOnlyList<TownGameplayActorDurableState> Actors,
    IReadOnlyList<TownGameplayPlaceExceptionDurableState> PlaceExceptions,
    IReadOnlyList<TownGameplayExchangeCooldownDurableState> ExchangeCooldowns,
    IReadOnlyList<TownGameplayItemInstanceDurableState> ItemInstances,
    long NextItemSequence);

/// <summary>
/// Single mutable Phase 13 product authority for finite regions, shared assets, room access and fixed shops.
/// Player and NPC executors delegate here and therefore use the exact same validation and commit path.
/// </summary>
public sealed class RegionSocialGameplayRuntime : IInteractionRangeQuery, IActorVisibleTargetSpatialQuery
{
    private const string CoinAssetId = "coin";
    private readonly TownGameplayConfigurationDocument _configuration;
    private readonly long _ticksPerDay;
    private readonly Dictionary<string, TownGameplayAssetDefinitionConfiguration> _assetDefinitions;
    private readonly Dictionary<string, RegionState> _regions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FarmPlotState> _farmPlots = new(StringComparer.Ordinal);
    private readonly Dictionary<AssetContainerOwnerId, MutableContainer> _containers = [];
    private readonly Dictionary<string, MutableItemInstance> _itemInstances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActorTools> _actors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _placeClosedExceptions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _exchangeCooldowns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Alice.LivingTown.TownSleepFacilityConfiguration> _sleepFacilities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorldPosition> _placePositions = new(StringComparer.Ordinal);
    private long _nextItemSequence;

    private RegionSocialGameplayRuntime(
        TownGameplayConfigurationDocument configuration,
        ProductPlayerConfiguration player,
        Alice.LivingTown.TownPopulationManifest population,
        long ticksPerDay)
    {
        _configuration = configuration;
        _ticksPerDay = ticksPerDay;
        _assetDefinitions = configuration.AssetDefinitions.ToDictionary(value => value.AssetId, StringComparer.Ordinal);
        foreach (TownGameplayRegionConfiguration region in configuration.Regions)
            _regions.Add(region.RegionId, new RegionState(region));
        foreach (TownGameplayFarmPlotConfiguration plot in configuration.FarmPlots)
            _farmPlots.Add(plot.PlotId, new FarmPlotState(plot));
        foreach (Alice.LivingTown.TownPlaceConfiguration place in population.Places)
            _placePositions.Add(place.PlaceRef, new WorldPosition(place.WorldX, place.WorldY));
        foreach (Alice.LivingTown.TownSleepFacilityConfiguration facility in population.SleepFacilities)
            _sleepFacilities.Add(facility.FacilityId, facility);

        AddPlayer(player);
        foreach (Alice.LivingTown.TownNpcConfiguration actor in population.Actors) AddNpc(actor);
        foreach (TownGameplayContainerConfiguration container in configuration.Containers)
        {
            AssetContainerOwnerKind kind = Enum.Parse<AssetContainerOwnerKind>(container.OwnerKind, false);
            AddContainer(new AssetContainerOwnerId(kind, container.OwnerId), container.Balances);
        }
    }

    public static RegionSocialGameplayRuntime Create(
        TownGameplayConfigurationDocument configuration,
        ProductPlayerConfiguration player,
        Alice.LivingTown.TownPopulationManifest population,
        long ticksPerDay)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(population);
        if (ticksPerDay <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerDay));
        ValidateConfiguration(configuration);
        return new RegionSocialGameplayRuntime(configuration, player, population, ticksPerDay);
    }

    public TownGameplayActorExecutor CreateExecutor(ActorId actorId) => new(this, actorId);

    public TownRegionSnapshot GetRegion(string regionId, SimTime now)
    {
        RegionState region = RequireRegion(regionId);
        Refresh(region, now);
        return new TownRegionSnapshot(region.Configuration.RegionId, region.Stock,
            region.Configuration.Capacity, region.RefreshAtTicks, region.Revision);
    }

    public TownFarmPlotSnapshot GetFarmPlot(string plotId, SimTime now)
    {
        FarmPlotState plot = RequireFarmPlot(plotId);
        Refresh(plot, now);
        return new TownFarmPlotSnapshot(plot.Configuration.PlotId, plot.Stage, plot.GrowingUntilTicks, plot.Revision);
    }

    public void AdvanceWorld(SimTime now)
    {
        foreach (RegionState region in _regions.Values) Refresh(region, now);
        foreach (FarmPlotState plot in _farmPlots.Values) Refresh(plot, now);
    }

    public AssetContainerState GetContainer(AssetContainerOwnerId ownerId) => RequireContainer(ownerId).Snapshot();

    public long GetBalance(AssetContainerOwnerId ownerId, string assetId) =>
        GetAssetQuantity(RequireContainer(ownerId), assetId);

    public ItemInstance GetItemInstance(string itemInstanceId) => RequireItemInstance(itemInstanceId).Snapshot();

    public IReadOnlyList<ItemInstance> GetItemInstances(AssetContainerOwnerId ownerId) =>
        RequireContainer(ownerId).ItemInstanceIds
            .Select(RequireItemInstance)
            .OrderBy(value => value.ItemInstanceId, StringComparer.Ordinal)
            .Select(value => value.Snapshot())
            .ToArray();

    public string? GetEquippedTool(string actorId) => RequireActor(actorId).EquippedInstanceId;

    public TownActorVitalsSnapshot GetVitals(string actorId)
    {
        ActorTools actor = RequireActor(actorId);
        return Vitals(actor);
    }

    /// <summary>Authority-owned low-frequency world-rule commit; it does not invoke cognition.</summary>
    public TownBodyRuleCommitReceipt? CommitNeeds(string actorId, SimTime now)
    {
        ActorTools actor = RequireActor(actorId);
        long interval = Math.Max(1, _ticksPerDay / 16);
        if (now.Ticks - actor.LastNeedsAtTicks < interval) return null;
        TownActorVitalsSnapshot previous = Vitals(actor);
        long steps = Math.Max(1, (now.Ticks - actor.LastNeedsAtTicks) / interval);
        actor.LastNeedsAtTicks += steps * interval;
        actor.Satiety = Math.Max(0, actor.Satiety - checked((int)Math.Min(steps * 2, 100)));
        actor.Spirit = Math.Max(0, actor.Spirit - checked((int)Math.Min(steps * 2, 100)));
        TownActorVitalsSnapshot current = Vitals(actor);
        return new TownBodyRuleCommitReceipt(
            new CommitOrigin.WorldRule(
                new RuleId("town-body-needs"),
                new EvaluationId($"town-body-needs/{actorId}/{actor.LastNeedsAtTicks}"),
                now),
            new ActorId(actorId),
            previous,
            current);
    }

    public TownGameplayDurableState CaptureDurableState()
    {
        TownGameplayRegionDurableState[] regions = _regions.Values
            .OrderBy(value => value.Configuration.RegionId, StringComparer.Ordinal)
            .Select(value => new TownGameplayRegionDurableState(
                value.Configuration.RegionId, value.Stock, value.RefreshAtTicks, value.Revision)).ToArray();
        TownGameplayFarmPlotDurableState[] farmPlots = _farmPlots.Values
            .OrderBy(value => value.Configuration.PlotId, StringComparer.Ordinal)
            .Select(value => new TownGameplayFarmPlotDurableState(
                value.Configuration.PlotId, value.Stage, value.GrowingUntilTicks, value.Revision)).ToArray();
        TownGameplayContainerDurableState[] containers = _containers.Values
            .OrderBy(value => value.OwnerId.Kind).ThenBy(value => value.OwnerId.Value, StringComparer.Ordinal)
            .Select(value =>
            {
                AssetContainerState snapshot = value.Snapshot();
                return new TownGameplayContainerDurableState(
                    snapshot.OwnerId.Kind,
                    snapshot.OwnerId.Value,
                    snapshot.Balances.Select(balance =>
                        new TownGameplayAssetBalanceDurableState(balance.AssetId.Value, balance.Quantity)).ToArray(),
                    snapshot.ItemInstances.Select(item => item.Value).ToArray(),
                    snapshot.Revision);
            }).ToArray();
        TownGameplayActorDurableState[] actors = _actors.OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new TownGameplayActorDurableState(
                value.Key, value.Value.EquippedInstanceId, value.Value.EquipmentRevision,
                value.Value.HealthCurrent, value.Value.HealthMaximum, value.Value.Satiety, value.Value.Spirit,
                value.Value.Disease, value.Value.LastNeedsAtTicks)).ToArray();
        TownGameplayPlaceExceptionDurableState[] places = _placeClosedExceptions
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new TownGameplayPlaceExceptionDurableState(value.Key, value.Value)).ToArray();
        TownGameplayExchangeCooldownDurableState[] cooldowns = _exchangeCooldowns
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new TownGameplayExchangeCooldownDurableState(value.Key, value.Value)).ToArray();
        TownGameplayItemInstanceDurableState[] items = _itemInstances.Values
            .OrderBy(value => value.ItemInstanceId, StringComparer.Ordinal)
            .Select(value => value.Capture()).ToArray();
        return new TownGameplayDurableState(regions, farmPlots, containers, actors, places, cooldowns, items, _nextItemSequence);
    }

    public void RestoreDurableState(TownGameplayDurableState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (TownGameplayRegionDurableState saved in state.Regions)
        {
            RegionState region = RequireRegion(saved.RegionId);
            if (saved.Stock < 0 || saved.Stock > region.Configuration.Capacity || saved.Revision < 1)
                throw new InvalidDataException("Saved region state is invalid.");
            region.Stock = saved.Stock;
            region.RefreshAtTicks = saved.RefreshAtTicks;
            region.Revision = saved.Revision;
        }
        foreach (TownGameplayFarmPlotDurableState saved in state.FarmPlots)
        {
            FarmPlotState plot = RequireFarmPlot(saved.PlotId);
            if (!Enum.IsDefined(saved.Stage) || saved.Revision < 1
                || saved.Stage == TownFarmPlotStage.Growing != saved.GrowingUntilTicks.HasValue)
                throw new InvalidDataException("Saved farm-plot state is invalid.");
            plot.Stage = saved.Stage;
            plot.GrowingUntilTicks = saved.GrowingUntilTicks;
            plot.Revision = saved.Revision;
        }
        foreach (TownGameplayContainerDurableState saved in state.Containers)
        {
            MutableContainer container = RequireContainer(new AssetContainerOwnerId(saved.OwnerKind, saved.OwnerId));
            container.Restore(saved);
        }
        if (state.NextItemSequence < 0)
            throw new InvalidDataException("Saved item sequence is invalid.");
        _itemInstances.Clear();
        foreach (TownGameplayItemInstanceDurableState saved in state.ItemInstances)
        {
            AssetContainerOwnerId owner = new(saved.OwnerKind, saved.OwnerId);
            MutableContainer container = RequireContainer(owner);
            if (!container.ContainsInstance(saved.ItemInstanceId)
                || !_assetDefinitions.TryGetValue(saved.ItemTypeId, out TownGameplayAssetDefinitionConfiguration? definition)
                || ParseStorage(definition) != TownGameplayAssetStorageKind.Instance
                || definition.MaximumDurability != saved.MaximumDurability
                || !_itemInstances.TryAdd(saved.ItemInstanceId, MutableItemInstance.Restore(saved)))
                throw new InvalidDataException("Saved item-instance state is invalid.");
        }
        if (_containers.Values.SelectMany(value => value.ItemInstanceIds).Any(value => !_itemInstances.ContainsKey(value))
            || _itemInstances.Values.Any(value => !RequireContainer(value.OwnerId).ContainsInstance(value.ItemInstanceId)))
            throw new InvalidDataException("Saved item ownership is incomplete.");
        _nextItemSequence = state.NextItemSequence;
        foreach (TownGameplayActorDurableState saved in state.Actors)
        {
            ActorTools actor = RequireActor(saved.ActorId);
            if (saved.EquipmentRevision < 1 || saved.HealthMaximum <= 0
                || saved.HealthCurrent < 0 || saved.HealthCurrent > saved.HealthMaximum
                || saved.Satiety is < 0 or > 100 || saved.Spirit is < 0 or > 100 || saved.LastNeedsAtTicks < 0
                || !Enum.IsDefined(saved.Disease)
                || saved.EquippedInstanceId is not null
                && !ActorOwnsInstance(saved.ActorId, saved.EquippedInstanceId))
                throw new InvalidDataException("Saved Actor equipment state is invalid.");
            actor.EquippedInstanceId = saved.EquippedInstanceId;
            actor.EquipmentRevision = saved.EquipmentRevision;
            actor.HealthCurrent = saved.HealthCurrent;
            actor.HealthMaximum = saved.HealthMaximum;
            actor.Satiety = saved.Satiety;
            actor.Spirit = saved.Spirit;
            actor.Disease = saved.Disease;
            actor.LastNeedsAtTicks = saved.LastNeedsAtTicks;
        }
        _placeClosedExceptions.Clear();
        foreach (TownGameplayPlaceExceptionDurableState saved in state.PlaceExceptions)
            _placeClosedExceptions.Add(saved.PlaceId, saved.Closed);
        _exchangeCooldowns.Clear();
        foreach (TownGameplayExchangeCooldownDurableState saved in state.ExchangeCooldowns)
        {
            if (string.IsNullOrWhiteSpace(saved.GroupId) || saved.NextAvailableAtTicks < 0
                || !_configuration.Listings.Any(value => value.CooldownGroupId == saved.GroupId))
                throw new InvalidDataException("Saved exchange cooldown state is invalid.");
            _exchangeCooldowns.Add(saved.GroupId, saved.NextAvailableAtTicks);
        }
    }

    public IReadOnlyList<TownGameplayActionOffer> GetActionOffers(ActorId actorId, string targetId, SimTime now)
    {
        if (_regions.ContainsKey(targetId))
        {
            GameActionSpec action = CreateRegionOperation(actorId, targetId, now);
            return [Offer($"region-operation/{targetId}", "Gather", action, now)];
        }
        if (_farmPlots.ContainsKey(targetId))
        {
            FarmPlotState plot = RequireFarmPlot(targetId);
            Refresh(plot, now);
            string label = plot.Stage == TownFarmPlotStage.Empty ? "Plant"
                : plot.Stage == TownFarmPlotStage.Harvestable ? "Harvest" : "Growing";
            GameActionSpec action = CreateRegionOperation(actorId, targetId, now);
            return [Offer($"region-operation/{targetId}", label, action, now)];
        }

        var offers = new List<TownGameplayActionOffer>();
        foreach (TownGameplayRecipeConfiguration recipe in _configuration.Recipes
                     .Where(value => StringComparer.Ordinal.Equals(value.PlaceId, targetId)))
        {
            GameActionSpec action = CreateCraft(actorId, recipe.RecipeId);
            offers.Add(Offer($"craft/{recipe.RecipeId}", $"Craft {recipe.RecipeId}", action, now));
        }
        foreach (TownGameplayServiceConfiguration service in _configuration.Services
                     .Where(value => StringComparer.Ordinal.Equals(value.PlaceId, targetId)))
        {
            if (service.TargetItemTypeIds.Length == 0)
            {
                GameActionSpec action = CreateServiceExchange(
                    actorId, service.ServiceId, service.ProviderActorIds[0], null);
                offers.Add(Offer($"service/{service.ServiceId}", $"Use {service.ServiceId}", action, now));
                continue;
            }

            MutableContainer inventory = RequireContainer(ActorOwner(actorId.Value));
            foreach (string instanceId in inventory.ItemInstanceIds.OrderBy(value => value, StringComparer.Ordinal))
            {
                MutableItemInstance item = RequireItemInstance(instanceId);
                if (!service.TargetItemTypeIds.Contains(item.ItemTypeId, StringComparer.Ordinal)
                    || item.Durability is null || item.MaximumDurability is null
                    || item.Durability >= item.MaximumDurability) continue;
                GameActionSpec action = CreateServiceExchange(
                    actorId, service.ServiceId, service.ProviderActorIds[0], instanceId);
                offers.Add(Offer($"service/{service.ServiceId}/{instanceId}",
                    $"Use {service.ServiceId} on {item.ItemTypeId}", action, now));
            }
        }

        TownGameplayShopConfiguration? shop = _configuration.Shops.FirstOrDefault(value =>
            StringComparer.Ordinal.Equals(value.PlaceId, targetId)
            || value.ManagerActorIds.Contains(targetId, StringComparer.Ordinal));
        if (shop is not null)
        {
            MutableContainer actorInventory = RequireContainer(ActorOwner(actorId.Value));
            offers.AddRange(_configuration.Listings
                .Where(value => StringComparer.Ordinal.Equals(value.ShopId, shop.ShopId))
                .Where(value => ParseExchangeKind(value) is TownGameplayExchangeKind.ShopToActor
                    or TownGameplayExchangeKind.ActorToShop)
                .Where(value => ParseExchangeKind(value) == TownGameplayExchangeKind.ShopToActor
                    || GetAssetQuantity(actorInventory, value.AssetId) >= value.Quantity)
                .Select(value =>
                {
                    GameActionSpec action = CreateListedExchange(actorId, value.ListingId);
                    string verb = ParseExchangeKind(value) == TownGameplayExchangeKind.ShopToActor ? "Buy" : "Sell";
                    return Offer($"listed-exchange/{value.ListingId}",
                        $"{verb} {value.Quantity} {value.AssetId} ({value.CoinPrice} Coin)", action, now);
                }));
        }
        return offers.ToArray();
    }

    /// <summary>
    /// Actor-visible action catalogue used by autonomous L1/L2 routing. It extends the same place catalogue used by
    /// the player with only manager-authorized stock work; it does not add a profession-specific action family.
    /// </summary>
    public IReadOnlyList<TownGameplayActionOffer> GetDecisionActionOffers(
        ActorId actorId,
        string targetId,
        SimTime now)
    {
        var offers = new List<TownGameplayActionOffer>(GetActionOffers(actorId, targetId, now));
        TownGameplayShopConfiguration? shop = _configuration.Shops.FirstOrDefault(value =>
            StringComparer.Ordinal.Equals(value.PlaceId, targetId)
            && value.ManagerActorIds.Contains(actorId.Value, StringComparer.Ordinal));
        if (shop is null) return offers.ToArray();

        foreach (TownGameplayListingConfiguration listing in _configuration.Listings
                     .Where(value => StringComparer.Ordinal.Equals(value.ShopId, shop.ShopId)
                         && ParseExchangeKind(value) is TownGameplayExchangeKind.ExternalToShop
                             or TownGameplayExchangeKind.ShopToExternal)
                     .OrderBy(value => value.ListingId, StringComparer.Ordinal))
        {
            GameActionSpec action = CreateListedExchange(actorId, listing.ListingId);
            string verb = ParseExchangeKind(listing) == TownGameplayExchangeKind.ExternalToShop ? "Import" : "Export";
            offers.Add(Offer($"listed-exchange/{listing.ListingId}",
                $"{verb} {listing.Quantity} {listing.AssetId} ({listing.CoinPrice} Coin)", action, now));
        }

        foreach (TownBusinessStockTargetSnapshot target in GetStockTargets(actorId)
                     .Where(value => StringComparer.Ordinal.Equals(value.ShopId, shop.ShopId)
                         && value.CurrentQuantity < value.TargetQuantity))
            offers.AddRange(CreateStockTargetOffers(actorId, shop, target, now));

        return offers.GroupBy(value => value.EntryId, StringComparer.Ordinal)
            .Select(value => value.First())
            .OrderBy(value => value.EntryId, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<TownGameplayActionOffer> GetCraftActionOffers(ActorId actorId, SimTime now) =>
        _configuration.Recipes.Where(value => value.PlaceId is null)
            .Select(value =>
            {
                GameActionSpec action = CreateCraft(actorId, value.RecipeId);
                return Offer($"craft/{value.RecipeId}", $"Craft {value.RecipeId}", action, now);
            })
            .ToArray();

    public IReadOnlyList<TownGameplayActionOffer> GetBodyActionOffers(ActorId actorId, SimTime now)
    {
        var offers = new List<TownGameplayActionOffer>();
        MutableContainer inventory = RequireContainer(ActorOwner(actorId.Value));
        foreach (TownGameplayConsumableConfiguration consumable in _configuration.Consumables
                     .Where(value => inventory.Get(value.AssetId) > 0)
                     .OrderByDescending(value => value.SatietyRestore)
                     .ThenBy(value => value.AssetId, StringComparer.Ordinal))
        {
            string verb = consumable.SatietyRestore > 0 ? "Eat" : "Use";
            offers.Add(Offer($"consumption/{actorId.Value}/{consumable.AssetId}",
                $"{verb} {consumable.AssetId}", CreateConsumption(actorId, consumable.AssetId), now));
        }
        foreach (Alice.LivingTown.TownSleepFacilityConfiguration facility in _sleepFacilities.Values
                     .Where(value => CanUseSleepFacility(actorId.Value, value))
                     .OrderBy(value => value.FacilityId, StringComparer.Ordinal))
            offers.Add(Offer($"rest/{facility.FacilityId}", "Rest", CreateRest(actorId, facility.FacilityId), now));
        return offers;
    }

    public long GetInteractionDurationTicks(string targetId) =>
        _configuration.Regions.FirstOrDefault(value =>
            StringComparer.Ordinal.Equals(value.RegionId, targetId))?.OperationTicks
        ?? _configuration.FarmPlots.FirstOrDefault(value =>
            StringComparer.Ordinal.Equals(value.PlotId, targetId))?.OperationTicks
        ?? 1;

    public GameActionSpec CreateRegionOperation(ActorId actorId, string regionId, SimTime now)
    {
        _ = RequireActor(actorId.Value);
        RegionState? region = _regions.GetValueOrDefault(regionId);
        FarmPlotState? plot = _farmPlots.GetValueOrDefault(regionId);
        if (region is null && plot is null) throw new KeyNotFoundException($"Region '{regionId}' is absent.");
        if (region is not null) Refresh(region, now);
        if (plot is not null) Refresh(plot, now);
        string toolType = region?.Configuration.RequiredToolTypeId ?? FarmToolType(plot!);
        long revision = region?.Revision ?? plot!.Revision;
        string capability = region?.Configuration.RequiredCapabilityId ?? plot!.Configuration.RequiredCapabilityId;
        MutableItemInstance? matchingTool = string.IsNullOrWhiteSpace(toolType)
            ? null
            : FindCarriedItem(actorId.Value, toolType);
        return new GameActionSpec(
            actorId,
            new InteractionBinding(
                RegionContract(regionId),
                new ExpectedContractVersion(revision),
                new CapabilityIdentity(capability),
                matchingTool is null ? null : new InstrumentRef(matchingTool.ItemInstanceId)),
            new RegionOperationActionArguments(
                regionId,
                matchingTool?.Version));
    }

    public GameActionSpec CreateCraft(ActorId actorId, string recipeId)
    {
        MutableContainer container = RequireContainer(ActorOwner(actorId.Value));
        TownGameplayRecipeConfiguration recipe = RequireRecipe(recipeId);
        return new GameActionSpec(
            actorId,
            new InteractionBinding(
                new ContractRef(
                    new TargetRef(recipe.PlaceId is null ? $"actor/{actorId.Value}/craft" : $"place/{recipe.PlaceId}"),
                    $"craft/{recipeId}"),
                new ExpectedContractVersion(container.Revision),
                new CapabilityIdentity(recipe.RequiredCapabilityId),
                null),
            new CraftActionArguments(recipeId));
    }

    public GameActionSpec CreateServiceExchange(
        ActorId actorId,
        string serviceId,
        string providerActorId,
        string? targetItemInstanceId)
    {
        MutableContainer customer = RequireContainer(ActorOwner(actorId.Value));
        TownGameplayServiceConfiguration service = RequireService(serviceId);
        AssetContainerOwnerId providerOwner = new(
            Enum.Parse<AssetContainerOwnerKind>(service.ProviderOwnerKind, false),
            service.ProviderContainerId);
        MutableContainer provider = RequireContainer(providerOwner);
        return new GameActionSpec(
            actorId,
            new InteractionBinding(
                new ContractRef(new TargetRef($"place/{service.PlaceId}"), $"service/{serviceId}"),
                new ExpectedContractVersion(customer.Revision),
                new CapabilityIdentity("service-exchange"),
                null),
            new ServiceExchangeActionArguments(
                serviceId,
                providerActorId,
                provider.Revision,
                targetItemInstanceId));
    }

    public GameActionSpec CreateListedExchange(ActorId actorId, string listingId)
    {
        TownGameplayListingConfiguration listing = RequireListing(listingId);
        TownGameplayShopConfiguration shop = RequireShop(listing.ShopId);
        MutableContainer stock = RequireContainer(new AssetContainerOwnerId(AssetContainerOwnerKind.Shop, shop.ContainerId));
        return new GameActionSpec(
            actorId,
            new InteractionBinding(
                new ContractRef(new TargetRef($"shop/{shop.PlaceId}"), $"listed-exchange/{listingId}"),
                new ExpectedContractVersion(stock.Revision),
                new CapabilityIdentity("listed-exchange"),
                null),
            new ListedExchangeActionArguments(listingId));
    }

    public GameActionSpec CreatePlaceStateChange(ActorId actorId, string placeId, bool closed)
    {
        TownGameplayShopConfiguration shop = _configuration.Shops.Single(value =>
            StringComparer.Ordinal.Equals(value.PlaceId, placeId));
        return new GameActionSpec(
            actorId,
            new InteractionBinding(
                new ContractRef(new TargetRef($"shop/{placeId}"), "place-state-change"),
                new ExpectedContractVersion(1),
                new CapabilityIdentity("place-state-change"),
                null),
            new PlaceStateChangeActionArguments(shop.PlaceId, closed));
    }

    public GameActionSpec CreateRestock(ActorId actorId, string restockId)
    {
        TownGameplayRestockConfiguration restock = _configuration.Restocks.Single(value =>
            StringComparer.Ordinal.Equals(value.RestockId, restockId));
        return CreateAssetTransfer(actorId,
            AssetContainerOwnerKind.Warehouse, restock.SourceContainerId,
            AssetContainerOwnerKind.Shop, restock.ShopContainerId,
            restock.AssetId, restock.Quantity);
    }

    public IReadOnlyList<string> GetRestockIds(ActorId actorId) => _configuration.Restocks
        .Where(value => StringComparer.Ordinal.Equals(value.MerchantActorId, actorId.Value))
        .Select(value => value.RestockId)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<string> GetShopPlaceIdsOffering(string assetId) => _configuration.Listings
        .Where(value => StringComparer.Ordinal.Equals(value.AssetId, assetId)
            && ParseExchangeKind(value) == TownGameplayExchangeKind.ShopToActor)
        .Select(value => RequireShop(value.ShopId).PlaceId)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    public string GetListingAssetId(string listingId) => RequireListing(listingId).AssetId;

    public IReadOnlyList<string> GetFoodAssetIds() => _configuration.Consumables
        .Where(value => value.SatietyRestore > 0)
        .OrderBy(value => value.AssetId, StringComparer.Ordinal)
        .Select(value => value.AssetId)
        .ToArray();

    public bool HasFood(ActorId actorId)
    {
        MutableContainer container = RequireContainer(ActorOwner(actorId.Value));
        return _configuration.Consumables.Any(value => value.SatietyRestore > 0 && container.Get(value.AssetId) > 0);
    }

    public IReadOnlyList<string> GetCompatibleMedicineAssetIds(ActorId actorId)
    {
        Disease disease = RequireActor(actorId.Value).Disease;
        return _configuration.Consumables
            .Where(value => value.TreatsDiseases.Contains(disease.ToString(), StringComparer.Ordinal))
            .OrderBy(value => value.AssetId, StringComparer.Ordinal)
            .Select(value => value.AssetId)
            .ToArray();
    }

    public IReadOnlyList<string> GetExternalTradeIds(ActorId actorId) => _configuration.Listings
        .Where(value => ParseExchangeKind(value) is TownGameplayExchangeKind.ExternalToShop
            or TownGameplayExchangeKind.ShopToExternal)
        .Where(value => RequireShop(value.ShopId).ManagerActorIds.Contains(actorId.Value, StringComparer.Ordinal))
        .OrderBy(value => value.ListingId, StringComparer.Ordinal)
        .Select(value => value.ListingId)
        .ToArray();

    public IReadOnlyList<TownBusinessStockTargetSnapshot> GetStockTargets(ActorId actorId) =>
        _configuration.StockTargets
            .Where(value => RequireShop(value.ShopId).ManagerActorIds.Contains(actorId.Value, StringComparer.Ordinal))
            .OrderBy(value => value.StockTargetId, StringComparer.Ordinal)
            .Select(value =>
            {
                TownGameplayShopConfiguration shop = RequireShop(value.ShopId);
                MutableContainer stock = RequireContainer(new AssetContainerOwnerId(AssetContainerOwnerKind.Shop, shop.ContainerId));
                return new TownBusinessStockTargetSnapshot(
                    value.StockTargetId, value.ShopId, value.AssetId,
                    GetAssetQuantity(stock, value.AssetId), value.TargetQuantity);
            })
            .ToArray();

    public IReadOnlyList<string> GetManagedShopPlaceIds(ActorId actorId) => _configuration.Shops
        .Where(value => value.ManagerActorIds.Contains(actorId.Value, StringComparer.Ordinal))
        .Select(value => value.PlaceId)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    public bool IsInInteractionRange(WorldPosition actorPosition, GameActionSpec action)
    {
        if (!TryResolve(action.Binding, out InteractionRange range)
            || !TryResolve(action.Binding.ContractRef.TargetRef, out ActorVisibleTargetSpatialSnapshot? target)
            || target is null) return false;
        double dx = actorPosition.X - target.Position.X;
        double dy = actorPosition.Y - target.Position.Y;
        return dx * dx + dy * dy <= range.Value * range.Value;
    }

    public bool IsInInteractionRange(
        WorldPosition actorPosition,
        GameActionSpec action,
        string fallbackPlaceId)
    {
        if (IsInInteractionRange(actorPosition, action)) return true;
        if (!_placePositions.TryGetValue(fallbackPlaceId, out WorldPosition place)) return false;
        double dx = actorPosition.X - place.X;
        double dy = actorPosition.Y - place.Y;
        return dx * dx + dy * dy <= 48 * 48;
    }

    public string? GetTravelPlaceId(InteractionBinding binding)
    {
        string target = binding.ContractRef.TargetRef.Value;
        if (target.StartsWith("region/", StringComparison.Ordinal)) return target[7..];
        if (target.StartsWith("shop/", StringComparison.Ordinal)) return target[5..];
        if (target.StartsWith("place/", StringComparison.Ordinal)) return target[6..];
        return _sleepFacilities.Values.FirstOrDefault(value =>
            StringComparer.Ordinal.Equals(value.TargetRef, target))?.PlaceRef;
    }

    public GameActionSpec CreateAssetTransfer(
        ActorId actorId,
        AssetContainerOwnerKind sourceKind,
        string sourceId,
        AssetContainerOwnerKind destinationKind,
        string destinationId,
        string assetId,
        long quantity)
    {
        MutableContainer source = RequireContainer(new AssetContainerOwnerId(sourceKind, sourceId));
        return new GameActionSpec(
            actorId,
            new InteractionBinding(
                new ContractRef(new TargetRef($"container/{destinationKind}/{destinationId}"), "asset-transfer"),
                new ExpectedContractVersion(source.Revision),
                new CapabilityIdentity("asset-transfer"),
                null),
            new AssetTransferActionArguments(sourceKind.ToString(), sourceId, destinationKind.ToString(), destinationId, assetId, quantity));
    }

    public GameActionSpec CreateEquipmentChange(ActorId actorId, string? itemInstanceId)
    {
        ActorTools actor = RequireActor(actorId.Value);
        return new GameActionSpec(
            actorId,
            new InteractionBinding(
                new ContractRef(new TargetRef($"actor/{actorId.Value}/equipment"), "equipment-change"),
                new ExpectedContractVersion(actor.EquipmentRevision),
                new CapabilityIdentity("equipment"),
                itemInstanceId is null ? null : new InstrumentRef(itemInstanceId)),
            new EquipmentChangeActionArguments(itemInstanceId is null
                ? null
                : new InstanceHandItemRef(new ItemInstanceId(itemInstanceId))));
    }

    public GameActionSpec CreateConsumption(ActorId actorId, string assetId)
    {
        MutableContainer container = RequireContainer(ActorOwner(actorId.Value));
        return new GameActionSpec(
            actorId,
            new InteractionBinding(
                new ContractRef(new TargetRef($"actor/{actorId.Value}/body"), "consumption"),
                new ExpectedContractVersion(container.Revision),
                new CapabilityIdentity("consumption"),
                null),
            new ConsumptionActionArguments(new ItemTypeId(assetId)));
    }

    public GameActionSpec CreateRest(ActorId actorId, string facilityId)
    {
        Alice.LivingTown.TownSleepFacilityConfiguration facility = RequireSleepFacility(facilityId);
        return new GameActionSpec(
            actorId,
            new InteractionBinding(
                new ContractRef(new TargetRef(facility.TargetRef), facility.ContractId),
                new ExpectedContractVersion(facility.ContractVersion),
                new CapabilityIdentity(facility.CapabilityId),
                null),
            new RestActionArguments(facilityId));
    }

    public GameplayValidationResult Validate(GameActionSpec action, SimTime now)
    {
        if (!_actors.ContainsKey(action.ActorId.Value)) return GameplayValidationResult.Rejected("unknown actor");
        return action.Arguments switch
        {
            RegionOperationActionArguments region => ValidateRegion(action, region, now),
            CraftActionArguments craft => ValidateCraft(action, craft),
            AssetTransferActionArguments transfer => ValidateTransfer(action, transfer),
            ListedExchangeActionArguments exchange => ValidateExchange(action, exchange, now),
            ServiceExchangeActionArguments service => ValidateService(action, service),
            PlaceStateChangeActionArguments place => ValidatePlaceState(action, place),
            EquipmentChangeActionArguments equipment => ValidateEquipment(action, equipment),
            ConsumptionActionArguments consumption => ValidateConsumption(action, consumption),
            RestActionArguments rest => ValidateRest(action, rest),
            _ => GameplayValidationResult.Rejected("unsupported gameplay action")
        };
    }

    public GameplayValidationResult ValidateAccess(ActorId actorId, string placeId)
    {
        TownGameplayPlaceConfiguration? place = _configuration.Places.FirstOrDefault(value =>
            StringComparer.Ordinal.Equals(value.PlaceId, placeId));
        if (place is null) return GameplayValidationResult.Rejected("unknown place");
        TownPlaceAccessKind kind = Enum.Parse<TownPlaceAccessKind>(place.AccessKind, false);
        if (kind == TownPlaceAccessKind.Public || place.ResidentActorIds.Contains(actorId.Value, StringComparer.Ordinal))
            return GameplayValidationResult.Accepted();
        bool invited = kind == TownPlaceAccessKind.PrivateRoom && _configuration.Invitations.Any(value =>
            value.Active
            && StringComparer.Ordinal.Equals(value.PlaceId, placeId)
            && StringComparer.Ordinal.Equals(value.InviteeActorId, actorId.Value));
        return invited ? GameplayValidationResult.Accepted() : GameplayValidationResult.Rejected("private place: resident or invitation required");
    }

    public bool IsShopOpen(string shopId, SimTime now)
    {
        TownGameplayShopConfiguration shop = RequireShop(shopId);
        if (_placeClosedExceptions.TryGetValue(shop.PlaceId, out bool closed)) return !closed;
        long tickOfDay = now.Ticks % _ticksPerDay;
        return tickOfDay >= shop.OpensAtTickOfDay && tickOfDay < shop.ClosesAtTickOfDay;
    }

    internal ActorExecutionReceipt Execute(ActorExecutionRequest request)
    {
        if (request.Payload is not InteractExecutionPayload interaction)
            return ActorExecutionReceipt.Rejected(request, ActorExecutionFailure.Unsupported, "gameplay/interact-required");
        GameplayValidationResult validation = Validate(interaction.Action, request.SourceTime);
        if (!validation.Available)
            return ActorExecutionReceipt.Rejected(request, ActorExecutionFailure.Unavailable, validation.Reason ?? "unavailable");

        ProductActionFamily family = interaction.Action.Arguments switch
        {
            RegionOperationActionArguments region => CommitRegion(request.ActorId, region, request.SourceTime),
            CraftActionArguments craft => CommitCraft(request.ActorId, craft),
            AssetTransferActionArguments transfer => CommitTransfer(transfer),
            ListedExchangeActionArguments exchange => CommitExchange(request.ActorId, exchange, request.SourceTime),
            ServiceExchangeActionArguments service => CommitService(request.ActorId, service),
            PlaceStateChangeActionArguments place => CommitPlaceState(place),
            EquipmentChangeActionArguments equipment => CommitEquipment(request.ActorId, equipment),
            ConsumptionActionArguments consumption => CommitConsumption(request.ActorId, consumption),
            RestActionArguments rest => CommitRest(request.ActorId, rest),
            _ => throw new InvalidOperationException("Validated action family was not committed.")
        };
        return ActorExecutionReceipt.Completed(request, $"gameplay/{family}", new AuthorityCommitExecutionResult(family.ToString()));
    }

    public bool TryResolve(InteractionBinding binding, out InteractionRange range)
    {
        string target = binding.ContractRef.TargetRef.Value;
        if (target.StartsWith("region/", StringComparison.Ordinal)
            && _regions.TryGetValue(target[7..], out RegionState? region))
        {
            range = new InteractionRange(region.Configuration.InteractionRange);
            return true;
        }
        if (target.StartsWith("region/", StringComparison.Ordinal)
            && _farmPlots.TryGetValue(target[7..], out FarmPlotState? farmPlot))
        {
            range = new InteractionRange(farmPlot.Configuration.InteractionRange);
            return true;
        }
        if (target.StartsWith("shop/", StringComparison.Ordinal))
        {
            range = new InteractionRange(48);
            return true;
        }
        if (target.StartsWith("place/", StringComparison.Ordinal))
        {
            range = new InteractionRange(48);
            return true;
        }
        Alice.LivingTown.TownSleepFacilityConfiguration? facility = _sleepFacilities.Values.FirstOrDefault(value =>
            StringComparer.Ordinal.Equals(value.TargetRef, target));
        if (facility is not null)
        {
            range = new InteractionRange(facility.InteractionRange);
            return true;
        }
        range = default;
        return false;
    }

    public bool TryResolve(TargetRef targetRef, out ActorVisibleTargetSpatialSnapshot? snapshot)
    {
        if (targetRef.Value.StartsWith("region/", StringComparison.Ordinal)
            && _regions.TryGetValue(targetRef.Value[7..], out RegionState? region))
        {
            snapshot = new ActorVisibleTargetSpatialSnapshot(targetRef, TargetKind.ResourceNode,
                new WorldPosition(region.Configuration.WorldX, region.Configuration.WorldY));
            return true;
        }
        if (targetRef.Value.StartsWith("region/", StringComparison.Ordinal)
            && _farmPlots.TryGetValue(targetRef.Value[7..], out FarmPlotState? farmPlot))
        {
            snapshot = new ActorVisibleTargetSpatialSnapshot(targetRef, TargetKind.ResourceNode,
                new WorldPosition(farmPlot.Configuration.WorldX, farmPlot.Configuration.WorldY));
            return true;
        }
        if (targetRef.Value.StartsWith("shop/", StringComparison.Ordinal))
        {
            TownGameplayShopConfiguration? shop = _configuration.Shops.FirstOrDefault(value =>
                StringComparer.Ordinal.Equals(value.PlaceId, targetRef.Value[5..]));
            if (shop is not null)
            {
                snapshot = new ActorVisibleTargetSpatialSnapshot(targetRef, TargetKind.PointOfInterest,
                    new WorldPosition(shop.WorldX, shop.WorldY));
                return true;
            }
        }
        if (targetRef.Value.StartsWith("place/", StringComparison.Ordinal)
            && _placePositions.TryGetValue(targetRef.Value[6..], out WorldPosition placePosition))
        {
            snapshot = new ActorVisibleTargetSpatialSnapshot(
                targetRef, TargetKind.PointOfInterest, placePosition);
            return true;
        }
        Alice.LivingTown.TownSleepFacilityConfiguration? facility = _sleepFacilities.Values.FirstOrDefault(value =>
            StringComparer.Ordinal.Equals(value.TargetRef, targetRef.Value));
        if (facility is not null && _placePositions.TryGetValue(facility.PlaceRef, out WorldPosition restPosition))
        {
            snapshot = new ActorVisibleTargetSpatialSnapshot(targetRef, TargetKind.PointOfInterest, restPosition);
            return true;
        }
        snapshot = null;
        return false;
    }

    private TownGameplayActionOffer Offer(string entryId, string label, GameActionSpec action, SimTime now) =>
        new(entryId, label, new PlayerInteractionSelection(action.Binding, action.Arguments), Validate(action, now));

    private IEnumerable<TownGameplayActionOffer> CreateStockTargetOffers(
        ActorId actorId,
        TownGameplayShopConfiguration shop,
        TownBusinessStockTargetSnapshot target,
        SimTime now)
    {
        MutableContainer actor = RequireContainer(ActorOwner(actorId.Value));
        long actorQuantity = GetAssetQuantity(actor, target.AssetId);
        if (actorQuantity > 0)
        {
            long quantity = Math.Min(actorQuantity, target.TargetQuantity - target.CurrentQuantity);
            GameActionSpec transfer = CreateAssetTransfer(
                actorId,
                AssetContainerOwnerKind.Actor,
                actorId.Value,
                AssetContainerOwnerKind.Shop,
                shop.ContainerId,
                target.AssetId,
                quantity);
            yield return Offer($"stock-transfer/{target.StockTargetId}",
                $"Stock {quantity} {target.AssetId}", transfer, now);
        }

        foreach (TownGameplayRecipeConfiguration recipe in _configuration.Recipes
                     .Where(value => value.Outputs.Any(output =>
                         StringComparer.Ordinal.Equals(output.AssetId, target.AssetId))
                         && (value.PlaceId is null || StringComparer.Ordinal.Equals(value.PlaceId, shop.PlaceId)))
                     .OrderBy(value => value.RecipeId, StringComparer.Ordinal))
        {
            GameActionSpec craft = CreateCraft(actorId, recipe.RecipeId);
            yield return Offer($"stock-craft/{target.StockTargetId}/{recipe.RecipeId}",
                $"Craft {target.AssetId} for stock", craft, now);
        }

        foreach (TownGameplayRestockConfiguration restock in _configuration.Restocks
                     .Where(value => StringComparer.Ordinal.Equals(value.MerchantActorId, actorId.Value)
                         && StringComparer.Ordinal.Equals(value.ShopContainerId, shop.ContainerId)
                         && StringComparer.Ordinal.Equals(value.AssetId, target.AssetId))
                     .OrderBy(value => value.RestockId, StringComparer.Ordinal))
        {
            GameActionSpec action = CreateRestock(actorId, restock.RestockId);
            yield return Offer($"stock-restock/{target.StockTargetId}/{restock.RestockId}",
                $"Restock {target.AssetId}", action, now);
        }
    }

    private GameplayValidationResult ValidateRegion(GameActionSpec action, RegionOperationActionArguments arguments, SimTime now)
    {
        if (_farmPlots.TryGetValue(arguments.RegionId, out FarmPlotState? plot))
            return ValidateFarmPlot(action, arguments, plot, now);
        RegionState region = RequireRegion(arguments.RegionId);
        Refresh(region, now);
        if (action.Binding.ContractRef != RegionContract(arguments.RegionId)
            || action.Binding.ExpectedVersion.Value != region.Revision)
            return GameplayValidationResult.Rejected("resource state changed");
        if (region.Stock < region.Configuration.YieldQuantity)
            return GameplayValidationResult.Rejected($"resource depleted until tick {region.RefreshAtTicks}");
        GameplayValidationResult? tool = ValidateOperationTool(
            action, arguments, region.Configuration.RequiredToolTypeId, region.Configuration.DurabilityCost);
        if (tool is not null) return tool;
        ActorTools actor = RequireActor(action.ActorId.Value);
        if (actor.Capabilities.GetValueOrDefault(region.Configuration.RequiredCapabilityId) < region.Configuration.MinimumCapability)
            return GameplayValidationResult.Rejected($"requires capability {region.Configuration.RequiredCapabilityId}");
        return GameplayValidationResult.Accepted();
    }

    private GameplayValidationResult ValidateFarmPlot(
        GameActionSpec action,
        RegionOperationActionArguments arguments,
        FarmPlotState plot,
        SimTime now)
    {
        Refresh(plot, now);
        if (action.Binding.ContractRef != RegionContract(arguments.RegionId)
            || action.Binding.ExpectedVersion.Value != plot.Revision)
            return GameplayValidationResult.Rejected("farm state changed");
        if (plot.Stage == TownFarmPlotStage.Growing)
            return GameplayValidationResult.Rejected($"growing until tick {plot.GrowingUntilTicks}");
        TownGameplayFarmPlotConfiguration configuration = plot.Configuration;
        string toolType = FarmToolType(plot);
        int durabilityCost = plot.Stage == TownFarmPlotStage.Empty
            ? configuration.PlantDurabilityCost : configuration.HarvestDurabilityCost;
        GameplayValidationResult? tool = ValidateOperationTool(action, arguments, toolType, durabilityCost);
        if (tool is not null) return tool;
        ActorTools actor = RequireActor(action.ActorId.Value);
        if (actor.Capabilities.GetValueOrDefault(configuration.RequiredCapabilityId) < configuration.MinimumCapability)
            return GameplayValidationResult.Rejected($"requires capability {configuration.RequiredCapabilityId}");
        if (plot.Stage == TownFarmPlotStage.Empty
            && RequireContainer(ActorOwner(action.ActorId.Value)).Get(configuration.SeedAssetId) < configuration.SeedQuantity)
            return GameplayValidationResult.Rejected($"requires {configuration.SeedQuantity} {configuration.SeedAssetId}");
        return GameplayValidationResult.Accepted();
    }

    private GameplayValidationResult? ValidateOperationTool(
        GameActionSpec action,
        RegionOperationActionArguments arguments,
        string toolType,
        int durabilityCost)
    {
        if (string.IsNullOrWhiteSpace(toolType))
            return action.Binding.InstrumentRef is null && arguments.ExpectedInstrumentVersion is null
                ? null : GameplayValidationResult.Rejected("operation has an unexpected tool");
        MutableItemInstance? matching = FindCarriedItem(action.ActorId.Value, toolType);
        if (matching is null) return GameplayValidationResult.Rejected($"missing carried {toolType}");
        if (!StringComparer.Ordinal.Equals(action.Binding.InstrumentRef?.Value, matching.ItemInstanceId)
            || arguments.ExpectedInstrumentVersion != matching.Version)
            return GameplayValidationResult.Rejected("selected tool changed");
        ActorTools actor = RequireActor(action.ActorId.Value);
        if (actor.EquippedInstanceId is null)
            return GameplayValidationResult.Rejected($"{toolType} is carried but not equipped");
        MutableItemInstance equipped = RequireItemInstance(actor.EquippedInstanceId);
        if (!StringComparer.Ordinal.Equals(equipped.ItemTypeId, toolType))
            return GameplayValidationResult.Rejected($"wrong equipped tool: requires {toolType}");
        return matching.Durability is int durability && durability < durabilityCost
            ? GameplayValidationResult.Rejected($"{toolType} durability is insufficient")
            : null;
    }

    private GameplayValidationResult ValidateCraft(GameActionSpec action, CraftActionArguments craft)
    {
        TownGameplayRecipeConfiguration recipe = RequireRecipe(craft.RecipeId);
        MutableContainer container = RequireContainer(ActorOwner(action.ActorId.Value));
        if (action.Binding.ExpectedVersion.Value != container.Revision)
            return GameplayValidationResult.Rejected("inventory changed");
        if (RequireActor(action.ActorId.Value).Capabilities.GetValueOrDefault(recipe.RequiredCapabilityId)
            < recipe.MinimumCapability)
            return GameplayValidationResult.Rejected($"requires capability {recipe.RequiredCapabilityId}");
        if (recipe.RequiredAssetId is not null && GetAssetQuantity(container, recipe.RequiredAssetId) < 1)
            return GameplayValidationResult.Rejected($"missing reusable {recipe.RequiredAssetId}");
        return HasAmounts(container, recipe.Inputs)
            ? GameplayValidationResult.Accepted()
            : GameplayValidationResult.Rejected("recipe inputs are insufficient");
    }

    private GameplayValidationResult ValidateService(GameActionSpec action, ServiceExchangeActionArguments exchange)
    {
        TownGameplayServiceConfiguration service = RequireService(exchange.ServiceId);
        if (!service.ProviderActorIds.Contains(exchange.ProviderActorId, StringComparer.Ordinal))
            return GameplayValidationResult.Rejected("service provider is not authorized");
        MutableContainer customer = RequireContainer(ActorOwner(action.ActorId.Value));
        MutableContainer provider = RequireContainer(ServiceOwner(service));
        if (ReferenceEquals(customer, provider))
            return GameplayValidationResult.Rejected("customer and provider containers must be distinct");
        if (action.Binding.ExpectedVersion.Value != customer.Revision
            || exchange.ExpectedProviderContainerRevision != provider.Revision)
            return GameplayValidationResult.Rejected("service inventory changed");
        TownGameplayAssetAmountConfiguration[] customerCosts = service.CustomerInputs
            .Append(new TownGameplayAssetAmountConfiguration { AssetId = CoinAssetId, Quantity = service.CoinFee })
            .Where(value => value.Quantity > 0).ToArray();
        if (!HasAmounts(customer, customerCosts) || !HasAmounts(provider, service.ProviderInputs))
            return GameplayValidationResult.Rejected("service inputs are insufficient");
        if (service.DurabilityRestore <= 0)
            return exchange.TargetItemInstanceId is null
                ? GameplayValidationResult.Accepted()
                : GameplayValidationResult.Rejected("service does not accept a target item");
        if (exchange.TargetItemInstanceId is null || !customer.ContainsInstance(exchange.TargetItemInstanceId))
            return GameplayValidationResult.Rejected("repair target is not carried");
        MutableItemInstance target = RequireItemInstance(exchange.TargetItemInstanceId);
        if (service.CustomerInputs.Any(value => StringComparer.Ordinal.Equals(value.AssetId, target.ItemTypeId)))
            return GameplayValidationResult.Rejected("repair target cannot also be a consumed service input");
        if (!service.TargetItemTypeIds.Contains(target.ItemTypeId, StringComparer.Ordinal)
            || target.Durability is null || target.MaximumDurability is null
            || target.Durability >= target.MaximumDurability)
            return GameplayValidationResult.Rejected("repair target is incompatible or already full");
        return GameplayValidationResult.Accepted();
    }

    private GameplayValidationResult ValidateTransfer(GameActionSpec action, AssetTransferActionArguments transfer)
    {
        AssetContainerOwnerKind sourceKind = Enum.Parse<AssetContainerOwnerKind>(transfer.SourceKind, false);
        AssetContainerOwnerKind destinationKind = Enum.Parse<AssetContainerOwnerKind>(transfer.DestinationKind, false);
        MutableContainer source = RequireContainer(new AssetContainerOwnerId(sourceKind, transfer.SourceId));
        _ = RequireContainer(new AssetContainerOwnerId(destinationKind, transfer.DestinationId));
        if (transfer.Quantity <= 0 || GetAssetQuantity(source, transfer.AssetId) < transfer.Quantity)
            return GameplayValidationResult.Rejected("source stock is insufficient");
        bool selfTransfer = sourceKind == AssetContainerOwnerKind.Actor
            && StringComparer.Ordinal.Equals(transfer.SourceId, action.ActorId.Value);
        bool configuredRestock = _configuration.Restocks.Any(value =>
            StringComparer.Ordinal.Equals(value.MerchantActorId, action.ActorId.Value)
            && StringComparer.Ordinal.Equals(value.SourceContainerId, transfer.SourceId)
            && StringComparer.Ordinal.Equals(value.ShopContainerId, transfer.DestinationId)
            && StringComparer.Ordinal.Equals(value.AssetId, transfer.AssetId)
            && value.Quantity == transfer.Quantity);
        if (!selfTransfer && !configuredRestock) return GameplayValidationResult.Rejected("transfer is not authorized");
        if (action.Binding.ExpectedVersion.Value != source.Revision) return GameplayValidationResult.Rejected("source container changed");
        return GameplayValidationResult.Accepted();
    }

    private GameplayValidationResult ValidateExchange(GameActionSpec action, ListedExchangeActionArguments exchange, SimTime now)
    {
        TownGameplayListingConfiguration listing = RequireListing(exchange.ListingId);
        TownGameplayShopConfiguration shop = RequireShop(listing.ShopId);
        TownGameplayExchangeKind exchangeKind = ParseExchangeKind(listing);
        MutableContainer actor = RequireContainer(ActorOwner(action.ActorId.Value));
        MutableContainer stock = RequireContainer(new AssetContainerOwnerId(AssetContainerOwnerKind.Shop, shop.ContainerId));
        if (action.Binding.ExpectedVersion.Value != stock.Revision)
            return GameplayValidationResult.Rejected("business stock changed");
        if (exchangeKind is TownGameplayExchangeKind.ShopToActor or TownGameplayExchangeKind.ActorToShop)
        {
            if (!IsShopOpen(shop.ShopId, now)) return GameplayValidationResult.Rejected("shop is closed");
            if (exchangeKind == TownGameplayExchangeKind.ShopToActor)
                return actor.Get(CoinAssetId) < listing.CoinPrice
                    ? GameplayValidationResult.Rejected("not enough Coin")
                    : GetAssetQuantity(stock, listing.AssetId) < listing.Quantity
                        ? GameplayValidationResult.Rejected("listing is out of stock")
                        : GameplayValidationResult.Accepted();
            return GetAssetQuantity(actor, listing.AssetId) < listing.Quantity
                ? GameplayValidationResult.Rejected("seller stock is insufficient")
                : stock.Get(CoinAssetId) < listing.CoinPrice
                    ? GameplayValidationResult.Rejected("business Coin is insufficient")
                    : GameplayValidationResult.Accepted();
        }
        if (!shop.ManagerActorIds.Contains(action.ActorId.Value, StringComparer.Ordinal))
            return GameplayValidationResult.Rejected("business manager authorization required");
        if (listing.CooldownGroupId is not null
            && _exchangeCooldowns.GetValueOrDefault(listing.CooldownGroupId) > now.Ticks)
            return GameplayValidationResult.Rejected("external order cooldown is active");
        return exchangeKind == TownGameplayExchangeKind.ExternalToShop
            ? stock.Get(CoinAssetId) < listing.CoinPrice
                ? GameplayValidationResult.Rejected("business Coin is insufficient")
                : GameplayValidationResult.Accepted()
            : GetAssetQuantity(stock, listing.AssetId) < listing.Quantity
                ? GameplayValidationResult.Rejected("business stock is insufficient")
                : GameplayValidationResult.Accepted();
    }

    private GameplayValidationResult ValidatePlaceState(GameActionSpec action, PlaceStateChangeActionArguments place)
    {
        TownGameplayShopConfiguration? shop = _configuration.Shops.FirstOrDefault(value =>
            StringComparer.Ordinal.Equals(value.PlaceId, place.PlaceId));
        if (shop is null || !shop.ManagerActorIds.Contains(action.ActorId.Value, StringComparer.Ordinal))
            return GameplayValidationResult.Rejected("shop manager authorization required");
        return GameplayValidationResult.Accepted();
    }

    private GameplayValidationResult ValidateEquipment(GameActionSpec action, EquipmentChangeActionArguments equipment)
    {
        ActorTools actor = RequireActor(action.ActorId.Value);
        if (action.Binding.ExpectedVersion.Value != actor.EquipmentRevision)
            return GameplayValidationResult.Rejected("equipment state changed");
        if (equipment.HandItemRef is not InstanceHandItemRef instance)
            return equipment.HandItemRef is null ? GameplayValidationResult.Accepted() : GameplayValidationResult.Rejected("only instanced tools can be equipped");
        return ActorOwnsInstance(action.ActorId.Value, instance.ItemInstanceId.Value)
            ? GameplayValidationResult.Accepted()
            : GameplayValidationResult.Rejected("tool is not carried");
    }

    private GameplayValidationResult ValidateConsumption(GameActionSpec action, ConsumptionActionArguments consumption)
    {
        TownGameplayConsumableConfiguration consumable = RequireConsumable(consumption.SourceItemTypeId.Value);
        MutableContainer container = RequireContainer(ActorOwner(action.ActorId.Value));
        if (action.Binding.ExpectedVersion.Value != container.Revision)
            return GameplayValidationResult.Rejected("inventory changed");
        if (container.Get(consumption.SourceItemTypeId.Value) < 1)
            return GameplayValidationResult.Rejected($"missing {consumption.SourceItemTypeId.Value}");
        ActorTools actor = RequireActor(action.ActorId.Value);
        bool canFeed = consumable.SatietyRestore > 0 && actor.Satiety < 100;
        bool canTreat = consumable.TreatsDiseases.Contains(actor.Disease.ToString(), StringComparer.Ordinal);
        return canFeed || canTreat
            ? GameplayValidationResult.Accepted()
            : GameplayValidationResult.Rejected("consumable has no applicable effect");
    }

    private GameplayValidationResult ValidateRest(GameActionSpec action, RestActionArguments rest)
    {
        Alice.LivingTown.TownSleepFacilityConfiguration facility = RequireSleepFacility(rest.FacilityId);
        if (action.Binding.ExpectedVersion.Value != facility.ContractVersion)
            return GameplayValidationResult.Rejected("sleep facility changed");
        if (!CanUseSleepFacility(action.ActorId.Value, facility))
            return GameplayValidationResult.Rejected("sleep facility access denied");
        return RequireActor(action.ActorId.Value).Spirit >= 100
            ? GameplayValidationResult.Rejected("spirit is already full")
            : GameplayValidationResult.Accepted();
    }

    private ProductActionFamily CommitRegion(ActorId actorId, RegionOperationActionArguments arguments, SimTime now)
    {
        if (_farmPlots.TryGetValue(arguments.RegionId, out FarmPlotState? plot))
            return CommitFarmPlot(actorId, plot, now);
        RegionState region = RequireRegion(arguments.RegionId);
        ConsumeOperationDurability(actorId, region.Configuration.RequiredToolTypeId, region.Configuration.DurabilityCost);
        region.Stock -= region.Configuration.YieldQuantity;
        region.Revision++;
        RequireContainer(ActorOwner(actorId.Value)).Adjust(region.Configuration.OutputAssetId, region.Configuration.YieldQuantity);
        return ProductActionFamily.RegionOperation;
    }

    private ProductActionFamily CommitFarmPlot(ActorId actorId, FarmPlotState plot, SimTime now)
    {
        TownGameplayFarmPlotConfiguration configuration = plot.Configuration;
        if (plot.Stage == TownFarmPlotStage.Empty)
        {
            RequireContainer(ActorOwner(actorId.Value)).Adjust(configuration.SeedAssetId, -configuration.SeedQuantity);
            ConsumeOperationDurability(actorId, configuration.PlantToolTypeId, configuration.PlantDurabilityCost);
            plot.Stage = TownFarmPlotStage.Growing;
            plot.GrowingUntilTicks = now.Add(new SimDuration(configuration.GrowthTicks)).Ticks;
        }
        else
        {
            ConsumeOperationDurability(actorId, configuration.HarvestToolTypeId, configuration.HarvestDurabilityCost);
            RequireContainer(ActorOwner(actorId.Value)).Adjust(configuration.OutputAssetId, configuration.YieldQuantity);
            plot.Stage = TownFarmPlotStage.Empty;
            plot.GrowingUntilTicks = null;
        }
        plot.Revision++;
        return ProductActionFamily.RegionOperation;
    }

    private void ConsumeOperationDurability(ActorId actorId, string toolType, int durabilityCost)
    {
        if (durabilityCost == 0 || string.IsNullOrWhiteSpace(toolType)) return;
        MutableItemInstance tool = FindCarriedItem(actorId.Value, toolType)
            ?? throw new InvalidOperationException("Validated operation tool disappeared.");
        tool.ConsumeDurability(durabilityCost);
    }

    private ProductActionFamily CommitCraft(ActorId actorId, CraftActionArguments craft)
    {
        TownGameplayRecipeConfiguration recipe = RequireRecipe(craft.RecipeId);
        MutableContainer container = RequireContainer(ActorOwner(actorId.Value));
        ConsumeAssets(container, recipe.Inputs);
        AddAssets(container, recipe.Outputs, $"craft/{actorId.Value}/{recipe.RecipeId}");
        return ProductActionFamily.Craft;
    }

    private ProductActionFamily CommitTransfer(AssetTransferActionArguments transfer)
    {
        MutableContainer source = RequireContainer(new AssetContainerOwnerId(Enum.Parse<AssetContainerOwnerKind>(transfer.SourceKind), transfer.SourceId));
        MutableContainer destination = RequireContainer(new AssetContainerOwnerId(Enum.Parse<AssetContainerOwnerKind>(transfer.DestinationKind), transfer.DestinationId));
        TransferAsset(source, destination, transfer.AssetId, transfer.Quantity);
        return ProductActionFamily.AssetTransfer;
    }

    private ProductActionFamily CommitExchange(ActorId actorId, ListedExchangeActionArguments exchange, SimTime now)
    {
        TownGameplayListingConfiguration listing = RequireListing(exchange.ListingId);
        TownGameplayShopConfiguration shop = RequireShop(listing.ShopId);
        TownGameplayExchangeKind exchangeKind = ParseExchangeKind(listing);
        MutableContainer actor = RequireContainer(ActorOwner(actorId.Value));
        MutableContainer stock = RequireContainer(new AssetContainerOwnerId(AssetContainerOwnerKind.Shop, shop.ContainerId));
        if (exchangeKind == TownGameplayExchangeKind.ShopToActor)
        {
            actor.Adjust(CoinAssetId, -listing.CoinPrice);
            stock.Adjust(CoinAssetId, listing.CoinPrice);
            TransferAsset(stock, actor, listing.AssetId, listing.Quantity);
        }
        else if (exchangeKind == TownGameplayExchangeKind.ActorToShop)
        {
            stock.Adjust(CoinAssetId, -listing.CoinPrice);
            actor.Adjust(CoinAssetId, listing.CoinPrice);
            TransferAsset(actor, stock, listing.AssetId, listing.Quantity);
        }
        else if (exchangeKind == TownGameplayExchangeKind.ExternalToShop)
        {
            stock.Adjust(CoinAssetId, -listing.CoinPrice);
            AddAssets(stock, [Amount(listing.AssetId, listing.Quantity)], $"external-import/{listing.ListingId}");
            CommitExchangeCooldown(listing, now);
        }
        else
        {
            ConsumeAssets(stock, [Amount(listing.AssetId, listing.Quantity)]);
            stock.Adjust(CoinAssetId, listing.CoinPrice);
            CommitExchangeCooldown(listing, now);
        }
        return ProductActionFamily.ListedExchange;
    }

    private ProductActionFamily CommitService(ActorId actorId, ServiceExchangeActionArguments exchange)
    {
        TownGameplayServiceConfiguration service = RequireService(exchange.ServiceId);
        MutableContainer customer = RequireContainer(ActorOwner(actorId.Value));
        MutableContainer provider = RequireContainer(ServiceOwner(service));
        ConsumeAssets(customer, service.CustomerInputs);
        ConsumeAssets(provider, service.ProviderInputs);
        if (service.CoinFee > 0)
        {
            customer.Adjust(CoinAssetId, -service.CoinFee);
            provider.Adjust(CoinAssetId, service.CoinFee);
        }
        AddAssets(customer, service.CustomerOutputs, $"service/{actorId.Value}/{service.ServiceId}");
        if (exchange.TargetItemInstanceId is not null)
            RequireItemInstance(exchange.TargetItemInstanceId).RestoreDurability(service.DurabilityRestore);
        return ProductActionFamily.ServiceExchange;
    }

    private ProductActionFamily CommitPlaceState(PlaceStateChangeActionArguments place)
    {
        _placeClosedExceptions[place.PlaceId] = place.Closed;
        return ProductActionFamily.PlaceStateChange;
    }

    private ProductActionFamily CommitEquipment(ActorId actorId, EquipmentChangeActionArguments equipment)
    {
        ActorTools actor = RequireActor(actorId.Value);
        actor.EquippedInstanceId = (equipment.HandItemRef as InstanceHandItemRef)?.ItemInstanceId.Value;
        actor.EquipmentRevision++;
        return ProductActionFamily.EquipmentChange;
    }

    private ProductActionFamily CommitConsumption(ActorId actorId, ConsumptionActionArguments consumption)
    {
        RequireContainer(ActorOwner(actorId.Value)).Adjust(consumption.SourceItemTypeId.Value, -1);
        ActorTools actor = RequireActor(actorId.Value);
        TownGameplayConsumableConfiguration consumable = RequireConsumable(consumption.SourceItemTypeId.Value);
        actor.Satiety = Math.Min(100, actor.Satiety + consumable.SatietyRestore);
        if (consumable.TreatsDiseases.Contains(actor.Disease.ToString(), StringComparer.Ordinal))
            actor.Disease = Disease.Healthy;
        return ProductActionFamily.Consumption;
    }

    private ProductActionFamily CommitRest(ActorId actorId, RestActionArguments rest)
    {
        ActorTools actor = RequireActor(actorId.Value);
        Alice.LivingTown.TownSleepFacilityConfiguration facility = RequireSleepFacility(rest.FacilityId);
        actor.Spirit = Math.Min(100, actor.Spirit + facility.SpiritRestore);
        return ProductActionFamily.Rest;
    }

    private void Refresh(RegionState region, SimTime now)
    {
        if (region.RefreshAtTicks is not long refreshAt || now.Ticks < refreshAt) return;
        long periods = (now.Ticks - refreshAt) / region.Configuration.RefreshTicks + 1;
        long previous = region.Stock;
        region.Stock = Math.Min(region.Configuration.Capacity,
            checked(region.Stock + periods * region.Configuration.ReplenishQuantity));
        region.RefreshAtTicks = checked(refreshAt + periods * region.Configuration.RefreshTicks);
        if (region.Stock != previous) region.Revision++;
    }

    private static void Refresh(FarmPlotState plot, SimTime now)
    {
        if (plot.Stage != TownFarmPlotStage.Growing
            || plot.GrowingUntilTicks is not long growingUntil
            || now.Ticks < growingUntil) return;
        plot.Stage = TownFarmPlotStage.Harvestable;
        plot.GrowingUntilTicks = null;
        plot.Revision++;
    }

    private void AddPlayer(ProductPlayerConfiguration player)
    {
        AssetContainerOwnerId owner = ActorOwner(player.ActorId);
        AddContainer(owner, player.FungibleAssets);
        foreach (ProductPlayerToolConfiguration tool in player.Tools)
            AddConfiguredInstance(owner, tool.ItemInstanceId, tool.ToolTypeId);
        AddActor(player.ActorId, player.EquippedToolInstanceId, player.Capabilities,
            player.HealthCurrent, player.HealthMaximum, player.Satiety, player.Spirit, Disease.Healthy, []);
    }

    private void AddNpc(Alice.LivingTown.TownNpcConfiguration actor)
    {
        var balances = actor.Inventory.Stacks.Select(value => new TownGameplayAssetBalanceConfiguration
        {
            AssetId = value.ItemTypeId,
            Quantity = value.Quantity
        }).Concat(actor.Currency.Select(value => new TownGameplayAssetBalanceConfiguration
        {
            AssetId = value.CurrencyId,
            Quantity = value.Quantity
        }));
        AssetContainerOwnerId owner = ActorOwner(actor.Identity.ActorId);
        AddContainer(owner, balances);
        foreach (Alice.LivingTown.TownItemInstanceConfiguration item in actor.Inventory.Instances)
            AddConfiguredInstance(owner, item.ItemInstanceId, item.ItemTypeId);
        AddActor(actor.Identity.ActorId, actor.Inventory.EquippedHandInstanceId, actor.Capabilities,
            actor.Body.HealthCurrent, actor.Body.HealthMaximum, actor.Body.Satiety, actor.Body.Spirit,
            Enum.Parse<Disease>(actor.Body.Disease, false), actor.AccessRefs);
    }

    private void AddActor(string actorId, string? equipped,
        IEnumerable<Alice.LivingTown.TownCapabilityConfiguration> capabilities,
        int healthCurrent,
        int healthMaximum,
        int satiety,
        int spirit,
        Disease disease,
        IEnumerable<string> accessRefs)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Alice.LivingTown.TownCapabilityConfiguration capability in capabilities)
            values[capability.CapabilityId] = capability.Value;
        _actors.Add(actorId, new ActorTools(
            equipped, values, healthCurrent, healthMaximum, satiety, spirit, disease, accessRefs));
    }

    private void AddContainer(AssetContainerOwnerId ownerId, IEnumerable<TownGameplayAssetBalanceConfiguration> balances,
        bool generateConfiguredInstances = true)
    {
        var merged = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (TownGameplayAssetBalanceConfiguration balance in balances)
        {
            TownGameplayAssetDefinitionConfiguration definition = RequireAssetDefinition(balance.AssetId);
            if (ParseStorage(definition) == TownGameplayAssetStorageKind.Fungible)
                merged[balance.AssetId] = merged.GetValueOrDefault(balance.AssetId) + balance.Quantity;
        }
        var container = new MutableContainer(ownerId, merged, []);
        _containers.Add(ownerId, container);
        if (!generateConfiguredInstances) return;
        foreach (TownGameplayAssetBalanceConfiguration balance in balances)
        {
            TownGameplayAssetDefinitionConfiguration definition = RequireAssetDefinition(balance.AssetId);
            if (ParseStorage(definition) != TownGameplayAssetStorageKind.Instance) continue;
            for (long index = 0; index < balance.Quantity; index++)
                AddGeneratedInstance(container, definition.AssetId, $"stock/{ownerId.Kind}/{ownerId.Value}/{definition.AssetId}");
        }
    }

    private RegionState RequireRegion(string id) => _regions.TryGetValue(id, out RegionState? value)
        ? value : throw new KeyNotFoundException($"Region '{id}' is absent.");
    private FarmPlotState RequireFarmPlot(string id) => _farmPlots.TryGetValue(id, out FarmPlotState? value)
        ? value : throw new KeyNotFoundException($"Farm plot '{id}' is absent.");
    private ActorTools RequireActor(string id) => _actors.TryGetValue(id, out ActorTools? value)
        ? value : throw new KeyNotFoundException($"Actor '{id}' is absent.");
    private MutableContainer RequireContainer(AssetContainerOwnerId id) => _containers.TryGetValue(id, out MutableContainer? value)
        ? value : throw new KeyNotFoundException($"Container '{id.Kind}/{id.Value}' is absent.");
    private MutableItemInstance RequireItemInstance(string id) => _itemInstances.TryGetValue(id, out MutableItemInstance? value)
        ? value : throw new KeyNotFoundException($"Item instance '{id}' is absent.");
    private TownGameplayAssetDefinitionConfiguration RequireAssetDefinition(string id) =>
        _assetDefinitions.TryGetValue(id, out TownGameplayAssetDefinitionConfiguration? value)
            ? value
            : throw new KeyNotFoundException($"Asset definition '{id}' is absent.");
    private TownGameplayListingConfiguration RequireListing(string id) => _configuration.Listings.Single(value =>
        StringComparer.Ordinal.Equals(value.ListingId, id));
    private TownGameplayShopConfiguration RequireShop(string id) => _configuration.Shops.Single(value =>
        StringComparer.Ordinal.Equals(value.ShopId, id));
    private TownGameplayRecipeConfiguration RequireRecipe(string id) => _configuration.Recipes.Single(value =>
        StringComparer.Ordinal.Equals(value.RecipeId, id));
    private TownGameplayServiceConfiguration RequireService(string id) => _configuration.Services.Single(value =>
        StringComparer.Ordinal.Equals(value.ServiceId, id));
    private TownGameplayConsumableConfiguration RequireConsumable(string id) => _configuration.Consumables.Single(value =>
        StringComparer.Ordinal.Equals(value.AssetId, id));
    private Alice.LivingTown.TownSleepFacilityConfiguration RequireSleepFacility(string id) =>
        _sleepFacilities.TryGetValue(id, out Alice.LivingTown.TownSleepFacilityConfiguration? value)
            ? value
            : throw new KeyNotFoundException($"Sleep facility '{id}' is absent.");
    private bool CanUseSleepFacility(string actorId, Alice.LivingTown.TownSleepFacilityConfiguration facility)
    {
        Alice.LivingTown.SleepAccessPolicy access = Enum.Parse<Alice.LivingTown.SleepAccessPolicy>(facility.AccessPolicy, false);
        if (access == Alice.LivingTown.SleepAccessPolicy.Public) return true;
        return facility.RequiredAccessRef is not null
            && RequireActor(actorId).AccessRefs.Contains(facility.RequiredAccessRef);
    }

    private void AddConfiguredInstance(AssetContainerOwnerId ownerId, string instanceId, string assetId)
    {
        TownGameplayAssetDefinitionConfiguration definition = RequireAssetDefinition(assetId);
        if (ParseStorage(definition) != TownGameplayAssetStorageKind.Instance)
            throw new InvalidDataException($"Configured item '{instanceId}' does not use instance storage.");
        var item = MutableItemInstance.Create(instanceId, definition, ownerId);
        if (!_itemInstances.TryAdd(instanceId, item))
            throw new InvalidDataException($"Item-instance identity '{instanceId}' is duplicated.");
        RequireContainer(ownerId).AddInstance(instanceId);
    }

    private MutableItemInstance AddGeneratedInstance(
        MutableContainer container,
        string assetId,
        string sourcePrefix)
    {
        TownGameplayAssetDefinitionConfiguration definition = RequireAssetDefinition(assetId);
        if (ParseStorage(definition) != TownGameplayAssetStorageKind.Instance)
            throw new InvalidOperationException("Only instance assets may create item identities.");
        string instanceId = $"{sourcePrefix}/{checked(++_nextItemSequence)}";
        var item = MutableItemInstance.Create(instanceId, definition, container.OwnerId);
        _itemInstances.Add(instanceId, item);
        container.AddInstance(instanceId);
        return item;
    }

    private MutableItemInstance? FindCarriedItem(string actorId, string assetId)
    {
        MutableContainer container = RequireContainer(ActorOwner(actorId));
        string? equipped = RequireActor(actorId).EquippedInstanceId;
        if (equipped is not null
            && container.ContainsInstance(equipped)
            && StringComparer.Ordinal.Equals(RequireItemInstance(equipped).ItemTypeId, assetId))
            return RequireItemInstance(equipped);
        return container.ItemInstanceIds
            .Select(RequireItemInstance)
            .Where(value => StringComparer.Ordinal.Equals(value.ItemTypeId, assetId))
            .OrderBy(value => value.ItemInstanceId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private bool ActorOwnsInstance(string actorId, string instanceId) =>
        RequireContainer(ActorOwner(actorId)).ContainsInstance(instanceId)
        && _itemInstances.ContainsKey(instanceId);

    private long GetAssetQuantity(MutableContainer container, string assetId)
    {
        TownGameplayAssetDefinitionConfiguration definition = RequireAssetDefinition(assetId);
        return ParseStorage(definition) == TownGameplayAssetStorageKind.Fungible
            ? container.Get(assetId)
            : container.ItemInstanceIds.LongCount(value =>
                StringComparer.Ordinal.Equals(RequireItemInstance(value).ItemTypeId, assetId));
    }

    private bool HasAmounts(MutableContainer container, IEnumerable<TownGameplayAssetAmountConfiguration> amounts) =>
        amounts.GroupBy(value => value.AssetId, StringComparer.Ordinal)
            .All(group => GetAssetQuantity(container, group.Key) >= group.Sum(value => value.Quantity));

    private void ConsumeAssets(MutableContainer container, IEnumerable<TownGameplayAssetAmountConfiguration> amounts)
    {
        foreach (IGrouping<string, TownGameplayAssetAmountConfiguration> group in amounts
                     .GroupBy(value => value.AssetId, StringComparer.Ordinal))
        {
            long quantity = group.Sum(value => value.Quantity);
            TownGameplayAssetDefinitionConfiguration definition = RequireAssetDefinition(group.Key);
            if (ParseStorage(definition) == TownGameplayAssetStorageKind.Fungible)
            {
                container.Adjust(group.Key, -quantity);
                continue;
            }
            string[] instances = container.ItemInstanceIds
                .Where(value => StringComparer.Ordinal.Equals(RequireItemInstance(value).ItemTypeId, group.Key))
                .OrderBy(value => value, StringComparer.Ordinal)
                .Take(checked((int)quantity)).ToArray();
            foreach (string instanceId in instances)
            {
                container.RemoveInstance(instanceId);
                _itemInstances.Remove(instanceId);
            }
        }
    }

    private void AddAssets(
        MutableContainer container,
        IEnumerable<TownGameplayAssetAmountConfiguration> amounts,
        string sourcePrefix)
    {
        foreach (IGrouping<string, TownGameplayAssetAmountConfiguration> group in amounts
                     .GroupBy(value => value.AssetId, StringComparer.Ordinal))
        {
            long quantity = group.Sum(value => value.Quantity);
            TownGameplayAssetDefinitionConfiguration definition = RequireAssetDefinition(group.Key);
            if (ParseStorage(definition) == TownGameplayAssetStorageKind.Fungible)
            {
                container.Adjust(group.Key, quantity);
                continue;
            }
            for (long index = 0; index < quantity; index++)
                AddGeneratedInstance(container, group.Key, sourcePrefix);
        }
    }

    private void TransferAsset(MutableContainer source, MutableContainer destination, string assetId, long quantity)
    {
        TownGameplayAssetDefinitionConfiguration definition = RequireAssetDefinition(assetId);
        if (ParseStorage(definition) == TownGameplayAssetStorageKind.Fungible)
        {
            source.Adjust(assetId, -quantity);
            destination.Adjust(assetId, quantity);
            return;
        }
        string[] instances = source.ItemInstanceIds
            .Where(value => StringComparer.Ordinal.Equals(RequireItemInstance(value).ItemTypeId, assetId))
            .OrderBy(value => value, StringComparer.Ordinal)
            .Take(checked((int)quantity)).ToArray();
        foreach (string instanceId in instances)
        {
            source.RemoveInstance(instanceId);
            destination.AddInstance(instanceId);
            RequireItemInstance(instanceId).MoveTo(destination.OwnerId);
        }
    }

    private static AssetContainerOwnerId ServiceOwner(TownGameplayServiceConfiguration service) =>
        new(Enum.Parse<AssetContainerOwnerKind>(service.ProviderOwnerKind, false), service.ProviderContainerId);

    private void CommitExchangeCooldown(TownGameplayListingConfiguration listing, SimTime now)
    {
        if (listing.CooldownGroupId is null) return;
        _exchangeCooldowns[listing.CooldownGroupId] = checked(now.Ticks + listing.CooldownTicks);
    }

    private static TownGameplayAssetAmountConfiguration Amount(string assetId, long quantity) =>
        new() { AssetId = assetId, Quantity = quantity };

    private static TownGameplayExchangeKind ParseExchangeKind(TownGameplayListingConfiguration listing) =>
        Enum.Parse<TownGameplayExchangeKind>(listing.ExchangeKind, false);

    private static TownActorVitalsSnapshot Vitals(ActorTools actor) => new(
        actor.HealthCurrent, actor.HealthMaximum, actor.Satiety, actor.Spirit, actor.Disease);

    private static string FarmToolType(FarmPlotState plot) => plot.Stage switch
    {
        TownFarmPlotStage.Empty => plot.Configuration.PlantToolTypeId,
        TownFarmPlotStage.Harvestable => plot.Configuration.HarvestToolTypeId,
        _ => string.Empty
    };

    private static TownGameplayAssetStorageKind ParseStorage(TownGameplayAssetDefinitionConfiguration definition) =>
        Enum.Parse<TownGameplayAssetStorageKind>(definition.StorageKind, false);

    internal static void ValidateConfiguration(TownGameplayConfigurationDocument configuration)
    {
        if (configuration.AssetDefinitions.Length == 0
            || configuration.AssetDefinitions.Any(value => string.IsNullOrWhiteSpace(value.AssetId)
                || !Enum.TryParse(value.StorageKind, false, out TownGameplayAssetStorageKind storage)
                || storage == TownGameplayAssetStorageKind.Fungible && value.MaximumDurability is not null
                || storage == TownGameplayAssetStorageKind.Instance && value.MaximumDurability is <= 0)
            || configuration.AssetDefinitions.Select(value => value.AssetId).Distinct(StringComparer.Ordinal).Count()
                != configuration.AssetDefinitions.Length)
            throw new InvalidDataException("Gameplay asset definitions are invalid.");
        HashSet<string> assets = configuration.AssetDefinitions.Select(value => value.AssetId)
            .ToHashSet(StringComparer.Ordinal);
        bool InvalidAmounts(IEnumerable<TownGameplayAssetAmountConfiguration> values) =>
            values.Any(value => !assets.Contains(value.AssetId) || value.Quantity <= 0);
        if (configuration.Regions.Any(value => string.IsNullOrWhiteSpace(value.RegionId)
                || !assets.Contains(value.OutputAssetId)
                || value.YieldQuantity <= 0 || value.Capacity <= 0
                || value.YieldQuantity > value.Capacity || value.RefreshTicks <= 0
                || value.ReplenishQuantity <= 0 || value.OperationTicks <= 0 || value.InteractionRange <= 0
                || string.IsNullOrWhiteSpace(value.RequiredCapabilityId) || value.MinimumCapability < 0
                || value.DurabilityCost < 0
                || value.DurabilityCost > 0 && (string.IsNullOrWhiteSpace(value.RequiredToolTypeId)
                    || !assets.Contains(value.RequiredToolTypeId)))
            || configuration.FarmPlots.Any(value => string.IsNullOrWhiteSpace(value.PlotId)
                || !Enum.TryParse(value.InitialStage, false, out TownFarmPlotStage stage)
                || stage == TownFarmPlotStage.Growing != value.InitialGrowthRemainingTicks.HasValue
                || value.InitialGrowthRemainingTicks is <= 0
                || value.GrowthTicks <= 0 || value.SeedQuantity <= 0 || value.YieldQuantity <= 0
                || !assets.Contains(value.SeedAssetId) || !assets.Contains(value.OutputAssetId)
                || !assets.Contains(value.PlantToolTypeId) || !assets.Contains(value.HarvestToolTypeId)
                || value.PlantDurabilityCost < 0 || value.HarvestDurabilityCost < 0
                || string.IsNullOrWhiteSpace(value.RequiredCapabilityId) || value.MinimumCapability < 0
                || value.OperationTicks <= 0 || value.InteractionRange <= 0)
            || configuration.Consumables.Any(value => !assets.Contains(value.AssetId)
                || value.SatietyRestore < 0
                || value.SatietyRestore == 0 && value.TreatsDiseases.Length == 0
                || value.TreatsDiseases.Any(value => !Enum.TryParse(value, false, out Disease disease)
                    || disease is Disease.Healthy or Disease.Dead))
            || configuration.Regions.Select(value => value.RegionId)
                .Concat(configuration.FarmPlots.Select(value => value.PlotId))
                .Distinct(StringComparer.Ordinal).Count() != configuration.Regions.Length + configuration.FarmPlots.Length
            || configuration.Consumables.Select(value => value.AssetId).Distinct(StringComparer.Ordinal).Count()
                != configuration.Consumables.Length
            || configuration.Containers.SelectMany(value => value.Balances)
                .Any(value => !assets.Contains(value.AssetId) || value.Quantity < 0)
            || configuration.Listings.Any(value => string.IsNullOrWhiteSpace(value.ListingId)
                || !assets.Contains(value.AssetId)
                || !Enum.TryParse(value.ExchangeKind, false, out TownGameplayExchangeKind exchangeKind)
                || value.CooldownTicks < 0
                || value.CooldownGroupId is null && value.CooldownTicks != 0
                || value.CooldownGroupId is not null && value.CooldownTicks <= 0
                || (exchangeKind is TownGameplayExchangeKind.ShopToActor or TownGameplayExchangeKind.ActorToShop)
                    && value.CooldownGroupId is not null)
            || configuration.StockTargets.Any(value => string.IsNullOrWhiteSpace(value.StockTargetId)
                || !assets.Contains(value.AssetId) || value.TargetQuantity <= 0)
            || configuration.Restocks.Any(value => !assets.Contains(value.AssetId))
            || configuration.Recipes.Any(value => string.IsNullOrWhiteSpace(value.RecipeId)
                || string.IsNullOrWhiteSpace(value.RequiredCapabilityId)
                || value.MinimumCapability < 0
                || value.RequiredAssetId is not null && !assets.Contains(value.RequiredAssetId)
                || InvalidAmounts(value.Inputs) || InvalidAmounts(value.Outputs) || value.Outputs.Length == 0)
            || configuration.Services.Any(value => string.IsNullOrWhiteSpace(value.ServiceId)
                || string.IsNullOrWhiteSpace(value.PlaceId)
                || value.ProviderActorIds.Length == 0
                || !Enum.TryParse(value.ProviderOwnerKind, false, out AssetContainerOwnerKind _)
                || string.IsNullOrWhiteSpace(value.ProviderContainerId)
                || value.CoinFee < 0 || value.DurabilityRestore < 0
                || InvalidAmounts(value.CustomerInputs) || InvalidAmounts(value.ProviderInputs)
                || InvalidAmounts(value.CustomerOutputs)
                || value.TargetItemTypeIds.Any(asset => !assets.Contains(asset)))
            || configuration.Recipes.Select(value => value.RecipeId).Distinct(StringComparer.Ordinal).Count()
                != configuration.Recipes.Length
            || configuration.Services.Select(value => value.ServiceId).Distinct(StringComparer.Ordinal).Count()
                != configuration.Services.Length
            || configuration.Listings.Select(value => value.ListingId).Distinct(StringComparer.Ordinal).Count()
                != configuration.Listings.Length
            || configuration.StockTargets.Select(value => value.StockTargetId).Distinct(StringComparer.Ordinal).Count()
                != configuration.StockTargets.Length)
            throw new InvalidDataException("Gameplay production configuration is invalid.");
    }

    private static AssetContainerOwnerId ActorOwner(string actorId) => new(AssetContainerOwnerKind.Actor, actorId);
    private static ContractRef RegionContract(string regionId) => new(new TargetRef($"region/{regionId}"), "region-operation");

    private sealed class RegionState
    {
        public RegionState(TownGameplayRegionConfiguration configuration)
        {
            Configuration = configuration;
            Stock = configuration.Capacity;
            RefreshAtTicks = configuration.RefreshTicks;
        }
        public TownGameplayRegionConfiguration Configuration { get; }
        public long Stock { get; set; }
        public long? RefreshAtTicks { get; set; }
        public long Revision { get; set; } = 1;
    }

    private sealed class FarmPlotState
    {
        public FarmPlotState(TownGameplayFarmPlotConfiguration configuration)
        {
            Configuration = configuration;
            Stage = Enum.Parse<TownFarmPlotStage>(configuration.InitialStage, false);
            GrowingUntilTicks = Stage == TownFarmPlotStage.Growing
                ? configuration.InitialGrowthRemainingTicks : null;
        }
        public TownGameplayFarmPlotConfiguration Configuration { get; }
        public TownFarmPlotStage Stage { get; set; }
        public long? GrowingUntilTicks { get; set; }
        public long Revision { get; set; } = 1;
    }

    private sealed class MutableItemInstance
    {
        private MutableItemInstance(
            string itemInstanceId,
            string itemTypeId,
            int? durability,
            int? maximumDurability,
            int version,
            AssetContainerOwnerId ownerId)
        {
            ItemInstanceId = itemInstanceId;
            ItemTypeId = itemTypeId;
            Durability = durability;
            MaximumDurability = maximumDurability;
            Version = version;
            OwnerId = ownerId;
        }

        public string ItemInstanceId { get; }
        public string ItemTypeId { get; }
        public int? Durability { get; private set; }
        public int? MaximumDurability { get; }
        public int Version { get; private set; }
        public AssetContainerOwnerId OwnerId { get; private set; }

        public static MutableItemInstance Create(
            string itemInstanceId,
            TownGameplayAssetDefinitionConfiguration definition,
            AssetContainerOwnerId ownerId) =>
            new(itemInstanceId, definition.AssetId, definition.MaximumDurability,
                definition.MaximumDurability, 1, ownerId);

        public static MutableItemInstance Restore(TownGameplayItemInstanceDurableState state)
        {
            if (string.IsNullOrWhiteSpace(state.ItemInstanceId)
                || string.IsNullOrWhiteSpace(state.ItemTypeId)
                || state.Version < 1
                || state.Durability is < 0
                || state.MaximumDurability is < 1
                || state.Durability is not null && state.MaximumDurability is null
                || state.Durability > state.MaximumDurability)
                throw new InvalidDataException("Saved item-instance values are invalid.");
            return new MutableItemInstance(
                state.ItemInstanceId,
                state.ItemTypeId,
                state.Durability,
                state.MaximumDurability,
                state.Version,
                new AssetContainerOwnerId(state.OwnerKind, state.OwnerId));
        }

        public ItemInstance Snapshot() => new(
            new ItemInstanceId(ItemInstanceId),
            new ItemTypeId(ItemTypeId),
            Durability,
            Version);

        public TownGameplayItemInstanceDurableState Capture() => new(
            ItemInstanceId,
            ItemTypeId,
            Durability,
            MaximumDurability,
            Version,
            OwnerId.Kind,
            OwnerId.Value);

        public void ConsumeDurability(int amount)
        {
            if (amount == 0) return;
            if (amount < 0 || Durability is null || Durability < amount)
                throw new InvalidOperationException("Item durability cannot satisfy this operation.");
            Durability -= amount;
            Version = checked(Version + 1);
        }

        public void RestoreDurability(int amount)
        {
            if (amount <= 0 || Durability is null || MaximumDurability is null)
                throw new InvalidOperationException("Item cannot receive durability restoration.");
            Durability = Math.Min(MaximumDurability.Value, Durability.Value + amount);
            Version = checked(Version + 1);
        }

        public void MoveTo(AssetContainerOwnerId ownerId)
        {
            OwnerId = ownerId;
            Version = checked(Version + 1);
        }
    }

    private sealed class ActorTools
    {
        public ActorTools(string? equipped,
            Dictionary<string, int> capabilities,
            int healthCurrent,
            int healthMaximum,
            int satiety,
            int spirit,
            Disease disease,
            IEnumerable<string> accessRefs)
        {
            EquippedInstanceId = equipped;
            Capabilities = capabilities;
            HealthCurrent = healthCurrent;
            HealthMaximum = healthMaximum;
            Satiety = satiety;
            Spirit = spirit;
            Disease = disease;
            AccessRefs = accessRefs.ToHashSet(StringComparer.Ordinal);
        }
        public string? EquippedInstanceId { get; set; }
        public long EquipmentRevision { get; set; } = 1;
        public Dictionary<string, int> Capabilities { get; }
        public int HealthCurrent { get; set; }
        public int HealthMaximum { get; set; }
        public int Satiety { get; set; }
        public int Spirit { get; set; }
        public Disease Disease { get; set; }
        public long LastNeedsAtTicks { get; set; }
        public HashSet<string> AccessRefs { get; }
    }

    private sealed class MutableContainer
    {
        private readonly Dictionary<string, long> _balances;
        private readonly HashSet<string> _instances;
        public MutableContainer(AssetContainerOwnerId ownerId, Dictionary<string, long> balances, IEnumerable<string> instances)
        {
            OwnerId = ownerId;
            _balances = balances;
            _instances = instances.ToHashSet(StringComparer.Ordinal);
        }
        public AssetContainerOwnerId OwnerId { get; }
        public long Revision { get; private set; } = 1;
        public IReadOnlyCollection<string> ItemInstanceIds => _instances;
        public bool ContainsInstance(string id) => _instances.Contains(id);
        public long Get(string assetId) => _balances.GetValueOrDefault(assetId);
        public void Adjust(string assetId, long delta)
        {
            long replacement = checked(Get(assetId) + delta);
            if (replacement < 0) throw new InvalidOperationException("Asset balance cannot become negative.");
            _balances[assetId] = replacement;
            Revision++;
        }
        public void AddInstance(string id)
        {
            if (!_instances.Add(id)) throw new InvalidOperationException("Item instance is already in this container.");
            Revision++;
        }
        public void RemoveInstance(string id)
        {
            if (!_instances.Remove(id)) throw new InvalidOperationException("Item instance is not in this container.");
            Revision++;
        }
        public AssetContainerState Snapshot() => new(
            OwnerId,
            _balances.Select(value => new FungibleAssetBalance(new FungibleAssetId(value.Key), value.Value)),
            _instances.Select(value => new ItemInstanceId(value)),
            Revision);
        public void Restore(TownGameplayContainerDurableState state)
        {
            if (state.Revision < 1
                || state.ItemInstanceIds.Any(string.IsNullOrWhiteSpace)
                || state.ItemInstanceIds.Distinct(StringComparer.Ordinal).Count() != state.ItemInstanceIds.Count)
                throw new InvalidDataException("Saved container identity state is invalid.");
            _balances.Clear();
            foreach (TownGameplayAssetBalanceDurableState balance in state.Balances)
            {
                if (balance.Quantity < 0 || !_balances.TryAdd(balance.AssetId, balance.Quantity))
                    throw new InvalidDataException("Saved container balance state is invalid.");
            }
            _instances.Clear();
            foreach (string instanceId in state.ItemInstanceIds) _instances.Add(instanceId);
            Revision = state.Revision;
        }
    }
}

public sealed class TownGameplayActorExecutor : IActorExecutionExecutor
{
    private readonly RegionSocialGameplayRuntime _runtime;
    public TownGameplayActorExecutor(RegionSocialGameplayRuntime runtime, ActorId actorId)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        ActorId = actorId;
    }
    public ActorId ActorId { get; }
    public ActorExecutionReceipt Execute(ActorExecutionRequest request)
    {
        if (request.ActorId != ActorId)
            return ActorExecutionReceipt.Rejected(request, ActorExecutionFailure.ForeignActor, "gameplay/foreign-actor");
        if (request.Mode == ActorExecutionMode.Wait)
            return ActorExecutionReceipt.Completed(request, "gameplay/wait");
        return request.Mode == ActorExecutionMode.Interact
            ? _runtime.Execute(request)
            : ActorExecutionReceipt.Rejected(request, ActorExecutionFailure.Unsupported, "gameplay/mode-unsupported");
    }
}
