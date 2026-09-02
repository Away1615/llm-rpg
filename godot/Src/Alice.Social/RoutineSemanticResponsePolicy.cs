using System.Collections.ObjectModel;
using System.Globalization;
using Alice.Actors;

namespace Alice.Social;

/// <summary>An immutable actor-visible projection for one exact scheduled response.</summary>
public sealed class RoutineSemanticResponseContext
{
    private readonly ReadOnlyCollection<DialogueClaimReference> _claimReferences;

    private RoutineSemanticResponseContext(ConversationResponseSelection selection)
    {
        Selection = selection;
        RespondingActor = selection.Opportunity.Recipient;
        OriginalSpeaker = selection.Opportunity.OriginalSpeaker;
        SourceActKind = selection.SourceAct.Kind;
        TopicRef = selection.SourceAct.TopicRef;
        _claimReferences = Array.AsReadOnly(selection.SourceAct.ClaimReferences.ToArray());
    }

    public ActorId RespondingActor { get; }
    public ActorId OriginalSpeaker { get; }
    public SemanticDialogueActKind SourceActKind { get; }
    public DialogueTopicRef? TopicRef { get; }
    public IReadOnlyList<DialogueClaimReference> ClaimReferences => _claimReferences;

    internal ConversationResponseSelection Selection { get; }

    public static RoutineSemanticResponseContext Create(ConversationResponseSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return new RoutineSemanticResponseContext(selection);
    }
}

public enum RoutineSemanticResponsePolicyOutcome
{
    CandidateReady,
    NoRoutineRecipe,
    ConsequentialDecisionRequired,
    CandidateIdentityConflict
}

/// <summary>A typed deterministic-policy result carrying a candidate set only when ready.</summary>
public sealed record RoutineSemanticResponsePolicyResult
{
    private RoutineSemanticResponsePolicyResult(
        RoutineSemanticResponsePolicyOutcome outcome,
        RoutineSemanticResponseCandidateSet? candidateSet)
    {
        Outcome = outcome;
        CandidateSet = candidateSet;
    }

    public RoutineSemanticResponsePolicyOutcome Outcome { get; }
    public RoutineSemanticResponseCandidateSet? CandidateSet { get; }

    internal static RoutineSemanticResponsePolicyResult CandidateReady(
        RoutineSemanticResponseCandidateSet candidateSet) =>
        new(RoutineSemanticResponsePolicyOutcome.CandidateReady, candidateSet);

    internal static RoutineSemanticResponsePolicyResult NoRoutineRecipe() =>
        new(RoutineSemanticResponsePolicyOutcome.NoRoutineRecipe, null);

    internal static RoutineSemanticResponsePolicyResult ConsequentialDecisionRequired() =>
        new(RoutineSemanticResponsePolicyOutcome.ConsequentialDecisionRequired, null);

    internal static RoutineSemanticResponsePolicyResult CandidateIdentityConflict() =>
        new(RoutineSemanticResponsePolicyOutcome.CandidateIdentityConflict, null);
}

/// <summary>The single frozen zero-model L0 response recipe.</summary>
public static class DeterministicRoutineSemanticResponsePolicy
{
    private const string CandidateIdPrefix = "l0-response:v1:";
    private const string TerminalThankToken = "thank:none";

    public static RoutineSemanticResponsePolicyResult Evaluate(RoutineSemanticResponseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.SourceActKind == SemanticDialogueActKind.Invite)
        {
            return RoutineSemanticResponsePolicyResult.ConsequentialDecisionRequired();
        }

        if (context.SourceActKind != SemanticDialogueActKind.Congratulate)
        {
            return RoutineSemanticResponsePolicyResult.NoRoutineRecipe();
        }

        SemanticDialogueActId candidateId = CreateCandidateId(context.Selection);
        foreach (SemanticDialogueTurn turn in context.Selection.Session.Transcript)
        {
            if (turn.Act.ActId == candidateId)
            {
                return RoutineSemanticResponsePolicyResult.CandidateIdentityConflict();
            }
        }

        var candidate = new SemanticDialogueAct(
            candidateId,
            SemanticDialogueActKind.Thank,
            context.RespondingActor,
            [context.OriginalSpeaker],
            context.TopicRef,
            [],
            null,
            DialogueResponseExpectation.None);
        var candidateSet = new RoutineSemanticResponseCandidateSet(context.Selection, [candidate]);
        return RoutineSemanticResponsePolicyResult.CandidateReady(candidateSet);
    }

    private static SemanticDialogueActId CreateCandidateId(ConversationResponseSelection selection) =>
        new(string.Concat(
            CandidateIdPrefix,
            EncodeIdentityComponent(selection.Session.SessionId.Value),
            EncodeIdentityComponent(selection.Opportunity.OpportunityId.Value),
            EncodeIdentityComponent(selection.SourceAct.ActId.Value),
            EncodeIdentityComponent(selection.Opportunity.Recipient.Value),
            EncodeIdentityComponent(selection.Opportunity.OriginalSpeaker.Value),
            TerminalThankToken));

    private static string EncodeIdentityComponent(string value) =>
        string.Concat(value.Length.ToString(CultureInfo.InvariantCulture), ":", value);
}
