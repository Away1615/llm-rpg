using Alice.Capabilities;

namespace Alice.Interaction;

/// <summary>The exact contract version, capability and optional instrument selected by an actor.</summary>
public sealed record InteractionBinding
{
    public InteractionBinding(
        ContractRef contractRef,
        ExpectedContractVersion expectedVersion,
        CapabilityIdentity capability,
        InstrumentRef? instrumentRef)
    {
        ArgumentNullException.ThrowIfNull(contractRef);
        ArgumentNullException.ThrowIfNull(capability);
        if (expectedVersion.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        ContractRef = contractRef;
        ExpectedVersion = expectedVersion;
        Capability = capability;
        InstrumentRef = instrumentRef;
    }

    public ContractRef ContractRef { get; }
    public ExpectedContractVersion ExpectedVersion { get; }
    public CapabilityIdentity Capability { get; }
    public InstrumentRef? InstrumentRef { get; }
}
