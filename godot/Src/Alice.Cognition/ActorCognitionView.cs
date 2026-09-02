using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Npc;

namespace Alice.Cognition;

/// <summary>Immutable semantic projection of the current plan without plan identity or runtime state.</summary>
public sealed class CognitionPlanView : IEquatable<CognitionPlanView>
{
    private readonly ReadOnlyCollection<PlanStep> _steps;

    internal CognitionPlanView(NpcGoal goal, IEnumerable<PlanStep> steps)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(steps);
        PlanStep[] snapshot = steps.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(steps));
        }

        foreach (PlanStep step in snapshot)
        {
            ArgumentNullException.ThrowIfNull(step);
        }

        Goal = goal;
        _steps = Array.AsReadOnly(snapshot);
    }

    public NpcGoal Goal { get; }
    public IReadOnlyList<PlanStep> Steps => _steps;

    public bool Equals(CognitionPlanView? other)
    {
        return other is not null && Goal == other.Goal && Steps.SequenceEqual(other.Steps);
    }

    public override bool Equals(object? obj) => Equals(obj as CognitionPlanView);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Goal);
        foreach (PlanStep step in Steps)
        {
            hash.Add(step);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Filtered immutable actor-visible input for current-step cognition.</summary>
public sealed class ActorCognitionView : IEquatable<ActorCognitionView>
{
    private readonly NpcPersonalityState _personality;
    private readonly ReadOnlyCollection<NpcGoal> _activeGoals;

    private ActorCognitionView(
        SharedActorState self,
        NpcPersonalityState personality,
        IEnumerable<NpcGoal> activeGoals,
        PlanId sourcePlanId,
        CognitionPlanView currentPlan,
        PlanStep currentStep,
        NpcKnowledgeState knowledge)
    {
        Self = self;
        ActorId = self.Identity.ActorId;
        _personality = personality;
        _activeGoals = Array.AsReadOnly(CanonicalizeGoals(activeGoals));
        SourcePlanId = sourcePlanId;
        CurrentPlan = currentPlan;
        CurrentStep = currentStep;
        Knowledge = knowledge;
    }

    public ActorId ActorId { get; }
    public SharedActorState Self { get; }
    public IPersonalityPriorView Personality => _personality;
    public IReadOnlyList<NpcGoal> ActiveGoals => _activeGoals;
    internal PlanId SourcePlanId { get; }
    public CognitionPlanView CurrentPlan { get; }
    public PlanStep CurrentStep { get; }
    public NpcKnowledgeState Knowledge { get; }

    public static ActorCognitionView Create(
        SharedActorState self,
        NpcState npcState,
        PlanRuntime planRuntime)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(npcState);
        ArgumentNullException.ThrowIfNull(planRuntime);
        ActorId actorId = self.Identity.ActorId;
        if (npcState.ActorId != actorId)
        {
            throw new ArgumentException("Shared Actor and NPC state must belong to the same ActorId.", nameof(npcState));
        }

        NpcPlan currentPlan = npcState.Planning.CurrentPlan ??
            throw new InvalidOperationException("Current-step cognition requires an active current plan.");
        if (currentPlan.ActorId != actorId || planRuntime.PlanId != currentPlan.PlanId)
        {
            throw new ArgumentException("Current plan and runtime must correlate to the same actor and PlanId.", nameof(planRuntime));
        }

        int currentIndex = planRuntime.CurrentStepIndex;
        if (planRuntime.Status != PlanRuntimeStatus.Active ||
            currentIndex < 0 ||
            currentIndex >= currentPlan.Steps.Count ||
            currentIndex >= planRuntime.Steps.Count)
        {
            throw new InvalidOperationException("Current-step cognition requires an active runtime at an in-range current step.");
        }

        PlanStep expectedStep = currentPlan.Steps[currentIndex];
        PlanStep? runtimeStep = planRuntime.GetCurrentPlanStep();
        if (runtimeStep is null ||
            !runtimeStep.Equals(expectedStep) ||
            planRuntime.Steps[currentIndex].Status != PlanStepRuntimeStatus.InProgress ||
            planRuntime.Steps[currentIndex].PlanStepId != expectedStep.PlanStepId)
        {
            throw new InvalidOperationException("Runtime current step must equal the current plan's in-progress semantic step.");
        }

        var semanticPlan = new CognitionPlanView(currentPlan.Goal, currentPlan.Steps);
        return new ActorCognitionView(
            self,
            npcState.Personality,
            npcState.Planning.ActiveGoals,
            currentPlan.PlanId,
            semanticPlan,
            semanticPlan.Steps[currentIndex],
            npcState.Knowledge);
    }

    public bool Equals(ActorCognitionView? other)
    {
        return other is not null &&
            ActorId == other.ActorId &&
            Self.Equals(other.Self) &&
            _personality.Equals(other._personality) &&
            ActiveGoals.SequenceEqual(other.ActiveGoals) &&
            CurrentPlan.Equals(other.CurrentPlan) &&
            CurrentStep.Equals(other.CurrentStep) &&
            Knowledge.Equals(other.Knowledge);
    }

    public override bool Equals(object? obj) => Equals(obj as ActorCognitionView);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ActorId);
        hash.Add(Self);
        hash.Add(_personality);
        foreach (NpcGoal goal in ActiveGoals)
        {
            hash.Add(goal);
        }

        hash.Add(CurrentPlan);
        hash.Add(CurrentStep);
        hash.Add(Knowledge);
        return hash.ToHashCode();
    }

    private static NpcGoal[] CanonicalizeGoals(IEnumerable<NpcGoal> goals)
    {
        NpcGoal[] snapshot = goals.ToArray();
        Array.Sort(snapshot, GoalComparer.Instance);
        return snapshot;
    }

    private sealed class GoalComparer : IComparer<NpcGoal>
    {
        public static GoalComparer Instance { get; } = new();

        public int Compare(NpcGoal? left, NpcGoal? right)
        {
            return StringComparer.Ordinal.Compare(left?.GoalId.Value, right?.GoalId.Value);
        }
    }
}
