using System.Collections.ObjectModel;
using System.Globalization;
using Alice.Actors;
using Alice.Authority;
using Alice.Commitments;
using Alice.Npc;

namespace Alice.Social;

public enum InviteResponseScoringOutcome
{
    OrdinaryCandidate,
    StrategicDecisionRequired,
    InvalidInput
}

public enum InviteResponseStrategicReason
{
    MissingInviterAppraisal,
    MissingRoutineInviteAcceptanceValue,
    MixedStrongRelationshipEvidence,
    ActiveOwnCommitment,
    ScoreWithinStrategicBand
}

public enum InviteResponseScoringInvalidReason
{
    InvalidSelection,
    RespondingActorStateMismatch,
    CommitmentActorMismatch,
    DuplicateCommitmentIdentity
}

/// <summary>Public actor-visible score evidence with no system correlation or Authority state.</summary>
public sealed record InviteResponseScoreEvidence
{
    internal InviteResponseScoreEvidence(
        string scorerVersion,
        double? positiveMean,
        double? negativeMean,
        double? relationshipNet,
        double? personalityNet,
        double? inviteScore)
    {
        ScorerVersion = scorerVersion;
        PositiveMean = positiveMean;
        NegativeMean = negativeMean;
        RelationshipNet = relationshipNet;
        PersonalityNet = personalityNet;
        InviteScore = inviteScore;
    }

    public string ScorerVersion { get; }
    public double? PositiveMean { get; }
    public double? NegativeMean { get; }
    public double? RelationshipNet { get; }
    public double? PersonalityNet { get; }
    public double? InviteScore { get; }
}

/// <summary>Immutable actor-owned input projected for one exact pending Invite.</summary>
public sealed class InviteResponseScoringInput
{
    private readonly ReadOnlyCollection<CommitmentStatus> _ownCommitmentStatuses;

    private InviteResponseScoringInput(
        RoutineSemanticResponseContext context,
        NpcRelationshipAppraisal? inviterAppraisal,
        double? routineInviteAcceptance,
        IEnumerable<CommitmentStatus> ownCommitmentStatuses)
    {
        Context = context;
        InviterAppraisal = inviterAppraisal;
        RoutineInviteAcceptance = routineInviteAcceptance;
        _ownCommitmentStatuses = Array.AsReadOnly(ownCommitmentStatuses.ToArray());
    }

    public NpcRelationshipAppraisal? InviterAppraisal { get; }
    public double? RoutineInviteAcceptance { get; }
    public IReadOnlyList<CommitmentStatus> OwnCommitmentStatuses => _ownCommitmentStatuses;

    internal RoutineSemanticResponseContext Context { get; }

    internal static InviteResponseScoringInput? TryCreate(
        RoutineSemanticResponseContext context,
        NpcState respondingNpc,
        IEnumerable<Commitment> currentOwnCommitments,
        out InviteResponseScoringInvalidReason? invalidReason)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(respondingNpc);
        ArgumentNullException.ThrowIfNull(currentOwnCommitments);

        if (!IsExactPendingInvite(context))
        {
            invalidReason = InviteResponseScoringInvalidReason.InvalidSelection;
            return null;
        }

        if (respondingNpc.ActorId != context.RespondingActor)
        {
            invalidReason = InviteResponseScoringInvalidReason.RespondingActorStateMismatch;
            return null;
        }

        Commitment[] commitmentSnapshot = currentOwnCommitments.ToArray();
        var commitmentIds = new HashSet<CommitmentId>();
        foreach (Commitment commitment in commitmentSnapshot)
        {
            if (commitment is null || commitment.Debtor != context.RespondingActor)
            {
                invalidReason = InviteResponseScoringInvalidReason.CommitmentActorMismatch;
                return null;
            }

            if (!commitmentIds.Add(commitment.CommitmentId))
            {
                invalidReason = InviteResponseScoringInvalidReason.DuplicateCommitmentIdentity;
                return null;
            }
        }

