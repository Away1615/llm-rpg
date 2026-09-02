using System.Collections.ObjectModel;
using Alice.Actors;

namespace Alice.Social;

/// <summary>The sole closed catalogue of primary semantic dialogue acts.</summary>
public enum SemanticDialogueActKind
{
    Ask,
    Inform,
    Clarify,
    Request,
    Offer,
    Recommend,
    Invite,
    Accept,
    Decline,
    CounterOffer,
    Warn,
    Apologize,
    Thank,
    Complain,
    Tease,
    Comfort,
    Congratulate,
    CasualComment,
    ShareNews,
    ShareGossip
}

public enum DialogueResponseExpectation
{
    Required,
    None
}

public readonly record struct ConversationSessionId(string Value);
public readonly record struct SemanticDialogueActId(string Value);
public readonly record struct DialogueTopicRef(string Value);
public readonly record struct DialogueClaimRef(string Value);
public readonly record struct ClaimProvenanceRef(string Value);
public readonly record struct GatheringRef(string Value);
public readonly record struct BelievedAuthorizationRef(string Value);
public readonly record struct DialogueResponseOpportunityId(string Value);

/// <summary>A claim reference paired with the source from which the speaker received it.</summary>
public sealed record DialogueClaimReference
{
    public DialogueClaimReference(DialogueClaimRef claimRef, ClaimProvenanceRef provenanceRef)
    {
        SemanticDialogueIdentity.Validate(claimRef.Value, nameof(claimRef));
        SemanticDialogueIdentity.Validate(provenanceRef.Value, nameof(provenanceRef));

        ClaimRef = claimRef;
        ProvenanceRef = provenanceRef;
    }

    public DialogueClaimRef ClaimRef { get; }
    public ClaimProvenanceRef ProvenanceRef { get; }
}

/// <summary>Actor-visible invitation details; this is not an Authority settlement request.</summary>
public sealed record DialogueInvitePayload
{
    public DialogueInvitePayload(
        GatheringRef gatheringRef,
        int expectedGatheringRevision,
        ActorId invitedActorId,
        BelievedAuthorizationRef? believedAuthorizationRef)
    {
        SemanticDialogueIdentity.Validate(gatheringRef.Value, nameof(gatheringRef));
        SemanticDialogueIdentity.ValidateActor(invitedActorId, nameof(invitedActorId));
        if (expectedGatheringRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedGatheringRevision));
        }

        if (believedAuthorizationRef is { } authorizationRef)
        {
            SemanticDialogueIdentity.Validate(authorizationRef.Value, nameof(believedAuthorizationRef));
        }

        GatheringRef = gatheringRef;
        ExpectedGatheringRevision = expectedGatheringRevision;
        InvitedActorId = invitedActorId;
        BelievedAuthorizationRef = believedAuthorizationRef;
    }

    public GatheringRef GatheringRef { get; }
    public int ExpectedGatheringRevision { get; }
    public ActorId InvitedActorId { get; }
    public BelievedAuthorizationRef? BelievedAuthorizationRef { get; }
}

/// <summary>An immutable, one-primary-act semantic turn with no natural-language or world-effect surface.</summary>
public sealed record SemanticDialogueAct
{
    private readonly ReadOnlyCollection<ActorId> _recipients;
    private readonly ReadOnlyCollection<DialogueClaimReference> _claimReferences;

    public SemanticDialogueAct(
        SemanticDialogueActId actId,
        SemanticDialogueActKind kind,
        ActorId speaker,
        IEnumerable<ActorId> recipients,
        DialogueTopicRef? topicRef,
        IEnumerable<DialogueClaimReference> claimReferences,
        DialogueInvitePayload? invitePayload)
        : this(
            actId,
            kind,
            speaker,
            recipients,
            topicRef,
            claimReferences,
            invitePayload,
            DialogueResponseExpectation.Required)
    {
    }

    public SemanticDialogueAct(
        SemanticDialogueActId actId,
        SemanticDialogueActKind kind,
        ActorId speaker,
        IEnumerable<ActorId> recipients,
        DialogueTopicRef? topicRef,
        IEnumerable<DialogueClaimReference> claimReferences,
        DialogueInvitePayload? invitePayload,
        DialogueResponseExpectation responseExpectation)
    {
        SemanticDialogueIdentity.Validate(actId.Value, nameof(actId));
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!Enum.IsDefined(responseExpectation))
        {
            throw new ArgumentOutOfRangeException(nameof(responseExpectation));
        }

