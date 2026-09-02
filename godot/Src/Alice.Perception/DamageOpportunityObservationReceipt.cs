using Alice.Interaction;
using Alice.Navigation;
using Alice.Npc;

namespace Alice.Perception;

/// <summary>Actor-visible evidence for one observed Damage opportunity.</summary>
public sealed class DamageOpportunityObservedReceipt : IEquatable<DamageOpportunityObservedReceipt>
{
    public DamageOpportunityObservedReceipt(ActorVisibleTargetSpatialSnapshot targetSnapshot, KnownDamageOpportunity opportunity)
    {
        ArgumentNullException.ThrowIfNull(targetSnapshot);
        ArgumentNullException.ThrowIfNull(targetSnapshot.TargetRef);
        ArgumentNullException.ThrowIfNull(opportunity);
        if (!Enum.IsDefined(targetSnapshot.TargetKind))
        {
            throw new ArgumentOutOfRangeException(nameof(targetSnapshot));
        }

        if (!double.IsFinite(targetSnapshot.Position.X) || !double.IsFinite(targetSnapshot.Position.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(targetSnapshot));
        }

        if (targetSnapshot.TargetRef != opportunity.ContractRef.TargetRef)
        {
            throw new ArgumentException("Observed target and opportunity ContractRef must identify the same target.", nameof(opportunity));
        }

        TargetSnapshot = targetSnapshot;
        Opportunity = opportunity;
    }

    public ActorVisibleTargetSpatialSnapshot TargetSnapshot { get; }
    public KnownDamageOpportunity Opportunity { get; }

    public bool Equals(DamageOpportunityObservedReceipt? other)
    {
        return other is not null && TargetSnapshot == other.TargetSnapshot && Opportunity.Equals(other.Opportunity);
    }

    public override bool Equals(object? obj) => Equals(obj as DamageOpportunityObservedReceipt);

    public override int GetHashCode() => HashCode.Combine(TargetSnapshot, Opportunity);
}

/// <summary>Actor-visible evidence that one exact Damage opportunity was unavailable through a version.</summary>
public sealed class DamageOpportunityUnavailableReceipt : IEquatable<DamageOpportunityUnavailableReceipt>
{
    public DamageOpportunityUnavailableReceipt(ContractRef contractRef, long observedVersion)
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

    public bool Equals(DamageOpportunityUnavailableReceipt? other)
    {
        return other is not null && ContractRef == other.ContractRef && ObservedVersion == other.ObservedVersion;
    }

    public override bool Equals(object? obj) => Equals(obj as DamageOpportunityUnavailableReceipt);

    public override int GetHashCode() => HashCode.Combine(ContractRef, ObservedVersion);
}
