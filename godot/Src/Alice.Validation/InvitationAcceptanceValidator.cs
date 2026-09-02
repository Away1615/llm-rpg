using Alice.Commitments;
using Alice.Social;

namespace Alice.Validation;

internal enum InvitationAcceptanceValidationFailure
{
    DialogueCorrelationMismatch,
    GatheringUnavailable,
    StaleGatheringRevision,
    GatheringLifecycleUnavailable,
    InviterUnauthorized,
    DuplicateCommitmentIdentity,
    CommitConstructionFailed
}

internal readonly record struct InvitationAcceptanceValidationResult(
    InvitationAcceptanceValidationFailure? Failure)
{
    public bool IsValid => Failure is null;

    public static InvitationAcceptanceValidationResult Accepted() => new(null);

    public static InvitationAcceptanceValidationResult Rejected(InvitationAcceptanceValidationFailure failure)
    {
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        return new InvitationAcceptanceValidationResult(failure);
    }
}

internal static class InvitationAcceptanceValidator
{
    public static InvitationAcceptanceValidationResult Validate(
        AuthorityInviteAcceptanceHandoff handoff,
        ScheduledGathering? gathering,
        IReadOnlyCollection<Commitment> commitments,
        CommitmentId proposedCommitmentId)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentNullException.ThrowIfNull(commitments);
        if (gathering is null)
        {
            return InvitationAcceptanceValidationResult.Rejected(InvitationAcceptanceValidationFailure.GatheringUnavailable);
        }

        DialogueInvitePayload payload = handoff.SourceInvite.InvitePayload!;
        if (payload.ExpectedGatheringRevision != gathering.Revision)
        {
            return InvitationAcceptanceValidationResult.Rejected(InvitationAcceptanceValidationFailure.StaleGatheringRevision);
        }

        if (gathering.Lifecycle is not ScheduledGatheringLifecycle.Planned and not ScheduledGatheringLifecycle.Active)
        {
            return InvitationAcceptanceValidationResult.Rejected(InvitationAcceptanceValidationFailure.GatheringLifecycleUnavailable);
        }

        if (!gathering.AuthorizedInviterActorIds.Contains(handoff.SourceInvite.Speaker))
        {
            return InvitationAcceptanceValidationResult.Rejected(InvitationAcceptanceValidationFailure.InviterUnauthorized);
        }

        if (commitments.Any(commitment => commitment.CommitmentId == proposedCommitmentId))
        {
            return InvitationAcceptanceValidationResult.Rejected(InvitationAcceptanceValidationFailure.DuplicateCommitmentIdentity);
        }

        return InvitationAcceptanceValidationResult.Accepted();
    }
}
