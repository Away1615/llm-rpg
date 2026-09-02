using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Authority;
using Alice.Cognition;
using Alice.Memory;
using Alice.Npc;
using Alice.Social;

namespace Alice.ProductRuntime;

public enum ConversationTurnMemoryAdmissionOutcome
{
    Admitted,
    AlreadyAdmitted,
    AdmissionConflict,
    OwnershipConflict
}

public sealed record ConversationTurnAdmissionSnapshot(
    ConversationSessionId SessionId,
    int TurnSequence,
    SemanticDialogueActId ActId,
    SimTime OccurredAt);

public sealed record ConversationTurnMemoryAdmissionReceipt(
    ConversationTurnMemoryAdmissionOutcome Outcome,
    ConversationTurnAdmissionSnapshot? Admission,
    SemanticDialogueMemoryHostAdmissionResult? HostResult);

/// <summary>Concrete post-commit owner for every accepted conversation turn, including initial and terminal turns.</summary>
public sealed class ConversationTurnMemoryAdmissionOwner
{
    private readonly SemanticDialogueMemoryHost _memoryHost;
    private readonly Dictionary<ConversationTurnKey, ConversationTurnAdmissionSnapshot> _admitted = [];
    private readonly List<ConversationTurnAdmissionSnapshot> _insertionOrder = [];

    public ConversationTurnMemoryAdmissionOwner(SemanticDialogueMemoryHost memoryHost)
    {
        ArgumentNullException.ThrowIfNull(memoryHost);
        _memoryHost = memoryHost;
    }

    public SemanticDialogueMemoryHost MemoryHost => _memoryHost;

    public ConversationTurnMemoryAdmissionReceipt AdmitCommittedTurn(
        ConversationSession session,
        SemanticDialogueTurn turn,
        SimTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        int transcriptIndex = turn.Sequence - 1;
        if (transcriptIndex < 0
            || transcriptIndex >= session.Transcript.Count
            || !ReferenceEquals(session.Transcript[transcriptIndex], turn))
        {
            return new ConversationTurnMemoryAdmissionReceipt(
                ConversationTurnMemoryAdmissionOutcome.OwnershipConflict,
                null,
                null);
        }

        var key = new ConversationTurnKey(session.SessionId, turn.Sequence);
        var candidate = new ConversationTurnAdmissionSnapshot(
            session.SessionId,
            turn.Sequence,
            turn.Act.ActId,
            occurredAt);
        if (_admitted.TryGetValue(key, out ConversationTurnAdmissionSnapshot? existing))
        {
            bool exact = existing.ActId == candidate.ActId && existing.OccurredAt == candidate.OccurredAt;
            return new ConversationTurnMemoryAdmissionReceipt(
                exact
                    ? ConversationTurnMemoryAdmissionOutcome.AlreadyAdmitted
                    : ConversationTurnMemoryAdmissionOutcome.AdmissionConflict,
                exact ? existing : null,
                null);
        }

        SemanticDialogueMemoryHostAdmissionResult hostResult = _memoryHost.AdmitAcceptedTurn(session, turn, occurredAt);
        if (hostResult.Kind is not SemanticDialogueMemoryHostAdmissionKind.Integrated
            and not SemanticDialogueMemoryHostAdmissionKind.AlreadyIntegrated)
        {
            return new ConversationTurnMemoryAdmissionReceipt(
                ConversationTurnMemoryAdmissionOutcome.AdmissionConflict,
                null,
                hostResult);
        }

        _admitted.Add(key, candidate);
        _insertionOrder.Add(candidate);
        return new ConversationTurnMemoryAdmissionReceipt(
            hostResult.Kind == SemanticDialogueMemoryHostAdmissionKind.Integrated
                ? ConversationTurnMemoryAdmissionOutcome.Admitted
                : ConversationTurnMemoryAdmissionOutcome.AlreadyAdmitted,
            candidate,
            hostResult);
    }

    public IReadOnlyList<ConversationTurnAdmissionSnapshot> GetAdmissionSnapshot() =>
        new ReadOnlyCollection<ConversationTurnAdmissionSnapshot>(_insertionOrder.ToArray());

    private readonly record struct ConversationTurnKey(ConversationSessionId SessionId, int Sequence);
}

