using Alice.Actors;

namespace Alice.Items;

/// <summary>Pure resolver for the one current hand reference and supplied item snapshots.</summary>
public static class HeldItemDefinitionResolver
{
    public static ItemDefinition? Resolve(
        InventoryState inventoryState,
        EquipmentState equipmentState,
        IEnumerable<ItemInstance> itemInstances,
        IEnumerable<ItemDefinition> itemDefinitions)
    {
        ArgumentNullException.ThrowIfNull(inventoryState);
        ArgumentNullException.ThrowIfNull(equipmentState);
        ArgumentNullException.ThrowIfNull(itemInstances);
        ArgumentNullException.ThrowIfNull(itemDefinitions);
        if (inventoryState.ActorId != equipmentState.ActorId)
        {
            throw new ArgumentException("Inventory and equipment must belong to the same actor.", nameof(equipmentState));
        }

        HandItemRef? handItemRef = equipmentState.HandItemRef;
        if (handItemRef is null)
        {
            return null;
        }

        return handItemRef switch
        {
            StackHandItemRef stackHandItemRef => ResolveStackHand(inventoryState, stackHandItemRef, itemDefinitions),
            InstanceHandItemRef instanceHandItemRef => ResolveInstanceHand(inventoryState, instanceHandItemRef, itemInstances, itemDefinitions),
            _ => throw new InvalidOperationException("Unsupported hand item reference.")
        };
    }

    private static ItemDefinition ResolveStackHand(
        InventoryState inventoryState,
        StackHandItemRef handItemRef,
        IEnumerable<ItemDefinition> itemDefinitions)
    {
        if (!ContainsStackEntry(inventoryState.Entries, handItemRef.ItemTypeId))
        {
            throw new InvalidOperationException("Hand stack reference is no longer carried.");
        }

        ItemDefinition definition = FindExactlyOneDefinition(handItemRef.ItemTypeId, itemDefinitions);
        if (definition.Stackability != ItemStackability.Stackable)
        {
            throw new InvalidOperationException("Held stack reference requires a stackable definition.");
        }

        return definition;
    }

    private static ItemDefinition ResolveInstanceHand(
        InventoryState inventoryState,
        InstanceHandItemRef handItemRef,
        IEnumerable<ItemInstance> itemInstances,
        IEnumerable<ItemDefinition> itemDefinitions)
    {
        if (!ContainsInstanceEntry(inventoryState.Entries, handItemRef.ItemInstanceId))
        {
            throw new InvalidOperationException("Hand instance reference is no longer carried.");
        }

        ItemInstance instance = FindExactlyOneInstance(handItemRef.ItemInstanceId, itemInstances);
        ItemDefinition definition = FindExactlyOneDefinition(instance.ItemTypeId, itemDefinitions);
        if (definition.Stackability != ItemStackability.Instanced)
        {
            throw new InvalidOperationException("Held instance reference requires an instanced definition.");
        }

        return definition;
    }

    internal static ItemInstance FindExactlyOneInstance(ItemInstanceId itemInstanceId, IEnumerable<ItemInstance> itemInstances)
    {
        ItemInstance? match = null;
        foreach (ItemInstance instance in itemInstances)
        {
            ArgumentNullException.ThrowIfNull(instance);
            if (instance.ItemInstanceId != itemInstanceId)
            {
                continue;
            }

            if (match is not null)
            {
                throw new InvalidOperationException("Held instance data must have exactly one matching snapshot.");
            }

            match = instance;
        }

        return match ?? throw new InvalidOperationException("Held instance data must have exactly one matching snapshot.");
    }

    private static ItemDefinition FindExactlyOneDefinition(ItemTypeId itemTypeId, IEnumerable<ItemDefinition> itemDefinitions)
    {
        ItemDefinition? match = null;
        foreach (ItemDefinition definition in itemDefinitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (definition.ItemTypeId != itemTypeId)
            {
                continue;
            }

            if (match is not null)
            {
                throw new InvalidOperationException("Held item type must have exactly one matching definition.");
            }

            match = definition;
        }

        return match ?? throw new InvalidOperationException("Held item type must have exactly one matching definition.");
    }

    private static bool ContainsStackEntry(IReadOnlyList<InventoryEntry> entries, ItemTypeId itemTypeId)
    {
        foreach (InventoryEntry entry in entries)
        {
            if (entry is StackEntry stackEntry && stackEntry.ItemTypeId == itemTypeId)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsInstanceEntry(IReadOnlyList<InventoryEntry> entries, ItemInstanceId itemInstanceId)
    {
        foreach (InventoryEntry entry in entries)
        {
            if (entry is InstanceEntry instanceEntry && instanceEntry.ItemInstanceId == itemInstanceId)
            {
                return true;
            }
        }

        return false;
    }
}
