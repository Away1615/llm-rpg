using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Npc;
using Alice.Social;
using Alice.World;

namespace Alice.Cognition;

public abstract record DecisionProblemDescriptor
{
    private protected DecisionProblemDescriptor()
    {
    }

    public abstract ActorId ActorId { get; }

    public byte[] GetCanonicalBytes() => DecisionNeedCanonicalJson.SerializeProblemDescriptor(this);

    public DecisionProblemDescriptorHash DescriptorHash =>
        DecisionNeedCanonicalJson.HashProblemDescriptor(this);
}

public sealed record CurrentStepDecisionProblemDescriptor : DecisionProblemDescriptor
{
    internal CurrentStepDecisionProblemDescriptor(
        ActorId actorId,
        DecisionProblemCode problemCode,
        GoalId currentGoalId,
        GoalObjective currentGoalObjective,
        PlanStepId planStepId,
        GoalObjective stepObjective,
        TargetRef? target,
        ResultPredicate desiredResult)
    {
        ActorId = actorId;
        ProblemCode = problemCode;
        CurrentGoalId = currentGoalId;
        CurrentGoalObjective = currentGoalObjective;
        PlanStepId = planStepId;
        StepObjective = stepObjective;
        Target = target;
        DesiredResult = desiredResult;
    }

    public override ActorId ActorId { get; }
    public DecisionProblemCode ProblemCode { get; }
    public GoalId CurrentGoalId { get; }
    public GoalObjective CurrentGoalObjective { get; }
    public PlanStepId PlanStepId { get; }
    public GoalObjective StepObjective { get; }
    public TargetRef? Target { get; }
    public ResultPredicate DesiredResult { get; }
}

public sealed record PlanlessStrategicDecisionProblemDescriptor : DecisionProblemDescriptor
{
    private readonly ReadOnlyCollection<NpcGoal> _activeGoals;

    internal PlanlessStrategicDecisionProblemDescriptor(
        ActorId actorId,
        DecisionProblemCode problemCode,
        IEnumerable<NpcGoal> activeGoals)
    {
        ArgumentNullException.ThrowIfNull(problemCode);
        ArgumentNullException.ThrowIfNull(activeGoals);
        ActorIdentity.ValidateActorId(actorId);
        NpcGoal[] goalSnapshot = activeGoals.ToArray();
        if (goalSnapshot.Length == 0)
        {
            throw new ArgumentException("A planless strategic problem requires at least one active Goal.", nameof(activeGoals));
        }

        foreach (NpcGoal? goal in goalSnapshot)
        {
            if (goal is null)
            {
                throw new ArgumentException("Planless strategic Goals must be non-null.", nameof(activeGoals));
            }
        }

        Array.Sort(goalSnapshot, PlanlessGoalComparer.Instance);
        for (int index = 1; index < goalSnapshot.Length; index++)
        {
            if (goalSnapshot[index - 1].GoalId == goalSnapshot[index].GoalId)
            {
                throw new ArgumentException("Planless strategic Goal identities must be unique.", nameof(activeGoals));
            }
        }

        ActorId = actorId;
        ProblemCode = problemCode;
        _activeGoals = Array.AsReadOnly(goalSnapshot);
    }

    public override ActorId ActorId { get; }
    public DecisionProblemCode ProblemCode { get; }
    public IReadOnlyList<NpcGoal> ActiveGoals => _activeGoals;

    private sealed class PlanlessGoalComparer : IComparer<NpcGoal>
    {
        public static PlanlessGoalComparer Instance { get; } = new();

        public int Compare(NpcGoal? left, NpcGoal? right)
        {
            return StringComparer.Ordinal.Compare(left!.GoalId.Value, right!.GoalId.Value);
        }
    }
}

public sealed record InviteResponseDecisionProblemDescriptor : DecisionProblemDescriptor
{
    private readonly ReadOnlyCollection<DialogueClaimReference> _claimReferences;

