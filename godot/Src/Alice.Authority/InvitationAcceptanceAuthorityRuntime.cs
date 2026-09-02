using System.Collections.ObjectModel;
using System.Globalization;
using Alice.Actors;
using Alice.Commitments;
using Alice.Social;
using Alice.Validation;

namespace Alice.Authority;

/// <summary>Actor-visible projection of one invitation acceptance that Authority did not settle.</summary>
public sealed record InvitationAcceptanceRejectionReceipt
{
    private readonly ReadOnlyCollection<ActorId> _visibleToActorIds;

    internal InvitationAcceptanceRejectionReceipt(
        ConversationSessionId sessionId,
        SemanticDialogueActId sourceInviteActId,
        SemanticDialogueActId attemptedAcceptActId,
        ActorId inviteeActorId,
        ActorId inviteSpeakerActorId)
    {
        SessionId = sessionId;
        SourceInviteActId = sourceInviteActId;
        AttemptedAcceptActId = attemptedAcceptActId;
        InviteeActorId = inviteeActorId;
        _visibleToActorIds = Array.AsReadOnly([inviteSpeakerActorId, inviteeActorId]);
    }

    public ConversationSessionId SessionId { get; }
    public SemanticDialogueActId SourceInviteActId { get; }
    public SemanticDialogueActId AttemptedAcceptActId { get; }
    public ActorId InviteeActorId { get; }
    public IReadOnlyList<ActorId> VisibleToActorIds => _visibleToActorIds;
}

/// <summary>One Authority invitation-acceptance attempt with projected public failure data only.</summary>
public sealed class InvitationAcceptanceSettlementResult
{
    internal InvitationAcceptanceSettlementResult(Commitment commitment, SemanticDialogueTurn recordedTurn)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(recordedTurn);
        Commitment = commitment;
        RecordedTurn = recordedTurn;
    }

    internal InvitationAcceptanceSettlementResult(
        InvitationAcceptanceRejectionReceipt? rejectionReceipt,
        InvitationAcceptanceValidationFailure failure)
    {
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        RejectionReceipt = rejectionReceipt;
        Failure = failure;
    }

    public bool IsSettled => Commitment is not null;
    public Commitment? Commitment { get; }
    public SemanticDialogueTurn? RecordedTurn { get; }
    public InvitationAcceptanceRejectionReceipt? RejectionReceipt { get; }
    internal InvitationAcceptanceValidationFailure? Failure { get; }
}

/// <summary>Synchronous Authority owner for immutable gathering terms and attendance Commitments.</summary>
public sealed class InvitationAcceptanceAuthorityRuntime
{
    private const string CommitmentIdPrefix = "invite-acceptance-v1:";
    private readonly ScheduledGatheringAuthorityRuntime _gatheringAuthority;
    private readonly List<Commitment> _commitments;

    public InvitationAcceptanceAuthorityRuntime(
        ScheduledGatheringAuthorityRuntime gatheringAuthority,
        IEnumerable<Commitment>? commitments = null)
    {
        ArgumentNullException.ThrowIfNull(gatheringAuthority);
        Commitment[] commitmentSnapshot = commitments?.ToArray() ?? [];
        EnsureUniqueCommitments(commitmentSnapshot);
        _gatheringAuthority = gatheringAuthority;
        _commitments = [.. commitmentSnapshot];
    }

    public IReadOnlyList<ScheduledGathering> Gatherings => _gatheringAuthority.Gatherings;
    public IReadOnlyList<Commitment> Commitments => _commitments.AsReadOnly();

    internal ScheduledGathering? FindCurrentGathering(GatheringRef gatheringRef) =>
        _gatheringAuthority.FindCurrent(gatheringRef);

    internal Commitment? FindCommitment(CommitmentId commitmentId) =>
        _commitments.SingleOrDefault(candidate => candidate.CommitmentId == commitmentId);

    internal void ReplaceCommitment(Commitment expectedCurrent, Commitment replacement)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(replacement);
        int index = _commitments.FindIndex(candidate => candidate.CommitmentId == expectedCurrent.CommitmentId);
        if (index < 0 || !ReferenceEquals(_commitments[index], expectedCurrent))
        {
            throw new InvalidOperationException("The exact current Commitment must still be owned before replacement.");
        }

        if (replacement.CommitmentId != expectedCurrent.CommitmentId ||
            replacement.Debtor != expectedCurrent.Debtor ||
            replacement.Creditor != expectedCurrent.Creditor ||
            !ReferenceEquals(replacement.Term, expectedCurrent.Term) ||
            replacement.Deadline != expectedCurrent.Deadline ||
            !ReferenceEquals(replacement.SourceRef, expectedCurrent.SourceRef) ||
            expectedCurrent.Status != CommitmentStatus.Active ||
            replacement.Status != CommitmentStatus.Fulfilled)
        {
            throw new ArgumentException("Presence fulfilment may replace only Active status with Fulfilled.", nameof(replacement));
        }

