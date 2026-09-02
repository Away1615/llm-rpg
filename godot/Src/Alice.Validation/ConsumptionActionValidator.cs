using Alice.Actors;
using Alice.Capabilities;
using Alice.Interaction;
using Alice.Items;
using Alice.Navigation;

namespace Alice.Validation;

/// <summary>Pure exact-current validation for the one typed Consumption family.</summary>
public static class ConsumptionActionValidator
{
    public static ConsumptionValidationResult Validate(GameActionSpec action, ConsumptionValidationContext context, ConsumptionContract? contract, SharedActorState? actorState, WorldPosition targetPosition, IEnumerable<ItemInstance> itemInstances, IEnumerable<ItemDefinition> itemDefinitions)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(itemInstances);
        ArgumentNullException.ThrowIfNull(itemDefinitions);

        if (action.Arguments is not ConsumptionActionArguments arguments) return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.ActionKindMismatch);
        if (action.ActorId != context.ActorId) return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.ActorContextMismatch);
        if (actorState is null || actorState.Identity.ActorId != action.ActorId) return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.ActorUnavailable);
        if (contract is null || action.Binding.ContractRef != contract.ContractRef) return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.ContractUnavailable);
        if (action.Binding.ExpectedVersion.Value != contract.Version) return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.StaleContractVersion);
        if (!WorldPositionValidation.IsFinite(targetPosition) || !WorldPositionValidation.IsFinite(context.ActorPosition)) return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.InvalidSpatialEvidence);
        if (!InRange(context.ActorPosition, targetPosition, contract.InteractionRange)) return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.OutOfRange);
        if (action.Binding.Capability != contract.Requirement.CapabilityIdentity) return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.BindingCapabilityMismatch);
        if (action.Binding.InstrumentRef is not null) return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.InstrumentNotAllowed);
        if (arguments.SourceItemTypeId != contract.SourceItemTypeId) return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.SourceItemMismatch);

        ItemDefinition? sourceDefinition = FindDefinition(contract.SourceItemTypeId, itemDefinitions);
        if (sourceDefinition is null) return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.ItemDefinitionUnavailable);
        if (sourceDefinition.Stackability != ItemStackability.Stackable) return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.ItemNotStackable);
        if (!ContainsSourceStack(actorState.Inventory, contract.SourceItemTypeId)) return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.InventoryQuantityUnavailable);

        ItemDefinition? heldDefinition;
        try
        {
            heldDefinition = HeldItemDefinitionResolver.Resolve(actorState.Inventory, actorState.Equipment, itemInstances, itemDefinitions);
        }
        catch (InvalidOperationException)
        {
            return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.CapabilitySourceUnavailable);
        }

        try
        {
            EffectiveCapabilities capabilities = EffectiveCapabilities.Resolve(context.IntrinsicContributions, heldDefinition, context.TemporaryModifiers);
            if (capabilities.GetValue(contract.Requirement.CapabilityIdentity) < contract.Requirement.MinimumValue) return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.InsufficientCapability);
            _ = checked(actorState.Inventory.Version + 1);
            if (WillClearHeldSource(actorState, contract.SourceItemTypeId)) _ = checked(actorState.Equipment.Version + 1);
            _ = checked(actorState.Body.Satiety.Value + contract.SatietyRestore);
        }
        catch (OverflowException)
        {
            return ConsumptionValidationResult.Rejected(ConsumptionValidationFailure.CheckedNumericOverflow);
        }

        return ConsumptionValidationResult.Accepted();
    }

    private static ItemDefinition? FindDefinition(ItemTypeId itemTypeId, IEnumerable<ItemDefinition> definitions)
    {
        ItemDefinition? found = null;
        foreach (ItemDefinition definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (definition.ItemTypeId != itemTypeId) continue;
            if (found is not null) return null;
            found = definition;
        }

        return found;
    }

    private static bool ContainsSourceStack(InventoryState inventory, ItemTypeId sourceItemTypeId) => inventory.Entries.Any(entry => entry is StackEntry stack && stack.ItemTypeId == sourceItemTypeId);

    private static bool WillClearHeldSource(SharedActorState actorState, ItemTypeId sourceItemTypeId)
    {
        if (actorState.Equipment.HandItemRef is not StackHandItemRef held || held.ItemTypeId != sourceItemTypeId) return false;
        StackEntry[] matchingStacks = actorState.Inventory.Entries.OfType<StackEntry>().Where(stack => stack.ItemTypeId == sourceItemTypeId).ToArray();
        return matchingStacks.Length == 1 && matchingStacks[0].Quantity == 1;
    }

    private static bool InRange(WorldPosition left, WorldPosition right, InteractionRange range)
    {
        double horizontal = left.X - right.X;
        double vertical = left.Y - right.Y;
        return Math.Sqrt((horizontal * horizontal) + (vertical * vertical)) <= range.Value;
    }
}
