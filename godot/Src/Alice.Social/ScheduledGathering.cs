using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Actors;

namespace Alice.Social;

public readonly record struct PlaceRef(string Value);

public enum ScheduledGatheringLifecycle
{
    Planned,
    Active,
    Cancelled,
    Ended
}

/// <summary>An Authority fact granting one Host use of one Place for one gathering only.</summary>
public sealed record GatheringHostPlaceUseAuthorityFact
{
    public GatheringHostPlaceUseAuthorityFact(
        GatheringRef gatheringRef,
        ActorId hostActorId,
        PlaceRef placeRef)
    {
        SemanticDialogueIdentity.Validate(gatheringRef.Value, nameof(gatheringRef));
        SemanticDialogueIdentity.ValidateActor(hostActorId, nameof(hostActorId));
        SemanticDialogueIdentity.Validate(placeRef.Value, nameof(placeRef));

        GatheringRef = gatheringRef;
        HostActorId = hostActorId;
        PlaceRef = placeRef;
    }

    public GatheringRef GatheringRef { get; }
    public ActorId HostActorId { get; }
    public PlaceRef PlaceRef { get; }
}

/// <summary>Untrusted immutable terms proposed for a new gathering.</summary>
public sealed record ScheduledGatheringCreationProposal
{
    private readonly ReadOnlyCollection<ActorId> _authorizedInviterActorIds;

    public ScheduledGatheringCreationProposal(
        GatheringRef gatheringRef,
        ActorId hostActorId,
        PlaceRef placeRef,
        SimTime startsAt,
        SimTime endsAt,
        IEnumerable<ActorId> authorizedInviterActorIds,
        ScheduledGatheringLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(authorizedInviterActorIds);
        GatheringRef = gatheringRef;
        HostActorId = hostActorId;
        PlaceRef = placeRef;
        StartsAt = startsAt;
        EndsAt = endsAt;
        _authorizedInviterActorIds = Array.AsReadOnly(authorizedInviterActorIds.ToArray());
        Lifecycle = lifecycle;
    }

    public GatheringRef GatheringRef { get; }
    public ActorId HostActorId { get; }
    public PlaceRef PlaceRef { get; }
    public SimTime StartsAt { get; }
    public SimTime EndsAt { get; }
    public IReadOnlyList<ActorId> AuthorizedInviterActorIds => _authorizedInviterActorIds;
    public ScheduledGatheringLifecycle Lifecycle { get; }
}

/// <summary>Untrusted immutable full-replacement terms proposed for one current gathering.</summary>
public sealed record ScheduledGatheringRevisionProposal
{
    private readonly ReadOnlyCollection<ActorId> _authorizedInviterActorIds;

    public ScheduledGatheringRevisionProposal(
        GatheringRef gatheringRef,
        int expectedCurrentRevision,
        ActorId hostActorId,
        PlaceRef placeRef,
        SimTime startsAt,
        SimTime endsAt,
        IEnumerable<ActorId> authorizedInviterActorIds,
        ScheduledGatheringLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(authorizedInviterActorIds);
        GatheringRef = gatheringRef;
        ExpectedCurrentRevision = expectedCurrentRevision;
        HostActorId = hostActorId;
        PlaceRef = placeRef;
        StartsAt = startsAt;
        EndsAt = endsAt;
        _authorizedInviterActorIds = Array.AsReadOnly(authorizedInviterActorIds.ToArray());
        Lifecycle = lifecycle;
    }

    public GatheringRef GatheringRef { get; }
    public int ExpectedCurrentRevision { get; }
    public ActorId HostActorId { get; }
    public PlaceRef PlaceRef { get; }
    public SimTime StartsAt { get; }
    public SimTime EndsAt { get; }
    public IReadOnlyList<ActorId> AuthorizedInviterActorIds => _authorizedInviterActorIds;
    public ScheduledGatheringLifecycle Lifecycle { get; }
}

/// <summary>Immutable Authority-owned terms for one scheduled social gathering.</summary>
public sealed record ScheduledGathering
{
    private readonly ReadOnlyCollection<ActorId> _authorizedInviterActorIds;

    public ScheduledGathering(
        GatheringRef gatheringRef,
        ActorId hostActorId,
        PlaceRef placeRef,
        SimTime startsAt,
        SimTime endsAt,
        IEnumerable<ActorId> authorizedInviterActorIds,
        ScheduledGatheringLifecycle lifecycle,
        int revision)
    {
        SemanticDialogueIdentity.Validate(gatheringRef.Value, nameof(gatheringRef));
        SemanticDialogueIdentity.ValidateActor(hostActorId, nameof(hostActorId));
        SemanticDialogueIdentity.Validate(placeRef.Value, nameof(placeRef));
        ArgumentNullException.ThrowIfNull(authorizedInviterActorIds);
        if (startsAt.CompareTo(endsAt) >= 0)
        {
            throw new ArgumentException("A gathering must start before it ends.", nameof(endsAt));
        }

        if (!Enum.IsDefined(lifecycle))
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycle));
        }

        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        ActorId[] inviterSnapshot = authorizedInviterActorIds.ToArray();
        foreach (ActorId inviterActorId in inviterSnapshot)
        {
            SemanticDialogueIdentity.ValidateActor(inviterActorId, nameof(authorizedInviterActorIds));
        }

        if (inviterSnapshot.Distinct().Count() != inviterSnapshot.Length)
        {
            throw new ArgumentException("Authorized inviters must be distinct.", nameof(authorizedInviterActorIds));
        }

        GatheringRef = gatheringRef;
        HostActorId = hostActorId;
        PlaceRef = placeRef;
        StartsAt = startsAt;
        EndsAt = endsAt;
        _authorizedInviterActorIds = Array.AsReadOnly(inviterSnapshot);
        Lifecycle = lifecycle;
        Revision = revision;
    }

    public GatheringRef GatheringRef { get; }
    public ActorId HostActorId { get; }
    public PlaceRef PlaceRef { get; }
    public SimTime StartsAt { get; }
    public SimTime EndsAt { get; }
    public IReadOnlyList<ActorId> AuthorizedInviterActorIds => _authorizedInviterActorIds;
    public ScheduledGatheringLifecycle Lifecycle { get; }
    public int Revision { get; }
}
