namespace Alice.Actors;

/// <summary>Immutable controller-neutral composition of one Actor's accepted Shared Actor components.</summary>
public sealed class SharedActorState : IEquatable<SharedActorState>
{
    public SharedActorState(
        ActorIdentity identity,
        ActorBodyState body,
        ActorTraversalState traversal,
        InventoryState inventory,
        EquipmentState equipment)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(equipment);

        EnsureSameActor(identity.ActorId, body.ActorId, nameof(body));
        EnsureSameActor(identity.ActorId, traversal.ActorId, nameof(traversal));
        EnsureSameActor(identity.ActorId, inventory.ActorId, nameof(inventory));
        EnsureSameActor(identity.ActorId, equipment.ActorId, nameof(equipment));

        Identity = identity;
        Body = body;
        Traversal = traversal;
        Inventory = inventory;
        Equipment = equipment;
    }

    public ActorIdentity Identity { get; }
    public ActorBodyState Body { get; }
    public ActorTraversalState Traversal { get; }
    public InventoryState Inventory { get; }
    public EquipmentState Equipment { get; }

    public bool Equals(SharedActorState? other)
    {
        return other is not null &&
            Identity == other.Identity &&
            Body == other.Body &&
            Traversal == other.Traversal &&
            Inventory.Equals(other.Inventory) &&
            Equipment == other.Equipment;
    }

    public override bool Equals(object? obj) => Equals(obj as SharedActorState);

    public override int GetHashCode() => HashCode.Combine(Identity, Body, Traversal, Inventory, Equipment);

    private static void EnsureSameActor(ActorId identityActorId, ActorId componentActorId, string parameterName)
    {
        if (componentActorId != identityActorId)
        {
            throw new ArgumentException("Shared Actor components must belong to the identity ActorId.", parameterName);
        }
    }
}
