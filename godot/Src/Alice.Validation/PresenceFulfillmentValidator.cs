using Alice.Activities;
using Alice.Commitments;
using Alice.Social;

namespace Alice.Validation;

public enum PresenceFulfillmentFailure
{
    CommitmentUnavailable,
    CommitmentInactive,
    PresenceWindowTermRequired,
    TravelResultUncorrelated,
    TravelActorMismatch,
    TravelNotReached,
    GatheringUnavailable,
    StaleGatheringRevision,
    GatheringLifecycleUnavailable,
    ArrivalBeforeWindow,
    ArrivalAfterWindow,
    CommitmentDeadlineMismatch,
    ArrivalBindingUnavailable,
    AmbiguousArrivalBinding,
    TravelTargetMismatch
}

internal readonly record struct PresenceFulfillmentValidationResult(
    PresenceFulfillmentFailure? Failure,
    PresenceWindowTerm? Term,
    GatheringArrivalBinding? ArrivalBinding)
{
    public bool IsValid => Failure is null;

    public static PresenceFulfillmentValidationResult Accepted(
        PresenceWindowTerm term,
        GatheringArrivalBinding arrivalBinding) => new(null, term, arrivalBinding);

    public static PresenceFulfillmentValidationResult Rejected(PresenceFulfillmentFailure failure) =>
        new(failure, null, null);
}

internal static class PresenceFulfillmentValidator
{
    public static PresenceFulfillmentValidationResult Validate(
        Commitment? commitment,
        ActivityRuntime sourceActivity,
        TravelActivityResult travelResult,
        ScheduledGathering? gathering,
        IReadOnlyCollection<GatheringArrivalBinding> arrivalBindings)
    {
        ArgumentNullException.ThrowIfNull(sourceActivity);
        ArgumentNullException.ThrowIfNull(travelResult);
        ArgumentNullException.ThrowIfNull(arrivalBindings);

        if (commitment is null)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.CommitmentUnavailable);
        }

        if (commitment.Status != CommitmentStatus.Active)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.CommitmentInactive);
        }

        if (commitment.Term is not PresenceWindowTerm term)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.PresenceWindowTermRequired);
        }

        if (sourceActivity.Kind != ActivityRuntimeKind.Travel ||
            !ReferenceEquals(sourceActivity.TravelResult, travelResult) ||
            sourceActivity.ActivityId != travelResult.ActivityId ||
            sourceActivity.ActorId != travelResult.ActorId ||
            sourceActivity.TargetRef != travelResult.TargetRef)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.TravelResultUncorrelated);
        }

        if (commitment.Debtor != travelResult.ActorId)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.TravelActorMismatch);
        }

        if (travelResult.Kind != TravelActivityResultKind.Reached ||
            sourceActivity.Status != ActivityRuntimeStatus.Completed)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.TravelNotReached);
        }

        if (gathering is null)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.GatheringUnavailable);
        }

        if (gathering.GatheringRef != term.GatheringRef || gathering.Revision != term.ExpectedGatheringRevision)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.StaleGatheringRevision);
        }

        if (gathering.Lifecycle is not ScheduledGatheringLifecycle.Planned and not ScheduledGatheringLifecycle.Active)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.GatheringLifecycleUnavailable);
        }

        if (sourceActivity.LastProgressTime.CompareTo(gathering.StartsAt) < 0)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.ArrivalBeforeWindow);
        }

        if (sourceActivity.LastProgressTime.CompareTo(gathering.EndsAt) > 0)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.ArrivalAfterWindow);
        }

        if (commitment.Deadline != gathering.EndsAt)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.CommitmentDeadlineMismatch);
        }

        GatheringArrivalBinding[] placeBindings = arrivalBindings
            .Where(binding =>
                binding.GatheringRef == gathering.GatheringRef &&
                binding.PlaceRef == gathering.PlaceRef)
            .ToArray();
        if (placeBindings.Length == 0)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.ArrivalBindingUnavailable);
        }

        if (placeBindings.Length != 1)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.AmbiguousArrivalBinding);
        }

        GatheringArrivalBinding arrivalBinding = placeBindings[0];
        if (arrivalBinding.TargetRef != travelResult.TargetRef)
        {
            return PresenceFulfillmentValidationResult.Rejected(PresenceFulfillmentFailure.TravelTargetMismatch);
        }

        return PresenceFulfillmentValidationResult.Accepted(term, arrivalBinding);
    }
}
