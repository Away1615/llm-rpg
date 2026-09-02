using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Social;
using Alice.Validation;

namespace Alice.Authority;

public enum ScheduledGatheringAuthorityOperationKind
{
    Create,
    Revise
}

public enum ScheduledGatheringAuthorityFailure
{
    GatheringIdentityInvalid,
    HostActorUnavailable,
    PlaceUnavailable,
    HostPlaceUseUnavailable,
    AuthorizedInviterUnavailable,
    DuplicateAuthorizedInviter,
    InvalidTimeWindow,
    LifecycleUndefined,
    DuplicateGatheringIdentity,
    RevisionTargetUnavailable,
    ExpectedRevisionNotPositive,
    StaleExpectedRevision,
    RevisionOverflow
}

/// <summary>Bounded result of one gathering creation or full-replacement revision attempt.</summary>
public sealed class ScheduledGatheringAuthorityResult
{
    internal ScheduledGatheringAuthorityResult(
        ScheduledGatheringAuthorityOperationKind operationKind,
        GatheringRef targetGatheringRef,
        int previousRevision,
        ScheduledGathering committedGathering)
    {
        OperationKind = operationKind;
        TargetGatheringRef = targetGatheringRef;
        PreviousRevision = previousRevision;
        CurrentRevision = committedGathering.Revision;
        CommittedGathering = committedGathering;
    }

    internal ScheduledGatheringAuthorityResult(
        ScheduledGatheringAuthorityOperationKind operationKind,
        GatheringRef targetGatheringRef,
        ScheduledGatheringAuthorityFailure failure)
    {
        OperationKind = operationKind;
        TargetGatheringRef = targetGatheringRef;
        Failure = failure;
    }

    public bool IsCommitted => CommittedGathering is not null;
    public ScheduledGatheringAuthorityOperationKind OperationKind { get; }
    public GatheringRef TargetGatheringRef { get; }
    public int PreviousRevision { get; }
    public int CurrentRevision { get; }
    public ScheduledGathering? CommittedGathering { get; }
    public ScheduledGatheringAuthorityFailure? Failure { get; }
}

/// <summary>Single Authority owner of live immutable ScheduledGathering records.</summary>
public sealed class ScheduledGatheringAuthorityRuntime
{
    private readonly ReadOnlyCollection<ActorId> _actors;
    private readonly ReadOnlyCollection<PlaceRef> _places;
    private readonly ReadOnlyCollection<GatheringHostPlaceUseAuthorityFact> _hostPlaceUseFacts;
    private ReadOnlyCollection<ScheduledGathering> _gatherings;

    public ScheduledGatheringAuthorityRuntime(
        IEnumerable<ActorId> actors,
        IEnumerable<PlaceRef> places,
        IEnumerable<GatheringHostPlaceUseAuthorityFact> hostPlaceUseFacts,
        IEnumerable<ScheduledGathering>? initialGatherings = null)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(hostPlaceUseFacts);

        ActorId[] actorSnapshot = actors.ToArray();
        PlaceRef[] placeSnapshot = places.ToArray();
        GatheringHostPlaceUseAuthorityFact[] factSnapshot = hostPlaceUseFacts.ToArray();
        ScheduledGathering[] gatheringSnapshot = initialGatherings?.ToArray() ?? [];
        EnsureValidActors(actorSnapshot);
        EnsureValidPlaces(placeSnapshot);
        EnsureValidFacts(factSnapshot);
        EnsureUniqueGatherings(gatheringSnapshot);