        SemanticDialogueIdentity.ValidateActor(speaker, nameof(speaker));
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentNullException.ThrowIfNull(claimReferences);

        ActorId[] recipientSnapshot = recipients.ToArray();
        if (recipientSnapshot.Length == 0)
        {
            throw new ArgumentException("A semantic dialogue act requires at least one recipient.", nameof(recipients));
        }

        foreach (ActorId recipient in recipientSnapshot)
        {
            SemanticDialogueIdentity.ValidateActor(recipient, nameof(recipients));
            if (recipient == speaker)
            {
                throw new ArgumentException("The speaker cannot be a recipient of the same semantic dialogue act.", nameof(recipients));
            }
        }

        if (recipientSnapshot.Distinct().Count() != recipientSnapshot.Length)
        {
            throw new ArgumentException("Recipients must be distinct.", nameof(recipients));
        }

        DialogueClaimReference[] claimSnapshot = claimReferences.ToArray();
        foreach (DialogueClaimReference claimReference in claimSnapshot)
        {
            ArgumentNullException.ThrowIfNull(claimReference);
        }

        if (claimSnapshot.Select(reference => reference.ClaimRef).Distinct().Count() != claimSnapshot.Length)
        {
            throw new ArgumentException("Claim references must be distinct.", nameof(claimReferences));
        }

        if (topicRef is { } topic)
        {
            SemanticDialogueIdentity.Validate(topic.Value, nameof(topicRef));
        }

        if (kind == SemanticDialogueActKind.Invite)
        {
            ArgumentNullException.ThrowIfNull(invitePayload);
            if (recipientSnapshot.Length != 1 || invitePayload.InvitedActorId != recipientSnapshot[0])
            {
                throw new ArgumentException("An Invite must name its sole recipient as the invited actor.", nameof(invitePayload));
            }
        }
        else if (invitePayload is not null)
        {
            throw new ArgumentException("Only an Invite can carry an invitation payload.", nameof(invitePayload));
        }

        ActId = actId;
        Kind = kind;
        Speaker = speaker;
        _recipients = Array.AsReadOnly(recipientSnapshot);
        TopicRef = topicRef;
        _claimReferences = Array.AsReadOnly(claimSnapshot);
        InvitePayload = invitePayload;
        ResponseExpectation = responseExpectation;
    }

    public SemanticDialogueActId ActId { get; }
    public SemanticDialogueActKind Kind { get; }
    public ActorId Speaker { get; }
    public IReadOnlyList<ActorId> Recipients => _recipients;
    public DialogueTopicRef? TopicRef { get; }
    public IReadOnlyList<DialogueClaimReference> ClaimReferences => _claimReferences;
    public DialogueInvitePayload? InvitePayload { get; }
    public readonly DialogueResponseExpectation ResponseExpectation;
}

/// <summary>One transcript entry retaining the exact accepted act and its deterministic order.</summary>
public sealed record SemanticDialogueTurn
{
    public SemanticDialogueTurn(int sequence, SemanticDialogueAct act)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        ArgumentNullException.ThrowIfNull(act);
        Sequence = sequence;
        Act = act;
    }

    public int Sequence { get; }
    public SemanticDialogueAct Act { get; }
}

/// <summary>The actor-visible semantic experience derived directly from one accepted act.</summary>
public sealed record SemanticDialogueExperience
{
    public SemanticDialogueExperience(ConversationSessionId sessionId, SemanticDialogueActId sourceActId, ActorId visibleToActorId)
    {
        SemanticDialogueIdentity.Validate(sessionId.Value, nameof(sessionId));
        SemanticDialogueIdentity.Validate(sourceActId.Value, nameof(sourceActId));
        SemanticDialogueIdentity.ValidateActor(visibleToActorId, nameof(visibleToActorId));

        SessionId = sessionId;
        SourceActId = sourceActId;
        VisibleToActorId = visibleToActorId;
    }

    public ConversationSessionId SessionId { get; }
    public SemanticDialogueActId SourceActId { get; }
    public ActorId VisibleToActorId { get; }
}