        NpcRelationshipAppraisal? appraisal = respondingNpc.Social.FindAppraisal(context.OriginalSpeaker);
        WeightedPersonalityValue? personalityValue = respondingNpc.Personality.Values.SingleOrDefault(
            value => StringComparer.Ordinal.Equals(
                value.ValueIdentity.Value,
                DeterministicInviteResponseScorer.RoutineInviteAcceptanceValueIdentity));
        invalidReason = null;
        return new InviteResponseScoringInput(
            context,
            appraisal,
            personalityValue?.Weight,
            commitmentSnapshot.Select(commitment => commitment.Status));
    }

    private static bool IsExactPendingInvite(RoutineSemanticResponseContext context)
    {
        ConversationResponseSelection selection = context.Selection;
        DialogueInvitePayload? payload = selection.SourceAct.InvitePayload;
        return context.SourceActKind == SemanticDialogueActKind.Invite
            && payload is not null
            && selection.SourceAct.ResponseExpectation == DialogueResponseExpectation.Required
            && selection.Metadata.IsMandatoryInviteResponse
            && selection.Metadata.SessionId == selection.Session.SessionId
            && selection.Metadata.OpportunityId == selection.Opportunity.OpportunityId
            && selection.Metadata.Recipient == context.RespondingActor
            && selection.Opportunity.SessionId == selection.Session.SessionId
            && selection.Opportunity.SourceActId == selection.SourceAct.ActId
            && selection.Opportunity.Recipient == context.RespondingActor
            && selection.Opportunity.OriginalSpeaker == context.OriginalSpeaker
            && payload.InvitedActorId == context.RespondingActor
            && ReferenceEquals(selection.SourceTurn.Act, selection.SourceAct)
            && selection.Session.Transcript.Count(turn => ReferenceEquals(turn, selection.SourceTurn)) == 1
            && selection.Session.PendingResponseOpportunities.Count(
                opportunity => ReferenceEquals(opportunity, selection.Opportunity)) == 1;
    }
}

/// <summary>Closed v1 Invite scorer result.</summary>
public sealed record InviteResponseScoringResult
{
    private InviteResponseScoringResult(
        InviteResponseScoringOutcome outcome,
        InviteResponseScoringInput? input,
        InviteResponseScoreEvidence evidence,
        SemanticDialogueActKind? ordinaryResponseKind = null,
        InviteResponseStrategicReason? strategicReason = null,
        InviteResponseScoringInvalidReason? invalidReason = null)
    {
        Outcome = outcome;
        Input = input;
        Evidence = evidence;
        OrdinaryResponseKind = ordinaryResponseKind;
        StrategicReason = strategicReason;
        InvalidReason = invalidReason;
    }

    public InviteResponseScoringOutcome Outcome { get; }
    public InviteResponseScoringInput? Input { get; }
    public InviteResponseScoreEvidence Evidence { get; }
    public SemanticDialogueActKind? OrdinaryResponseKind { get; }
    public InviteResponseStrategicReason? StrategicReason { get; }
    public InviteResponseScoringInvalidReason? InvalidReason { get; }

    internal static InviteResponseScoringResult Ordinary(
        InviteResponseScoringInput input,
        InviteResponseScoreEvidence evidence,
        SemanticDialogueActKind kind) =>
        new(InviteResponseScoringOutcome.OrdinaryCandidate, input, evidence, ordinaryResponseKind: kind);

    internal static InviteResponseScoringResult Strategic(
        InviteResponseScoringInput input,
        InviteResponseScoreEvidence evidence,
        InviteResponseStrategicReason reason) =>
        new(InviteResponseScoringOutcome.StrategicDecisionRequired, input, evidence, strategicReason: reason);

    internal static InviteResponseScoringResult Invalid(InviteResponseScoringInvalidReason reason) =>
        new(
            InviteResponseScoringOutcome.InvalidInput,
            null,
            DeterministicInviteResponseScorer.EmptyEvidence(),
            invalidReason: reason);
}

public static class DeterministicInviteResponseScorer
{
    public const string ScorerVersion = "ordinary_invite_response_v1";
    public const string RoutineInviteAcceptanceValueIdentity = "routine_invite_acceptance";

