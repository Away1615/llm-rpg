using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Actors;
using Alice.Authority;
using Alice.Cognition;
using Alice.Commitments;
using Alice.Npc;

namespace Alice.Social;

/// <summary>An immutable automatic-dispatch epoch over live owners and actor-visible state.</summary>
public sealed class ConversationResponseDispatchEpoch
{
    private readonly ReadOnlyCollection<NpcState> _npcStates;
    private readonly ReadOnlyCollection<Commitment> _commitments;

    public ConversationResponseDispatchEpoch(
        IEnumerable<ConversationSession> sessions,
        IEnumerable<NpcState> npcStates,
        IEnumerable<Commitment> commitments,
        IEnumerable<DialogueResponseOpportunityId> inFlightOpportunityIds)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(npcStates);
        ArgumentNullException.ThrowIfNull(commitments);
        ArgumentNullException.ThrowIfNull(inFlightOpportunityIds);

        ConversationSession[] sessionSnapshot = sessions.ToArray();
        NpcState[] npcSnapshot = npcStates.ToArray();
        Commitment[] commitmentSnapshot = commitments.ToArray();
        DialogueResponseOpportunityId[] inFlightSnapshot = inFlightOpportunityIds.ToArray();

        SchedulingSnapshot = new ConversationResponseSchedulingSnapshot(sessionSnapshot, inFlightSnapshot);
        ValidateNpcIdentities(npcSnapshot);
        ValidateCommitmentIdentities(commitmentSnapshot);

        _npcStates = Array.AsReadOnly(npcSnapshot);
        _commitments = Array.AsReadOnly(commitmentSnapshot);
    }

    public ConversationResponseSchedulingSnapshot SchedulingSnapshot { get; }
    public IReadOnlyList<NpcState> NpcStates => _npcStates;
    public IReadOnlyList<Commitment> Commitments => _commitments;

    private static void ValidateNpcIdentities(IEnumerable<NpcState> npcStates)
    {
        var identities = new Dictionary<ActorId, NpcState>();
        foreach (NpcState? npcState in npcStates)
        {
            if (npcState is null)
            {
                throw new ArgumentException("Dispatch NPC states must be non-null.", nameof(npcStates));
            }

            if (identities.TryGetValue(npcState.ActorId, out NpcState? existing))
            {
                string message = ReferenceEquals(existing, npcState)
                    ? "An NPC actor identity was supplied more than once."
                    : "Distinct NPC state objects cannot claim the same actor identity.";
                throw new ArgumentException(message, nameof(npcStates));
            }

            identities.Add(npcState.ActorId, npcState);
        }
    }

    private static void ValidateCommitmentIdentities(IEnumerable<Commitment> commitments)
    {
        var identities = new Dictionary<CommitmentId, Commitment>();
        foreach (Commitment? commitment in commitments)
        {
            if (commitment is null)
            {
                throw new ArgumentException("Dispatch Commitments must be non-null.", nameof(commitments));
            }

            if (identities.TryGetValue(commitment.CommitmentId, out Commitment? existing))
            {
                string message = ReferenceEquals(existing, commitment)
                    ? "A Commitment identity was supplied more than once."
                    : "Distinct Commitment objects cannot claim the same identity.";
                throw new ArgumentException(message, nameof(commitments));
            }

            identities.Add(commitment.CommitmentId, commitment);
        }
    }
}

public enum AutomaticConversationResponseDispatchOutcome
{
    NoPending,
    InFlightBlocked,
    SchedulingConflict,
    SelectedRecipientStateConflict,
    Dispatched
}

public enum ConversationResponseDispatchStateConflict
{
    MissingSelectedRecipientState,
    SelectedCorrelationMismatch
}

/// <summary>Closed automatic-dispatch result retaining the exact scheduler and Host evidence.</summary>
public sealed record AutomaticConversationResponseDispatchResult
{
    private AutomaticConversationResponseDispatchResult(
        AutomaticConversationResponseDispatchOutcome outcome,
        ConversationResponseSelection? selection = null,
        ConversationResponseSchedulingConflict? schedulingConflict = null,
        ConversationResponseDispatchStateConflict? stateConflict = null,
        OrdinaryInviteResponseHostResult? hostResult = null)
    {
        Outcome = outcome;
        Selection = selection;
        SchedulingConflict = schedulingConflict;
        StateConflict = stateConflict;
        HostResult = hostResult;
    }

    public AutomaticConversationResponseDispatchOutcome Outcome { get; }
    public ConversationResponseSelection? Selection { get; }
    public ConversationResponseSchedulingMetadata? SchedulingMetadata => Selection?.Metadata;
    public ConversationResponseSchedulingConflict? SchedulingConflict { get; }
    public ConversationResponseDispatchStateConflict? StateConflict { get; }
    public OrdinaryInviteResponseHostResult? HostResult { get; }

