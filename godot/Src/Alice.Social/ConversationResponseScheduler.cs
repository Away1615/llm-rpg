using System.Collections.ObjectModel;
using Alice.Actors;

namespace Alice.Social;

/// <summary>An immutable scheduling epoch over live conversation owners and exact responses already in flight.</summary>
public sealed class ConversationResponseSchedulingSnapshot
{
    private readonly ReadOnlyCollection<ConversationSession> _sessions;
    private readonly ReadOnlyCollection<DialogueResponseOpportunityId> _inFlightOpportunityIds;

    public ConversationResponseSchedulingSnapshot(
        IEnumerable<ConversationSession> sessions,
        IEnumerable<DialogueResponseOpportunityId> inFlightOpportunityIds)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(inFlightOpportunityIds);

        ConversationSession[] sessionSnapshot = sessions.ToArray();
        var sessionIdentities = new Dictionary<ConversationSessionId, ConversationSession>();
        foreach (ConversationSession? session in sessionSnapshot)
        {
            if (session is null)
            {
                throw new ArgumentException("Scheduling sessions must be non-null.", nameof(sessions));
            }

            if (sessionIdentities.TryGetValue(session.SessionId, out ConversationSession? existingSession))
            {
                string message = ReferenceEquals(existingSession, session)
                    ? "A conversation session identity was supplied more than once."
                    : "Distinct conversation session objects cannot claim the same identity.";
                throw new ArgumentException(message, nameof(sessions));
            }

            sessionIdentities.Add(session.SessionId, session);
        }

        DialogueResponseOpportunityId[] inFlightSnapshot = inFlightOpportunityIds.ToArray();
        if (inFlightSnapshot.Distinct().Count() != inFlightSnapshot.Length)
        {
            throw new ArgumentException("In-flight response opportunity identities must be distinct.", nameof(inFlightOpportunityIds));
        }

        var knownOpportunityIds = sessionSnapshot
            .SelectMany(session => session.PendingResponseOpportunities)
            .Select(opportunity => opportunity.OpportunityId)
            .ToHashSet();
        if (inFlightSnapshot.Any(inFlightId => !knownOpportunityIds.Contains(inFlightId)))
        {
            throw new ArgumentException("Every in-flight response opportunity must currently be pending in a supplied session.", nameof(inFlightOpportunityIds));
        }

        _sessions = Array.AsReadOnly(sessionSnapshot);
        _inFlightOpportunityIds = Array.AsReadOnly(inFlightSnapshot);
    }

    public IReadOnlyList<ConversationSession> Sessions => _sessions;
    public IReadOnlyList<DialogueResponseOpportunityId> InFlightOpportunityIds => _inFlightOpportunityIds;
}

public enum ConversationResponseSchedulingOutcome
{
    NoPending,
    InFlightBlocked,
    ConflictRequired,
    Scheduled
}

public enum ConversationResponseSchedulingConflict
{
    PendingOpportunityIdentityConflict,
    SessionCorrelationMismatch,
    SourceTurnCorrelationMismatch,
    InFlightOpportunityNoLongerPending
}

/// <summary>Stable value metadata for one exact selected pending response.</summary>
public readonly record struct ConversationResponseSchedulingMetadata(
    bool IsMandatoryInviteResponse,
    int SourceSequence,
    ConversationSessionId SessionId,
    DialogueResponseOpportunityId OpportunityId,
    ActorId Recipient);

/// <summary>Exact live references selected for the existing reply or invitation-settlement path.</summary>
public sealed record ConversationResponseSelection
{
    internal ConversationResponseSelection(
        ConversationSession session,
        DialogueResponseOpportunity opportunity,
        SemanticDialogueTurn sourceTurn)
    {
        Session = session;
        Opportunity = opportunity;
        SourceTurn = sourceTurn;
        SourceAct = sourceTurn.Act;
        Metadata = new ConversationResponseSchedulingMetadata(
            SourceAct.Kind == SemanticDialogueActKind.Invite,
            sourceTurn.Sequence,
            session.SessionId,
            opportunity.OpportunityId,
            opportunity.Recipient);
    }

    public ConversationSession Session { get; }
    public DialogueResponseOpportunity Opportunity { get; }
    public SemanticDialogueTurn SourceTurn { get; }
    public SemanticDialogueAct SourceAct { get; }
    public ConversationResponseSchedulingMetadata Metadata { get; }
}

