using Alice.Activities;
using Alice.Actors;
using Alice.Social;

namespace Alice.Validation;

internal enum ScheduledGatheringValidationFailure
{
    GatheringIdentityInvalid,
    HostActorUnavailable,
    PlaceUnavailable,
    HostPlaceUseUnavailable,
    AuthorizedInviterUnavailable,
    DuplicateAuthorizedInviter,
    InvalidTimeWindow,
    LifecycleUndefined,
    ExpectedRevisionNotPositive
}

internal static class ScheduledGatheringValidator
{
    public static ScheduledGatheringValidationFailure? Validate(
        ScheduledGatheringCreationProposal proposal,
        IReadOnlyCollection<ActorId> actors,
        IReadOnlyCollection<PlaceRef> places,
        IReadOnlyCollection<GatheringHostPlaceUseAuthorityFact> hostPlaceUseFacts) =>
        ValidateTerms(
            proposal.GatheringRef,
            proposal.HostActorId,
            proposal.PlaceRef,
            proposal.StartsAt,
            proposal.EndsAt,
            proposal.AuthorizedInviterActorIds,
            proposal.Lifecycle,
            actors,
            places,
            hostPlaceUseFacts);

    public static ScheduledGatheringValidationFailure? Validate(
        ScheduledGatheringRevisionProposal proposal,
        IReadOnlyCollection<ActorId> actors,
        IReadOnlyCollection<PlaceRef> places,
        IReadOnlyCollection<GatheringHostPlaceUseAuthorityFact> hostPlaceUseFacts)
    {
        if (proposal.ExpectedCurrentRevision <= 0)
        {
            return ScheduledGatheringValidationFailure.ExpectedRevisionNotPositive;
        }

        return ValidateTerms(
            proposal.GatheringRef,
            proposal.HostActorId,
            proposal.PlaceRef,
            proposal.StartsAt,
            proposal.EndsAt,
            proposal.AuthorizedInviterActorIds,
            proposal.Lifecycle,
            actors,
            places,
            hostPlaceUseFacts);
    }

    private static ScheduledGatheringValidationFailure? ValidateTerms(
        GatheringRef gatheringRef,
        ActorId hostActorId,
        PlaceRef placeRef,
        SimTime startsAt,
        SimTime endsAt,
        IReadOnlyCollection<ActorId> authorizedInviterActorIds,
        ScheduledGatheringLifecycle lifecycle,
        IReadOnlyCollection<ActorId> actors,
        IReadOnlyCollection<PlaceRef> places,
        IReadOnlyCollection<GatheringHostPlaceUseAuthorityFact> hostPlaceUseFacts)
    {
        if (string.IsNullOrWhiteSpace(gatheringRef.Value))
        {
            return ScheduledGatheringValidationFailure.GatheringIdentityInvalid;
        }

        if (!actors.Contains(hostActorId))
        {
            return ScheduledGatheringValidationFailure.HostActorUnavailable;
        }

        if (!places.Contains(placeRef))
        {
            return ScheduledGatheringValidationFailure.PlaceUnavailable;
        }

        if (!hostPlaceUseFacts.Any(fact =>
                fact.GatheringRef == gatheringRef &&
                fact.HostActorId == hostActorId &&
                fact.PlaceRef == placeRef))
        {
            return ScheduledGatheringValidationFailure.HostPlaceUseUnavailable;
        }

        var inviterIdentities = new HashSet<ActorId>();
        foreach (ActorId inviterActorId in authorizedInviterActorIds)
        {
            if (!actors.Contains(inviterActorId))
            {
                return ScheduledGatheringValidationFailure.AuthorizedInviterUnavailable;
            }

            if (!inviterIdentities.Add(inviterActorId))
            {
                return ScheduledGatheringValidationFailure.DuplicateAuthorizedInviter;
            }
        }

        if (startsAt.CompareTo(endsAt) >= 0)
        {
            return ScheduledGatheringValidationFailure.InvalidTimeWindow;
        }

        if (!Enum.IsDefined(lifecycle))
        {
            return ScheduledGatheringValidationFailure.LifecycleUndefined;
        }

        return null;
    }
}