    internal static AutomaticConversationResponseDispatchResult NoPending() =>
        new(AutomaticConversationResponseDispatchOutcome.NoPending);

    internal static AutomaticConversationResponseDispatchResult InFlightBlocked() =>
        new(AutomaticConversationResponseDispatchOutcome.InFlightBlocked);

    internal static AutomaticConversationResponseDispatchResult SchedulingConflictRequired(
        ConversationResponseSchedulingConflict conflict) =>
        new(AutomaticConversationResponseDispatchOutcome.SchedulingConflict, schedulingConflict: conflict);

    internal static AutomaticConversationResponseDispatchResult SelectedStateConflict(
        ConversationResponseSelection selection,
        ConversationResponseDispatchStateConflict conflict) =>
        new(
            AutomaticConversationResponseDispatchOutcome.SelectedRecipientStateConflict,
            selection: selection,
            stateConflict: conflict);

    internal static AutomaticConversationResponseDispatchResult Dispatched(
        ConversationResponseSelection selection,
        OrdinaryInviteResponseHostResult hostResult) =>
        new(
            AutomaticConversationResponseDispatchOutcome.Dispatched,
            selection: selection,
            hostResult: hostResult);
}

/// <summary>Selects and advances at most one current conversation response.</summary>
public static class ConversationResponseDispatchRuntime
{
    public static AutomaticConversationResponseDispatchResult Step(
        ConversationResponseDispatchEpoch epoch,
        InvitationAcceptanceAuthorityRuntime invitationAcceptanceAuthority,
        DecisionNeedDiscoveryRegistrar decisionRegistrar,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        ArgumentNullException.ThrowIfNull(invitationAcceptanceAuthority);
        ArgumentNullException.ThrowIfNull(decisionRegistrar);
        ArgumentNullException.ThrowIfNull(firstObservedWorldRevision);

        ConversationResponseSchedulingResult scheduling =
            ConversationResponseScheduler.Schedule(epoch.SchedulingSnapshot);
        switch (scheduling.Outcome)
        {
            case ConversationResponseSchedulingOutcome.NoPending:
                return AutomaticConversationResponseDispatchResult.NoPending();
            case ConversationResponseSchedulingOutcome.InFlightBlocked:
                return AutomaticConversationResponseDispatchResult.InFlightBlocked();
            case ConversationResponseSchedulingOutcome.ConflictRequired:
                return AutomaticConversationResponseDispatchResult.SchedulingConflictRequired(
                    scheduling.Conflict!.Value);
            case ConversationResponseSchedulingOutcome.Scheduled:
                break;
            default:
                throw new InvalidOperationException("Unknown conversation response scheduling outcome.");
        }

        ConversationResponseSelection selection = scheduling.Selection!;
        if (!HasExactCorrelation(selection))
        {
            return AutomaticConversationResponseDispatchResult.SelectedStateConflict(
                selection,
                ConversationResponseDispatchStateConflict.SelectedCorrelationMismatch);
        }

        NpcState? selectedNpc = epoch.NpcStates.SingleOrDefault(
            npcState => npcState.ActorId == selection.Opportunity.Recipient);
        if (selectedNpc is null)
        {
            return AutomaticConversationResponseDispatchResult.SelectedStateConflict(
                selection,
                ConversationResponseDispatchStateConflict.MissingSelectedRecipientState);
        }

        Commitment[] ownCommitments = epoch.Commitments
            .Where(commitment => commitment.Debtor == selection.Opportunity.Recipient)
            .ToArray();
        OrdinaryInviteResponseHostResult hostResult =
            ConversationResponseHost.ExecuteScheduledOrdinaryInviteResponse(
                selection,
                invitationAcceptanceAuthority,
                decisionRegistrar,
                selectedNpc,
                ownCommitments,
                firstObservedWorldRevision,
                createdAt);
        return AutomaticConversationResponseDispatchResult.Dispatched(selection, hostResult);
    }

    private static bool HasExactCorrelation(ConversationResponseSelection selection) =>
        selection.Metadata.SessionId == selection.Session.SessionId
        && selection.Metadata.OpportunityId == selection.Opportunity.OpportunityId
        && selection.Metadata.Recipient == selection.Opportunity.Recipient
        && selection.Opportunity.SessionId == selection.Session.SessionId
        && selection.Opportunity.SourceActId == selection.SourceAct.ActId
        && selection.Opportunity.OriginalSpeaker == selection.SourceAct.Speaker
        && ReferenceEquals(selection.SourceTurn.Act, selection.SourceAct)
        && selection.Session.Transcript.Count(turn => ReferenceEquals(turn, selection.SourceTurn)) == 1
        && selection.Session.PendingResponseOpportunities.Count(
            opportunity => ReferenceEquals(opportunity, selection.Opportunity)) == 1;
}
