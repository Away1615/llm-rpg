using Alice.Actors;
using Alice.Damage;
using Alice.Interaction;
using Alice.Items;
using Alice.World;

namespace Alice.Authority;

/// <summary>Immutable audit fact emitted only after a full damage commit settles.</summary>
public sealed class DamageCommitReceipt
{
    public DamageCommitReceipt(
        CommitOrigin.ActorAction origin,
        ContractRef contractRef,
        long contractVersion,
        DamageType damageType,
        int baseDamage,
        int finalDamage,
        int previousHP,
        int currentHP,
        ItemInstanceId? instrumentInstanceId,
        int? previousInstrumentDurability,
        int? currentInstrumentDurability,
        int? previousInstrumentVersion,
        int? currentInstrumentVersion,
        TerminalRepresentationId? terminalRepresentation,
        WorldDrop? worldDrop)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(contractRef);
        if (contractVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contractVersion));
        }

        if (baseDamage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseDamage));
        }

        if (finalDamage < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(finalDamage));
        }

        if (previousHP < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previousHP));
        }

        if (currentHP < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentHP));
        }

        Origin = origin;
        ContractRef = contractRef;
        ContractVersion = contractVersion;
        DamageType = damageType;
        BaseDamage = baseDamage;
        FinalDamage = finalDamage;
        PreviousHP = previousHP;
        CurrentHP = currentHP;
        InstrumentInstanceId = instrumentInstanceId;
        PreviousInstrumentDurability = previousInstrumentDurability;
        CurrentInstrumentDurability = currentInstrumentDurability;
        PreviousInstrumentVersion = previousInstrumentVersion;
        CurrentInstrumentVersion = currentInstrumentVersion;
        TerminalRepresentation = terminalRepresentation;
        WorldDrop = worldDrop;
    }

    public CommitOrigin.ActorAction Origin { get; }
    public ActorId ActorId => Origin.ActorId;
    public ContractRef ContractRef { get; }
    public long ContractVersion { get; }
    public DamageType DamageType { get; }
    public int BaseDamage { get; }
    public int FinalDamage { get; }
    public int PreviousHP { get; }
    public int CurrentHP { get; }
    public ItemInstanceId? InstrumentInstanceId { get; }
    public int? PreviousInstrumentDurability { get; }
    public int? CurrentInstrumentDurability { get; }
    public int? PreviousInstrumentVersion { get; }
    public int? CurrentInstrumentVersion { get; }
    public TerminalRepresentationId? TerminalRepresentation { get; }
    public WorldDrop? WorldDrop { get; }
}

/// <summary>One Authority attempt outcome; raw validation reasons remain internal audit data.</summary>
public sealed class DamageCommitResult
{
    internal DamageCommitResult(DamageCommitReceipt? receipt, Alice.Validation.DamageValidationFailure? failure)
    {
        Receipt = receipt;
        Failure = failure;
    }

    public DamageCommitReceipt? Receipt { get; }
    public bool IsCommitted => Receipt is not null;
    internal Alice.Validation.DamageValidationFailure? Failure { get; }
}
