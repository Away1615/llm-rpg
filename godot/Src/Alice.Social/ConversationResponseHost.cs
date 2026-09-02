using Alice.Activities;
using Alice.Authority;
using Alice.Cognition;
using Alice.Npc;

namespace Alice.Social;

public enum InviteResponseDecisionClassificationOutcome
{
    UnresolvedMandatoryInviteDecision,
    InvalidSelection,
    ClassificationConflict
}

public static class DeterministicInviteResponseDecisionClassifier
{
    public static InviteResponseDecisionClassificationOutcome Evaluate(
        RoutineSemanticResponseContext context,
        RoutineSemanticResponsePolicyResult policyResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policyResult);
        if (policyResult.Outcome != RoutineSemanticResponsePolicyOutcome.ConsequentialDecisionRequired)
        {
            return InviteResponseDecisionClassificationOutcome.ClassificationConflict;
        }

        ConversationResponseSelection selection = context.Selection;
        DialogueInvitePayload? invitePayload = selection.SourceAct.InvitePayload;
        bool exactPendingInvite = context.SourceActKind == SemanticDialogueActKind.Invite
            && invitePayload is not null
            && selection.SourceAct.ResponseExpectation == DialogueResponseExpectation.Required
            && selection.Metadata.IsMandatoryInviteResponse
            && selection.Metadata.SessionId == selection.Session.SessionId
            && selection.Metadata.OpportunityId == selection.Opportunity.OpportunityId
            && selection.Metadata.Recipient == context.RespondingActor
            && selection.Opportunity.SessionId == selection.Session.SessionId
            && selection.Opportunity.SourceActId == selection.SourceAct.ActId
            && selection.Opportunity.Recipient == context.RespondingActor
            && selection.Opportunity.OriginalSpeaker == context.OriginalSpeaker
            && invitePayload.InvitedActorId == context.RespondingActor
            && ReferenceEquals(selection.SourceTurn.Act, selection.SourceAct)
            && selection.Session.Transcript.Count(turn => ReferenceEquals(turn, selection.SourceTurn)) == 1
            && selection.Session.PendingResponseOpportunities.Count(
                opportunity => ReferenceEquals(opportunity, selection.Opportunity)) == 1;
        return exactPendingInvite
            ? InviteResponseDecisionClassificationOutcome.UnresolvedMandatoryInviteDecision
            : InviteResponseDecisionClassificationOutcome.InvalidSelection;
    }
}

public enum OrdinaryInviteResponseHostOutcome
{
    NoPending,
    InFlightBlocked,
    SchedulingConflict,
    NoRoutineRecipe,
    CandidateIdentityConflict,
    Routed,
    InviteDecisionClassificationConflict,
    DecisionRegistered,
    DecisionAlreadyRetained,
    DecisionPreviouslySeen,
    DecisionRegistrationConflict,
    RetainedDecisionOwner,
    TerminalDecisionOwnerConflict,
    InviteScoringInvalid,
    InviteCandidateIdentityConflict,
    InvitationRejectedClarified,
    InvitationRejectionFallbackConflict
}

/// <summary>Closed result for the owner-first ordinary Invite scoring Host step.</summary>
public sealed record OrdinaryInviteResponseHostResult
{
    private OrdinaryInviteResponseHostResult(
        OrdinaryInviteResponseHostOutcome outcome,
        ConversationResponseSchedulingConflict? schedulingConflict = null,
        ConversationSemanticResponseResult? routingResult = null,
        InviteResponseDecisionClassificationOutcome? classification = null,
        DecisionNeedRegistrationOutcome? registrationOutcome = null,
        MandatoryResponseDecisionOwnershipInspection? ownershipInspection = null,
        InviteResponseScoringResult? scoringResult = null,
        InviteResponseCandidateMaterializationOutcome? candidateMaterializationOutcome = null,
        InvitationAcceptanceRejectionReceipt? rejectionReceipt = null,
        SemanticDialogueTurn? rejectionFallbackTurn = null)
    {
        Outcome = outcome;
        SchedulingConflict = schedulingConflict;
        RoutingResult = routingResult;
        Classification = classification;
        RegistrationOutcome = registrationOutcome;
        OwnershipInspection = ownershipInspection;
        ScoringResult = scoringResult;
        CandidateMaterializationOutcome = candidateMaterializationOutcome;
        RejectionReceipt = rejectionReceipt;
        RejectionFallbackTurn = rejectionFallbackTurn;
    }

