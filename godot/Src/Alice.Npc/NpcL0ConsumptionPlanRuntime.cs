using Alice.Activities;
using Alice.Actors;
using Alice.Authority;
using Alice.Execution;
using Alice.Interaction;
using Alice.Perception;
using Alice.Validation;
using Alice.Items;

namespace Alice.Npc;



/// <summary>NPC-only bounded L0 orchestration over accepted Body planning and shared Consumption execution.</summary>
public sealed class NpcL0ConsumptionPlanRuntime
{
    private readonly ActorId _actorId;
    private readonly BodyGoalActivationRuntime _bodyGoals;
    private NpcKnowledgeState _knowledge;

    public NpcL0ConsumptionPlanRuntime(ActorId actorId, BodyGoalActivationRuntime bodyGoals, NpcKnowledgeState knowledge)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(bodyGoals);
        ArgumentNullException.ThrowIfNull(knowledge);
        _actorId = actorId;
        _bodyGoals = bodyGoals;
        _knowledge = knowledge;
    }

    public NpcKnowledgeState Knowledge => _knowledge;
    public BodyGoalActivationRuntime BodyGoals => _bodyGoals;

    public NpcL0ConsumptionAdvanceResult Advance(ConsumptionAuthorityRuntime authority, ConsumptionValidationContext context, GameActionId gameActionId, SimTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(gameActionId);
        if (authority.ActorState.Identity.ActorId != _actorId || context.ActorId != _actorId) throw new ArgumentException("Authority and validation context must belong to this NPC.");

        GoalActivationDecision before = _bodyGoals.Reconcile(authority.ActorState, _knowledge);
        PlanRuntime? runtime = _bodyGoals.CurrentPlanRuntime;
        GameActionSpec? action = runtime?.GetCurrentExecutableAction();
        if (action is null || action.Arguments is not ConsumptionActionArguments || action.ActorId != _actorId || runtime!.GetCurrentBoundAction() != action)
        {
            return new NpcL0ConsumptionAdvanceResult(NpcL0ConsumptionAdvanceKind.NoExecutableAction, before, before, null);
        }

        ConsumptionActionOutcomeReceipt outcome = ConsumptionActionExecutor.Execute(authority, action, gameActionId, context, observedAt);
        if (outcome.Outcome == ConsumptionActionOutcome.Rejected)
        {
            _knowledge = NpcConsumptionActionOutcomeKnowledgeTransition.Apply(_knowledge, outcome);
            runtime.InvalidateBoundAction(runtime.Steps[runtime.CurrentStepIndex].PlanStepId);
        }

        GoalActivationDecision after = _bodyGoals.Reconcile(authority.ActorState, _knowledge);
        NpcL0ConsumptionAdvanceKind kind = outcome.Outcome == ConsumptionActionOutcome.Committed ? NpcL0ConsumptionAdvanceKind.Committed : NpcL0ConsumptionAdvanceKind.Rejected;
        return new NpcL0ConsumptionAdvanceResult(kind, before, after, outcome);
    }
}



public enum NpcL0ConsumptionAdvanceKind { NoExecutableAction, Committed, Rejected }

/// <summary>Closed audit result for one zero-or-one-action NPC L0 host advance.</summary>
public sealed class NpcL0ConsumptionAdvanceResult
{
    public NpcL0ConsumptionAdvanceResult(NpcL0ConsumptionAdvanceKind kind, GoalActivationDecision beforeActionDecision, GoalActivationDecision afterActionDecision, ConsumptionActionOutcomeReceipt? outcome)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(beforeActionDecision);
        ArgumentNullException.ThrowIfNull(afterActionDecision);
        if (kind == NpcL0ConsumptionAdvanceKind.NoExecutableAction && outcome is not null || kind == NpcL0ConsumptionAdvanceKind.Committed && outcome?.Outcome != ConsumptionActionOutcome.Committed || kind == NpcL0ConsumptionAdvanceKind.Rejected && outcome?.Outcome != ConsumptionActionOutcome.Rejected) throw new ArgumentException("Advance result facts are inconsistent.");
        Kind = kind;
        BeforeActionDecision = beforeActionDecision;
        AfterActionDecision = afterActionDecision;
        Outcome = outcome;
    }

    public NpcL0ConsumptionAdvanceKind Kind { get; }
    public GoalActivationDecision BeforeActionDecision { get; }
    public GoalActivationDecision AfterActionDecision { get; }
    public ConsumptionActionOutcomeReceipt? Outcome { get; }
}



/// <summary>Deterministically materializes the one carried-item Satiety Consumption plan from actor knowledge.</summary>
public static class SatietyConsumptionPlanMaterializer
{
    public static bool TryMaterialize(SharedActorState actorState, NpcKnowledgeState knowledge, BodyGoalIdentitySet identities, out NpcPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(actorState); ArgumentNullException.ThrowIfNull(knowledge); ArgumentNullException.ThrowIfNull(identities);
        foreach (KnownConsumptionOpportunity opportunity in knowledge.KnownOpportunities.ConsumptionOpportunities)
        {
            if (!HasCarriedStack(actorState.Inventory, opportunity.SourceItemTypeId)) continue;
            ActorId actorId = actorState.Identity.ActorId;
            NpcGoal goal = new(identities.SatietyGoalId, new MaintainBodyObjective(BodyMetric.Satiety, 50));
            GameActionSpec action = new(actorId, new InteractionBinding(opportunity.ContractRef, new ExpectedContractVersion(opportunity.ObservedVersion), opportunity.BelievedRequirement.CapabilityIdentity, null), new ConsumptionActionArguments(opportunity.SourceItemTypeId));
            PlanStep step = new(identities.SatietyPlanStepId, goal.Objective, action, opportunity.ContractRef.TargetRef, new BodyStateWithin(actorId, BodyMetric.Satiety, 50));
            plan = new NpcPlan(identities.SatietyPlanId, actorId, goal, 1, [step]);
            return true;
        }
        plan = null;
        return false;
    }

    private static bool HasCarriedStack(InventoryState inventory, ItemTypeId itemTypeId) => inventory.Entries.Any(entry => entry is StackEntry stack && stack.ItemTypeId == itemTypeId);
}
