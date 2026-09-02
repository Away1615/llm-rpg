using Alice.Actors;
using Alice.Interaction;
using Alice.Items;

namespace Alice.Authority;

/// <summary>Transfers the one live terminal drop only after its Pickup owner is valid.</summary>
public static class TerminalWorldDropPickupAuthorityHandoff
{
    public static WorldDropPickupAuthorityRuntime Transfer(
        DamageAuthorityRuntime damageAuthority,
        PickupContract pickupContract,
        IEnumerable<SharedActorState> actorStates,
        IEnumerable<ItemDefinition> itemDefinitions)
    {
        ArgumentNullException.ThrowIfNull(damageAuthority);
        ArgumentNullException.ThrowIfNull(pickupContract);
        ArgumentNullException.ThrowIfNull(actorStates);
        ArgumentNullException.ThrowIfNull(itemDefinitions);

        World.WorldDrop worldDrop = damageAuthority.WorldDrop ?? throw new InvalidOperationException("Damage Authority has no terminal WorldDrop to transfer.");
        WorldDropPickupAuthorityRuntime destination = new(
            damageAuthority.TargetRef,
            pickupContract,
            worldDrop,
            actorStates,
            damageAuthority.ItemInstances,
            itemDefinitions);
        damageAuthority.ReleaseTerminalWorldDrop(worldDrop);
        return destination;
    }
}
