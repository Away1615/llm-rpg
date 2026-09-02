using Alice.Actors;
using Alice.Damage;
using Alice.Interaction;
using Alice.Items;

namespace Alice.Validation;

internal enum DamageValidationFailure
{
    ActionKindMismatch,
    ActorMismatch,
    ContractUnavailable,
    StaleContractVersion,
    TerminalTarget,
    InvalidSpatialEvidence,
    OutOfRange,
    BindingCapabilityMismatch,
    InsufficientCapability,
    SelectedInstrumentUnavailable,
    DamageSourceUnavailable,
    DamageTypeMismatch,
    CheckedNumericOverflow
}

/// <summary>Validator outcome; its successful commit value cannot be constructed by callers.</summary>
public sealed class DamageValidationResult
{
    private DamageValidationResult(ValidatedDamageCommit? commit, DamageValidationFailure? failure)
    {
        Commit = commit;
        Failure = failure;
    }

    public bool IsValid => Commit is not null;

    internal ValidatedDamageCommit? Commit { get; }
    internal DamageValidationFailure? Failure { get; }

    internal static DamageValidationResult Accepted(ValidatedDamageCommit commit) => new(commit, null);
    internal static DamageValidationResult Rejected(DamageValidationFailure failure) => new(null, failure);
}

internal sealed class ValidatedDamageCommit
{
    public ValidatedDamageCommit(
        ActorId actorId,
        DamageContract contract,
        DamageType damageType,
        int baseDamage,
        int finalDamage,
        ItemInstance? instrument)
    {
        ActorId = actorId;
        Contract = contract;
        DamageType = damageType;
        BaseDamage = baseDamage;
        FinalDamage = finalDamage;
        Instrument = instrument;
    }

    public ActorId ActorId { get; }
    public DamageContract Contract { get; }
    public DamageType DamageType { get; }
    public int BaseDamage { get; }
    public int FinalDamage { get; }
    public ItemInstance? Instrument { get; }
}
