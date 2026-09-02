using System.Collections.ObjectModel;

namespace Alice.Social;

/// <summary>An immutable actor-visible candidate snapshot for one exact scheduled response.</summary>
public sealed class RoutineSemanticResponseCandidateSet
{
    private readonly ReadOnlyCollection<SemanticDialogueAct> _candidates;

    public RoutineSemanticResponseCandidateSet(
        ConversationResponseSelection selection,
        IEnumerable<SemanticDialogueAct> candidates)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(candidates);

        SemanticDialogueAct[] candidateSnapshot = candidates.ToArray();
        var candidateIds = new HashSet<SemanticDialogueActId>();
        var transcriptActIds = selection.Session.Transcript
            .Select(turn => turn.Act.ActId)
            .ToHashSet();
        foreach (SemanticDialogueAct? candidate in candidateSnapshot)
        {
            if (candidate is null)
            {
                throw new ArgumentException("Routine semantic response candidates must be non-null.", nameof(candidates));
            }

            if (!candidateIds.Add(candidate.ActId))
            {
                throw new ArgumentException("Routine semantic response candidate identities must be distinct.", nameof(candidates));
            }

            if (candidate.Speaker != selection.Opportunity.Recipient
                || candidate.Recipients.Count != 1
                || candidate.Recipients[0] != selection.Opportunity.OriginalSpeaker)
            {
                throw new ArgumentException("Every candidate must reply from the pending recipient to the source speaker only.", nameof(candidates));
            }

            if (transcriptActIds.Contains(candidate.ActId))
            {
                throw new ArgumentException("A routine response candidate identity is already present in the selected session transcript.", nameof(candidates));
            }

            if (!IsAdmitted(selection.SourceAct.Kind, candidate.Kind))
            {
                throw new ArgumentException("The candidate kind is not admitted for the selected source act.", nameof(candidates));
            }
        }

        Selection = selection;
        _candidates = Array.AsReadOnly(candidateSnapshot);
    }

    public ConversationResponseSelection Selection { get; }
    public IReadOnlyList<SemanticDialogueAct> Candidates => _candidates;

    private static bool IsAdmitted(SemanticDialogueActKind sourceKind, SemanticDialogueActKind candidateKind)
    {
        if (sourceKind == SemanticDialogueActKind.Invite)
        {
            return candidateKind is SemanticDialogueActKind.Accept
                or SemanticDialogueActKind.Decline
                or SemanticDialogueActKind.Clarify
                or SemanticDialogueActKind.CounterOffer;
        }

        return candidateKind != SemanticDialogueActKind.Invite;
    }
}
