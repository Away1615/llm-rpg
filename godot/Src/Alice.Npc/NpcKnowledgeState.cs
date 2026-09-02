using System.Collections.ObjectModel;
using Alice.Commitments;
using Alice.Cognition;
using Alice.Interaction;
using Alice.Navigation;
using Alice.World;
using Alice.Items;
using Alice.Capabilities;
using Alice.Perception;

namespace Alice.Npc;



public sealed class NpcKnowledgeState : IEquatable<NpcKnowledgeState>
{
    private readonly ReadOnlyCollection<KnownAttendanceDestination> _knownAttendanceDestinations;

    public NpcKnowledgeState(
        NpcKnownTargetSpatialState knownTargets,
        NpcKnownOpportunityState knownOpportunities,
        IEnumerable<KnownAttendanceDestination>? knownAttendanceDestinations = null)
    {
        ArgumentNullException.ThrowIfNull(knownTargets);
        ArgumentNullException.ThrowIfNull(knownOpportunities);
        KnownAttendanceDestination[] attendanceSnapshot = knownAttendanceDestinations?.ToArray() ?? [];
        var commitmentIds = new HashSet<CommitmentId>();
        foreach (KnownAttendanceDestination destination in attendanceSnapshot)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (!commitmentIds.Add(destination.CommitmentId))
            {
                throw new ArgumentException("Known attendance destinations must have unique exact CommitmentIds.", nameof(knownAttendanceDestinations));
            }
        }

        Array.Sort(attendanceSnapshot, AttendanceDestinationComparer.Instance);
        KnownTargets = knownTargets;
        KnownOpportunities = knownOpportunities;
        _knownAttendanceDestinations = Array.AsReadOnly(attendanceSnapshot);
    }

    public NpcKnownTargetSpatialState KnownTargets { get; }
    public NpcKnownOpportunityState KnownOpportunities { get; }
    public IReadOnlyList<KnownAttendanceDestination> KnownAttendanceDestinations => _knownAttendanceDestinations;

    public bool TryResolveAttendanceDestination(CommitmentId commitmentId, out KnownAttendanceDestination? destination)
    {
        AttendancePlanningIdentity.Validate(commitmentId.Value, nameof(commitmentId));
        foreach (KnownAttendanceDestination candidate in KnownAttendanceDestinations)
        {
            if (candidate.CommitmentId == commitmentId)
            {
                destination = candidate;
                return true;
            }
        }

        destination = null;
        return false;
    }

    public bool Equals(NpcKnowledgeState? other) => other is not null && KnownTargets.Equals(other.KnownTargets) && KnownOpportunities.Equals(other.KnownOpportunities) && KnownAttendanceDestinations.SequenceEqual(other.KnownAttendanceDestinations);
    public override bool Equals(object? obj) => Equals(obj as NpcKnowledgeState);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(KnownTargets);
        hash.Add(KnownOpportunities);
        foreach (KnownAttendanceDestination destination in KnownAttendanceDestinations)
        {
            hash.Add(destination);
        }

        return hash.ToHashCode();
    }

    private sealed class AttendanceDestinationComparer : IComparer<KnownAttendanceDestination>
    {
        public static AttendanceDestinationComparer Instance { get; } = new();

        public int Compare(KnownAttendanceDestination? left, KnownAttendanceDestination? right) =>
            StringComparer.Ordinal.Compare(left?.CommitmentId.Value, right?.CommitmentId.Value);
    }
}



public sealed record KnowledgeFactRef
{
    private KnowledgeFactRef(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static KnowledgeFactRef ForProblemDescriptor(DecisionProblemDescriptorHash problemDescriptorHash)
    {
        ArgumentNullException.ThrowIfNull(problemDescriptorHash);
        return new KnowledgeFactRef("problem_descriptor:" + problemDescriptorHash.Value);
    }
}



/// <summary>Immutable independent catalogues of actor-known interaction opportunities.</summary>
public sealed class NpcKnownOpportunityState : IEquatable<NpcKnownOpportunityState>
{
    private readonly ReadOnlyCollection<KnownDamageOpportunity> _damageOpportunities;
    private readonly ReadOnlyCollection<KnownConsumptionOpportunity> _consumptionOpportunities;
    private readonly ReadOnlyCollection<KnownPickupOpportunity> _pickupOpportunities;
    private readonly ReadOnlyCollection<KnownResourceYieldOpportunity> _resourceYieldOpportunities;

