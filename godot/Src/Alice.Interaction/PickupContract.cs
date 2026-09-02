namespace Alice.Interaction;

public enum PickupAccessPolicy
{
    ClaimantOnly,
    AnyActor
}

public sealed record PickupActionArguments : GameActionArguments
{
    public PickupActionArguments(Alice.World.WorldDropId worldDropId)
    {
        ArgumentNullException.ThrowIfNull(worldDropId);
        WorldDropId = worldDropId;
    }

    public Alice.World.WorldDropId WorldDropId { get; }
}

public sealed class PickupContract : IEquatable<PickupContract>
{
    public PickupContract(ContractRef contractRef, long version, InteractionRange interactionRange, InteractionCapabilityRequirement requirement, PickupAccessPolicy accessPolicy)
    {
        ArgumentNullException.ThrowIfNull(contractRef);
        ArgumentNullException.ThrowIfNull(requirement);
        if (version <= 0 || !Enum.IsDefined(accessPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        ContractRef = contractRef;
        Version = version;
        InteractionRange = interactionRange;
        Requirement = requirement;
        AccessPolicy = accessPolicy;
    }

    public ContractRef ContractRef { get; }
    public long Version { get; }
    public InteractionRange InteractionRange { get; }
    public InteractionCapabilityRequirement Requirement { get; }
    public PickupAccessPolicy AccessPolicy { get; }
    public bool Equals(PickupContract? other) => other is not null && ContractRef == other.ContractRef && Version == other.Version && InteractionRange == other.InteractionRange && Requirement.Equals(other.Requirement) && AccessPolicy == other.AccessPolicy;
    public override bool Equals(object? obj) => Equals(obj as PickupContract);
    public override int GetHashCode() => HashCode.Combine(ContractRef, Version, InteractionRange, Requirement, AccessPolicy);
}