/// <summary>A pending response obligation correlated to one recipient of one source act.</summary>
public sealed record DialogueResponseOpportunity
{
    public DialogueResponseOpportunity(
        DialogueResponseOpportunityId opportunityId,
        ConversationSessionId sessionId,
        SemanticDialogueActId sourceActId,
        ActorId originalSpeaker,
        ActorId recipient)
    {
        SemanticDialogueIdentity.Validate(opportunityId.Value, nameof(opportunityId));
        SemanticDialogueIdentity.Validate(sessionId.Value, nameof(sessionId));
        SemanticDialogueIdentity.Validate(sourceActId.Value, nameof(sourceActId));
        SemanticDialogueIdentity.ValidateActor(originalSpeaker, nameof(originalSpeaker));
        SemanticDialogueIdentity.ValidateActor(recipient, nameof(recipient));
        if (originalSpeaker == recipient)
        {
            throw new ArgumentException("A response opportunity requires distinct source speaker and recipient.", nameof(recipient));
        }

        OpportunityId = opportunityId;
        SessionId = sessionId;
        SourceActId = sourceActId;
        OriginalSpeaker = originalSpeaker;
        Recipient = recipient;
    }

    public DialogueResponseOpportunityId OpportunityId { get; }
    public ConversationSessionId SessionId { get; }
    public SemanticDialogueActId SourceActId { get; }
    public ActorId OriginalSpeaker { get; }
    public ActorId Recipient { get; }
}

public enum DialogueReplyOutcome
{
    Recorded,
    AuthoritySettlementRequired
}

/// <summary>The reply recording result; AuthoritySettlementRequired intentionally has no mutation payload.</summary>
public sealed record DialogueReplyResult
{
    private DialogueReplyResult(DialogueReplyOutcome outcome, SemanticDialogueTurn? recordedTurn)
    {
        Outcome = outcome;
        RecordedTurn = recordedTurn;
    }

    public DialogueReplyOutcome Outcome { get; }
    public SemanticDialogueTurn? RecordedTurn { get; }

    internal static DialogueReplyResult Recorded(SemanticDialogueTurn turn) => new(DialogueReplyOutcome.Recorded, turn);
    internal static DialogueReplyResult AuthoritySettlementRequired() => new(DialogueReplyOutcome.AuthoritySettlementRequired, null);
}

/// <summary>An in-memory session that owns its transcript, derived experiences, and pending response opportunities.</summary>
public sealed class ConversationSession
{
    private readonly ReadOnlyCollection<ActorId> _participants;
    private readonly List<SemanticDialogueTurn> _transcript = [];
    private readonly List<SemanticDialogueExperience> _experiences = [];
    private readonly List<DialogueResponseOpportunity> _pendingResponseOpportunities = [];
    private readonly HashSet<SemanticDialogueActId> _acceptedActIds = [];

    public ConversationSession(ConversationSessionId sessionId, IEnumerable<ActorId> participants)
    {
        SemanticDialogueIdentity.Validate(sessionId.Value, nameof(sessionId));
        ArgumentNullException.ThrowIfNull(participants);

        ActorId[] participantSnapshot = participants.ToArray();
        if (participantSnapshot.Length == 0)
        {
            throw new ArgumentException("A conversation session requires at least one participant.", nameof(participants));
        }

        foreach (ActorId participant in participantSnapshot)
        {
            SemanticDialogueIdentity.ValidateActor(participant, nameof(participants));
        }

        if (participantSnapshot.Distinct().Count() != participantSnapshot.Length)
        {
            throw new ArgumentException("Conversation session participants must be distinct.", nameof(participants));
        }

        SessionId = sessionId;
        _participants = Array.AsReadOnly(participantSnapshot);
    }

    public ConversationSessionId SessionId { get; }
    public IReadOnlyList<ActorId> Participants => _participants;
    public IReadOnlyList<SemanticDialogueTurn> Transcript => _transcript.AsReadOnly();
    public IReadOnlyList<SemanticDialogueExperience> Experiences => _experiences.AsReadOnly();
    public IReadOnlyList<DialogueResponseOpportunity> PendingResponseOpportunities => _pendingResponseOpportunities.AsReadOnly();

    public SemanticDialogueTurn Accept(SemanticDialogueAct act)
    {
        ArgumentNullException.ThrowIfNull(act);
        ValidateActForSession(act);
        if (_acceptedActIds.Contains(act.ActId))
        {
            throw new ArgumentException("The semantic dialogue act was already accepted by this session.", nameof(act));
        }

        return AppendValidatedAct(act);
    }