    public NpcKnownOpportunityState(IEnumerable<KnownDamageOpportunity> damageOpportunities) : this(damageOpportunities, []) { }
    public NpcKnownOpportunityState(IEnumerable<KnownDamageOpportunity> damageOpportunities, IEnumerable<KnownConsumptionOpportunity> consumptionOpportunities) : this(damageOpportunities, consumptionOpportunities, []) { }
    public NpcKnownOpportunityState(
        IEnumerable<KnownDamageOpportunity> damageOpportunities,
        IEnumerable<KnownConsumptionOpportunity> consumptionOpportunities,
        IEnumerable<KnownPickupOpportunity> pickupOpportunities,
        IEnumerable<KnownResourceYieldOpportunity>? resourceYieldOpportunities = null)
    {
        ArgumentNullException.ThrowIfNull(damageOpportunities);
        ArgumentNullException.ThrowIfNull(consumptionOpportunities);
        ArgumentNullException.ThrowIfNull(pickupOpportunities);
        KnownResourceYieldOpportunity[] resourceYields = resourceYieldOpportunities?.ToArray() ?? [];
        _damageOpportunities = Array.AsReadOnly(Canonicalize(damageOpportunities, nameof(damageOpportunities)));
        _consumptionOpportunities = Array.AsReadOnly(Canonicalize(consumptionOpportunities, nameof(consumptionOpportunities)));
        _pickupOpportunities = Array.AsReadOnly(Canonicalize(pickupOpportunities, nameof(pickupOpportunities)));
        _resourceYieldOpportunities = Array.AsReadOnly(Canonicalize(resourceYields, nameof(resourceYieldOpportunities)));
    }

    public IReadOnlyList<KnownDamageOpportunity> DamageOpportunities => _damageOpportunities;
    public IReadOnlyList<KnownConsumptionOpportunity> ConsumptionOpportunities => _consumptionOpportunities;
    public IReadOnlyList<KnownPickupOpportunity> PickupOpportunities => _pickupOpportunities;
    public IReadOnlyList<KnownResourceYieldOpportunity> ResourceYieldOpportunities => _resourceYieldOpportunities;
    public bool TryResolveDamage(ContractRef contractRef, out KnownDamageOpportunity? opportunity) => TryResolve(DamageOpportunities, contractRef, out opportunity);
    public bool TryResolveConsumption(ContractRef contractRef, out KnownConsumptionOpportunity? opportunity) => TryResolve(ConsumptionOpportunities, contractRef, out opportunity);
    public bool TryResolvePickup(ContractRef contractRef, out KnownPickupOpportunity? opportunity) => TryResolve(PickupOpportunities, contractRef, out opportunity);
    public bool TryResolveResourceYield(ContractRef contractRef, out KnownResourceYieldOpportunity? opportunity) => TryResolve(ResourceYieldOpportunities, contractRef, out opportunity);
    public bool Equals(NpcKnownOpportunityState? other) => other is not null && DamageOpportunities.SequenceEqual(other.DamageOpportunities) && ConsumptionOpportunities.SequenceEqual(other.ConsumptionOpportunities) && PickupOpportunities.SequenceEqual(other.PickupOpportunities) && ResourceYieldOpportunities.SequenceEqual(other.ResourceYieldOpportunities);
    public override bool Equals(object? obj) => Equals(obj as NpcKnownOpportunityState);
    public override int GetHashCode() { var hash = new HashCode(); foreach (KnownDamageOpportunity value in DamageOpportunities) hash.Add(value); foreach (KnownConsumptionOpportunity value in ConsumptionOpportunities) hash.Add(value); foreach (KnownPickupOpportunity value in PickupOpportunities) hash.Add(value); foreach (KnownResourceYieldOpportunity value in ResourceYieldOpportunities) hash.Add(value); return hash.ToHashCode(); }

