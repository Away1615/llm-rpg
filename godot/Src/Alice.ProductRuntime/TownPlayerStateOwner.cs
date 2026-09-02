using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Alice.Activities;
using Alice.Actors;
using Alice.Interaction;
using Alice.Items;
using Alice.LivingTown;
using Alice.Navigation;
using Alice.World;

namespace Alice.ProductRuntime;

public sealed record TownPlayerViewSnapshot(
    ActorId ActorId,
    IReadOnlyList<string> InventoryEntries,
    string? EquippedHandItem,
    long ContainerRevision,
    int ExecutionReceiptCount);

public sealed record TownPlayerDurableState(
    double WorldX,
    double WorldY,
    long ExecutionSequence);

/// <summary>Player adapter over the same world gameplay authority used by NPCs.</summary>
public sealed class TownPlayerStateOwner :
    IInteractionRangeQuery,
    IActorVisibleTargetSpatialQuery,
    IActorExecutionExecutor
{
    private readonly ProductPlayerConfiguration _configuration;
    private readonly RegionSocialGameplayRuntime _gameplay;
    private readonly TownGameplayActorExecutor _executor;
    private readonly List<ActorExecutionReceipt> _receipts = [];
    private WorldPosition _confirmedPosition;
    private long _executionSequence;

    public TownPlayerStateOwner(ProductPlayerConfiguration configuration, RegionSocialGameplayRuntime gameplay)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _gameplay = gameplay ?? throw new ArgumentNullException(nameof(gameplay));
        ActorState = CreateInitialActorState(configuration);
        _executor = gameplay.CreateExecutor(ActorState.Identity.ActorId);
        _confirmedPosition = new WorldPosition(configuration.StartWorldX, configuration.StartWorldY);
    }

    public ActorId ActorId => ActorState.Identity.ActorId;
    public SharedActorState ActorState { get; private set; }
    public WorldPosition ConfirmedPosition => _confirmedPosition;
    public IReadOnlyList<ActorExecutionReceipt> ExecutionReceipts => _receipts.AsReadOnly();

    public TownPlayerViewSnapshot GetViewSnapshot()
    {
        AssetContainerState container = _gameplay.GetContainer(new AssetContainerOwnerId(AssetContainerOwnerKind.Actor, ActorId.Value));
        string[] entries = container.Balances.Select(value => $"{value.AssetId.Value} x{value.Quantity}")
            .Concat(_gameplay.GetItemInstances(container.OwnerId).Select(value => value.Durability is int durability
                ? $"{value.ItemTypeId.Value} [{durability}] ({value.ItemInstanceId.Value})"
                : $"{value.ItemTypeId.Value} ({value.ItemInstanceId.Value})"))
            .ToArray();
        return new TownPlayerViewSnapshot(
            ActorId,
            new ReadOnlyCollection<string>(entries),
            _gameplay.GetEquippedTool(ActorId.Value),
            container.Revision,
            _receipts.Count);
    }

    public ActorExecutionRequest CreateInteractRequest(GameActionSpec action, SimTime now)
    {
        var payload = new InteractExecutionPayload(ActorId, action);
        return CreateRequest(ActorExecutionMode.Interact, payload, now);
    }

    public ActorExecutionRequest CreateEquipmentRequest(bool equip, SimTime now)
    {
        string? item = equip ? _configuration.Tools.FirstOrDefault()?.ItemInstanceId : null;
        return CreateInteractRequest(_gameplay.CreateEquipmentChange(ActorId, item), now);
    }

    public void ConfirmPosition(WorldPosition position) => _confirmedPosition = position;

    public TownPlayerDurableState CaptureDurableState() =>
        new(_confirmedPosition.X, _confirmedPosition.Y, _executionSequence);

    public void RestoreDurableState(TownPlayerDurableState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ExecutionSequence < 0) throw new InvalidDataException("Saved Player execution sequence is invalid.");
        _confirmedPosition = new WorldPosition(state.WorldX, state.WorldY);
        _executionSequence = state.ExecutionSequence;
        _receipts.Clear();
    }

    public void ApplyVitals(TownActorVitalsSnapshot vitals)
    {
        ActorState = new SharedActorState(
            ActorState.Identity,
            new ActorBodyState(ActorId, new Health(vitals.HealthCurrent, vitals.HealthMaximum),
                new Satiety(vitals.Satiety), new Spirit(vitals.Spirit), vitals.Disease),
            ActorState.Traversal,
            ActorState.Inventory,
            ActorState.Equipment);
    }

    public bool TryResolve(InteractionBinding binding, out InteractionRange range) =>
        _gameplay.TryResolve(binding, out range);

    public bool TryResolve(TargetRef targetRef, out ActorVisibleTargetSpatialSnapshot? snapshot) =>
        _gameplay.TryResolve(targetRef, out snapshot);

    public ActorExecutionReceipt Execute(ActorExecutionRequest request)
    {
        ActorExecutionReceipt receipt = _executor.Execute(request);
        if (receipt.ActorId == ActorId && !_receipts.Any(value => value.ExecutionId == receipt.ExecutionId))
            _receipts.Add(receipt);
        if (receipt.Outcome == ActorExecutionOutcome.Completed
            && request.Payload is InteractExecutionPayload { Action.Arguments: ConsumptionActionArguments or RestActionArguments })
            ApplyVitals(_gameplay.GetVitals(ActorId.Value));
        return receipt;
    }

    private ActorExecutionRequest CreateRequest(ActorExecutionMode mode, ActorExecutionPayload payload, SimTime now) =>
        new(
            new ActorExecutionId($"demo/{ActorId.Value}/{checked(++_executionSequence)}"),
            ActorId,
            mode,
            payload,
            now,
            AutonomousNpcCognitionRoute.None);

    private static SharedActorState CreateInitialActorState(ProductPlayerConfiguration source)
    {
        var actorId = new ActorId(source.ActorId);
        var inventory = new InventoryState(
            actorId,
            source.Tools.Select(value => new InstanceEntry(new ItemInstanceId(value.ItemInstanceId))),
            1);
        HandItemRef? hand = source.EquippedToolInstanceId is null
            ? null
            : new InstanceHandItemRef(new ItemInstanceId(source.EquippedToolInstanceId));
        return new SharedActorState(
            new ActorIdentity(actorId, new ActorName(source.Name), new ActorAge(source.Age)),
            new ActorBodyState(actorId, new Health(source.HealthCurrent, source.HealthMaximum),
                new Satiety(source.Satiety), new Spirit(source.Spirit), Disease.Healthy),
            new ActorTraversalState(actorId, MovementMode.Land),
            inventory,
            new EquipmentState(actorId, hand, 1, inventory));
    }
}