public sealed record ConversationOpenResult(
    ConversationSession Session,
    SemanticDialogueTurn InitialTurn,
    ConversationTurnMemoryAdmissionReceipt MemoryAdmission);

public sealed record ConversationReplyResult(
    DialogueReplyResult Reply,
    ConversationTurnMemoryAdmissionReceipt? MemoryAdmission);

public sealed record AutomaticConversationResponseResult(
    AutomaticConversationResponseDispatchResult Dispatch,
    ConversationTurnMemoryAdmissionReceipt? MemoryAdmission);

public sealed record ConversationDurableSnapshot(
    ConversationSession Session,
    IReadOnlyList<SimTime> TurnTimes);

/// <summary>Conversation composition that cannot accept a recordable turn without immediately admitting memory.</summary>
public sealed class ConversationRuntime
{
    private readonly ConversationTurnMemoryAdmissionOwner _memoryAdmissionOwner;
    private readonly Dictionary<ConversationSessionId, ConversationLedger> _ledgers = [];

    public ConversationRuntime(ConversationTurnMemoryAdmissionOwner memoryAdmissionOwner)
    {
        ArgumentNullException.ThrowIfNull(memoryAdmissionOwner);
        _memoryAdmissionOwner = memoryAdmissionOwner;
    }

    public ConversationOpenResult Open(
        ConversationSessionId sessionId,
        IEnumerable<Alice.Actors.ActorId> participants,
        SemanticDialogueAct initialAct,
        SimTime occurredAt)
    {
        if (_ledgers.ContainsKey(sessionId))
        {
            throw new ArgumentException("Conversation session identity is already owned by this runtime.", nameof(sessionId));
        }
        var session = new ConversationSession(sessionId, participants);
        SemanticDialogueTurn turn = session.Accept(initialAct);
        ConversationTurnMemoryAdmissionReceipt admission =
            _memoryAdmissionOwner.AdmitCommittedTurn(session, turn, occurredAt);
        EnsureIntegrated(admission);
        var ledger = new ConversationLedger(session);
        ledger.Record(turn, occurredAt);
        _ledgers.Add(sessionId, ledger);
        return new ConversationOpenResult(session, turn, admission);
    }

    public SemanticDialogueTurn Accept(
        ConversationSession session,
        SemanticDialogueAct act,
        SimTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(session);
        ConversationLedger ledger = RequireLedger(session);
        SemanticDialogueTurn turn = session.Accept(act);
        ConversationTurnMemoryAdmissionReceipt admission =
            _memoryAdmissionOwner.AdmitCommittedTurn(session, turn, occurredAt);
        EnsureIntegrated(admission);
        ledger.Record(turn, occurredAt);
        return turn;
    }

    public ConversationReplyResult Reply(
        ConversationSession session,
        DialogueResponseOpportunity opportunity,
        SemanticDialogueAct reply,
        SimTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(session);
        ConversationLedger ledger = RequireLedger(session);
        DialogueReplyResult result = session.Reply(opportunity, reply);
        if (result.RecordedTurn is not SemanticDialogueTurn recordedTurn)
        {
            return new ConversationReplyResult(result, null);
        }

        ConversationTurnMemoryAdmissionReceipt admission =
            _memoryAdmissionOwner.AdmitCommittedTurn(session, recordedTurn, occurredAt);
        EnsureIntegrated(admission);
        ledger.Record(recordedTurn, occurredAt);
        return new ConversationReplyResult(result, admission);
    }

    public void AbandonResponse(
        ConversationSession session,
        DialogueResponseOpportunity opportunity)
    {
        ArgumentNullException.ThrowIfNull(session);
        _ = RequireLedger(session);
        session.AbandonResponse(opportunity);
    }