    private static T[] Canonicalize<T>(IEnumerable<T> values, string parameterName) where T : class
    {
        T[] snapshot = values.ToArray();
        var references = new HashSet<ContractRef>();
        foreach (T value in snapshot)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!references.Add(GetContractRef(value))) throw new ArgumentException("Known opportunities must have unique ContractRefs within their family.", parameterName);
        }
        Array.Sort(snapshot, OpportunityComparer<T>.Instance);
        return snapshot;
    }

    private static bool TryResolve<T>(IReadOnlyList<T> opportunities, ContractRef contractRef, out T? opportunity) where T : class
    {
        ArgumentNullException.ThrowIfNull(contractRef);
        foreach (T candidate in opportunities) if (GetContractRef(candidate) == contractRef) { opportunity = candidate; return true; }
        opportunity = null;
        return false;
    }

    private static ContractRef GetContractRef(object value) => value switch { KnownDamageOpportunity damage => damage.ContractRef, KnownConsumptionOpportunity consumption => consumption.ContractRef, KnownPickupOpportunity pickup => pickup.ContractRef, KnownResourceYieldOpportunity resourceYield => resourceYield.ContractRef, _ => throw new ArgumentException("Opportunity family is outside this slice.") };
    private sealed class OpportunityComparer<T> : IComparer<T> where T : class
    {
        public static OpportunityComparer<T> Instance { get; } = new();
        public int Compare(T? left, T? right)
        {
            ContractRef? leftRef = left is null ? null : GetContractRef(left);
            ContractRef? rightRef = right is null ? null : GetContractRef(right);
            int target = StringComparer.Ordinal.Compare(leftRef?.TargetRef.Value, rightRef?.TargetRef.Value);
            return target != 0 ? target : StringComparer.Ordinal.Compare(leftRef?.ContractId, rightRef?.ContractId);
        }
    }
}



public sealed class NpcKnownTargetSpatialState : IActorVisibleTargetSpatialQuery, IEquatable<NpcKnownTargetSpatialState>
{
    private readonly ReadOnlyCollection<ActorVisibleTargetSpatialSnapshot> _snapshots;
    public NpcKnownTargetSpatialState(IEnumerable<ActorVisibleTargetSpatialSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ActorVisibleTargetSpatialSnapshot[] copy = snapshots.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ActorVisibleTargetSpatialSnapshot snapshot in copy)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(snapshot.TargetRef);
            if (!Enum.IsDefined(snapshot.TargetKind) || !double.IsFinite(snapshot.Position.X) || !double.IsFinite(snapshot.Position.Y) || !ids.Add(snapshot.TargetRef.Value)) throw new ArgumentException("Known target snapshots must be valid and unique.", nameof(snapshots));
        }
        Array.Sort(copy, SnapshotComparer.Instance);
        _snapshots = Array.AsReadOnly(copy);
    }
    public IReadOnlyList<ActorVisibleTargetSpatialSnapshot> Snapshots => _snapshots;
    public bool TryResolve(TargetRef targetRef, out ActorVisibleTargetSpatialSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(targetRef);
        foreach (ActorVisibleTargetSpatialSnapshot candidate in _snapshots) if (candidate.TargetRef == targetRef) { snapshot = candidate; return true; }
        snapshot = null; return false;
    }
    public bool Equals(NpcKnownTargetSpatialState? other) => other is not null && Snapshots.SequenceEqual(other.Snapshots);
    public override bool Equals(object? obj) => Equals(obj as NpcKnownTargetSpatialState);
    public override int GetHashCode() { var hash = new HashCode(); foreach (var snapshot in Snapshots) hash.Add(snapshot); return hash.ToHashCode(); }
    private sealed class SnapshotComparer : IComparer<ActorVisibleTargetSpatialSnapshot> { public static SnapshotComparer Instance { get; } = new(); public int Compare(ActorVisibleTargetSpatialSnapshot? a, ActorVisibleTargetSpatialSnapshot? b) => StringComparer.Ordinal.Compare(a?.TargetRef.Value, b?.TargetRef.Value); }
}

