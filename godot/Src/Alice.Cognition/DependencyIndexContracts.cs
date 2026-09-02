using Alice.Actors;
using Alice.Commitments;
using Alice.Social;

namespace Alice.Cognition;

public enum DependencySourceKind
{
    Event,
    Pressure
}

public enum DependencyEdgeKind
{
    AffectsPlace,
    ControlsOrHoldsResource,
    AssignedToActor,
    RequiresCapability,
    BoundByCommitment,
    HoldsRequiredInformation,
    MemberOfOrganization
}

public enum EventCentricRankBand
{
    E0,
    E1,
    E2
}

/// <summary>An opaque identity for one indexed duty.</summary>
public readonly record struct DutyRef
{
    public DutyRef(string value)
    {
        DependencyContractIdentity.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

/// <summary>An opaque identity for one organization in a role assignment.</summary>
public readonly record struct OrganizationId
{
    public OrganizationId(string value)
    {
        DependencyContractIdentity.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

/// <summary>Exactly one directly affected typed world node.</summary>
public sealed record AffectedNode
{
    public AffectedNode(
        PlaceRef? placeRef,
        ResourceRef? resourceRef,
        CommitmentId? commitmentId,
        ActorId? actorId,
        DutyRef? dutyRef)
    {
        int caseCount =
            (placeRef.HasValue ? 1 : 0) +
            (resourceRef.HasValue ? 1 : 0) +
            (commitmentId.HasValue ? 1 : 0) +
            (actorId.HasValue ? 1 : 0) +
            (dutyRef.HasValue ? 1 : 0);
        if (caseCount != 1)
        {
            throw new ArgumentException("An affected node must contain exactly one typed case.");
        }

        if (placeRef is PlaceRef place)
        {
            DependencyContractIdentity.Validate(place.Value, nameof(placeRef));
        }

        if (resourceRef is ResourceRef resource)
        {
            DependencyContractIdentity.Validate(resource.Value, nameof(resourceRef));
        }

        if (commitmentId is CommitmentId commitment)
        {
            DependencyContractIdentity.Validate(commitment.Value, nameof(commitmentId));
        }

        if (actorId is ActorId actor)
        {
            DependencyContractIdentity.Validate(actor.Value, nameof(actorId));
        }

        if (dutyRef is DutyRef duty)
        {
            DependencyContractIdentity.Validate(duty.Value, nameof(dutyRef));
        }

        PlaceRef = placeRef;
        ResourceRef = resourceRef;
        CommitmentId = commitmentId;
        ActorId = actorId;
        DutyRef = dutyRef;
    }

    public PlaceRef? PlaceRef { get; }
    public ResourceRef? ResourceRef { get; }
    public CommitmentId? CommitmentId { get; }
    public ActorId? ActorId { get; }
    public DutyRef? DutyRef { get; }

    public static AffectedNode FromPlace(PlaceRef value) => new(value, null, null, null, null);
    public static AffectedNode FromResource(ResourceRef value) => new(null, value, null, null, null);
    public static AffectedNode FromCommitment(CommitmentId value) => new(null, null, value, null, null);
    public static AffectedNode FromActor(ActorId value) => new(null, null, null, value, null);
    public static AffectedNode FromDuty(DutyRef value) => new(null, null, null, null, value);
}

/// <summary>One source-correlated typed node supplied to the current-snapshot index.</summary>
public sealed record AffectedNodeFact
{
    public AffectedNodeFact(
        DependencySourceKind sourceKind,
        string sourceId,
        AffectedNode affectedNode)
    {
        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        }

        DependencyContractIdentity.Validate(sourceId, nameof(sourceId));
        ArgumentNullException.ThrowIfNull(affectedNode);

        SourceKind = sourceKind;
        SourceId = sourceId;
        AffectedNode = affectedNode;
    }

    public DependencySourceKind SourceKind { get; }
    public string SourceId { get; }
    public AffectedNode AffectedNode { get; }
}

/// <summary>Exactly one typed responsibility carried by a role assignment.</summary>
public sealed record ResponsibilityRef
{
    public ResponsibilityRef(
        PlaceRef? placeRef,
        ResourceRef? resourceRef,
        CommitmentId? commitmentId,
        DutyRef? dutyRef)
    {
        int caseCount =
            (placeRef.HasValue ? 1 : 0) +
            (resourceRef.HasValue ? 1 : 0) +
            (commitmentId.HasValue ? 1 : 0) +
            (dutyRef.HasValue ? 1 : 0);
        if (caseCount != 1)
        {
            throw new ArgumentException("A responsibility must contain exactly one typed case.");
        }

        if (placeRef is PlaceRef place)
        {
            DependencyContractIdentity.Validate(place.Value, nameof(placeRef));
        }

        if (resourceRef is ResourceRef resource)
        {
            DependencyContractIdentity.Validate(resource.Value, nameof(resourceRef));
        }

        if (commitmentId is CommitmentId commitment)
        {
            DependencyContractIdentity.Validate(commitment.Value, nameof(commitmentId));
        }

        if (dutyRef is DutyRef duty)
        {
            DependencyContractIdentity.Validate(duty.Value, nameof(dutyRef));
        }

        PlaceRef = placeRef;
        ResourceRef = resourceRef;
        CommitmentId = commitmentId;
        DutyRef = dutyRef;
    }

    public PlaceRef? PlaceRef { get; }
    public ResourceRef? ResourceRef { get; }
    public CommitmentId? CommitmentId { get; }
    public DutyRef? DutyRef { get; }

    public static ResponsibilityRef FromPlace(PlaceRef value) => new(value, null, null, null);
    public static ResponsibilityRef FromResource(ResourceRef value) => new(null, value, null, null);
    public static ResponsibilityRef FromCommitment(CommitmentId value) => new(null, null, value, null);
    public static ResponsibilityRef FromDuty(DutyRef value) => new(null, null, null, value);

    internal bool Matches(AffectedNode affectedNode)
    {
        ArgumentNullException.ThrowIfNull(affectedNode);
        return
            (PlaceRef is PlaceRef place && affectedNode.PlaceRef == place) ||
            (ResourceRef is ResourceRef resource && affectedNode.ResourceRef == resource) ||
            (CommitmentId is CommitmentId commitment && affectedNode.CommitmentId == commitment) ||
            (DutyRef is DutyRef duty && affectedNode.DutyRef == duty);
    }
}

/// <summary>An Actor's exact typed responsibility within one organization.</summary>
public sealed record RoleAssignment
{
    public RoleAssignment(
        ActorId actorId,
        OrganizationId organizationId,
        ResponsibilityRef responsibility)
    {
        DependencyContractIdentity.Validate(actorId.Value, nameof(actorId));
        DependencyContractIdentity.Validate(organizationId.Value, nameof(organizationId));
        ArgumentNullException.ThrowIfNull(responsibility);

        ActorId = actorId;
        OrganizationId = organizationId;
        ResponsibilityRef = responsibility;
    }

    public ActorId ActorId { get; }
    public OrganizationId OrganizationId { get; }
    public ResponsibilityRef ResponsibilityRef { get; }
}

/// <summary>One explicit current dependency assertion in the supplied snapshot.</summary>
public sealed record DependencyEdge
{
    private DependencyEdge(
        DependencyEdgeKind kind,
        AffectedNode affectedNode,
        ActorId actorId)
    {
        Kind = kind;
        AffectedNode = affectedNode;
        ActorId = actorId;
    }

    public DependencyEdgeKind Kind { get; }
    public AffectedNode AffectedNode { get; }
    public ActorId ActorId { get; }

    public static DependencyEdge Create(
        DependencyEdgeKind kind,
        AffectedNode affectedNode,
        ActorId actorId)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind == DependencyEdgeKind.MemberOfOrganization)
        {
            throw new ArgumentException(
                "MemberOfOrganization edges require an exact RoleAssignment.",
                nameof(kind));
        }

        ArgumentNullException.ThrowIfNull(affectedNode);
        DependencyContractIdentity.Validate(actorId.Value, nameof(actorId));
        return new DependencyEdge(kind, affectedNode, actorId);
    }

    public static DependencyEdge FromRoleAssignment(
        AffectedNode affectedNode,
        RoleAssignment roleAssignment)
    {
        ArgumentNullException.ThrowIfNull(affectedNode);
        ArgumentNullException.ThrowIfNull(roleAssignment);
        if (!roleAssignment.ResponsibilityRef.Matches(affectedNode))
        {
            throw new ArgumentException(
                "The role responsibility must exactly match the affected typed node.",
                nameof(roleAssignment));
        }

        return new DependencyEdge(
            DependencyEdgeKind.MemberOfOrganization,
            affectedNode,
            roleAssignment.ActorId);
    }
}

/// <summary>The index-owned EventCentric rank-band mapping.</summary>
public static class DependencyEdgeRankBandMapping
{
    public static EventCentricRankBand GetRankBand(DependencyEdgeKind kind)
    {
        return kind switch
        {
            DependencyEdgeKind.AssignedToActor => EventCentricRankBand.E0,
            DependencyEdgeKind.BoundByCommitment => EventCentricRankBand.E0,
            DependencyEdgeKind.AffectsPlace => EventCentricRankBand.E1,
            DependencyEdgeKind.ControlsOrHoldsResource => EventCentricRankBand.E1,
            DependencyEdgeKind.RequiresCapability => EventCentricRankBand.E1,
            DependencyEdgeKind.HoldsRequiredInformation => EventCentricRankBand.E2,
            DependencyEdgeKind.MemberOfOrganization => EventCentricRankBand.E2,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}

/// <summary>The complete public output of one index discovery.</summary>
public sealed record EventCentricDiscoverySeed
{
    public EventCentricDiscoverySeed(
        DependencySourceKind sourceKind,
        string sourceId,
        ActorId actorId,
        EventCentricRankBand rankBand)
    {
        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        }

        if (!Enum.IsDefined(rankBand))
        {
            throw new ArgumentOutOfRangeException(nameof(rankBand));
        }

        DependencyContractIdentity.Validate(sourceId, nameof(sourceId));
        DependencyContractIdentity.Validate(actorId.Value, nameof(actorId));

        SourceKind = sourceKind;
        SourceId = sourceId;
        ActorId = actorId;
        RankBand = rankBand;
    }

    public DependencySourceKind SourceKind { get; }
    public string SourceId { get; }
    public ActorId ActorId { get; }
    public EventCentricRankBand RankBand { get; }
}

internal static class DependencyContractIdentity
{
    public static void Validate(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identity values must be non-empty.", parameterName);
        }
    }
}
