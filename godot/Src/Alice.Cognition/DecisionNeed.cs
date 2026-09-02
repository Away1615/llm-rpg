using System.Globalization;
using Alice.Activities;
using Alice.Actors;
using Alice.Npc;
using Alice.Social;

namespace Alice.Cognition;

public enum DecisionNeedState
{
    Created,
    Queued,
    InFlight,
    Resolved,
    Superseded,
    Aborted
}

public sealed class DecisionNeed
{
    private DecisionNeed(
        DecisionNeedId needId,
        ActorId npcId,
        DecisionNeedKind kind,
        DecisionNeedDiscoveryTrace discoveryTrace,
        DecisionProblemDescriptor problemDescriptor,
        SimTime createdAt,
        SimTime? deadline,
        DecisionNeedFingerprint fingerprint,
        PlanId? planId,
        PlanStepId? planStepId,
        MandatoryResponseDecisionSubject? mandatoryResponseSubject,
        DecisionNeedId? supersedesNeedId)
    {
        NeedId = needId;
        NpcId = npcId;
        Kind = kind;
        DiscoveryTrace = discoveryTrace;
        ProblemDescriptor = problemDescriptor;
        CreatedAt = createdAt;
        Deadline = deadline;
        Fingerprint = fingerprint;
        PlanId = planId;
        PlanStepId = planStepId;
        MandatoryResponseSubject = mandatoryResponseSubject;
        SupersedesNeedId = supersedesNeedId;
        State = DecisionNeedState.Created;
    }

    public DecisionNeedId NeedId { get; }
    public ActorId NpcId { get; }
    public DecisionNeedState State { get; private set; }
    public DecisionNeedKind Kind { get; }
    public DecisionNeedDiscoveryTrace DiscoveryTrace { get; }
    public DecisionProblemDescriptor ProblemDescriptor { get; }
    public SimTime CreatedAt { get; }
    public SimTime? ResolvedAt { get; private set; }
    public DecisionNeedResolutionKind? ResolutionKind { get; private set; }
    public SimTime? Deadline { get; }
    public DecisionNeedFingerprint Fingerprint { get; }
    public PlanId? PlanId { get; }
    public PlanStepId? PlanStepId { get; }
    internal MandatoryResponseDecisionSubject? MandatoryResponseSubject { get; }
    public DecisionNeedResultReference? ResultingRef { get; private set; }
    public int AttemptCount { get; private set; }
    public DecisionNeedId? SupersedesNeedId { get; }

    public static DecisionNeed Create(
        ActorId npcId,
        PlanId? planId,
        PlanStepId? planStepId,
        DecisionNeedKind kind,
        DecisionProblemDescriptor problemDescriptor,
        DecisionNeedDiscoveryTrace discoveryTrace,
        DecisionNeedWorldRevision firstWorldRevision,
        SimTime createdAt,
        SimTime? deadline = null,
        DecisionNeedId? supersedesNeedId = null)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(problemDescriptor);
        ArgumentNullException.ThrowIfNull(discoveryTrace);
        ArgumentNullException.ThrowIfNull(firstWorldRevision);
        if (problemDescriptor is InviteResponseDecisionProblemDescriptor)
        {
            throw new ArgumentException(
                "Mandatory Invite response Needs must use the internal correlation-validating factory.",
                nameof(problemDescriptor));
        }

        DecisionNeedIdentityValidation.ValidateActorAndStepCorrelation(
            npcId,
            planId,
            planStepId,
            problemDescriptor);
        if (deadline is not null && deadline.Value.Ticks < createdAt.Ticks)
        {
            throw new ArgumentOutOfRangeException(nameof(deadline));
        }

        DecisionProblemDescriptorHash descriptorHash = problemDescriptor.DescriptorHash;
        DecisionNeedFingerprint fingerprint = DecisionNeedCanonicalJson.CreateFingerprint(
            npcId,
            planId,
            planStepId,
            kind,
            descriptorHash);
        DecisionNeedId needId = DecisionNeedCanonicalJson.CreateNeedId(fingerprint, firstWorldRevision);
        if (supersedesNeedId == needId)
        {
            throw new ArgumentException("A Decision Need cannot supersede its own identity.", nameof(supersedesNeedId));
        }

