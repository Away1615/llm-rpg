using Alice.Actors;
using Alice.Interaction;
using Alice.World;

namespace Alice.Validation;

internal enum PickupValidationFailure
{
    ActionKindMismatch, ActorContextMismatch, ActorUnavailable, DropUnavailable, DropIdentityMismatch,
    ContractUnavailable, StaleContractVersion, OutOfRange, BindingCapabilityMismatch, InstrumentNotAllowed,
    ClaimantMismatch, CapabilitySourceUnavailable, InsufficientCapability, ItemDefinitionUnavailable,
    ItemNotStackable, InventoryFull, CheckedNumericOverflow
}

public sealed class PickupValidationResult
{
    private PickupValidationResult(ValidatedPickupCommit? commit, PickupValidationFailure? failure) { Commit = commit; Failure = failure; }
    public bool IsValid => Commit is not null;
    internal ValidatedPickupCommit? Commit { get; }
    internal PickupValidationFailure? Failure { get; }
    internal static PickupValidationResult Accepted(ValidatedPickupCommit commit) => new(commit, null);
    internal static PickupValidationResult Rejected(PickupValidationFailure failure) => new(null, failure);
}

internal sealed class ValidatedPickupCommit
{
    public ValidatedPickupCommit(SharedActorState actorState, PickupContract contract, WorldDrop drop, InventoryState replacementInventory)
    {
        ActorState = actorState; Contract = contract; Drop = drop; ReplacementInventory = replacementInventory;
    }
    public SharedActorState ActorState { get; }
    public PickupContract Contract { get; }
    public WorldDrop Drop { get; }
    public InventoryState ReplacementInventory { get; }
}