public sealed class KnownPickupOpportunity : IEquatable<KnownPickupOpportunity>
{
 private readonly ReadOnlyCollection<KnownDestructionYield> _items;
 public KnownPickupOpportunity(ContractRef contractRef,long observedVersion,InteractionRange range,KnownCapabilityRequirement requirement,WorldDropId worldDropId,IEnumerable<KnownDestructionYield> believedItems){ArgumentNullException.ThrowIfNull(contractRef);ArgumentNullException.ThrowIfNull(requirement);ArgumentNullException.ThrowIfNull(worldDropId);ArgumentNullException.ThrowIfNull(believedItems);if(observedVersion<=0)throw new ArgumentOutOfRangeException(nameof(observedVersion));var a=believedItems.ToArray();var ids=new HashSet<string>(StringComparer.Ordinal);foreach(var x in a){ArgumentNullException.ThrowIfNull(x);if(!ids.Add(x.ItemTypeId.Value))throw new ArgumentException("Duplicate believed item.");}Array.Sort(a,(x,y)=>StringComparer.Ordinal.Compare(x.ItemTypeId.Value,y.ItemTypeId.Value));ContractRef=contractRef;ObservedVersion=observedVersion;BelievedInteractionRange=range;BelievedRequirement=requirement;WorldDropId=worldDropId;_items=Array.AsReadOnly(a);}
 public ContractRef ContractRef{get;} public long ObservedVersion{get;} public InteractionRange BelievedInteractionRange{get;} public KnownCapabilityRequirement BelievedRequirement{get;} public WorldDropId WorldDropId{get;} public IReadOnlyList<KnownDestructionYield> BelievedItems=>_items;
 public bool Equals(KnownPickupOpportunity? o)=>o is not null&&ContractRef==o.ContractRef&&ObservedVersion==o.ObservedVersion&&BelievedInteractionRange==o.BelievedInteractionRange&&BelievedRequirement.Equals(o.BelievedRequirement)&&WorldDropId==o.WorldDropId&&BelievedItems.SequenceEqual(o.BelievedItems);public override bool Equals(object? o)=>Equals(o as KnownPickupOpportunity);public override int GetHashCode(){var h=new HashCode();h.Add(ContractRef);h.Add(ObservedVersion);h.Add(BelievedInteractionRange);h.Add(BelievedRequirement);h.Add(WorldDropId);foreach(var x in BelievedItems)h.Add(x);return h.ToHashCode();}
}



/// <summary>Immutable actor-local belief about one observed Consumption Contract.</summary>
public sealed class KnownConsumptionOpportunity : IEquatable<KnownConsumptionOpportunity>
{
    public KnownConsumptionOpportunity(ContractRef contractRef, long observedVersion, InteractionRange believedInteractionRange, KnownCapabilityRequirement believedRequirement, ItemTypeId sourceItemTypeId, int quantity, int believedSatietyRestore)
    {
        ArgumentNullException.ThrowIfNull(contractRef);
        ArgumentNullException.ThrowIfNull(believedRequirement);
        ArgumentNullException.ThrowIfNull(sourceItemTypeId);
        if (observedVersion <= 0 || quantity != 1 || believedSatietyRestore <= 0) throw new ArgumentOutOfRangeException(nameof(observedVersion));
        ContractRef = contractRef;
        ObservedVersion = observedVersion;
        BelievedInteractionRange = believedInteractionRange;
        BelievedRequirement = believedRequirement;
        SourceItemTypeId = sourceItemTypeId;
        Quantity = quantity;
        BelievedSatietyRestore = believedSatietyRestore;
    }

    public ContractRef ContractRef { get; }
    public long ObservedVersion { get; }
    public InteractionRange BelievedInteractionRange { get; }
    public KnownCapabilityRequirement BelievedRequirement { get; }
    public ItemTypeId SourceItemTypeId { get; }
    public int Quantity { get; }
    public int BelievedSatietyRestore { get; }
    public bool Equals(KnownConsumptionOpportunity? other) => other is not null && ContractRef == other.ContractRef && ObservedVersion == other.ObservedVersion && BelievedInteractionRange == other.BelievedInteractionRange && BelievedRequirement.Equals(other.BelievedRequirement) && SourceItemTypeId == other.SourceItemTypeId && Quantity == other.Quantity && BelievedSatietyRestore == other.BelievedSatietyRestore;
    public override bool Equals(object? obj) => Equals(obj as KnownConsumptionOpportunity);
    public override int GetHashCode() => HashCode.Combine(ContractRef, ObservedVersion, BelievedInteractionRange, BelievedRequirement, SourceItemTypeId, Quantity, BelievedSatietyRestore);
}