        return new DecisionNeed(
            needId,
            npcId,
            kind,
            discoveryTrace,
            problemDescriptor,
            createdAt,
            deadline,
            fingerprint,
            planId,
            planStepId,
            null,
            supersedesNeedId);
    }

    internal static DecisionNeed CreateMandatoryResponse(
        RoutineSemanticResponseContext context,
        InviteResponseDecisionProblemDescriptor problemDescriptor,
        DecisionNeedDiscoveryTrace discoveryTrace,
        DecisionNeedWorldRevision firstWorldRevision,
        SimTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(problemDescriptor);
        ArgumentNullException.ThrowIfNull(discoveryTrace);
        ArgumentNullException.ThrowIfNull(firstWorldRevision);

        MandatoryResponseDecisionSubject subject = MandatoryResponseDecisionSubject.Create(context);
        ValidateMandatoryResponseDescriptor(context, problemDescriptor);
        if (discoveryTrace.Route != DecisionNeedDiscoveryRoute.MandatoryResponse
            || !StringComparer.Ordinal.Equals(discoveryTrace.SourceId.Value, subject.CanonicalValue))
        {
            throw new ArgumentException(
                "A mandatory-response Need requires the exact system-only response subject trace.",
                nameof(discoveryTrace));
        }

        var kind = new DecisionNeedKind("invite_response_unresolved");
        DecisionNeedFingerprint fingerprint = DecisionNeedCanonicalJson.CreateMandatoryResponseFingerprint(
            context.RespondingActor,
            kind,
            problemDescriptor.DescriptorHash,
            subject);
        DecisionNeedId needId = DecisionNeedCanonicalJson.CreateNeedId(fingerprint, firstWorldRevision);
        return new DecisionNeed(
            needId,
            context.RespondingActor,
            kind,
            discoveryTrace,
            problemDescriptor,
            createdAt,
            null,
            fingerprint,
            null,
            null,
            subject,
            null);
    }

    public void Queue()
    {
        EnsureState(DecisionNeedState.Created);
        State = DecisionNeedState.Queued;
    }

    public void BeginInFlightAttempt()
    {
        EnsureState(DecisionNeedState.Queued);
        int nextAttemptCount = checked(AttemptCount + 1);
        AttemptCount = nextAttemptCount;
        State = DecisionNeedState.InFlight;
    }

    public void ReturnToQueuedAfterRetryableTransportFailure()
    {
        EnsureState(DecisionNeedState.InFlight);
        State = DecisionNeedState.Queued;
    }

    internal void ReturnMandatoryResponseToQueued()
    {
        EnsureState(DecisionNeedState.InFlight);
        if (MandatoryResponseSubject is null)
        {
            throw new InvalidOperationException("Only a mandatory response Need may use this requeue transition.");
        }

        State = DecisionNeedState.Queued;
    }

    public void Resolve(
        SimTime resolvedAt,
        DecisionNeedResolutionKind resolutionKind,
        DecisionNeedResultReference? resultingRef)
    {
        EnsureState(DecisionNeedState.InFlight);
        if (resolvedAt.Ticks < CreatedAt.Ticks)
        {
            throw new ArgumentOutOfRangeException(nameof(resolvedAt));
        }

        ValidateResolutionPairing(resolutionKind, resultingRef);
        ResolvedAt = resolvedAt;
        ResolutionKind = resolutionKind;
        ResultingRef = resultingRef;
        State = DecisionNeedState.Resolved;
    }

    public void Supersede()
    {
        if (State is not DecisionNeedState.Queued and not DecisionNeedState.InFlight)
        {
            throw new InvalidOperationException("Only a Queued or InFlight Decision Need can be superseded.");
        }

        State = DecisionNeedState.Superseded;
    }

    public void Abort()
    {
        if (State is not DecisionNeedState.Created and not DecisionNeedState.Queued and not DecisionNeedState.InFlight)
        {
            throw new InvalidOperationException("Only an active Decision Need can be aborted.");
        }

        State = DecisionNeedState.Aborted;
    }

    private static void ValidateResolutionPairing(
        DecisionNeedResolutionKind resolutionKind,
        DecisionNeedResultReference? resultingRef)
    {
        bool valid = resolutionKind switch
        {
            DecisionNeedResolutionKind.CreatePlan or DecisionNeedResolutionKind.RevisePlan =>
                resultingRef is DecisionNeedPlanResultReference,
            DecisionNeedResolutionKind.Verify => resultingRef is DecisionNeedGoalResultReference,
            DecisionNeedResolutionKind.Defer or DecisionNeedResolutionKind.Cancel => resultingRef is null,
            DecisionNeedResolutionKind.Respond => resultingRef is DecisionNeedSemanticActResultReference,
            DecisionNeedResolutionKind.ExecuteAction => resultingRef is DecisionNeedExecutionResultReference,
            _ => throw new ArgumentOutOfRangeException(nameof(resolutionKind))
        };
        if (!valid)
        {
            throw new ArgumentException("Resolution kind and resulting reference do not form a legal pair.", nameof(resultingRef));
        }
    }

    private static void ValidateMandatoryResponseDescriptor(
        RoutineSemanticResponseContext context,
        InviteResponseDecisionProblemDescriptor descriptor)
    {
        ConversationResponseSelection selection = context.Selection;
        DialogueInvitePayload? invitePayload = selection.SourceAct.InvitePayload;
        if (selection.SourceAct.Kind != SemanticDialogueActKind.Invite
            || invitePayload is null
            || selection.SourceAct.ResponseExpectation != DialogueResponseExpectation.Required
            || !ReferenceEquals(selection.SourceTurn.Act, selection.SourceAct)
            || selection.Session.Transcript.Count(turn => ReferenceEquals(turn, selection.SourceTurn)) != 1
            || selection.Session.PendingResponseOpportunities.Count(
                opportunity => ReferenceEquals(opportunity, selection.Opportunity)) != 1
            || selection.Opportunity.SessionId != selection.Session.SessionId
            || selection.Opportunity.SourceActId != selection.SourceAct.ActId
            || selection.Opportunity.Recipient != context.RespondingActor
            || selection.Opportunity.OriginalSpeaker != context.OriginalSpeaker
            || invitePayload.InvitedActorId != context.RespondingActor)
        {
            throw new ArgumentException("The mandatory Invite response selection is not exact and pending.", nameof(context));
        }

        if (descriptor.ActorId != context.RespondingActor
            || descriptor.ProblemCode != new DecisionProblemCode("mandatory_invite_response")
            || descriptor.OriginalSpeaker != context.OriginalSpeaker
            || descriptor.GatheringRef != invitePayload.GatheringRef
            || descriptor.ExpectedGatheringRevision != invitePayload.ExpectedGatheringRevision
            || descriptor.BelievedAuthorizationRef != invitePayload.BelievedAuthorizationRef
            || descriptor.TopicRef != context.TopicRef
            || !CanonicalClaimsEqual(descriptor.ClaimReferences, context.ClaimReferences))
        {
            throw new ArgumentException("The mandatory-response descriptor must be the exact actor-visible Invite snapshot.", nameof(descriptor));
        }
    }

    internal bool MatchesMandatoryResponseContext(RoutineSemanticResponseContext context)
    {
        if (MandatoryResponseSubject is null
            || ProblemDescriptor is not InviteResponseDecisionProblemDescriptor descriptor)
        {
            return false;
        }

        try
        {
            ValidateMandatoryResponseDescriptor(context, descriptor);
            return MandatoryResponseDecisionSubject.Create(context) == MandatoryResponseSubject;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool CanonicalClaimsEqual(
        IReadOnlyList<DialogueClaimReference> descriptorClaims,
        IReadOnlyList<DialogueClaimReference> contextClaims)
    {
        DialogueClaimReference[] contextSnapshot = contextClaims
            .OrderBy(claim => claim.ClaimRef.Value, StringComparer.Ordinal)
            .ThenBy(claim => claim.ProvenanceRef.Value, StringComparer.Ordinal)
            .ToArray();
        if (descriptorClaims.Count != contextSnapshot.Length)
        {
            return false;
        }

        for (int index = 0; index < contextSnapshot.Length; index++)
        {
            if (descriptorClaims[index].ClaimRef != contextSnapshot[index].ClaimRef
                || descriptorClaims[index].ProvenanceRef != contextSnapshot[index].ProvenanceRef)
            {
                return false;
            }
        }

        return true;
    }

    private void EnsureState(DecisionNeedState expected)
    {
        if (State != expected)
        {
            throw new InvalidOperationException($"Decision Need must be {expected} for this transition.");
        }
    }
}

internal sealed record MandatoryResponseDecisionSubject
{
    private const string CanonicalPrefix = "mandatory-response:v1:";

    private MandatoryResponseDecisionSubject(
        ActorId actorId,
        ConversationSessionId sessionId,
        DialogueResponseOpportunityId opportunityId,
        SemanticDialogueActId sourceActId)
    {
        ActorId = actorId;
        SessionId = sessionId;
        OpportunityId = opportunityId;
        SourceActId = sourceActId;
        CanonicalValue = string.Concat(
            CanonicalPrefix,
            Encode(actorId.Value),
            Encode(sessionId.Value),
            Encode(opportunityId.Value),
            Encode(sourceActId.Value));
    }

    public ActorId ActorId { get; }
    public ConversationSessionId SessionId { get; }
    public DialogueResponseOpportunityId OpportunityId { get; }
    public SemanticDialogueActId SourceActId { get; }
    public string CanonicalValue { get; }

    internal static MandatoryResponseDecisionSubject Create(RoutineSemanticResponseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ConversationResponseSelection selection = context.Selection;
        if (selection.Opportunity.Recipient != context.RespondingActor
            || selection.Opportunity.SessionId != selection.Session.SessionId
            || selection.Opportunity.SourceActId != selection.SourceAct.ActId)
        {
            throw new ArgumentException("The response subject correlation is inconsistent.", nameof(context));
        }

        return new MandatoryResponseDecisionSubject(
            context.RespondingActor,
            selection.Session.SessionId,
            selection.Opportunity.OpportunityId,
            selection.SourceAct.ActId);
    }

    private static string Encode(string value) =>
        string.Concat(value.Length.ToString(CultureInfo.InvariantCulture), ":", value);
}
