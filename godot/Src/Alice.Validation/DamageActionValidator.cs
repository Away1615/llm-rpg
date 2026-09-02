using Alice.Actors;
using Alice.Capabilities;
using Alice.Damage;
using Alice.Interaction;
using Alice.Items;
using Alice.Navigation;

namespace Alice.Validation;

/// <summary>Pure current-snapshot validation for one typed damage action attempt.</summary>
public static class DamageActionValidator
{
    public static DamageValidationResult Validate(
        GameActionSpec action,
        DamageValidationContext context,
        DamageContract? currentContract,
        WorldPosition targetPosition,
        DestructibleState targetState,
        IEnumerable<ItemInstance> itemInstances,
        IEnumerable<ItemDefinition> itemDefinitions)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targetState);
        ArgumentNullException.ThrowIfNull(itemInstances);
        ArgumentNullException.ThrowIfNull(itemDefinitions);

        if (action.Arguments is not DamageActionArguments damageArguments)
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.ActionKindMismatch);
        }

        if (currentContract is null)
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.ContractUnavailable);
        }

        if (action.Binding.ContractRef != currentContract.ContractRef)
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.ContractUnavailable);
        }

        if (action.Binding.ExpectedVersion.Value != currentContract.Version)
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.StaleContractVersion);
        }

        if (targetState.CurrentHP == 0)
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.TerminalTarget);
        }

        if (action.ActorId != context.ActorState.Identity.ActorId ||
            context.ActorState.Inventory.ActorId != action.ActorId ||
            context.ActorState.Equipment.ActorId != action.ActorId)
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.ActorMismatch);
        }

        if (!WorldPositionValidation.IsFinite(context.ActorPosition) || !WorldPositionValidation.IsFinite(targetPosition))
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.InvalidSpatialEvidence);
        }

        if (!IsInRange(context.ActorPosition, targetPosition, currentContract.InteractionRange))
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.OutOfRange);
        }

        if (action.Binding.Capability != currentContract.Requirement.CapabilityIdentity)
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.BindingCapabilityMismatch);
        }

        ItemDefinition? heldDefinition;
        try
        {
            heldDefinition = HeldItemDefinitionResolver.Resolve(
                context.ActorState.Inventory,
                context.ActorState.Equipment,
                itemInstances,
                itemDefinitions);
        }
        catch (InvalidOperationException)
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.SelectedInstrumentUnavailable);
        }

        EffectiveCapabilities capabilities;
        try
        {
            capabilities = EffectiveCapabilities.Resolve(context.IntrinsicContributions, heldDefinition, context.TemporaryModifiers);
        }
        catch (OverflowException)
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.CheckedNumericOverflow);
        }

        if (capabilities.GetValue(currentContract.Requirement.CapabilityIdentity) < currentContract.Requirement.MinimumValue)
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.InsufficientCapability);
        }

        DamageContribution? source;
        ItemInstance? instrument;
        if (action.Binding.InstrumentRef is null)
        {
            source = context.IntrinsicDamageContribution;
            instrument = null;
            if (source is null)
            {
                return DamageValidationResult.Rejected(DamageValidationFailure.DamageSourceUnavailable);
            }
        }
        else
        {
            if (!TryResolveSelectedInstrument(action.Binding.InstrumentRef, context.ActorState, heldDefinition, itemInstances, out instrument, out source))
            {
                return DamageValidationResult.Rejected(DamageValidationFailure.SelectedInstrumentUnavailable);
            }
        }

        if (source is null)
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.DamageSourceUnavailable);
        }

        if (source.DamageType != damageArguments.DamageType)
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.DamageTypeMismatch);
        }

        try
        {
            int finalDamage = CalculateDamage(source, currentContract);
            return DamageValidationResult.Accepted(new ValidatedDamageCommit(action.ActorId, currentContract, source.DamageType, source.BaseDamage, finalDamage, instrument));
        }
        catch (OverflowException)
        {
            return DamageValidationResult.Rejected(DamageValidationFailure.CheckedNumericOverflow);
        }
    }

    private static bool TryResolveSelectedInstrument(
        InstrumentRef instrumentRef,
        SharedActorState actorState,
        ItemDefinition? heldDefinition,
        IEnumerable<ItemInstance> itemInstances,
        out ItemInstance? instrument,
        out DamageContribution? source)
    {
        instrument = null;
        source = null;
        if (actorState.Equipment.HandItemRef is not InstanceHandItemRef equipped ||
            !StringComparer.Ordinal.Equals(equipped.ItemInstanceId.Value, instrumentRef.Value))
        {
            return false;
        }

        ItemInstance resolvedInstance;
        try
        {
            resolvedInstance = HeldItemDefinitionResolver.FindExactlyOneInstance(equipped.ItemInstanceId, itemInstances);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (resolvedInstance.Durability is not > 0)
        {
            return false;
        }

        if (heldDefinition is null || heldDefinition.Stackability != ItemStackability.Instanced || heldDefinition.DamageContribution is null)
        {
            return false;
        }

        instrument = resolvedInstance;
        source = heldDefinition.DamageContribution;
        return true;
    }

    private static int CalculateDamage(DamageContribution source, DamageContract contract)
    {
        if (contract.Immunities.Contains(source.DamageType))
        {
            return 0;
        }

        int damage = source.BaseDamage;
        if (contract.Resistances.Contains(source.DamageType))
        {
            damage /= 2;
        }

        return contract.Vulnerabilities.Contains(source.DamageType) ? checked(damage * 2) : damage;
    }

    private static bool IsInRange(WorldPosition actorPosition, WorldPosition targetPosition, InteractionRange range)
    {
        double x = actorPosition.X - targetPosition.X;
        double y = actorPosition.Y - targetPosition.Y;
        double distance = Math.Sqrt((x * x) + (y * y));
        return distance <= range.Value;
    }
}
