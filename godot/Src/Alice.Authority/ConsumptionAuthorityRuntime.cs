using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Interaction;
using Alice.Items;
using Alice.Navigation;
using Alice.Validation;
using Alice.World;

namespace Alice.Authority;

/// <summary>Bounded synchronous owner for one exact Consumption target and Shared Actor snapshot.</summary>
public sealed class ConsumptionAuthorityRuntime
{
    private readonly HashSet<GameActionId> _committedGameActionIds = [];
    private readonly ReadOnlyCollection<ItemInstance> _itemInstances;
    private readonly ReadOnlyCollection<ItemDefinition> _itemDefinitions;
    private SharedActorState _actorState;

    public ConsumptionAuthorityRuntime(TargetRef targetRef, ConsumptionContract currentContract, WorldPosition targetPosition, SharedActorState actorState, IEnumerable<ItemInstance> itemInstances, IEnumerable<ItemDefinition> itemDefinitions)
    {
        ArgumentNullException.ThrowIfNull(targetRef);
        ArgumentNullException.ThrowIfNull(currentContract);
        ArgumentNullException.ThrowIfNull(actorState);
        ArgumentNullException.ThrowIfNull(itemInstances);
        ArgumentNullException.ThrowIfNull(itemDefinitions);
        WorldPositionValidation.Validate(targetPosition, nameof(targetPosition), "World position must be finite.");
        if (targetRef != currentContract.ContractRef.TargetRef) throw new ArgumentException("Authority target must match its exact Consumption contract.", nameof(targetRef));

        ItemInstance[] instances = itemInstances.ToArray();
        ItemDefinition[] definitions = itemDefinitions.ToArray();
        EnsureUniqueInstances(instances);
        EnsureUniqueDefinitions(definitions);
        TargetRef = targetRef;
        CurrentContract = currentContract;
        TargetPosition = targetPosition;
        _actorState = actorState;
        _itemInstances = Array.AsReadOnly(instances);
        _itemDefinitions = Array.AsReadOnly(definitions);
    }

    public TargetRef TargetRef { get; }
    public ConsumptionContract CurrentContract { get; }
    public WorldPosition TargetPosition { get; }
    public SharedActorState ActorState => _actorState;
    public IReadOnlyList<ItemInstance> ItemInstances => _itemInstances;
    public IReadOnlyList<ItemDefinition> ItemDefinitions => _itemDefinitions;

    public ConsumptionCommitResult TryCommitConsumption(GameActionSpec action, GameActionId gameActionId, ConsumptionValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(gameActionId);
        ArgumentNullException.ThrowIfNull(context);
        if (_committedGameActionIds.Contains(gameActionId)) return new ConsumptionCommitResult(ConsumptionValidationFailure.DuplicateGameActionId);
        ConsumptionValidationResult validation = ConsumptionActionValidator.Validate(action, context, CurrentContract, _actorState, TargetPosition, _itemInstances, _itemDefinitions);
        if (!validation.IsValid) return new ConsumptionCommitResult(validation.Failure!.Value);
        if (action.Arguments is not ConsumptionActionArguments arguments) return new ConsumptionCommitResult(ConsumptionValidationFailure.ActionKindMismatch);

        try
        {
            InventoryState replacementInventory = ConsumeFirstMatchingStack(_actorState.Inventory, arguments.SourceItemTypeId);
            EquipmentState replacementEquipment = ReplaceEquipment(_actorState.Equipment, replacementInventory, arguments.SourceItemTypeId);
            int previousSatiety = _actorState.Body.Satiety.Value;
            int currentSatiety = Math.Min(100, checked(previousSatiety + CurrentContract.SatietyRestore));
            ActorBodyState replacementBody = new(_actorState.Body.ActorId, _actorState.Body.Health, new Satiety(currentSatiety), _actorState.Body.Spirit, _actorState.Body.Disease);
            SharedActorState replacementActorState = new(_actorState.Identity, replacementBody, _actorState.Traversal, replacementInventory, replacementEquipment);
            ConsumptionCommitReceipt receipt = new(new CommitOrigin.ActorAction(action.ActorId, gameActionId), CurrentContract.ContractRef, CurrentContract.Version, arguments.SourceItemTypeId, _actorState.Inventory.Version, replacementInventory.Version, previousSatiety, currentSatiety);
            _actorState = replacementActorState;
            _committedGameActionIds.Add(gameActionId);
            return new ConsumptionCommitResult(receipt);
        }
        catch (ArgumentException)
        {
            return new ConsumptionCommitResult(ConsumptionValidationFailure.CommitConstructionFailed);
        }
        catch (OverflowException)
        {
            return new ConsumptionCommitResult(ConsumptionValidationFailure.CommitConstructionFailed);
        }
        catch (InvalidOperationException)
        {
            return new ConsumptionCommitResult(ConsumptionValidationFailure.CommitConstructionFailed);
        }
    }

    private static InventoryState ConsumeFirstMatchingStack(InventoryState inventory, ItemTypeId sourceItemTypeId)
    {
        var entries = inventory.Entries.ToList();
        int index = entries.FindIndex(entry => entry is StackEntry stack && stack.ItemTypeId == sourceItemTypeId);
        if (index < 0 || entries[index] is not StackEntry sourceStack) throw new InvalidOperationException("Validated source stack is no longer available.");
        if (sourceStack.Quantity == 1) entries.RemoveAt(index);
        else entries[index] = new StackEntry(sourceStack.ItemTypeId, sourceStack.Quantity - 1);
        return new InventoryState(inventory.ActorId, entries, checked(inventory.Version + 1));
    }

    private static EquipmentState ReplaceEquipment(EquipmentState equipment, InventoryState replacementInventory, ItemTypeId sourceItemTypeId)
    {
        if (equipment.HandItemRef is StackHandItemRef held && held.ItemTypeId == sourceItemTypeId && !replacementInventory.Entries.Any(entry => entry is StackEntry stack && stack.ItemTypeId == sourceItemTypeId)) return new EquipmentState(equipment.ActorId, null, checked(equipment.Version + 1), replacementInventory);
        return new EquipmentState(equipment.ActorId, equipment.HandItemRef, equipment.Version, replacementInventory);
    }

    private static void EnsureUniqueDefinitions(IEnumerable<ItemDefinition> definitions)
    {
        var identities = new HashSet<ItemTypeId>();
        foreach (ItemDefinition definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!identities.Add(definition.ItemTypeId)) throw new ArgumentException("Item definition snapshots must be unique.", nameof(definitions));
        }
    }

    private static void EnsureUniqueInstances(IEnumerable<ItemInstance> instances)
    {
        var identities = new HashSet<ItemInstanceId>();
        foreach (ItemInstance instance in instances)
        {
            ArgumentNullException.ThrowIfNull(instance);
            if (!identities.Add(instance.ItemInstanceId)) throw new ArgumentException("Item instance snapshots must be unique.", nameof(instances));
        }
    }
}
