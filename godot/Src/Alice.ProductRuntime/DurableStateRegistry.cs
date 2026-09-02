using System.Collections.ObjectModel;
using Alice.Identity;

namespace Alice.ProductRuntime;

public enum DurableAggregateKind
{
    WorldClock,
    CanonicalHistory,
    PlaceState,
    RegionState,
    ActorState,
    AssetContainer,
    PublicEventState,
    ConversationState,
    RelationshipState,
    CommitmentState
}

public enum DurableStateOwnerKind
{
    World,
    Actor,
    Place,
    Region,
    Shop,
    Warehouse
}

public sealed record DurableAggregateId
{
    public DurableAggregateId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Durable aggregate identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public sealed record DurableStateOwnerId
{
    public DurableStateOwnerId(DurableStateOwnerKind kind, string value)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        NonEmptyIdentityValue.Validate(value, nameof(value), "Durable-state owner identity must be non-empty.");
        Kind = kind;
        Value = value;
    }

    public DurableStateOwnerKind Kind { get; }
    public string Value { get; }
}

public sealed record DurableAggregateRegistration
{
    public DurableAggregateRegistration(
        DurableAggregateId aggregateId,
        DurableAggregateKind kind,
        DurableStateOwnerId ownerId,
        int restoreOrder)
    {
        ArgumentNullException.ThrowIfNull(aggregateId);
        ArgumentNullException.ThrowIfNull(ownerId);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (restoreOrder < 0) throw new ArgumentOutOfRangeException(nameof(restoreOrder));
        AggregateId = aggregateId;
        Kind = kind;
        OwnerId = ownerId;
        RestoreOrder = restoreOrder;
    }

    public DurableAggregateId AggregateId { get; }
    public DurableAggregateKind Kind { get; }
    public DurableStateOwnerId OwnerId { get; }
    public int RestoreOrder { get; }
}

/// <summary>Typed save ownership and restore sequence. G9 supplies serialization for these registrations.</summary>
public sealed class DurableStateRegistry
{
    private readonly ReadOnlyCollection<DurableAggregateRegistration> _registrations;

    public DurableStateRegistry(IEnumerable<DurableAggregateRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        DurableAggregateRegistration[] snapshot = registrations.ToArray();
        if (snapshot.Any(IsNullRegistration)
            || snapshot.Select(GetAggregateId).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Durable aggregate identities must be unique and have one owner.", nameof(registrations));
        Array.Sort(snapshot, DurableRegistrationComparer.Instance);
        _registrations = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<DurableAggregateRegistration> Registrations => _registrations;

    private static bool IsNullRegistration(DurableAggregateRegistration? registration) => registration is null;
    private static DurableAggregateId GetAggregateId(DurableAggregateRegistration registration) => registration.AggregateId;

    private sealed class DurableRegistrationComparer : IComparer<DurableAggregateRegistration>
    {
        public static DurableRegistrationComparer Instance { get; } = new();
        public int Compare(DurableAggregateRegistration? left, DurableAggregateRegistration? right)
        {
            if (left is null) return right is null ? 0 : -1;
            if (right is null) return 1;
            int order = left.RestoreOrder.CompareTo(right.RestoreOrder);
            return order != 0
                ? order
                : StringComparer.Ordinal.Compare(left.AggregateId.Value, right.AggregateId.Value);
        }
    }
}
