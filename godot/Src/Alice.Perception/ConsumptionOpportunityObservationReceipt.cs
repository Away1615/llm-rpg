using Alice.Interaction;
using Alice.Navigation;
using Alice.Npc;

namespace Alice.Perception;

public sealed class ConsumptionOpportunityObservedReceipt : IEquatable<ConsumptionOpportunityObservedReceipt>
{
    public ConsumptionOpportunityObservedReceipt(ActorVisibleTargetSpatialSnapshot targetSnapshot, KnownConsumptionOpportunity opportunity)
    {
        ArgumentNullException.ThrowIfNull(targetSnapshot);
        ArgumentNullException.ThrowIfNull(opportunity);
        if (targetSnapshot.TargetRef != opportunity.ContractRef.TargetRef) throw new ArgumentException("Observed target and opportunity ContractRef must identify the same target.", nameof(opportunity));
        TargetSnapshot = targetSnapshot;
        Opportunity = opportunity;
    }
    public ActorVisibleTargetSpatialSnapshot TargetSnapshot { get; }
    public KnownConsumptionOpportunity Opportunity { get; }
    public bool Equals(ConsumptionOpportunityObservedReceipt? other) => other is not null && TargetSnapshot == other.TargetSnapshot && Opportunity.Equals(other.Opportunity);
    public override bool Equals(object? obj) => Equals(obj as ConsumptionOpportunityObservedReceipt);
    public override int GetHashCode() => HashCode.Combine(TargetSnapshot, Opportunity);
}

public sealed class ConsumptionOpportunityUnavailableReceipt : IEquatable<ConsumptionOpportunityUnavailableReceipt>
{
    public ConsumptionOpportunityUnavailableReceipt(ContractRef contractRef, long observedVersion)
    {
        ArgumentNullException.ThrowIfNull(contractRef);
        if (observedVersion <= 0) throw new ArgumentOutOfRangeException(nameof(observedVersion));
        ContractRef = contractRef;
        ObservedVersion = observedVersion;
    }
    public ContractRef ContractRef { get; }
    public long ObservedVersion { get; }
    public bool Equals(ConsumptionOpportunityUnavailableReceipt? other) => other is not null && ContractRef == other.ContractRef && ObservedVersion == other.ObservedVersion;
    public override bool Equals(object? obj) => Equals(obj as ConsumptionOpportunityUnavailableReceipt);
    public override int GetHashCode() => HashCode.Combine(ContractRef, ObservedVersion);
}