/// <summary>Actor-visible knowledge that one resource source can create a claimant WorldDrop.</summary>
public sealed class KnownResourceYieldOpportunity : IEquatable<KnownResourceYieldOpportunity>
{
    private readonly ReadOnlyCollection<KnownDestructionYield> _believedYields;

    public KnownResourceYieldOpportunity(
        ContractRef contractRef,
        long observedVersion,
        InteractionRange interactionRange,
        KnownCapabilityRequirement requirement,
        IEnumerable<KnownDestructionYield> believedYields,
        WorldDropId worldDropId)
    {
        ArgumentNullException.ThrowIfNull(contractRef);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(believedYields);
        ArgumentNullException.ThrowIfNull(worldDropId);
        if (observedVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(observedVersion));
        }

        KnownDestructionYield[] yields = believedYields.ToArray();
        if (yields.Length == 0 || yields.Any(value => value is null))
        {
            throw new ArgumentException("A resource opportunity requires at least one known yield.", nameof(believedYields));
        }

        var itemTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (KnownDestructionYield yield in yields)
        {
            if (!itemTypes.Add(yield.ItemTypeId.Value))
            {
                throw new ArgumentException("Known resource yields must use unique item types.", nameof(believedYields));
            }
        }

        Array.Sort(yields, ResourceYieldComparer.Instance);
        ContractRef = contractRef;
        ObservedVersion = observedVersion;
        BelievedInteractionRange = interactionRange;
        BelievedRequirement = requirement;
        WorldDropId = worldDropId;
        _believedYields = Array.AsReadOnly(yields);
    }

    public ContractRef ContractRef { get; }
    public long ObservedVersion { get; }
    public InteractionRange BelievedInteractionRange { get; }
    public KnownCapabilityRequirement BelievedRequirement { get; }
    public WorldDropId WorldDropId { get; }
    public IReadOnlyList<KnownDestructionYield> BelievedYields => _believedYields;

    public bool Equals(KnownResourceYieldOpportunity? other) =>
        other is not null
        && ContractRef == other.ContractRef
        && ObservedVersion == other.ObservedVersion
        && BelievedInteractionRange == other.BelievedInteractionRange
        && BelievedRequirement.Equals(other.BelievedRequirement)
        && WorldDropId.Equals(other.WorldDropId)
        && BelievedYields.SequenceEqual(other.BelievedYields);

    public override bool Equals(object? obj) => Equals(obj as KnownResourceYieldOpportunity);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ContractRef);
        hash.Add(ObservedVersion);
        hash.Add(BelievedInteractionRange);
        hash.Add(BelievedRequirement);
        hash.Add(WorldDropId);
        foreach (KnownDestructionYield yield in BelievedYields)
        {
            hash.Add(yield);
        }
        return hash.ToHashCode();
    }

    private sealed class ResourceYieldComparer : IComparer<KnownDestructionYield>
    {
        public static ResourceYieldComparer Instance { get; } = new();

        public int Compare(KnownDestructionYield? left, KnownDestructionYield? right) =>
            StringComparer.Ordinal.Compare(left?.ItemTypeId.Value, right?.ItemTypeId.Value);
    }
}



/// <summary>An NPC's believed numeric capability requirement for a known damage opportunity.</summary>
public sealed class KnownCapabilityRequirement : IEquatable<KnownCapabilityRequirement>
{
    public KnownCapabilityRequirement(CapabilityIdentity capabilityIdentity, int minimumValue)
    {
        ArgumentNullException.ThrowIfNull(capabilityIdentity);
        if (minimumValue <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumValue));
        }

        CapabilityIdentity = capabilityIdentity;
        MinimumValue = minimumValue;
    }

    public CapabilityIdentity CapabilityIdentity { get; }
    public int MinimumValue { get; }

    public bool Equals(KnownCapabilityRequirement? other)
    {
        return other is not null && CapabilityIdentity == other.CapabilityIdentity && MinimumValue == other.MinimumValue;
    }

    public override bool Equals(object? obj) => Equals(obj as KnownCapabilityRequirement);

    public override int GetHashCode() => HashCode.Combine(CapabilityIdentity, MinimumValue);
}