    public OrdinaryInviteResponseHostOutcome Outcome { get; }
    public ConversationResponseSchedulingConflict? SchedulingConflict { get; }
    public ConversationSemanticResponseResult? RoutingResult { get; }
    public InviteResponseDecisionClassificationOutcome? Classification { get; }
    public DecisionNeedRegistrationOutcome? RegistrationOutcome { get; }
    public MandatoryResponseDecisionOwnershipInspection? OwnershipInspection { get; }
    public InviteResponseScoringResult? ScoringResult { get; }
    public InviteResponseCandidateMaterializationOutcome? CandidateMaterializationOutcome { get; }
    public InvitationAcceptanceRejectionReceipt? RejectionReceipt { get; }
    public SemanticDialogueTurn? RejectionFallbackTurn { get; }

    internal static OrdinaryInviteResponseHostResult Simple(OrdinaryInviteResponseHostOutcome outcome) =>
        new(outcome);

    internal static OrdinaryInviteResponseHostResult SchedulingConflictRequired(
        ConversationResponseSchedulingConflict conflict) =>
        new(OrdinaryInviteResponseHostOutcome.SchedulingConflict, schedulingConflict: conflict);

    internal static OrdinaryInviteResponseHostResult ClassificationConflict(
        InviteResponseDecisionClassificationOutcome classification) =>
        new(
            OrdinaryInviteResponseHostOutcome.InviteDecisionClassificationConflict,
            classification: classification);

    internal static OrdinaryInviteResponseHostResult Routed(ConversationSemanticResponseResult routingResult) =>
        new(OrdinaryInviteResponseHostOutcome.Routed, routingResult: routingResult);

    internal static OrdinaryInviteResponseHostResult DecisionRegistration(
        OrdinaryInviteResponseHostOutcome outcome,
        DecisionNeedRegistrationOutcome registrationOutcome,
        InviteResponseScoringResult scoringResult) =>
        new(
            outcome,
            classification: InviteResponseDecisionClassificationOutcome.UnresolvedMandatoryInviteDecision,
            registrationOutcome: registrationOutcome,
            scoringResult: scoringResult);

    internal static OrdinaryInviteResponseHostResult RetainedOwner(
        OrdinaryInviteResponseHostOutcome outcome,
        MandatoryResponseDecisionOwnershipInspection ownershipInspection) =>
        new(
            outcome,
            classification: InviteResponseDecisionClassificationOutcome.UnresolvedMandatoryInviteDecision,
            ownershipInspection: ownershipInspection);

    internal static OrdinaryInviteResponseHostResult ScoringInvalid(InviteResponseScoringResult scoringResult) =>
        new(
            OrdinaryInviteResponseHostOutcome.InviteScoringInvalid,
            classification: InviteResponseDecisionClassificationOutcome.UnresolvedMandatoryInviteDecision,
            scoringResult: scoringResult);

    internal static OrdinaryInviteResponseHostResult MaterializationConflict(
        OrdinaryInviteResponseHostOutcome outcome,
        InviteResponseScoringResult scoringResult,
        InviteResponseCandidateMaterializationOutcome materializationOutcome,
        InvitationAcceptanceRejectionReceipt? rejectionReceipt = null) =>
        new(
            outcome,
            classification: InviteResponseDecisionClassificationOutcome.UnresolvedMandatoryInviteDecision,
            scoringResult: scoringResult,
            candidateMaterializationOutcome: materializationOutcome,
            rejectionReceipt: rejectionReceipt);

    internal static OrdinaryInviteResponseHostResult ScoredRouted(
        InviteResponseScoringResult scoringResult,
        ConversationSemanticResponseResult routingResult) =>
        new(
            OrdinaryInviteResponseHostOutcome.Routed,
            routingResult: routingResult,
            classification: InviteResponseDecisionClassificationOutcome.UnresolvedMandatoryInviteDecision,
            scoringResult: scoringResult,
            candidateMaterializationOutcome: InviteResponseCandidateMaterializationOutcome.CandidateReady);

    internal static OrdinaryInviteResponseHostResult RejectedClarified(
        InviteResponseScoringResult scoringResult,
        ConversationSemanticResponseResult initialRoutingResult,
        SemanticDialogueTurn rejectionFallbackTurn) =>
        new(
            OrdinaryInviteResponseHostOutcome.InvitationRejectedClarified,
            routingResult: initialRoutingResult,
            classification: InviteResponseDecisionClassificationOutcome.UnresolvedMandatoryInviteDecision,
            scoringResult: scoringResult,
            candidateMaterializationOutcome: InviteResponseCandidateMaterializationOutcome.CandidateReady,
            rejectionReceipt: initialRoutingResult.RejectionReceipt,
            rejectionFallbackTurn: rejectionFallbackTurn);
}

