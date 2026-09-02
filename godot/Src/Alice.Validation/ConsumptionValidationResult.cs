namespace Alice.Validation;

internal enum ConsumptionValidationFailure
{
    ActionKindMismatch,
    ActorContextMismatch,
    ActorUnavailable,
    ContractUnavailable,
    StaleContractVersion,
    InvalidSpatialEvidence,
    OutOfRange,
    BindingCapabilityMismatch,
    InstrumentNotAllowed,
    SourceItemMismatch,
    ItemDefinitionUnavailable,
    ItemNotStackable,
    InventoryQuantityUnavailable,
    CapabilitySourceUnavailable,
    InsufficientCapability,
    CheckedNumericOverflow,
    DuplicateGameActionId,
    CommitConstructionFailed
}

/// <summary>Public Consumption validation boundary; raw failure reasons remain internal audit data.</summary>
public sealed class ConsumptionValidationResult
{
    private ConsumptionValidationResult(ConsumptionValidationFailure? failure) => Failure = failure;

    public bool IsValid => Failure is null;
    internal ConsumptionValidationFailure? Failure { get; }
    internal static ConsumptionValidationResult Accepted() => new(null);
    internal static ConsumptionValidationResult Rejected(ConsumptionValidationFailure failure) => new(failure);
}
