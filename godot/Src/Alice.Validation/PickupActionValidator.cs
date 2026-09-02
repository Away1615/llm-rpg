using Alice.Actors;
using Alice.Capabilities;
using Alice.Interaction;
using Alice.Items;
using Alice.Navigation;
using Alice.World;

namespace Alice.Validation;

public static class PickupActionValidator
{
    public static PickupValidationResult Validate(GameActionSpec action, PickupValidationContext context, PickupContract? contract, WorldDrop? drop, IEnumerable<SharedActorState> actorStates, IEnumerable<ItemInstance> itemInstances, IEnumerable<ItemDefinition> itemDefinitions)
    {
        ArgumentNullException.ThrowIfNull(action); ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(actorStates); ArgumentNullException.ThrowIfNull(itemInstances); ArgumentNullException.ThrowIfNull(itemDefinitions);
        if (action.Arguments is not PickupActionArguments pickup) return PickupValidationResult.Rejected(PickupValidationFailure.ActionKindMismatch);
        if (action.ActorId != context.ActorId) return PickupValidationResult.Rejected(PickupValidationFailure.ActorContextMismatch);
        SharedActorState? actor = FindActor(action.ActorId, actorStates);
        if (actor is null) return PickupValidationResult.Rejected(PickupValidationFailure.ActorUnavailable);
        if (drop is null) return PickupValidationResult.Rejected(PickupValidationFailure.DropUnavailable);
        if (!pickup.WorldDropId.Equals(drop.DropId)) return PickupValidationResult.Rejected(PickupValidationFailure.DropIdentityMismatch);
        if (contract is null || action.Binding.ContractRef != contract.ContractRef) return PickupValidationResult.Rejected(PickupValidationFailure.ContractUnavailable);
        if (action.Binding.ExpectedVersion.Value != contract.Version || drop.Version != contract.Version) return PickupValidationResult.Rejected(PickupValidationFailure.StaleContractVersion);
        if (!InRange(context.ActorPosition, drop.Position, contract.InteractionRange)) return PickupValidationResult.Rejected(PickupValidationFailure.OutOfRange);
        if (action.Binding.Capability != contract.Requirement.CapabilityIdentity) return PickupValidationResult.Rejected(PickupValidationFailure.BindingCapabilityMismatch);
        if (action.Binding.InstrumentRef is not null) return PickupValidationResult.Rejected(PickupValidationFailure.InstrumentNotAllowed);
        if (contract.AccessPolicy == PickupAccessPolicy.ClaimantOnly && drop.ClaimantActorId != action.ActorId) return PickupValidationResult.Rejected(PickupValidationFailure.ClaimantMismatch);
        ItemDefinition? held;
        try { held = HeldItemDefinitionResolver.Resolve(actor.Inventory, actor.Equipment, itemInstances, itemDefinitions); }
        catch (InvalidOperationException) { return PickupValidationResult.Rejected(PickupValidationFailure.CapabilitySourceUnavailable); }
        EffectiveCapabilities capabilities;
        try { capabilities = EffectiveCapabilities.Resolve(context.IntrinsicContributions, held, context.TemporaryModifiers); }
        catch (OverflowException) { return PickupValidationResult.Rejected(PickupValidationFailure.CheckedNumericOverflow); }
        if (capabilities.GetValue(contract.Requirement.CapabilityIdentity) < contract.Requirement.MinimumValue) return PickupValidationResult.Rejected(PickupValidationFailure.InsufficientCapability);
        foreach (Alice.Damage.DestructionYield item in drop.Items)
        {
            ItemDefinition? definition = FindDefinition(item.ItemTypeId, itemDefinitions);
            if (definition is null) return PickupValidationResult.Rejected(PickupValidationFailure.ItemDefinitionUnavailable);
            if (definition.Stackability != ItemStackability.Stackable) return PickupValidationResult.Rejected(PickupValidationFailure.ItemNotStackable);
        }
        try { return PickupValidationResult.Accepted(new ValidatedPickupCommit(actor, contract, drop, Pack(actor.Inventory, drop.Items))); }
        catch (InvalidOperationException) { return PickupValidationResult.Rejected(PickupValidationFailure.InventoryFull); }
        catch (OverflowException) { return PickupValidationResult.Rejected(PickupValidationFailure.CheckedNumericOverflow); }
    }

    private static SharedActorState? FindActor(ActorId actorId, IEnumerable<SharedActorState> actors) { foreach (SharedActorState actor in actors) { ArgumentNullException.ThrowIfNull(actor); if (actor.Identity.ActorId == actorId) return actor; } return null; }
    private static ItemDefinition? FindDefinition(ItemTypeId itemTypeId, IEnumerable<ItemDefinition> definitions) { ItemDefinition? found = null; foreach (ItemDefinition definition in definitions) { ArgumentNullException.ThrowIfNull(definition); if (definition.ItemTypeId == itemTypeId) { if (found is not null) throw new InvalidOperationException(); found = definition; } } return found; }
    private static bool InRange(WorldPosition a, WorldPosition b, InteractionRange range) { double x = a.X - b.X; double y = a.Y - b.Y; return Math.Sqrt((x * x) + (y * y)) <= range.Value; }
    private static InventoryState Pack(InventoryState inventory, IReadOnlyList<Alice.Damage.DestructionYield> items)
    {
        var entries = inventory.Entries.ToList();
        foreach (Alice.Damage.DestructionYield yield in items)
        {
            int remaining = yield.Quantity;
            for (int index = 0; index < entries.Count && remaining > 0; index++) if (entries[index] is StackEntry stack && stack.ItemTypeId == yield.ItemTypeId && stack.Quantity < 10) { int add = Math.Min(10 - stack.Quantity, remaining); entries[index] = new StackEntry(stack.ItemTypeId, checked(stack.Quantity + add)); remaining -= add; }
            while (remaining > 0) { int quantity = Math.Min(10, remaining); entries.Add(new StackEntry(yield.ItemTypeId, quantity)); remaining -= quantity; }
        }
        return new InventoryState(inventory.ActorId, entries, checked(inventory.Version + 1));
    }
}