/// <summary>A typed scheduling result with no mutation or response-consumption authority.</summary>
public sealed record ConversationResponseSchedulingResult
{
    private ConversationResponseSchedulingResult(
        ConversationResponseSchedulingOutcome outcome,
        ConversationResponseSelection? selection,
        ConversationResponseSchedulingConflict? conflict)
    {
        Outcome = outcome;
        Selection = selection;
        Conflict = conflict;
    }

    public ConversationResponseSchedulingOutcome Outcome { get; }
    public ConversationResponseSelection? Selection { get; }
    public ConversationResponseSchedulingConflict? Conflict { get; }

    internal static ConversationResponseSchedulingResult NoPending() =>
        new(ConversationResponseSchedulingOutcome.NoPending, null, null);

    internal static ConversationResponseSchedulingResult InFlightBlocked() =>
        new(ConversationResponseSchedulingOutcome.InFlightBlocked, null, null);

    internal static ConversationResponseSchedulingResult ConflictRequired(ConversationResponseSchedulingConflict conflict) =>
        new(ConversationResponseSchedulingOutcome.ConflictRequired, null, conflict);

    internal static ConversationResponseSchedulingResult Scheduled(ConversationResponseSelection selection) =>
        new(ConversationResponseSchedulingOutcome.Scheduled, selection, null);
}

/// <summary>Pure deterministic selector for pending ordinary-conversation responses.</summary>
public static class ConversationResponseScheduler
{
    public static ConversationResponseSchedulingResult Schedule(ConversationResponseSchedulingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var pending = new List<PendingResponseCandidate>();
        var opportunityIds = new HashSet<DialogueResponseOpportunityId>();
        foreach (ConversationSession session in snapshot.Sessions)
        {
            foreach (DialogueResponseOpportunity opportunity in session.PendingResponseOpportunities)
            {
                if (!opportunityIds.Add(opportunity.OpportunityId))
                {
                    return ConversationResponseSchedulingResult.ConflictRequired(
                        ConversationResponseSchedulingConflict.PendingOpportunityIdentityConflict);
                }

                if (opportunity.SessionId != session.SessionId)
                {
                    return ConversationResponseSchedulingResult.ConflictRequired(
                        ConversationResponseSchedulingConflict.SessionCorrelationMismatch);
                }

                SemanticDialogueTurn[] sourceTurns = session.Transcript
                    .Where(turn => turn.Act.ActId == opportunity.SourceActId)
                    .ToArray();
                if (sourceTurns.Length != 1
                    || sourceTurns[0].Act.Speaker != opportunity.OriginalSpeaker
                    || !sourceTurns[0].Act.Recipients.Contains(opportunity.Recipient))
                {
                    return ConversationResponseSchedulingResult.ConflictRequired(
                        ConversationResponseSchedulingConflict.SourceTurnCorrelationMismatch);
                }

                pending.Add(new PendingResponseCandidate(session, opportunity, sourceTurns[0]));
            }
        }

        var pendingById = pending.ToDictionary(candidate => candidate.Opportunity.OpportunityId);
        var busyRecipients = new HashSet<ActorId>();
        foreach (DialogueResponseOpportunityId inFlightId in snapshot.InFlightOpportunityIds)
        {
            if (!pendingById.TryGetValue(inFlightId, out PendingResponseCandidate? candidate))
            {
                return ConversationResponseSchedulingResult.ConflictRequired(
                    ConversationResponseSchedulingConflict.InFlightOpportunityNoLongerPending);
            }

            busyRecipients.Add(candidate.Opportunity.Recipient);
        }

        if (pending.Count == 0)
        {
            return ConversationResponseSchedulingResult.NoPending();
        }

        PendingResponseCandidate? selected = pending
            .Where(candidate => !busyRecipients.Contains(candidate.Opportunity.Recipient))
            .OrderBy(candidate => candidate.SourceTurn.Act.Kind == SemanticDialogueActKind.Invite ? 0 : 1)
            .ThenBy(candidate => candidate.SourceTurn.Sequence)
            .ThenBy(candidate => candidate.Session.SessionId.Value, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Opportunity.OpportunityId.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (selected is null)
        {
            return ConversationResponseSchedulingResult.InFlightBlocked();
        }

        return ConversationResponseSchedulingResult.Scheduled(
            new ConversationResponseSelection(selected.Session, selected.Opportunity, selected.SourceTurn));
    }

    private sealed record PendingResponseCandidate(
        ConversationSession Session,
        DialogueResponseOpportunity Opportunity,
        SemanticDialogueTurn SourceTurn);
}