/// <summary>Composes one scheduling, policy, and existing routing attempt without owning state.</summary>
public static class ConversationResponseHost
{
    public static OrdinaryInviteResponseHostResult StepOrdinaryInviteResponse(
        ConversationResponseSchedulingSnapshot snapshot,
        InvitationAcceptanceAuthorityRuntime invitationAcceptanceAuthority,
        DecisionNeedDiscoveryRegistrar decisionRegistrar,
        NpcState respondingNpc,
        IEnumerable<Alice.Commitments.Commitment> currentOwnCommitments,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(invitationAcceptanceAuthority);
        ArgumentNullException.ThrowIfNull(decisionRegistrar);
        ArgumentNullException.ThrowIfNull(respondingNpc);
        ArgumentNullException.ThrowIfNull(currentOwnCommitments);
        ArgumentNullException.ThrowIfNull(firstObservedWorldRevision);

        ConversationResponseSchedulingResult scheduling = ConversationResponseScheduler.Schedule(snapshot);
        switch (scheduling.Outcome)
        {
            case ConversationResponseSchedulingOutcome.NoPending:
                return OrdinaryInviteResponseHostResult.Simple(OrdinaryInviteResponseHostOutcome.NoPending);
            case ConversationResponseSchedulingOutcome.InFlightBlocked:
                return OrdinaryInviteResponseHostResult.Simple(OrdinaryInviteResponseHostOutcome.InFlightBlocked);
            case ConversationResponseSchedulingOutcome.ConflictRequired:
                return OrdinaryInviteResponseHostResult.SchedulingConflictRequired(scheduling.Conflict!.Value);
            case ConversationResponseSchedulingOutcome.Scheduled:
                break;
            default:
                throw new InvalidOperationException("Unknown conversation response scheduling outcome.");
        }

        return ExecuteScheduledOrdinaryInviteResponse(
            scheduling.Selection!,
            invitationAcceptanceAuthority,
            decisionRegistrar,
            respondingNpc,
            currentOwnCommitments,
            firstObservedWorldRevision,
            createdAt);
    }