    public static InviteResponseScoringResult Evaluate(
        ConversationResponseSelection selection,
        NpcState respondingNpc,
        IEnumerable<Commitment> currentOwnCommitments)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return Evaluate(
            RoutineSemanticResponseContext.Create(selection),
            respondingNpc,
            currentOwnCommitments);
    }

    internal static InviteResponseScoringResult Evaluate(
        RoutineSemanticResponseContext context,
        NpcState respondingNpc,
        IEnumerable<Commitment> currentOwnCommitments)
    {
        InviteResponseScoringInput? input = InviteResponseScoringInput.TryCreate(
            context,
            respondingNpc,
            currentOwnCommitments,
            out InviteResponseScoringInvalidReason? invalidReason);
        if (input is null)
        {
            return InviteResponseScoringResult.Invalid(invalidReason!.Value);
        }

        return Evaluate(input);
    }

    public static InviteResponseScoringResult Evaluate(InviteResponseScoringInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.InviterAppraisal is null)
        {
            return InviteResponseScoringResult.Strategic(
                input,
                EmptyEvidence(),
                InviteResponseStrategicReason.MissingInviterAppraisal);
        }

        NpcRelationshipAppraisal appraisal = input.InviterAppraisal;
        double positiveMean = ((appraisal.Familiarity + appraisal.Trust) + (appraisal.Affection + appraisal.Respect)) / 4.0;
        double negativeMean = (appraisal.Fear + appraisal.Grievance) / 2.0;
        double relationshipNet = positiveMean - negativeMean;
        if (input.RoutineInviteAcceptance is null)
        {
            return InviteResponseScoringResult.Strategic(
                input,
                new InviteResponseScoreEvidence(
                    ScorerVersion,
                    positiveMean,
                    negativeMean,
                    relationshipNet,
                    null,
                    null),
                InviteResponseStrategicReason.MissingRoutineInviteAcceptanceValue);
        }

        double personalityNet = (2.0 * input.RoutineInviteAcceptance.Value) - 1.0;
        double inviteScore = (relationshipNet + personalityNet) / 2.0;
        var evidence = new InviteResponseScoreEvidence(
            ScorerVersion,
            positiveMean,
            negativeMean,
            relationshipNet,
            personalityNet,
            inviteScore);
        if (positiveMean >= 0.75 && negativeMean >= 0.75)
        {
            return InviteResponseScoringResult.Strategic(
                input,
                evidence,
                InviteResponseStrategicReason.MixedStrongRelationshipEvidence);
        }

        if (input.OwnCommitmentStatuses.Contains(CommitmentStatus.Active))
        {
            return InviteResponseScoringResult.Strategic(
                input,
                evidence,
                InviteResponseStrategicReason.ActiveOwnCommitment);
        }

        if (inviteScore > 0.25)
        {
            return InviteResponseScoringResult.Ordinary(input, evidence, SemanticDialogueActKind.Accept);
        }

        if (inviteScore < -0.25)
        {
            return InviteResponseScoringResult.Ordinary(input, evidence, SemanticDialogueActKind.Decline);
        }

        return InviteResponseScoringResult.Strategic(
            input,
            evidence,
            InviteResponseStrategicReason.ScoreWithinStrategicBand);
    }

    internal static InviteResponseScoreEvidence EmptyEvidence() =>
        new(ScorerVersion, null, null, null, null, null);
}

public enum InviteResponseCandidateMaterializationOutcome
{
    CandidateReady,
    CandidateIdentityConflict,
    RejectionReceiptConflict,
    StaleFallbackConflict
}

public sealed record InviteResponseCandidateMaterializationResult
{
    private InviteResponseCandidateMaterializationResult(
        InviteResponseCandidateMaterializationOutcome outcome,
        RoutineSemanticResponseCandidateSet? candidateSet)
    {
        Outcome = outcome;
        CandidateSet = candidateSet;
    }

    public InviteResponseCandidateMaterializationOutcome Outcome { get; }
    public RoutineSemanticResponseCandidateSet? CandidateSet { get; }

    internal static InviteResponseCandidateMaterializationResult Ready(RoutineSemanticResponseCandidateSet candidateSet) =>
        new(InviteResponseCandidateMaterializationOutcome.CandidateReady, candidateSet);

    internal static InviteResponseCandidateMaterializationResult Conflict(InviteResponseCandidateMaterializationOutcome outcome) =>
        new(outcome, null);
}

public static class InviteResponseCandidateMaterializer
{
    private const string CandidateIdPrefix = "l0-invite-response:v1:";
    private const string RejectedAcceptClarifyPrefix = "l0-invite-response:v1:accept-rejected-clarify:";

    public static InviteResponseCandidateMaterializationResult CreateOrdinary(
        InviteResponseScoringResult scoringResult)
    {
        ArgumentNullException.ThrowIfNull(scoringResult);
        if (scoringResult.Outcome != InviteResponseScoringOutcome.OrdinaryCandidate
            || scoringResult.Input is null
            || scoringResult.OrdinaryResponseKind is not SemanticDialogueActKind.Accept and not SemanticDialogueActKind.Decline)
        {
            throw new ArgumentException("Only one exact ordinary Invite score may be materialized.", nameof(scoringResult));
        }

        return Create(
            scoringResult.Input.Context.Selection,
            scoringResult.OrdinaryResponseKind.Value,
            CandidateIdPrefix);
    }

