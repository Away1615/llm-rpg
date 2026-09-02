using Alice.Commitments;
using Alice.Social;

namespace Alice.Authority;

public enum ConversationSemanticResponseOutcome
{
    NoRoutineCandidate,
    SemanticDecisionRequired,
    StaleSelectionConflict,
    OrdinaryReplyRecorded,
    InvitationAccepted,
    InvitationRejected,
    UnsupportedSettlementRequired
}

/// <summary>One bounded routing result with only already-owned semantic or Authority evidence.</summary>
public sealed record ConversationSemanticResponseResult
{
    private ConversationSemanticResponseResult(
        ConversationSemanticResponseOutcome outcome,
        SemanticDialogueTurn? recordedTurn = null,
        Commitment? commitment = null,
        InvitationAcceptanceRejectionReceipt? rejectionReceipt = null)
    {
        Outcome = outcome;
        RecordedTurn = recordedTurn;
        Commitment = commitment;
        RejectionReceipt = rejectionReceipt;
    }

    public ConversationSemanticResponseOutcome Outcome { get; }
    public SemanticDialogueTurn? RecordedTurn { get; }
    public Commitment? Commitment { get; }
    public InvitationAcceptanceRejectionReceipt? RejectionReceipt { get; }

    internal static ConversationSemanticResponseResult NoRoutineCandidate() =>
        new(ConversationSemanticResponseOutcome.NoRoutineCandidate);

    internal static ConversationSemanticResponseResult SemanticDecisionRequired() =>
        new(ConversationSemanticResponseOutcome.SemanticDecisionRequired);

    internal static ConversationSemanticResponseResult StaleSelectionConflict() =>
        new(ConversationSemanticResponseOutcome.StaleSelectionConflict);

    internal static ConversationSemanticResponseResult OrdinaryReplyRecorded(SemanticDialogueTurn turn) =>
        new(ConversationSemanticResponseOutcome.OrdinaryReplyRecorded, recordedTurn: turn);

    internal static ConversationSemanticResponseResult InvitationAccepted(
        SemanticDialogueTurn turn,
        Commitment commitment) =>
        new(
            ConversationSemanticResponseOutcome.InvitationAccepted,
            recordedTurn: turn,
            commitment: commitment);

    internal static ConversationSemanticResponseResult InvitationRejected(
        InvitationAcceptanceRejectionReceipt? rejectionReceipt) =>
        new(
            ConversationSemanticResponseOutcome.InvitationRejected,
            rejectionReceipt: rejectionReceipt);

    internal static ConversationSemanticResponseResult UnsupportedSettlementRequired() =>
        new(ConversationSemanticResponseOutcome.UnsupportedSettlementRequired);
}

/// <summary>Stateless router from one bounded semantic candidate set to existing conversation owners.</summary>
public static class ConversationSemanticResponseRuntime
{
    public static ConversationSemanticResponseResult Route(
        RoutineSemanticResponseCandidateSet candidateSet,
        InvitationAcceptanceAuthorityRuntime invitationAcceptanceAuthority)
    {
        ArgumentNullException.ThrowIfNull(candidateSet);
        ArgumentNullException.ThrowIfNull(invitationAcceptanceAuthority);

        if (candidateSet.Candidates.Count == 0)
        {
            return ConversationSemanticResponseResult.NoRoutineCandidate();
        }

        if (candidateSet.Candidates.Count > 1)
        {
            return ConversationSemanticResponseResult.SemanticDecisionRequired();
        }

        ConversationResponseSelection selection = candidateSet.Selection;
        SemanticDialogueAct candidate = candidateSet.Candidates[0];
        if (!IsSelectionCurrent(selection, candidate))
        {
            return ConversationSemanticResponseResult.StaleSelectionConflict();
        }

        if (candidate.Kind == SemanticDialogueActKind.Accept)
        {
            if (selection.SourceAct.Kind != SemanticDialogueActKind.Invite)
            {
                return ConversationSemanticResponseResult.UnsupportedSettlementRequired();
            }

            InvitationAcceptanceSettlementResult settlement = invitationAcceptanceAuthority.TrySettle(
                selection.Session,
                selection.Opportunity,
                candidate);
            if (!settlement.IsSettled)
            {
                return ConversationSemanticResponseResult.InvitationRejected(settlement.RejectionReceipt);
            }

            return ConversationSemanticResponseResult.InvitationAccepted(
                settlement.RecordedTurn!,
                settlement.Commitment!);
        }

        DialogueReplyResult reply;
        try
        {
            reply = selection.Session.Reply(selection.Opportunity, candidate);
        }
        catch (ArgumentException)
        {
            return ConversationSemanticResponseResult.StaleSelectionConflict();
        }

        return reply.Outcome == DialogueReplyOutcome.Recorded && reply.RecordedTurn is not null
            ? ConversationSemanticResponseResult.OrdinaryReplyRecorded(reply.RecordedTurn)
            : ConversationSemanticResponseResult.StaleSelectionConflict();
    }

    private static bool IsSelectionCurrent(
        ConversationResponseSelection selection,
        SemanticDialogueAct candidate)
    {
        ConversationSession session = selection.Session;
        DialogueResponseOpportunity opportunity = selection.Opportunity;
        SemanticDialogueTurn sourceTurn = selection.SourceTurn;
        SemanticDialogueAct sourceAct = selection.SourceAct;

        return selection.Metadata.SessionId == session.SessionId
            && selection.Metadata.OpportunityId == opportunity.OpportunityId
            && selection.Metadata.Recipient == opportunity.Recipient
            && selection.Metadata.SourceSequence == sourceTurn.Sequence
            && selection.Metadata.IsMandatoryInviteResponse == (sourceAct.Kind == SemanticDialogueActKind.Invite)
            && opportunity.SessionId == session.SessionId
            && opportunity.SourceActId == sourceAct.ActId
            && opportunity.OriginalSpeaker == sourceAct.Speaker
            && sourceAct.Recipients.Contains(opportunity.Recipient)
            && ReferenceEquals(sourceTurn.Act, sourceAct)
            && session.PendingResponseOpportunities.Count(pending => ReferenceEquals(pending, opportunity)) == 1
            && session.Transcript.Count(turn => ReferenceEquals(turn, sourceTurn)) == 1
            && !session.Transcript.Any(turn => turn.Act.ActId == candidate.ActId);
    }
}