    internal static OrdinaryInviteResponseHostResult ExecuteScheduledOrdinaryInviteResponse(
        ConversationResponseSelection selection,
        InvitationAcceptanceAuthorityRuntime invitationAcceptanceAuthority,
        DecisionNeedDiscoveryRegistrar decisionRegistrar,
        NpcState respondingNpc,
        IEnumerable<Alice.Commitments.Commitment> currentOwnCommitments,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(invitationAcceptanceAuthority);
        ArgumentNullException.ThrowIfNull(decisionRegistrar);
        ArgumentNullException.ThrowIfNull(respondingNpc);
        ArgumentNullException.ThrowIfNull(currentOwnCommitments);
        ArgumentNullException.ThrowIfNull(firstObservedWorldRevision);

        RoutineSemanticResponseContext context = RoutineSemanticResponseContext.Create(selection);
        RoutineSemanticResponsePolicyResult policy = DeterministicRoutineSemanticResponsePolicy.Evaluate(context);
        switch (policy.Outcome)
        {
            case RoutineSemanticResponsePolicyOutcome.NoRoutineRecipe:
                return OrdinaryInviteResponseHostResult.Simple(OrdinaryInviteResponseHostOutcome.NoRoutineRecipe);
            case RoutineSemanticResponsePolicyOutcome.CandidateIdentityConflict:
                return OrdinaryInviteResponseHostResult.Simple(OrdinaryInviteResponseHostOutcome.CandidateIdentityConflict);
            case RoutineSemanticResponsePolicyOutcome.CandidateReady:
                ConversationSemanticResponseResult routineRouting = ConversationSemanticResponseRuntime.Route(
                    policy.CandidateSet!,
                    invitationAcceptanceAuthority);
                return OrdinaryInviteResponseHostResult.Routed(routineRouting);
            case RoutineSemanticResponsePolicyOutcome.ConsequentialDecisionRequired:
                break;
            default:
                throw new InvalidOperationException("Unknown routine semantic response policy outcome.");
        }

        InviteResponseDecisionClassificationOutcome classification =
            DeterministicInviteResponseDecisionClassifier.Evaluate(context, policy);
        if (classification != InviteResponseDecisionClassificationOutcome.UnresolvedMandatoryInviteDecision)
        {
            return OrdinaryInviteResponseHostResult.ClassificationConflict(classification);
        }

        MandatoryResponseDecisionOwnershipInspection ownership =
            decisionRegistrar.InspectMandatoryInviteResponse(context);
        if (ownership.Outcome == MandatoryResponseDecisionOwnershipOutcome.ActiveRetainedNeed)
        {
            return OrdinaryInviteResponseHostResult.RetainedOwner(
                OrdinaryInviteResponseHostOutcome.RetainedDecisionOwner,
                ownership);
        }

        if (ownership.Outcome == MandatoryResponseDecisionOwnershipOutcome.TerminalNeedConflict)
        {
            return OrdinaryInviteResponseHostResult.RetainedOwner(
                OrdinaryInviteResponseHostOutcome.TerminalDecisionOwnerConflict,
                ownership);
        }

        InviteResponseScoringResult scoring = DeterministicInviteResponseScorer.Evaluate(
            context,
            respondingNpc,
            currentOwnCommitments);
        if (scoring.Outcome == InviteResponseScoringOutcome.InvalidInput)
        {
            return OrdinaryInviteResponseHostResult.ScoringInvalid(scoring);
        }

        if (scoring.Outcome == InviteResponseScoringOutcome.StrategicDecisionRequired)
        {
            DecisionNeedRegistrationOutcome registration = decisionRegistrar.RegisterMandatoryInviteResponse(
                context,
                firstObservedWorldRevision,
                createdAt);
            return MapOrdinaryRegistration(registration, scoring);
        }

        InviteResponseCandidateMaterializationResult materialization =
            InviteResponseCandidateMaterializer.CreateOrdinary(scoring);
        if (materialization.Outcome != InviteResponseCandidateMaterializationOutcome.CandidateReady)
        {
            return OrdinaryInviteResponseHostResult.MaterializationConflict(
                OrdinaryInviteResponseHostOutcome.InviteCandidateIdentityConflict,
                scoring,
                materialization.Outcome);
        }

        RoutineSemanticResponseCandidateSet candidateSet = materialization.CandidateSet!;
        SemanticDialogueAct candidate = candidateSet.Candidates.Single();
        ConversationSemanticResponseResult routing = ConversationSemanticResponseRuntime.Route(
            candidateSet,
            invitationAcceptanceAuthority);
        if (candidate.Kind != SemanticDialogueActKind.Accept
            || routing.Outcome != ConversationSemanticResponseOutcome.InvitationRejected)
        {
            return OrdinaryInviteResponseHostResult.ScoredRouted(scoring, routing);
        }

        InviteResponseCandidateMaterializationResult fallback =
            InviteResponseCandidateMaterializer.CreateRejectedAcceptClarify(
                scoring,
                candidate,
                routing.RejectionReceipt);
        if (fallback.Outcome != InviteResponseCandidateMaterializationOutcome.CandidateReady)
        {
            return OrdinaryInviteResponseHostResult.MaterializationConflict(
                OrdinaryInviteResponseHostOutcome.InvitationRejectionFallbackConflict,
                scoring,
                fallback.Outcome,
                routing.RejectionReceipt);
        }

        ConversationSemanticResponseResult fallbackRouting = ConversationSemanticResponseRuntime.Route(
            fallback.CandidateSet!,
            invitationAcceptanceAuthority);
        if (fallbackRouting.Outcome != ConversationSemanticResponseOutcome.OrdinaryReplyRecorded
            || fallbackRouting.RecordedTurn is null)
        {
            return OrdinaryInviteResponseHostResult.MaterializationConflict(
                OrdinaryInviteResponseHostOutcome.InvitationRejectionFallbackConflict,
                scoring,
                InviteResponseCandidateMaterializationOutcome.StaleFallbackConflict,
                routing.RejectionReceipt);
        }

        return OrdinaryInviteResponseHostResult.RejectedClarified(
            scoring,
            routing,
            fallbackRouting.RecordedTurn);
    }

    private static OrdinaryInviteResponseHostResult MapOrdinaryRegistration(
        DecisionNeedRegistrationOutcome registration,
        InviteResponseScoringResult scoringResult)
    {
        return registration switch
        {
            RegisteredNew => OrdinaryInviteResponseHostResult.DecisionRegistration(
                OrdinaryInviteResponseHostOutcome.DecisionRegistered,
                registration,
                scoringResult),
            DuplicateActive => OrdinaryInviteResponseHostResult.DecisionRegistration(
                OrdinaryInviteResponseHostOutcome.DecisionAlreadyRetained,
                registration,
                scoringResult),
            StalePreviouslySeen => OrdinaryInviteResponseHostResult.DecisionRegistration(
                OrdinaryInviteResponseHostOutcome.DecisionPreviouslySeen,
                registration,
                scoringResult),
            MandatoryResponseSubjectConflict => OrdinaryInviteResponseHostResult.DecisionRegistration(
                OrdinaryInviteResponseHostOutcome.DecisionRegistrationConflict,
                registration,
                scoringResult),
            _ => throw new InvalidOperationException("Mandatory Invite registration returned an unrelated store outcome.")
        };
    }

}