        _commitments[index] = replacement;
    }

    public InvitationAcceptanceSettlementResult TrySettle(
        ConversationSession session,
        DialogueResponseOpportunity opportunity,
        SemanticDialogueAct proposedAccept)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(opportunity);
        ArgumentNullException.ThrowIfNull(proposedAccept);

        AuthorityInviteAcceptanceHandoff handoff;
        try
        {
            handoff = session.ResolveAuthorityInviteAcceptance(opportunity, proposedAccept);
        }
        catch (ArgumentException)
        {
            InvitationAcceptanceRejectionReceipt? trustedReceipt = TryCreateRejectionReceipt(
                session,
                opportunity.OpportunityId,
                proposedAccept.ActId);
            return Rejected(trustedReceipt, InvitationAcceptanceValidationFailure.DialogueCorrelationMismatch);
        }

        InvitationAcceptanceRejectionReceipt rejectionReceipt = CreateRejectionReceipt(
            session.SessionId,
            handoff.SourceInvite,
            proposedAccept.ActId);
        DialogueInvitePayload invitePayload = handoff.SourceInvite.InvitePayload!;
        ScheduledGathering? gathering = _gatheringAuthority.FindCurrent(invitePayload.GatheringRef);
        CommitmentId commitmentId = CreateCommitmentId(
            session.SessionId,
            handoff.SourceInvite.ActId,
            invitePayload.InvitedActorId);
        InvitationAcceptanceValidationResult validation = InvitationAcceptanceValidator.Validate(
            handoff,
            gathering,
            _commitments,
            commitmentId);
        if (!validation.IsValid)
        {
            return Rejected(rejectionReceipt, validation.Failure!.Value);
        }

        Commitment commitment;
        try
        {
            commitment = new Commitment(
                commitmentId,
                invitePayload.InvitedActorId,
                gathering!.HostActorId,
                new PresenceWindowTerm(invitePayload.GatheringRef, invitePayload.ExpectedGatheringRevision),
                gathering.EndsAt,
                CommitmentStatus.Active,
                new CommitmentSourceRef(session.SessionId, handoff.SourceInvite.ActId));
        }
        catch (ArgumentException)
        {
            return Rejected(rejectionReceipt, InvitationAcceptanceValidationFailure.CommitConstructionFailed);
        }

        SemanticDialogueTurn recordedTurn = session.RecordAuthorityInviteAcceptance(handoff);
        _commitments.Add(commitment);
        return new InvitationAcceptanceSettlementResult(commitment, recordedTurn);
    }

    private static InvitationAcceptanceSettlementResult Rejected(
        InvitationAcceptanceRejectionReceipt? receipt,
        InvitationAcceptanceValidationFailure failure) => new(receipt, failure);

    private static InvitationAcceptanceRejectionReceipt? TryCreateRejectionReceipt(
        ConversationSession session,
        DialogueResponseOpportunityId opportunityId,
        SemanticDialogueActId attemptedAcceptActId)
    {
        DialogueResponseOpportunity? pendingOpportunity = session.PendingResponseOpportunities
            .SingleOrDefault(candidate => candidate.OpportunityId == opportunityId);
        if (pendingOpportunity is null)
        {
            return null;
        }

        SemanticDialogueAct? sourceInvite = session.Transcript
            .Single(turn => turn.Act.ActId == pendingOpportunity.SourceActId)
            .Act;
        if (sourceInvite.Kind != SemanticDialogueActKind.Invite || sourceInvite.InvitePayload is null)
        {
            return null;
        }

        return CreateRejectionReceipt(session.SessionId, sourceInvite, attemptedAcceptActId);
    }

    private static InvitationAcceptanceRejectionReceipt CreateRejectionReceipt(
        ConversationSessionId sessionId,
        SemanticDialogueAct sourceInvite,
        SemanticDialogueActId attemptedAcceptActId) =>
        new(
            sessionId,
            sourceInvite.ActId,
            attemptedAcceptActId,
            sourceInvite.InvitePayload!.InvitedActorId,
            sourceInvite.Speaker);

    private static CommitmentId CreateCommitmentId(
        ConversationSessionId sessionId,
        SemanticDialogueActId sourceInviteActId,
        ActorId debtor) =>
        new(string.Concat(
            CommitmentIdPrefix,
            EncodeIdentityComponent(sessionId.Value),
            EncodeIdentityComponent(sourceInviteActId.Value),
            EncodeIdentityComponent(debtor.Value)));

    private static string EncodeIdentityComponent(string value) =>
        string.Concat(value.Length.ToString(CultureInfo.InvariantCulture), ":", value);

    private static void EnsureUniqueCommitments(IEnumerable<Commitment> commitments)
    {
        var identities = new HashSet<CommitmentId>();
        foreach (Commitment commitment in commitments)
        {
            ArgumentNullException.ThrowIfNull(commitment);
            if (!identities.Add(commitment.CommitmentId))
            {
                throw new ArgumentException("Authority Commitment snapshots must be unique.", nameof(commitments));
            }
        }
    }
}
