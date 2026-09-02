using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Npc;

namespace Alice.Cognition;

/// <summary>Immutable actor-visible input for an L2 decision that may be planless.</summary>
public sealed class ActorDecisionView : IEquatable<ActorDecisionView>
{
    private readonly NpcPersonalityState _personality;
    private readonly ReadOnlyCollection<NpcGoal> _activeGoals;

    private ActorDecisionView(
        SharedActorState self,
        NpcPersonalityState personality,
        IEnumerable<NpcGoal> activeGoals,
        CognitionPlanView? currentPlan,
        PlanStep? currentStep,
        NpcKnowledgeState knowledge)
    {
        Self = self;
        ActorId = self.Identity.ActorId;
        _personality = personality;
        _activeGoals = Array.AsReadOnly(activeGoals.OrderBy(goal => goal.GoalId.Value, StringComparer.Ordinal).ToArray());
        CurrentPlan = currentPlan;
        CurrentStep = currentStep;
        Knowledge = knowledge;
    }

    public ActorId ActorId { get; }
    public SharedActorState Self { get; }
    public IPersonalityPriorView Personality => _personality;
    public IReadOnlyList<NpcGoal> ActiveGoals => _activeGoals;
    public CognitionPlanView? CurrentPlan { get; }
    public PlanStep? CurrentStep { get; }
    public NpcKnowledgeState Knowledge { get; }

    public static ActorDecisionView Create(
        SharedActorState self,
        NpcState npcState,
        PlanRuntime? currentPlanRuntime)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(npcState);
        if (self.Identity.ActorId != npcState.ActorId)
        {
            throw new ArgumentException("Shared Actor and NPC state must belong to the same ActorId.", nameof(npcState));
        }

        NpcPlan? plan = npcState.Planning.CurrentPlan;
        if (plan is null)
        {
            if (currentPlanRuntime is not null)
            {
                throw new ArgumentException("A planless actor decision cannot have a PlanRuntime.", nameof(currentPlanRuntime));
            }

            return new ActorDecisionView(
                self,
                npcState.Personality,
                npcState.Planning.ActiveGoals,
                null,
                null,
                npcState.Knowledge);
        }

        if (currentPlanRuntime is null)
        {
            throw new ArgumentNullException(nameof(currentPlanRuntime), "A real current plan requires its active runtime.");
        }

        ActorCognitionView currentStepView = ActorCognitionView.Create(self, npcState, currentPlanRuntime);
        return FromCurrentStepView(currentStepView);
    }

    public static ActorDecisionView FromCurrentStepView(ActorCognitionView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new ActorDecisionView(
            view.Self,
            (NpcPersonalityState)view.Personality,
            view.ActiveGoals,
            view.CurrentPlan,
            view.CurrentStep,
            view.Knowledge);
    }

    public bool Equals(ActorDecisionView? other)
    {
        return other is not null
            && ActorId == other.ActorId
            && Self.Equals(other.Self)
            && _personality.Equals(other._personality)
            && ActiveGoals.SequenceEqual(other.ActiveGoals)
            && Equals(CurrentPlan, other.CurrentPlan)
            && Equals(CurrentStep, other.CurrentStep)
            && Knowledge.Equals(other.Knowledge);
    }

    public override bool Equals(object? obj) => Equals(obj as ActorDecisionView);

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
}