        _actors = Array.AsReadOnly(actorSnapshot);
        _places = Array.AsReadOnly(placeSnapshot);
        _hostPlaceUseFacts = Array.AsReadOnly(factSnapshot);
        _gatherings = Array.AsReadOnly(gatheringSnapshot);
    }

    public IReadOnlyList<ScheduledGathering> Gatherings => _gatherings;

    public ScheduledGathering? FindCurrent(GatheringRef gatheringRef) =>
        _gatherings.SingleOrDefault(candidate => candidate.GatheringRef == gatheringRef);

    public ScheduledGatheringAuthorityResult TryCreate(ScheduledGatheringCreationProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (FindCurrent(proposal.GatheringRef) is not null)
        {
            return Rejected(
                ScheduledGatheringAuthorityOperationKind.Create,
                proposal.GatheringRef,
                ScheduledGatheringAuthorityFailure.DuplicateGatheringIdentity);
        }

        ScheduledGatheringValidationFailure? validationFailure = ScheduledGatheringValidator.Validate(
            proposal,
            _actors,
            _places,
            _hostPlaceUseFacts);
        if (validationFailure is not null)
        {
            return Rejected(
                ScheduledGatheringAuthorityOperationKind.Create,
                proposal.GatheringRef,
                MapValidationFailure(validationFailure.Value));
        }

        var committed = new ScheduledGathering(
            proposal.GatheringRef,
            proposal.HostActorId,
            proposal.PlaceRef,
            proposal.StartsAt,
            proposal.EndsAt,
            proposal.AuthorizedInviterActorIds,
            proposal.Lifecycle,
            1);
        ScheduledGathering[] replacement = [.. _gatherings, committed];
        _gatherings = Array.AsReadOnly(replacement);
        return new ScheduledGatheringAuthorityResult(
            ScheduledGatheringAuthorityOperationKind.Create,
            proposal.GatheringRef,
            0,
            committed);
    }

    public ScheduledGatheringAuthorityResult TryRevise(ScheduledGatheringRevisionProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ScheduledGatheringValidationFailure? validationFailure = ScheduledGatheringValidator.Validate(
            proposal,
            _actors,
            _places,
            _hostPlaceUseFacts);
        if (validationFailure is not null)
        {
            return Rejected(
                ScheduledGatheringAuthorityOperationKind.Revise,
                proposal.GatheringRef,
                MapValidationFailure(validationFailure.Value));
        }

        ScheduledGathering? current = FindCurrent(proposal.GatheringRef);
        if (current is null)
        {
            return Rejected(
                ScheduledGatheringAuthorityOperationKind.Revise,
                proposal.GatheringRef,
                ScheduledGatheringAuthorityFailure.RevisionTargetUnavailable);
        }

        if (proposal.ExpectedCurrentRevision != current.Revision)
        {
            return Rejected(
                ScheduledGatheringAuthorityOperationKind.Revise,
                proposal.GatheringRef,
                ScheduledGatheringAuthorityFailure.StaleExpectedRevision);
        }

        if (MateriallyEquals(current, proposal))
        {
            return new ScheduledGatheringAuthorityResult(
                ScheduledGatheringAuthorityOperationKind.Revise,
                proposal.GatheringRef,
                current.Revision,
                current);
        }

        int nextRevision;
        try
        {
            nextRevision = checked(current.Revision + 1);
        }
        catch (OverflowException)
        {
            return Rejected(
                ScheduledGatheringAuthorityOperationKind.Revise,
                proposal.GatheringRef,
                ScheduledGatheringAuthorityFailure.RevisionOverflow);
        }

        var committed = new ScheduledGathering(
            proposal.GatheringRef,
            proposal.HostActorId,
            proposal.PlaceRef,
            proposal.StartsAt,
            proposal.EndsAt,
            proposal.AuthorizedInviterActorIds,
            proposal.Lifecycle,
            nextRevision);
        ScheduledGathering[] replacement = _gatherings.ToArray();
        int targetIndex = Array.FindIndex(replacement, candidate => candidate.GatheringRef == proposal.GatheringRef);
        replacement[targetIndex] = committed;
        _gatherings = Array.AsReadOnly(replacement);
        return new ScheduledGatheringAuthorityResult(
            ScheduledGatheringAuthorityOperationKind.Revise,
            proposal.GatheringRef,
            current.Revision,
            committed);
    }

    private static ScheduledGatheringAuthorityResult Rejected(
        ScheduledGatheringAuthorityOperationKind operationKind,
        GatheringRef gatheringRef,
        ScheduledGatheringAuthorityFailure failure) => new(operationKind, gatheringRef, failure);

    private static ScheduledGatheringAuthorityFailure MapValidationFailure(
        ScheduledGatheringValidationFailure failure) => failure switch
        {
            ScheduledGatheringValidationFailure.GatheringIdentityInvalid => ScheduledGatheringAuthorityFailure.GatheringIdentityInvalid,
            ScheduledGatheringValidationFailure.HostActorUnavailable => ScheduledGatheringAuthorityFailure.HostActorUnavailable,
            ScheduledGatheringValidationFailure.PlaceUnavailable => ScheduledGatheringAuthorityFailure.PlaceUnavailable,
            ScheduledGatheringValidationFailure.HostPlaceUseUnavailable => ScheduledGatheringAuthorityFailure.HostPlaceUseUnavailable,
            ScheduledGatheringValidationFailure.AuthorizedInviterUnavailable => ScheduledGatheringAuthorityFailure.AuthorizedInviterUnavailable,
            ScheduledGatheringValidationFailure.DuplicateAuthorizedInviter => ScheduledGatheringAuthorityFailure.DuplicateAuthorizedInviter,
            ScheduledGatheringValidationFailure.InvalidTimeWindow => ScheduledGatheringAuthorityFailure.InvalidTimeWindow,
            ScheduledGatheringValidationFailure.LifecycleUndefined => ScheduledGatheringAuthorityFailure.LifecycleUndefined,
            ScheduledGatheringValidationFailure.ExpectedRevisionNotPositive => ScheduledGatheringAuthorityFailure.ExpectedRevisionNotPositive,
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };

    private static bool MateriallyEquals(
        ScheduledGathering current,
        ScheduledGatheringRevisionProposal proposal) =>
        current.HostActorId == proposal.HostActorId &&
        current.PlaceRef == proposal.PlaceRef &&
        current.StartsAt == proposal.StartsAt &&
        current.EndsAt == proposal.EndsAt &&
        current.Lifecycle == proposal.Lifecycle &&
        current.AuthorizedInviterActorIds.ToHashSet().SetEquals(proposal.AuthorizedInviterActorIds);

    private static void EnsureValidActors(IEnumerable<ActorId> actors)
    {
        var identities = new HashSet<ActorId>();
        foreach (ActorId actor in actors)
        {
            if (string.IsNullOrWhiteSpace(actor.Value) || !identities.Add(actor))
            {
                throw new ArgumentException("Authority Actor catalogue must contain unique valid identities.", nameof(actors));
            }
        }
    }

    private static void EnsureValidPlaces(IEnumerable<PlaceRef> places)
    {
        var identities = new HashSet<PlaceRef>();
        foreach (PlaceRef place in places)
        {
            if (string.IsNullOrWhiteSpace(place.Value) || !identities.Add(place))
            {
                throw new ArgumentException("Authority Place catalogue must contain unique valid identities.", nameof(places));
            }
        }
    }

    private static void EnsureValidFacts(IEnumerable<GatheringHostPlaceUseAuthorityFact> facts)
    {
        var identities = new HashSet<(GatheringRef, ActorId, PlaceRef)>();
        foreach (GatheringHostPlaceUseAuthorityFact fact in facts)
        {
            ArgumentNullException.ThrowIfNull(fact);
            if (!identities.Add((fact.GatheringRef, fact.HostActorId, fact.PlaceRef)))
            {
                throw new ArgumentException("Gathering-scoped Host place-use facts must be unique.", nameof(facts));
            }
        }
    }

    private static void EnsureUniqueGatherings(IEnumerable<ScheduledGathering> gatherings)
    {
        var identities = new HashSet<GatheringRef>();
        foreach (ScheduledGathering gathering in gatherings)
        {
            ArgumentNullException.ThrowIfNull(gathering);
            if (!identities.Add(gathering.GatheringRef))
            {
                throw new ArgumentException("Authority gathering snapshots must be unique.", nameof(gatherings));
            }
        }
    }
}
