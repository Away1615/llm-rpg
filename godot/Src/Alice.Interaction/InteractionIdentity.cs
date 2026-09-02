using Alice.Identity;
using Alice.World;

namespace Alice.Interaction;

/// <summary>Identifies the one fixture contract selected for an interaction.</summary>
public sealed record ContractRef
{
    public ContractRef(TargetRef targetRef, string contractId)
    {
        ArgumentNullException.ThrowIfNull(targetRef);
        NonEmptyIdentityValue.Validate(contractId, nameof(contractId), "Contract identifier must be non-empty.");

        TargetRef = targetRef;
        ContractId = contractId;
    }

    public TargetRef TargetRef { get; }
    public string ContractId { get; }
}

/// <summary>A positive version expected by a selected binding.</summary>
public readonly record struct ExpectedContractVersion
{
    public ExpectedContractVersion(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Expected contract version must be positive.");
        }

        Value = value;
    }

    public long Value { get; }
}

/// <summary>A typed optional instrument reference.</summary>
public sealed record InstrumentRef
{
    public InstrumentRef(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Instrument reference must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}