/// <summary>An NPC's believed possible terminal item yield for a known damage opportunity.</summary>
public sealed class KnownDestructionYield : IEquatable<KnownDestructionYield>
{
    public KnownDestructionYield(ItemTypeId itemTypeId, int quantity)
    {
        ArgumentNullException.ThrowIfNull(itemTypeId);
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        ItemTypeId = itemTypeId;
        Quantity = quantity;
    }

    public ItemTypeId ItemTypeId { get; }
    public int Quantity { get; }

    public bool Equals(KnownDestructionYield? other)
    {
        return other is not null && ItemTypeId == other.ItemTypeId && Quantity == other.Quantity;
    }

    public override bool Equals(object? obj) => Equals(obj as KnownDestructionYield);

    public override int GetHashCode() => HashCode.Combine(ItemTypeId, Quantity);
}

/// <summary>Immutable actor-local belief about one observed Damage Contract.</summary>
public sealed class KnownDamageOpportunity : IEquatable<KnownDamageOpportunity>
{
    private readonly ReadOnlyCollection<KnownDestructionYield> _believedYields;

    public KnownDamageOpportunity(
        ContractRef contractRef,
        long observedVersion,
        InteractionRange believedInteractionRange,
        KnownCapabilityRequirement believedRequirement,
        IEnumerable<KnownDestructionYield> believedYields)
    {
        ArgumentNullException.ThrowIfNull(contractRef);
        ArgumentNullException.ThrowIfNull(believedRequirement);
        ArgumentNullException.ThrowIfNull(believedYields);
        if (observedVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(observedVersion));
        }

        KnownDestructionYield[] yieldSnapshot = CanonicalizeYields(believedYields);
        ContractRef = contractRef;
        ObservedVersion = observedVersion;
        BelievedInteractionRange = believedInteractionRange;
        BelievedRequirement = believedRequirement;
        _believedYields = Array.AsReadOnly(yieldSnapshot);
    }

    public ContractRef ContractRef { get; }
    public long ObservedVersion { get; }
    public InteractionRange BelievedInteractionRange { get; }
    public KnownCapabilityRequirement BelievedRequirement { get; }
    public IReadOnlyList<KnownDestructionYield> BelievedYields => _believedYields;

    public bool Equals(KnownDamageOpportunity? other)
    {
        return other is not null &&
            ContractRef == other.ContractRef &&
            ObservedVersion == other.ObservedVersion &&
            BelievedInteractionRange == other.BelievedInteractionRange &&
            BelievedRequirement.Equals(other.BelievedRequirement) &&
            BelievedYields.SequenceEqual(other.BelievedYields);
    }

    public override bool Equals(object? obj) => Equals(obj as KnownDamageOpportunity);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ContractRef);
        hash.Add(ObservedVersion);
        hash.Add(BelievedInteractionRange);
        hash.Add(BelievedRequirement);
        foreach (KnownDestructionYield yield in BelievedYields)
        {
            hash.Add(yield);
        }

        return hash.ToHashCode();
    }

    private static KnownDestructionYield[] CanonicalizeYields(IEnumerable<KnownDestructionYield> yields)
    {
        KnownDestructionYield[] snapshot = yields.ToArray();
        var itemTypeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (KnownDestructionYield yield in snapshot)
        {
            ArgumentNullException.ThrowIfNull(yield);
            if (!itemTypeIds.Add(yield.ItemTypeId.Value))
            {
                throw new ArgumentException("Believed yields must have unique item types.", nameof(yields));
            }
        }

        Array.Sort(snapshot, KnownDestructionYieldComparer.Instance);
        return snapshot;
    }

    private sealed class KnownDestructionYieldComparer : IComparer<KnownDestructionYield>
    {
        public static KnownDestructionYieldComparer Instance { get; } = new();

        public int Compare(KnownDestructionYield? left, KnownDestructionYield? right)
        {
            return StringComparer.Ordinal.Compare(left?.ItemTypeId.Value, right?.ItemTypeId.Value);
        }
    }
}