    public DialogueReplyResult Reply(DialogueResponseOpportunity opportunity, SemanticDialogueAct reply)
    {
        ArgumentNullException.ThrowIfNull(opportunity);
        ArgumentNullException.ThrowIfNull(reply);

        DialogueResponseOpportunity pendingOpportunity = ValidateReply(opportunity, reply);
        SemanticDialogueAct sourceAct = _transcript.Single(turn => turn.Act.ActId == pendingOpportunity.SourceActId).Act;
        if (sourceAct.Kind == SemanticDialogueActKind.Invite && reply.Kind == SemanticDialogueActKind.Accept)
        {
            return DialogueReplyResult.AuthoritySettlementRequired();
        }

        SemanticDialogueTurn turn = AppendValidatedAct(reply);
        _pendingResponseOpportunities.Remove(pendingOpportunity);
        return DialogueReplyResult.Recorded(turn);
    }

    public void AbandonResponse(DialogueResponseOpportunity opportunity)
    {
        ArgumentNullException.ThrowIfNull(opportunity);
        if (opportunity.SessionId != SessionId)
            throw new ArgumentException("The response opportunity belongs to another conversation session.", nameof(opportunity));
        DialogueResponseOpportunity pending = _pendingResponseOpportunities.SingleOrDefault(candidate =>
            candidate.OpportunityId == opportunity.OpportunityId)
            ?? throw new ArgumentException("The response opportunity is not pending in this session.", nameof(opportunity));
        _pendingResponseOpportunities.Remove(pending);
    }

    /// <summary>Persistence-only reconstruction from accepted acts plus the exact currently pending opportunity set.</summary>
    public static ConversationSession Restore(
        ConversationSessionId sessionId,
        IEnumerable<ActorId> participants,
        IEnumerable<SemanticDialogueAct> acceptedActs,
        IEnumerable<DialogueResponseOpportunityId> pendingOpportunityIds)
    {
        ArgumentNullException.ThrowIfNull(acceptedActs);
        ArgumentNullException.ThrowIfNull(pendingOpportunityIds);
        var session = new ConversationSession(sessionId, participants);
        foreach (SemanticDialogueAct act in acceptedActs)
        {
            ArgumentNullException.ThrowIfNull(act);
            session.ValidateActForSession(act);
            if (session._acceptedActIds.Contains(act.ActId))
            {
                throw new ArgumentException("Restored semantic act identities must be unique.", nameof(acceptedActs));
            }

            session.AppendValidatedAct(act);
        }

        DialogueResponseOpportunityId[] pending = pendingOpportunityIds.ToArray();
        if (pending.Distinct().Count() != pending.Length)
        {
            throw new ArgumentException("Restored pending opportunity identities must be unique.", nameof(pendingOpportunityIds));
        }

        var pendingSet = new HashSet<DialogueResponseOpportunityId>(pending);
        DialogueResponseOpportunity[] restoredPending = session._pendingResponseOpportunities
            .Where(opportunity => pendingSet.Contains(opportunity.OpportunityId))
            .ToArray();
        if (restoredPending.Length != pending.Length)
        {
            throw new ArgumentException("Restored pending opportunity identity was not derived from an accepted turn.", nameof(pendingOpportunityIds));
        }

        session._pendingResponseOpportunities.Clear();
        session._pendingResponseOpportunities.AddRange(restoredPending);
        return session;
    }

    internal AuthorityInviteAcceptanceHandoff ResolveAuthorityInviteAcceptance(
        DialogueResponseOpportunity opportunity,
        SemanticDialogueAct proposedAccept)
    {
        ArgumentNullException.ThrowIfNull(opportunity);
        ArgumentNullException.ThrowIfNull(proposedAccept);

        DialogueResponseOpportunity pendingOpportunity = ValidateReply(opportunity, proposedAccept);
        if (proposedAccept.Kind != SemanticDialogueActKind.Accept)
        {
            throw new ArgumentException("Invitation settlement requires an Accept reply.", nameof(proposedAccept));
        }

        SemanticDialogueAct sourceInvite = _transcript
            .Single(turn => turn.Act.ActId == pendingOpportunity.SourceActId)
            .Act;
        if (sourceInvite.Kind != SemanticDialogueActKind.Invite || sourceInvite.InvitePayload is null)
        {
            throw new ArgumentException("The response opportunity does not identify an Invite.", nameof(opportunity));
        }

        return new AuthorityInviteAcceptanceHandoff(this, pendingOpportunity, sourceInvite, proposedAccept);
    }

