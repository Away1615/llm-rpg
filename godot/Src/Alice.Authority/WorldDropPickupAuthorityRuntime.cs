using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Interaction;
using Alice.Items;
using Alice.Validation;
using Alice.World;

namespace Alice.Authority;

public sealed class WorldDropPickupAuthorityRuntime
{
    private readonly ReadOnlyCollection<ItemInstance> _itemInstances;
    private readonly ReadOnlyCollection<ItemDefinition> _itemDefinitions;
    private ReadOnlyCollection<SharedActorState> _actorStates;
    private WorldDrop? _worldDrop;

    public WorldDropPickupAuthorityRuntime(TargetRef targetRef, PickupContract currentContract, WorldDrop currentWorldDrop, IEnumerable<SharedActorState> actorStates, IEnumerable<ItemInstance> itemInstances, IEnumerable<ItemDefinition> itemDefinitions)
    {
        ArgumentNullException.ThrowIfNull(targetRef); ArgumentNullException.ThrowIfNull(currentContract); ArgumentNullException.ThrowIfNull(currentWorldDrop); ArgumentNullException.ThrowIfNull(actorStates); ArgumentNullException.ThrowIfNull(itemInstances); ArgumentNullException.ThrowIfNull(itemDefinitions);
        if (currentContract.ContractRef.TargetRef != targetRef || currentContract.Version != currentWorldDrop.Version || currentContract.AccessPolicy == PickupAccessPolicy.ClaimantOnly && currentWorldDrop.ClaimantActorId is null) throw new ArgumentException("Pickup target snapshots are incoherent.");
        SharedActorState[] states = actorStates.ToArray(); if (states.Length == 0) throw new ArgumentOutOfRangeException(nameof(actorStates)); Array.Sort(states, ActorComparer.Instance); EnsureUniqueActors(states);
        ItemInstance[] instances = itemInstances.ToArray(); EnsureUniqueInstances(instances);
        ItemDefinition[] definitions = itemDefinitions.ToArray(); EnsureUniqueDefinitions(definitions);
        TargetRef = targetRef; CurrentContract = currentContract; _worldDrop = currentWorldDrop; _actorStates = Array.AsReadOnly(states); _itemInstances = Array.AsReadOnly(instances); _itemDefinitions = Array.AsReadOnly(definitions);
    }
    public TargetRef TargetRef { get; }
    public PickupContract CurrentContract { get; }
    public WorldDrop? WorldDrop => _worldDrop;
    public IReadOnlyList<SharedActorState> ActorStates => _actorStates;
    public IReadOnlyList<ItemInstance> ItemInstances => _itemInstances;
    public IReadOnlyList<ItemDefinition> ItemDefinitions => _itemDefinitions;

    public PickupCommitResult TryCommitPickup(GameActionSpec action, GameActionId gameActionId, PickupValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(gameActionId);
        PickupValidationResult validation = PickupActionValidator.Validate(action, context, CurrentContract, _worldDrop, _actorStates, _itemInstances, _itemDefinitions);
        if (!validation.IsValid || validation.Commit is null) return new PickupCommitResult(null, validation.Failure);
        ValidatedPickupCommit commit = validation.Commit;
        EquipmentState equipment = new(commit.ActorState.Equipment.ActorId, commit.ActorState.Equipment.HandItemRef, commit.ActorState.Equipment.Version, commit.ReplacementInventory);
        SharedActorState replacement = new(commit.ActorState.Identity, commit.ActorState.Body, commit.ActorState.Traversal, commit.ReplacementInventory, equipment);
        PickupCommitReceipt receipt = new(new CommitOrigin.ActorAction(commit.ActorState.Identity.ActorId, gameActionId), commit.Contract.ContractRef, commit.Contract.Version, commit.Drop.DropId, commit.Drop.Version, commit.ActorState.Inventory.Version, commit.ReplacementInventory.Version, commit.Drop.Items);
        SharedActorState[] states = _actorStates.ToArray();
        for (int index = 0; index < states.Length; index++) if (states[index].Identity.ActorId == replacement.Identity.ActorId) { states[index] = replacement; break; }
        _actorStates = Array.AsReadOnly(states);
        _worldDrop = null;
        return new PickupCommitResult(receipt, null);
    }
    private static void EnsureUniqueActors(IEnumerable<SharedActorState> states) { var ids = new HashSet<ActorId>(); foreach (SharedActorState state in states) { ArgumentNullException.ThrowIfNull(state); if (!ids.Add(state.Identity.ActorId)) throw new ArgumentException("Actor snapshots must be unique."); } }
    private static void EnsureUniqueInstances(IEnumerable<ItemInstance> instances) { var ids = new HashSet<ItemInstanceId>(); foreach (ItemInstance item in instances) { ArgumentNullException.ThrowIfNull(item); if (!ids.Add(item.ItemInstanceId)) throw new ArgumentException("Item instances must be unique."); } }
    private static void EnsureUniqueDefinitions(IEnumerable<ItemDefinition> definitions) { var ids = new HashSet<ItemTypeId>(); foreach (ItemDefinition item in definitions) { ArgumentNullException.ThrowIfNull(item); if (!ids.Add(item.ItemTypeId)) throw new ArgumentException("Item definitions must be unique."); } }
    private sealed class ActorComparer : IComparer<SharedActorState> { public static ActorComparer Instance { get; } = new(); public int Compare(SharedActorState? left, SharedActorState? right) => StringComparer.Ordinal.Compare(left?.Identity.ActorId.Value, right?.Identity.ActorId.Value); }
}