    /// <summary>Runs one existing automatic Social Host step and persists its exact recorded turn.</summary>
    public AutomaticConversationResponseResult DispatchAutomaticResponse(
        ConversationResponseDispatchEpoch epoch,
        InvitationAcceptanceAuthorityRuntime invitationAcceptanceAuthority,
        DecisionNeedDiscoveryRegistrar decisionRegistrar,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt,
        SimTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        ArgumentNullException.ThrowIfNull(invitationAcceptanceAuthority);
        ArgumentNullException.ThrowIfNull(decisionRegistrar);
        ArgumentNullException.ThrowIfNull(firstObservedWorldRevision);
        AutomaticConversationResponseDispatchResult dispatch = ConversationResponseDispatchRuntime.Step(
            epoch,
            invitationAcceptanceAuthority,
            decisionRegistrar,
            firstObservedWorldRevision,
            createdAt);
        SemanticDialogueTurn? recordedTurn = dispatch.HostResult?.RoutingResult?.RecordedTurn
            ?? dispatch.HostResult?.RejectionFallbackTurn;
        if (recordedTurn is null)
        {
            return new AutomaticConversationResponseResult(dispatch, null);
        }

        ConversationSession session = dispatch.Selection?.Session
            ?? throw new InvalidOperationException("Automatic Social response recorded a turn without its selected session.");
        ConversationLedger ledger = RequireLedger(session);
        ConversationTurnMemoryAdmissionReceipt admission =
            _memoryAdmissionOwner.AdmitCommittedTurn(session, recordedTurn, occurredAt);
        EnsureIntegrated(admission);
        ledger.Record(recordedTurn, occurredAt);
        return new AutomaticConversationResponseResult(dispatch, admission);
    }

    public IReadOnlyList<ConversationSession> Sessions =>
        new ReadOnlyCollection<ConversationSession>(_ledgers.Values
            .Select(ledger => ledger.Session)
            .OrderBy(session => session.SessionId.Value, StringComparer.Ordinal)
            .ToArray());

    public IReadOnlyList<ConversationDurableSnapshot> CaptureDurableState() =>
        new ReadOnlyCollection<ConversationDurableSnapshot>(_ledgers.Values
            .OrderBy(value => value.Session.SessionId.Value, StringComparer.Ordinal)
            .Select(value => new ConversationDurableSnapshot(
                value.Session,
                new ReadOnlyCollection<SimTime>(value.Turns.Select(turn => turn.OccurredAt).ToArray())))
            .ToArray());

    public void RestoreDurableState(IEnumerable<ConversationDurableSnapshot> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (_ledgers.Count != 0) throw new InvalidOperationException("Conversation restore requires a fresh product composition.");
        foreach (ConversationDurableSnapshot saved in sessions)
        {
            if (saved.Session.Transcript.Count != saved.TurnTimes.Count)
                throw new InvalidDataException("Saved conversation turn times are incomplete.");
            var ledger = new ConversationLedger(saved.Session);
            for (int index = 0; index < saved.Session.Transcript.Count; index++)
            {
                SemanticDialogueTurn turn = saved.Session.Transcript[index];
                SimTime occurredAt = saved.TurnTimes[index];
                ConversationTurnMemoryAdmissionReceipt admission =
                    _memoryAdmissionOwner.AdmitCommittedTurn(saved.Session, turn, occurredAt);
                EnsureIntegrated(admission);
                ledger.Record(turn, occurredAt);
            }
            _ledgers.Add(saved.Session.SessionId, ledger);
        }
    }

    private static void EnsureIntegrated(ConversationTurnMemoryAdmissionReceipt admission)
    {
        if (admission.Outcome is not ConversationTurnMemoryAdmissionOutcome.Admitted
            and not ConversationTurnMemoryAdmissionOutcome.AlreadyAdmitted)
        {
            throw new InvalidOperationException($"Accepted dialogue turn could not enter memory: {admission.Outcome}.");
        }
    }

    private ConversationLedger RequireLedger(ConversationSession session)
    {
        if (!_ledgers.TryGetValue(session.SessionId, out ConversationLedger? ledger)
            || !ReferenceEquals(ledger.Session, session))
        {
            throw new ArgumentException("Conversation session is not owned by this Demo runtime.", nameof(session));
        }
        return ledger;
    }

    private sealed record RecordedConversationTurn(SemanticDialogueTurn Turn, SimTime OccurredAt);

    private sealed class ConversationLedger
    {
        private readonly List<RecordedConversationTurn> _turns = [];
        public ConversationLedger(ConversationSession session) { Session = session; }
        public ConversationSession Session { get; }
        public IReadOnlyList<RecordedConversationTurn> Turns => _turns;
        public void Record(SemanticDialogueTurn turn, SimTime occurredAt)
        {
            if (turn.Sequence != _turns.Count + 1) throw new InvalidOperationException("Conversation ledger sequence is not contiguous.");
            _turns.Add(new RecordedConversationTurn(turn, occurredAt));
        }
    }

}