/// <summary>Pure immutable actor-visible Knowledge transitions for typed opportunity observations.</summary>
public static class NpcKnowledgeObservationTransition
{
    public static NpcKnowledgeState Apply(NpcKnowledgeState knowledge, DamageOpportunityObservedReceipt receipt) { ArgumentNullException.ThrowIfNull(receipt); return ApplyObserved(knowledge, receipt.TargetSnapshot, receipt.Opportunity); }
    public static NpcKnowledgeState Apply(NpcKnowledgeState knowledge, ConsumptionOpportunityObservedReceipt receipt) { ArgumentNullException.ThrowIfNull(receipt); return ApplyObserved(knowledge, receipt.TargetSnapshot, receipt.Opportunity); }
    public static NpcKnowledgeState Apply(NpcKnowledgeState knowledge, DamageOpportunityUnavailableReceipt receipt) { ArgumentNullException.ThrowIfNull(receipt); return ApplyUnavailable(knowledge, receipt.ContractRef, receipt.ObservedVersion, true); }
    public static NpcKnowledgeState Apply(NpcKnowledgeState knowledge, ConsumptionOpportunityUnavailableReceipt receipt) { ArgumentNullException.ThrowIfNull(receipt); return ApplyUnavailable(knowledge, receipt.ContractRef, receipt.ObservedVersion, false); }
    public static NpcKnowledgeState Apply(NpcKnowledgeState knowledge, PickupOpportunityObservedReceipt receipt) { ArgumentNullException.ThrowIfNull(receipt); return ApplyPickupObserved(knowledge, receipt.TargetSnapshot, receipt.Opportunity); }
    public static NpcKnowledgeState Apply(NpcKnowledgeState knowledge, PickupOpportunityUnavailableReceipt receipt) { ArgumentNullException.ThrowIfNull(receipt); ArgumentNullException.ThrowIfNull(knowledge); if(!knowledge.KnownOpportunities.TryResolvePickup(receipt.ContractRef,out KnownPickupOpportunity? existing)||existing is null||existing.ObservedVersion>receipt.ObservedVersion)return knowledge;return new NpcKnowledgeState(knowledge.KnownTargets,new NpcKnownOpportunityState(knowledge.KnownOpportunities.DamageOpportunities,knowledge.KnownOpportunities.ConsumptionOpportunities,knowledge.KnownOpportunities.PickupOpportunities.Where(x=>x.ContractRef!=receipt.ContractRef),knowledge.KnownOpportunities.ResourceYieldOpportunities),knowledge.KnownAttendanceDestinations); }

    private static NpcKnowledgeState ApplyPickupObserved(NpcKnowledgeState knowledge, ActorVisibleTargetSpatialSnapshot target, KnownPickupOpportunity incoming)
    { ArgumentNullException.ThrowIfNull(knowledge); if(knowledge.KnownOpportunities.TryResolvePickup(incoming.ContractRef,out KnownPickupOpportunity? existing)&&existing is not null){if(existing.ObservedVersion>incoming.ObservedVersion)return knowledge;if(existing.ObservedVersion==incoming.ObservedVersion){if(!existing.Equals(incoming))throw new ArgumentException();if(knowledge.KnownTargets.TryResolve(target.TargetRef,out ActorVisibleTargetSpatialSnapshot? known)&&known==target)return knowledge;}}return new NpcKnowledgeState(new NpcKnownTargetSpatialState(knowledge.KnownTargets.Snapshots.Where(x=>x.TargetRef!=target.TargetRef).Append(target)),new NpcKnownOpportunityState(knowledge.KnownOpportunities.DamageOpportunities,knowledge.KnownOpportunities.ConsumptionOpportunities,knowledge.KnownOpportunities.PickupOpportunities.Where(x=>x.ContractRef!=incoming.ContractRef).Append(incoming),knowledge.KnownOpportunities.ResourceYieldOpportunities),knowledge.KnownAttendanceDestinations); }