    internal InviteResponseDecisionProblemDescriptor(
        ActorId actorId,
        DecisionProblemCode problemCode,
        ActorId originalSpeaker,
        GatheringRef gatheringRef,
        int expectedGatheringRevision,
        BelievedAuthorizationRef? believedAuthorizationRef,
        DialogueTopicRef? topicRef,
        IEnumerable<DialogueClaimReference> claimReferences)
    {
        ArgumentNullException.ThrowIfNull(problemCode);
        ArgumentNullException.ThrowIfNull(claimReferences);
        ActorIdentity.ValidateActorId(actorId);
        ActorIdentity.ValidateActorId(originalSpeaker);
        if (actorId == originalSpeaker)
        {
            throw new ArgumentException("An Invite response problem requires distinct actors.", nameof(originalSpeaker));
        }

        SemanticDialogueIdentity.Validate(gatheringRef.Value, nameof(gatheringRef));
        if (expectedGatheringRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedGatheringRevision));
        }

        if (believedAuthorizationRef is { } authorizationRef)
        {
            SemanticDialogueIdentity.Validate(authorizationRef.Value, nameof(believedAuthorizationRef));
        }

        if (topicRef is { } topic)
        {
            SemanticDialogueIdentity.Validate(topic.Value, nameof(topicRef));
        }

        DialogueClaimReference[] claimSnapshot = claimReferences.ToArray();
        foreach (DialogueClaimReference? claimReference in claimSnapshot)
        {
            if (claimReference is null)
            {
                throw new ArgumentException("Invite response claim references must be non-null.", nameof(claimReferences));
            }
        }

        Array.Sort(claimSnapshot, InviteClaimReferenceComparer.Instance);
        for (int index = 1; index < claimSnapshot.Length; index++)
        {
            if (claimSnapshot[index - 1].ClaimRef == claimSnapshot[index].ClaimRef)
            {
                throw new ArgumentException("Invite response claim identities must be unique.", nameof(claimReferences));
            }
        }

        ActorId = actorId;
        ProblemCode = problemCode;
        OriginalSpeaker = originalSpeaker;
        GatheringRef = gatheringRef;
        ExpectedGatheringRevision = expectedGatheringRevision;
        BelievedAuthorizationRef = believedAuthorizationRef;
        TopicRef = topicRef;
        _claimReferences = Array.AsReadOnly(claimSnapshot);
    }

    public override ActorId ActorId { get; }
    public DecisionProblemCode ProblemCode { get; }
    public ActorId OriginalSpeaker { get; }
    public GatheringRef GatheringRef { get; }
    public int ExpectedGatheringRevision { get; }
    public BelievedAuthorizationRef? BelievedAuthorizationRef { get; }
    public DialogueTopicRef? TopicRef { get; }
    public IReadOnlyList<DialogueClaimReference> ClaimReferences => _claimReferences;

    private sealed class InviteClaimReferenceComparer : IComparer<DialogueClaimReference>
    {
        public static InviteClaimReferenceComparer Instance { get; } = new();

        public int Compare(DialogueClaimReference? left, DialogueClaimReference? right)
        {
            int claimComparison = StringComparer.Ordinal.Compare(left?.ClaimRef.Value, right?.ClaimRef.Value);
            return claimComparison != 0
                ? claimComparison
                : StringComparer.Ordinal.Compare(left?.ProvenanceRef.Value, right?.ProvenanceRef.Value);
        }
    }
}

public static class DecisionProblemDescriptorBuilder
{
    public static CurrentStepDecisionProblemDescriptor CreateCurrentStep(
        ActorCognitionView view,
        DecisionProblemCode problemCode)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(problemCode);
        NpcGoal currentGoal = view.CurrentPlan.Goal;
        PlanStep currentStep = view.CurrentStep;
        bool currentGoalIsActive = false;
        foreach (NpcGoal activeGoal in view.ActiveGoals)
        {
            if (activeGoal.GoalId == currentGoal.GoalId && activeGoal == currentGoal)
            {
                currentGoalIsActive = true;
                break;
            }
        }

        if (!currentGoalIsActive)
        {
            throw new ArgumentException("Current Plan Goal must equal an actor-visible active Goal.", nameof(view));
        }

        bool belongsToCurrentPlan = false;
        foreach (PlanStep planStep in view.CurrentPlan.Steps)
        {
            if (planStep.PlanStepId == currentStep.PlanStepId && planStep.Equals(currentStep))
            {
                belongsToCurrentPlan = true;
                break;
            }
        }

        if (!belongsToCurrentPlan)
        {
            throw new ArgumentException("Current Step must belong to the semantic current Plan.", nameof(view));
        }

        ActorId resultActorId = currentStep.DesiredResult switch
        {
            InventoryAtLeast inventory => inventory.ActorId,
            BodyStateWithin body => body.ActorId,
            InteractionTargetReached reached => reached.ActorId,
            TargetTerminal terminal => terminal.ActorId,
            _ => throw new ArgumentException("Current Step result is outside the closed descriptor domain.", nameof(view))
        };
        if (resultActorId != view.ActorId)
        {
            throw new ArgumentException("Current Step result must belong to the cognition Actor.", nameof(view));
        }

        return new CurrentStepDecisionProblemDescriptor(
            view.ActorId,
            problemCode,
            currentGoal.GoalId,
            currentGoal.Objective,
            currentStep.PlanStepId,
            currentStep.Objective,
            currentStep.Target,
            currentStep.DesiredResult);
    }

    public static PlanlessStrategicDecisionProblemDescriptor CreatePlanlessStrategic(
        ActorDecisionView view,
        DecisionProblemCode problemCode)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(problemCode);
        if (view.CurrentPlan is not null || view.CurrentStep is not null)
        {
            throw new ArgumentException(
                "A planless strategic problem cannot contain a current Plan or Plan Step.",
                nameof(view));
        }

        return new PlanlessStrategicDecisionProblemDescriptor(
            view.ActorId,
            problemCode,
            view.ActiveGoals);
    }
}