    internal SemanticDialogueTurn RecordAuthorityInviteAcceptance(AuthorityInviteAcceptanceHandoff handoff)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        if (!ReferenceEquals(handoff.Session, this)
            || !_pendingResponseOpportunities.Any(candidate => ReferenceEquals(candidate, handoff.PendingOpportunity))
            || _acceptedActIds.Contains(handoff.ProposedAccept.ActId))
        {
            throw new InvalidOperationException("The prepared invitation acceptance is no longer current.");
        }

        SemanticDialogueTurn turn = AppendValidatedAct(handoff.ProposedAccept);
        _pendingResponseOpportunities.Remove(handoff.PendingOpportunity);
        return turn;
    }

    private void ValidateActForSession(SemanticDialogueAct act)
    {
        if (!_participants.Contains(act.Speaker) || act.Recipients.Any(recipient => !_participants.Contains(recipient)))
        {
            throw new ArgumentException("The speaker and every recipient must be participants in this conversation session.", nameof(act));
        }
    }

    private DialogueResponseOpportunity ValidateReply(DialogueResponseOpportunity opportunity, SemanticDialogueAct reply)
    {
        if (opportunity.SessionId != SessionId)
        {
            throw new ArgumentException("The response opportunity belongs to another conversation session.", nameof(opportunity));
        }

        DialogueResponseOpportunity? pendingOpportunity = _pendingResponseOpportunities.SingleOrDefault(candidate => candidate.OpportunityId == opportunity.OpportunityId);
        if (pendingOpportunity is null || pendingOpportunity != opportunity)
        {
            throw new ArgumentException("The response opportunity is not pending in this exact session.", nameof(opportunity));
        }

        ValidateActForSession(reply);
        if (_acceptedActIds.Contains(reply.ActId))
        {
            throw new ArgumentException("The reply act was already accepted by this session.", nameof(reply));
        }

        if (reply.Speaker != pendingOpportunity.Recipient || reply.Recipients.Count != 1 || reply.Recipients[0] != pendingOpportunity.OriginalSpeaker)
        {
            throw new ArgumentException("A reply must be from the pending recipient to the source speaker only.", nameof(reply));
        }

        return pendingOpportunity;
    }

    private SemanticDialogueTurn AppendValidatedAct(SemanticDialogueAct act)
    {
        var turn = new SemanticDialogueTurn(checked(_transcript.Count + 1), act);
        SemanticDialogueExperience[] experiences = [
            new SemanticDialogueExperience(SessionId, act.ActId, act.Speaker),
            .. act.Recipients.Select(recipient => new SemanticDialogueExperience(SessionId, act.ActId, recipient))
        ];
        DialogueResponseOpportunity[] opportunities = act.ResponseExpectation == DialogueResponseExpectation.Required
            ? act.Recipients.Select(recipient => CreateOpportunity(act, recipient)).ToArray()
            : [];

        _acceptedActIds.Add(act.ActId);
        _transcript.Add(turn);
        _experiences.AddRange(experiences);
        _pendingResponseOpportunities.AddRange(opportunities);
        return turn;
    }

    private DialogueResponseOpportunity CreateOpportunity(SemanticDialogueAct act, ActorId recipient)
    {
        string value = string.Concat(
            EncodeOpportunityIdComponent(SessionId.Value),
            EncodeOpportunityIdComponent(act.ActId.Value),
            EncodeOpportunityIdComponent(recipient.Value));
        return new DialogueResponseOpportunity(
            new DialogueResponseOpportunityId(value),
            SessionId,
            act.ActId,
            act.Speaker,
            recipient);
    }

    private static string EncodeOpportunityIdComponent(string value) => string.Concat(value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), ":", value);
}

internal static class SemanticDialogueIdentity
{
    public static void Validate(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identity values must be non-empty.", parameterName);
        }
    }

    public static void ValidateActor(ActorId actorId, string parameterName) => Validate(actorId.Value, parameterName);
}

internal sealed record AuthorityInviteAcceptanceHandoff(
    ConversationSession Session,
    DialogueResponseOpportunity PendingOpportunity,
    SemanticDialogueAct SourceInvite,
    SemanticDialogueAct ProposedAccept);