public sealed record ProductPlayerConfiguration
{
    [JsonRequired, JsonPropertyName("actor_id")] public string ActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("age")] public int Age { get; init; }
    [JsonRequired, JsonPropertyName("health_current")] public int HealthCurrent { get; init; }
    [JsonRequired, JsonPropertyName("health_maximum")] public int HealthMaximum { get; init; }
    [JsonRequired, JsonPropertyName("satiety")] public int Satiety { get; init; }
    [JsonRequired, JsonPropertyName("spirit")] public int Spirit { get; init; }
    [JsonRequired, JsonPropertyName("start_world_x")] public double StartWorldX { get; init; }
    [JsonRequired, JsonPropertyName("start_world_y")] public double StartWorldY { get; init; }
    [JsonRequired, JsonPropertyName("tools")] public ProductPlayerToolConfiguration[] Tools { get; init; } = [];
    [JsonRequired, JsonPropertyName("equipped_tool_instance_id")] public string? EquippedToolInstanceId { get; init; }
    [JsonRequired, JsonPropertyName("fungible_assets")] public TownGameplayAssetBalanceConfiguration[] FungibleAssets { get; init; } = [];
    [JsonRequired, JsonPropertyName("capabilities")] public TownCapabilityConfiguration[] Capabilities { get; init; } = [];
}