    private static NpcKnowledgeState ApplyObserved(NpcKnowledgeState knowledge, ActorVisibleTargetSpatialSnapshot target, object incoming)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(target);
        ContractRef contractRef = GetContractRef(incoming);
        object? existing = incoming is KnownDamageOpportunity ? (knowledge.KnownOpportunities.TryResolveDamage(contractRef, out KnownDamageOpportunity? damage) ? damage : null) : (knowledge.KnownOpportunities.TryResolveConsumption(contractRef, out KnownConsumptionOpportunity? consumption) ? consumption : null);
        if (existing is not null)
        {
            if (GetVersion(existing) > GetVersion(incoming)) return knowledge;
            if (GetVersion(existing) == GetVersion(incoming) && !existing.Equals(incoming)) throw new ArgumentException("Equal observed versions must carry the same opportunity belief.");
            if (GetVersion(existing) == GetVersion(incoming) && knowledge.KnownTargets.TryResolve(target.TargetRef, out ActorVisibleTargetSpatialSnapshot? knownTarget) && knownTarget == target) return knowledge;
        }
        NpcKnownTargetSpatialState targets = new(knowledge.KnownTargets.Snapshots.Where(snapshot => snapshot.TargetRef != target.TargetRef).Append(target));
        NpcKnownOpportunityState opportunities = incoming is KnownDamageOpportunity damageOpportunity
            ? new NpcKnownOpportunityState(knowledge.KnownOpportunities.DamageOpportunities.Where(value => value.ContractRef != contractRef).Append(damageOpportunity), knowledge.KnownOpportunities.ConsumptionOpportunities, knowledge.KnownOpportunities.PickupOpportunities, knowledge.KnownOpportunities.ResourceYieldOpportunities)
            : new NpcKnownOpportunityState(knowledge.KnownOpportunities.DamageOpportunities, knowledge.KnownOpportunities.ConsumptionOpportunities.Where(value => value.ContractRef != contractRef).Append((KnownConsumptionOpportunity)incoming), knowledge.KnownOpportunities.PickupOpportunities, knowledge.KnownOpportunities.ResourceYieldOpportunities);
        return new NpcKnowledgeState(targets, opportunities, knowledge.KnownAttendanceDestinations);
    }

    private static NpcKnowledgeState ApplyUnavailable(NpcKnowledgeState knowledge, ContractRef contractRef, long version, bool damage)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        object? existing = damage ? (knowledge.KnownOpportunities.TryResolveDamage(contractRef, out KnownDamageOpportunity? knownDamage) ? knownDamage : null) : (knowledge.KnownOpportunities.TryResolveConsumption(contractRef, out KnownConsumptionOpportunity? knownConsumption) ? knownConsumption : null);
        if (existing is null || GetVersion(existing) > version) return knowledge;
        NpcKnownOpportunityState opportunities = damage
            ? new NpcKnownOpportunityState(knowledge.KnownOpportunities.DamageOpportunities.Where(value => value.ContractRef != contractRef), knowledge.KnownOpportunities.ConsumptionOpportunities, knowledge.KnownOpportunities.PickupOpportunities, knowledge.KnownOpportunities.ResourceYieldOpportunities)
            : new NpcKnownOpportunityState(knowledge.KnownOpportunities.DamageOpportunities, knowledge.KnownOpportunities.ConsumptionOpportunities.Where(value => value.ContractRef != contractRef), knowledge.KnownOpportunities.PickupOpportunities, knowledge.KnownOpportunities.ResourceYieldOpportunities);
        return new NpcKnowledgeState(
            knowledge.KnownTargets,
            opportunities,
            knowledge.KnownAttendanceDestinations);
    }

    private static ContractRef GetContractRef(object opportunity) => opportunity switch { KnownDamageOpportunity damage => damage.ContractRef, KnownConsumptionOpportunity consumption => consumption.ContractRef, _ => throw new ArgumentException() };
    private static long GetVersion(object opportunity) => opportunity switch { KnownDamageOpportunity damage => damage.ObservedVersion, KnownConsumptionOpportunity consumption => consumption.ObservedVersion, _ => throw new ArgumentException() };
}

/// <summary>Applies only the Consumption Knowledge consequence justified by a redacted outcome.</summary>
public static class NpcConsumptionActionOutcomeKnowledgeTransition
{
    public static NpcKnowledgeState Apply(NpcKnowledgeState knowledge, ConsumptionActionOutcomeReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.Outcome != ConsumptionActionOutcome.Rejected || receipt.PerceivedFailure is not ConsumptionActionPerceivedFailure.InteractionUnavailable and not ConsumptionActionPerceivedFailure.InteractionChanged) return knowledge;
        GameActionSpec action = receipt.AttemptedAction;
        return NpcKnowledgeObservationTransition.Apply(knowledge, new ConsumptionOpportunityUnavailableReceipt(action.Binding.ContractRef, action.Binding.ExpectedVersion.Value));
    }
}
