using Alice.Interaction;
using Alice.Navigation;
using Alice.Npc;

namespace Alice.Perception;

public sealed class PickupOpportunityObservedReceipt : IEquatable<PickupOpportunityObservedReceipt>
{
    public PickupOpportunityObservedReceipt(ActorVisibleTargetSpatialSnapshot targetSnapshot, KnownPickupOpportunity opportunity)
    {
        ArgumentNullException.ThrowIfNull(targetSnapshot);
        ArgumentNullException.ThrowIfNull(opportunity);
        if (targetSnapshot.TargetRef != opportunity.ContractRef.TargetRef)
        {
            throw new ArgumentException("Observed target and Pickup opportunity must identify the same target.", nameof(opportunity));
        }

        TargetSnapshot = targetSnapshot;
        Opportunity = opportunity;
    }

    public ActorVisibleTargetSpatialSnapshot TargetSnapshot { get; }
    public KnownPickupOpportunity Opportunity { get; }

    public bool Equals(PickupOpportunityObservedReceipt? other) => other is not null && TargetSnapshot == other.TargetSnapshot && Opportunity.Equals(other.Opportunity);
    public override bool Equals(object? obj) => Equals(obj as PickupOpportunityObservedReceipt);
    public override int GetHashCode() => HashCode.Combine(TargetSnapshot, Opportunity);
}

public sealed class PickupOpportunityUnavailableReceipt : IEquatable<PickupOpportunityUnavailableReceipt>
{
    public PickupOpportunityUnavailableReceipt(ContractRef contractRef, long observedVersion)
    {
        ArgumentNullException.ThrowIfNull(contractRef);
        if (observedVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(observedVersion));
        }

        ContractRef = contractRef;
        ObservedVersion = observedVersion;
    }

    public ContractRef ContractRef { get; }
    public long ObservedVersion { get; }

    public bool Equals(PickupOpportunityUnavailableReceipt? other) => other is not null && ContractRef == other.ContractRef && ObservedVersion == other.ObservedVersion;
    public override bool Equals(object? obj) => Equals(obj as PickupOpportunityUnavailableReceipt);
    public override int GetHashCode() => HashCode.Combine(ContractRef, ObservedVersion);
}