    public static InviteResponseCandidateMaterializationResult CreateRejectedAcceptClarify(
        InviteResponseScoringResult scoringResult,
        SemanticDialogueAct attemptedAccept,
        InvitationAcceptanceRejectionReceipt? receipt)
    {
        ArgumentNullException.ThrowIfNull(scoringResult);
        ArgumentNullException.ThrowIfNull(attemptedAccept);
        if (scoringResult.Outcome != InviteResponseScoringOutcome.OrdinaryCandidate
            || scoringResult.OrdinaryResponseKind != SemanticDialogueActKind.Accept
            || scoringResult.Input is null)
        {
            throw new ArgumentException("A rejection fallback requires the exact ordinary Accept score.", nameof(scoringResult));
        }

        ConversationResponseSelection selection = scoringResult.Input.Context.Selection;
        if (receipt is null
            || receipt.SessionId != selection.Session.SessionId
            || receipt.SourceInviteActId != selection.SourceAct.ActId
            || receipt.AttemptedAcceptActId != attemptedAccept.ActId
            || receipt.InviteeActorId != scoringResult.Input.Context.RespondingActor
            || receipt.VisibleToActorIds.Count != 2
            || !receipt.VisibleToActorIds.Contains(scoringResult.Input.Context.RespondingActor)
            || !receipt.VisibleToActorIds.Contains(scoringResult.Input.Context.OriginalSpeaker)
            || attemptedAccept.ActId != CreateActId(selection, SemanticDialogueActKind.Accept, CandidateIdPrefix)
            || attemptedAccept.Kind != SemanticDialogueActKind.Accept
            || attemptedAccept.Speaker != scoringResult.Input.Context.RespondingActor
            || attemptedAccept.Recipients.Count != 1
            || attemptedAccept.Recipients[0] != scoringResult.Input.Context.OriginalSpeaker
            || attemptedAccept.TopicRef != selection.SourceAct.TopicRef
            || attemptedAccept.ClaimReferences.Count != 0
            || attemptedAccept.InvitePayload is not null
            || attemptedAccept.ResponseExpectation != DialogueResponseExpectation.Required)
        {
            return InviteResponseCandidateMaterializationResult.Conflict(
                InviteResponseCandidateMaterializationOutcome.RejectionReceiptConflict);
        }

        return Create(selection, SemanticDialogueActKind.Clarify, RejectedAcceptClarifyPrefix);
    }

    private static InviteResponseCandidateMaterializationResult Create(
        ConversationResponseSelection selection,
        SemanticDialogueActKind kind,
        string prefix)
    {
        SemanticDialogueActId actId = CreateActId(selection, kind, prefix);
        if (selection.Session.Transcript.Any(turn => turn.Act.ActId == actId))
        {
            return InviteResponseCandidateMaterializationResult.Conflict(
                InviteResponseCandidateMaterializationOutcome.CandidateIdentityConflict);
        }

        var candidate = new SemanticDialogueAct(
            actId,
            kind,
            selection.Opportunity.Recipient,
            [selection.Opportunity.OriginalSpeaker],
            selection.SourceAct.TopicRef,
            [],
            null,
            DialogueResponseExpectation.Required);
        return InviteResponseCandidateMaterializationResult.Ready(
            new RoutineSemanticResponseCandidateSet(selection, [candidate]));
    }

    private static SemanticDialogueActId CreateActId(
        ConversationResponseSelection selection,
        SemanticDialogueActKind kind,
        string prefix) =>
        new(string.Concat(
            prefix,
            Encode(selection.Session.SessionId.Value),
            Encode(selection.Opportunity.OpportunityId.Value),
            Encode(selection.SourceAct.ActId.Value),
            Encode(selection.Opportunity.Recipient.Value),
            Encode(selection.Opportunity.OriginalSpeaker.Value),
            Encode(kind == SemanticDialogueActKind.Accept ? "accept" : kind == SemanticDialogueActKind.Decline ? "decline" : "clarify")));

    private static string Encode(string value) =>
        string.Concat(value.Length.ToString(CultureInfo.InvariantCulture), ":", value);
}
