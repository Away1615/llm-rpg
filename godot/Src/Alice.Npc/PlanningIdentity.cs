using Alice.Identity;
using Alice.Interaction;
using Alice.Perception;
using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.World;
using Alice.Commitments;
using Alice.Items;
using Alice.Navigation;

namespace Alice.Npc;



public sealed record GoalId
{
    public GoalId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Goal identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public sealed record PlanId
{
    public PlanId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Plan identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public sealed record PlanStepId
{
    public PlanStepId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Plan step identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}


public enum PlanRuntimeStatus
{
    Active,
    Suspended,
    Completed,
    Superseded,
    Cancelled
}

public enum PlanStepRuntimeStatus
{
    Pending,
    InProgress,
    Completed,
    Blocked,
    Interrupted,
    Cancelled
}



/// <summary>Runtime-owned facts for one immutable plan step.</summary>
public sealed class PlanStepRuntimeState
{
    private PlanStepRuntimeStatus _status;
    private bool _boundActionInvalidated;
    private GameActionSpec? _resolvedAction;
    private DamageActionOutcomeReceipt? _lastFailureReceipt;
    private PickupActionOutcomeReceipt? _lastPickupFailureReceipt;
    private int _resolutionAttempts;

    internal PlanStepRuntimeState(PlanStepId planStepId, PlanStepRuntimeStatus status)
    {
        ArgumentNullException.ThrowIfNull(planStepId);
        PlanStepId = planStepId;
        _status = status;
    }

    public PlanStepId PlanStepId { get; }
    public PlanStepRuntimeStatus Status => _status;
    public bool BoundActionInvalidated => _boundActionInvalidated;
    public GameActionSpec? ResolvedAction => _resolvedAction;
    public DamageActionOutcomeReceipt? LastFailureReceipt => _lastFailureReceipt;
    public PickupActionOutcomeReceipt? LastPickupFailureReceipt => _lastPickupFailureReceipt;
    public int ResolutionAttempts => _resolutionAttempts;

    internal void SetStatus(PlanStepRuntimeStatus status)
    {
        _status = status;
    }

    internal void InvalidateBoundAction()
    {
        _boundActionInvalidated = true;
    }

    internal void ResolveAction(GameActionSpec action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _resolvedAction = action;
        _resolutionAttempts = checked(_resolutionAttempts + 1);
    }

    internal void ClearResolvedAction()
    {
        _resolvedAction = null;
    }

    internal void RecordFailure(DamageActionOutcomeReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        _lastFailureReceipt = receipt;
    }

    internal void RecordPickupFailure(PickupActionOutcomeReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        _lastPickupFailureReceipt = receipt;
    }
}



/// <summary>Immutable persistent NPC planning specification without runtime execution state.</summary>
public sealed class NpcPlanningState : IEquatable<NpcPlanningState>
{
    private readonly ReadOnlyCollection<NpcGoal> _activeGoals;

    public NpcPlanningState(IEnumerable<NpcGoal> activeGoals, NpcPlan? currentPlan)
    {
        ArgumentNullException.ThrowIfNull(activeGoals);
        NpcGoal[] snapshot = activeGoals.ToArray();
        var goalIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (NpcGoal goal in snapshot)
        {
            ArgumentNullException.ThrowIfNull(goal);
            if (!goalIds.Add(goal.GoalId.Value))
            {
                throw new ArgumentException("Active goals must have unique GoalIds.", nameof(activeGoals));
            }
        }

        Array.Sort(snapshot, NpcGoalComparer.Instance);
        if (currentPlan is not null)
        {
            EnsureCurrentPlanGoal(snapshot, currentPlan);
        }

        _activeGoals = Array.AsReadOnly(snapshot);
        CurrentPlan = currentPlan;
    }

    public IReadOnlyList<NpcGoal> ActiveGoals => _activeGoals;
    public NpcPlan? CurrentPlan { get; }

    public bool Equals(NpcPlanningState? other)
    {
        return other is not null && ActiveGoals.SequenceEqual(other.ActiveGoals) && Equals(CurrentPlan, other.CurrentPlan);
    }

    public override bool Equals(object? obj) => Equals(obj as NpcPlanningState);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (NpcGoal goal in ActiveGoals)
        {
            hash.Add(goal);
        }

        hash.Add(CurrentPlan);
        return hash.ToHashCode();
    }

    private static void EnsureCurrentPlanGoal(IEnumerable<NpcGoal> goals, NpcPlan currentPlan)
    {
        foreach (NpcGoal goal in goals)
        {
            if (goal.GoalId == currentPlan.Goal.GoalId)
            {
                if (goal != currentPlan.Goal)
                {
                    throw new ArgumentException("Current plan goal must equal the active goal with the same GoalId.", nameof(currentPlan));
                }

                return;
            }
        }

        throw new ArgumentException("Current plan goal must be active.", nameof(currentPlan));
    }

    private sealed class NpcGoalComparer : IComparer<NpcGoal>
    {
        public static NpcGoalComparer Instance { get; } = new();

        public int Compare(NpcGoal? left, NpcGoal? right)
        {
            return StringComparer.Ordinal.Compare(left?.GoalId.Value, right?.GoalId.Value);
        }
    }
}



public sealed class NpcPlan : IEquatable<NpcPlan>
{
    private readonly ReadOnlyCollection<PlanStep> _steps;

    public NpcPlan(PlanId planId, ActorId actorId, NpcGoal goal, int revision, IEnumerable<PlanStep> steps)
    {
        ArgumentNullException.ThrowIfNull(planId);
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(steps);
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        PlanStep[] snapshot = steps.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(steps));
        }

        var stepIds = new HashSet<PlanStepId>();
        foreach (PlanStep step in snapshot)
        {
            ArgumentNullException.ThrowIfNull(step);
            if (!stepIds.Add(step.PlanStepId))
            {
                throw new ArgumentException("Plan step identities must be unique.", nameof(steps));
            }

            EnsureStepActor(step, actorId);
        }

        PlanId = planId;
        ActorId = actorId;
        Goal = goal;
        Revision = revision;
        _steps = Array.AsReadOnly(snapshot);
    }

    public PlanId PlanId { get; }
    public ActorId ActorId { get; }
    public NpcGoal Goal { get; }
    public int Revision { get; }
    public IReadOnlyList<PlanStep> Steps => _steps;

    public bool Equals(NpcPlan? other)
    {
        return other is not null && PlanId == other.PlanId && ActorId == other.ActorId && Goal == other.Goal && Revision == other.Revision && Steps.SequenceEqual(other.Steps);
    }

    public override bool Equals(object? obj) => Equals(obj as NpcPlan);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PlanId);
        hash.Add(ActorId);
        hash.Add(Goal);
        hash.Add(Revision);
        foreach (PlanStep step in Steps)
        {
            hash.Add(step);
        }

        return hash.ToHashCode();
    }

    private static void EnsureStepActor(PlanStep step, ActorId actorId)
    {
        ActorId predicateActor = step.DesiredResult switch
        {
            InventoryAtLeast inventory => inventory.ActorId,
            BodyStateWithin body => body.ActorId,
            InteractionTargetReached reached => reached.ActorId,
            TargetTerminal terminal => terminal.ActorId,
            CommitmentStatusMatches commitment => commitment.Debtor,
            ExperienceCompleted experience => experience.ActorId,
            _ => throw new ArgumentException("Plan step result predicate is outside this closed planning slice.", nameof(step))
        };
        if (predicateActor != actorId || step.Action is not null && step.Action.ActorId != actorId)
        {
            throw new ArgumentException("Plan step predicate and action must belong to the plan actor.", nameof(step));
        }
    }
}



public sealed class PlanStep : IEquatable<PlanStep>
{
    public PlanStep(PlanStepId planStepId, GoalObjective objective, GameActionSpec? action, TargetRef? target, ResultPredicate desiredResult)
    {
        ArgumentNullException.ThrowIfNull(planStepId);
        ArgumentNullException.ThrowIfNull(objective);
        ArgumentNullException.ThrowIfNull(desiredResult);
        EnsureObjectiveResultPairing(objective, desiredResult);
        if (action is not null)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (target != action.Binding.ContractRef.TargetRef)
            {
                throw new ArgumentException("Plan step target must match its action binding target.", nameof(target));
            }
        }
        if (desiredResult is InteractionTargetReached reached && (target is null || target != reached.TargetRef) || desiredResult is TargetTerminal terminal && (target is null || target != terminal.TargetRef)) throw new ArgumentException("Target-scoped predicate must match the plan step target.", nameof(target));
        if (objective is FulfillCommitmentObjective && target is not null)
        {
            throw new ArgumentException("A Commitment fulfilment step cannot carry an unrelated target.", nameof(target));
        }

        PlanStepId = planStepId;
        Objective = objective;
        Action = action;
        Target = target;
        DesiredResult = desiredResult;
    }

    public PlanStepId PlanStepId { get; }
    public GoalObjective Objective { get; }
    public GameActionSpec? Action { get; }
    public TargetRef? Target { get; }
    public ResultPredicate DesiredResult { get; }

    public bool Equals(PlanStep? other)
    {
        return other is not null &&
            PlanStepId == other.PlanStepId &&
            Objective == other.Objective &&
            Action == other.Action &&
            Target == other.Target &&
            DesiredResult == other.DesiredResult;
    }

    public override bool Equals(object? obj) => Equals(obj as PlanStep);

    public override int GetHashCode() => HashCode.Combine(PlanStepId, Objective, Action, Target, DesiredResult);

    private static void EnsureObjectiveResultPairing(GoalObjective objective, ResultPredicate desiredResult)
    {
        if (objective is AcquireItemObjective acquire && desiredResult is InventoryAtLeast inventory &&
            acquire.ItemTypeId == inventory.ItemTypeId && acquire.Quantity == inventory.Quantity)
        {
            return;
        }

        if (objective is AcquireItemObjective && desiredResult is InteractionTargetReached or TargetTerminal) return;

        if (objective is ReachTargetObjective reach && desiredResult is InteractionTargetReached reached &&
            reach.TargetRef == reached.TargetRef)
        {
            return;
        }

        if (objective is FulfillCommitmentObjective fulfill && desiredResult is CommitmentStatusMatches commitment &&
            fulfill.CommitmentId == commitment.CommitmentId && commitment.Status == Alice.Commitments.CommitmentStatus.Fulfilled)
        {
            return;
        }

        if (objective is ExperienceObjective experience && desiredResult is ExperienceCompleted completed &&
            experience.ExperienceId == completed.ExperienceId)
        {
            return;
        }

        if (objective is MaintainBodyObjective maintain && desiredResult is BodyStateWithin body &&
            maintain.Metric == body.Metric && maintain.MinimumAcceptableLevel == body.MinimumAcceptableLevel)
        {
            return;
        }

        throw new ArgumentException("Plan step objective and desired result must be the same contracted semantic case.", nameof(desiredResult));
    }
}



public abstract record ResultPredicate
{
    private protected ResultPredicate()
    {
    }
}

public sealed record InventoryAtLeast : ResultPredicate
{
    public InventoryAtLeast(ActorId actorId, ItemTypeId itemTypeId, int quantity)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(itemTypeId);
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        ActorId = actorId;
        ItemTypeId = itemTypeId;
        Quantity = quantity;
    }

    public ActorId ActorId { get; }
    public ItemTypeId ItemTypeId { get; }
    public int Quantity { get; }
}

public sealed record BodyStateWithin : ResultPredicate
{
    public BodyStateWithin(ActorId actorId, BodyMetric metric, int minimumAcceptableLevel)
    {
        ActorIdentity.ValidateActorId(actorId);
        if (!Enum.IsDefined(metric))
        {
            throw new ArgumentOutOfRangeException(nameof(metric));
        }

        if (minimumAcceptableLevel != 50)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAcceptableLevel));
        }

        ActorId = actorId;
        Metric = metric;
        MinimumAcceptableLevel = minimumAcceptableLevel;
    }

    public ActorId ActorId { get; }
    public BodyMetric Metric { get; }
    public int MinimumAcceptableLevel { get; }
}

public sealed record InteractionTargetReached : ResultPredicate
{
    public InteractionTargetReached(ActorId actorId, TargetRef targetRef, InteractionRange interactionRange)
    {
        ActorIdentity.ValidateActorId(actorId); ArgumentNullException.ThrowIfNull(targetRef);
        ActorId = actorId; TargetRef = targetRef; InteractionRange = interactionRange;
    }
    public ActorId ActorId { get; }
    public TargetRef TargetRef { get; }
    public InteractionRange InteractionRange { get; }
}

public sealed record TargetTerminal : ResultPredicate
{
    public TargetTerminal(ActorId actorId, TargetRef targetRef)
    {
        ActorIdentity.ValidateActorId(actorId); ArgumentNullException.ThrowIfNull(targetRef);
        ActorId = actorId; TargetRef = targetRef;
    }
    public ActorId ActorId { get; }
    public TargetRef TargetRef { get; }
}

public sealed record CommitmentStatusMatches : ResultPredicate
{
    public CommitmentStatusMatches(ActorId debtor, CommitmentId commitmentId, CommitmentStatus status)
    {
        ActorIdentity.ValidateActorId(debtor);
        AttendancePlanningIdentity.Validate(commitmentId.Value, nameof(commitmentId));
        if (status != CommitmentStatus.Fulfilled)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Attendance planning currently evaluates only Fulfilled Commitment evidence.");
        }

        Debtor = debtor;
        CommitmentId = commitmentId;
        Status = status;
    }

    public ActorId Debtor { get; }
    public CommitmentId CommitmentId { get; }
    public CommitmentStatus Status { get; }
}

public sealed record ExperienceCompleted : ResultPredicate
{
    public ExperienceCompleted(ActorId actorId, ExperienceId experienceId)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(experienceId);
        ActorId = actorId;
        ExperienceId = experienceId;
    }

    public ActorId ActorId { get; }
    public ExperienceId ExperienceId { get; }
}



/// <summary>Immutable actor-legitimate evidence for bounded plan-result evaluation.</summary>
public readonly struct PlanResultEvidence : IEquatable<PlanResultEvidence>
{
    private readonly ReadOnlyCollection<ActorVisibleTargetSpatialSnapshot> _targets;
    private readonly ReadOnlyCollection<TargetRef> _terminalTargets;
    public PlanResultEvidence(SharedActorState actorState, WorldPosition actorPosition, IEnumerable<ActorVisibleTargetSpatialSnapshot> targets, IEnumerable<TargetRef> terminalTargets)
    {
        ArgumentNullException.ThrowIfNull(actorState); ArgumentNullException.ThrowIfNull(targets); ArgumentNullException.ThrowIfNull(terminalTargets);
        if (!double.IsFinite(actorPosition.X) || !double.IsFinite(actorPosition.Y)) throw new ArgumentOutOfRangeException(nameof(actorPosition));
        ActorVisibleTargetSpatialSnapshot[] targetCopy = targets.ToArray(); TargetRef[] terminalCopy = terminalTargets.ToArray();
        EnsureTargets(targetCopy); EnsureTerminalTargets(terminalCopy); Array.Sort(targetCopy, TargetComparer.Instance); Array.Sort(terminalCopy, TerminalComparer.Instance);
        ActorState = actorState; ActorPosition = actorPosition; _targets = Array.AsReadOnly(targetCopy); _terminalTargets = Array.AsReadOnly(terminalCopy);
    }
    public SharedActorState ActorState { get; }
    public WorldPosition ActorPosition { get; }
    public IReadOnlyList<ActorVisibleTargetSpatialSnapshot> Targets => _targets;
    public IReadOnlyList<TargetRef> TerminalTargets => _terminalTargets;
    public bool TryResolveTarget(TargetRef targetRef, out ActorVisibleTargetSpatialSnapshot? target) { ArgumentNullException.ThrowIfNull(targetRef); foreach (ActorVisibleTargetSpatialSnapshot value in Targets) if (value.TargetRef == targetRef) { target = value; return true; } target = null; return false; }
    public bool Equals(PlanResultEvidence other) => ActorState == other.ActorState && ActorPosition == other.ActorPosition && Targets.SequenceEqual(other.Targets) && TerminalTargets.SequenceEqual(other.TerminalTargets);
    public override bool Equals(object? obj) => obj is PlanResultEvidence other && Equals(other);
    public override int GetHashCode() { var hash = new HashCode(); hash.Add(ActorState); hash.Add(ActorPosition); foreach (var target in Targets) hash.Add(target); foreach (var terminal in TerminalTargets) hash.Add(terminal); return hash.ToHashCode(); }
    private static void EnsureTargets(IEnumerable<ActorVisibleTargetSpatialSnapshot> values) { var refs = new HashSet<string>(StringComparer.Ordinal); foreach (var value in values) { ArgumentNullException.ThrowIfNull(value); if (!Enum.IsDefined(value.TargetKind) || !double.IsFinite(value.Position.X) || !double.IsFinite(value.Position.Y) || !refs.Add(value.TargetRef.Value)) throw new ArgumentException("Target evidence must be valid and unique."); } }
    private static void EnsureTerminalTargets(IEnumerable<TargetRef> values) { var refs = new HashSet<string>(StringComparer.Ordinal); foreach (TargetRef value in values) { ArgumentNullException.ThrowIfNull(value); if (!refs.Add(value.Value)) throw new ArgumentException("Terminal targets must be unique."); } }
    private sealed class TargetComparer : IComparer<ActorVisibleTargetSpatialSnapshot> { public static TargetComparer Instance { get; } = new(); public int Compare(ActorVisibleTargetSpatialSnapshot? left, ActorVisibleTargetSpatialSnapshot? right) => StringComparer.Ordinal.Compare(left?.TargetRef.Value, right?.TargetRef.Value); }
    private sealed class TerminalComparer : IComparer<TargetRef> { public static TerminalComparer Instance { get; } = new(); public int Compare(TargetRef? left, TargetRef? right) => StringComparer.Ordinal.Compare(left?.Value, right?.Value); }
}



public static class PlanResultPredicateEvaluator
{
    public static bool Evaluate(ResultPredicate predicate, SharedActorState actorState)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(actorState);
        ActorId predicateActor = GetPredicateActorId(predicate);
        if (predicateActor != actorState.Identity.ActorId)
        {
            throw new ArgumentException("Result predicate actor must match the supplied Shared Actor state.", nameof(actorState));
        }

        return predicate switch
        {
            InventoryAtLeast inventory => HasInventoryAtLeast(actorState.Inventory, inventory),
            BodyStateWithin body => IsBodyStateWithin(actorState.Body, body),
            _ => throw new ArgumentException("Result predicate is outside this closed planning slice.", nameof(predicate))
        };
    }

    public static bool Evaluate(ResultPredicate predicate, PlanResultEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (GetPredicateActorId(predicate) != evidence.ActorState.Identity.ActorId) throw new ArgumentException("Result predicate actor must match evidence Actor state.", nameof(evidence));
        return predicate switch
        {
            InventoryAtLeast inventory => HasInventoryAtLeast(evidence.ActorState.Inventory, inventory),
            BodyStateWithin body => IsBodyStateWithin(evidence.ActorState.Body, body),
            InteractionTargetReached reached => IsTargetReached(evidence, reached),
            TargetTerminal terminal => evidence.TerminalTargets.Contains(terminal.TargetRef),
            _ => throw new ArgumentException("Result predicate is outside this closed planning slice.", nameof(predicate))
        };
    }

    public static bool Evaluate(ResultPredicate predicate, Commitment commitment)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(commitment);
        return predicate is CommitmentStatusMatches expected &&
            expected.Debtor == commitment.Debtor &&
            expected.CommitmentId == commitment.CommitmentId &&
            expected.Status == commitment.Status;
    }

    public static bool Evaluate(ResultPredicate predicate, ExperienceCompletionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(evidence);
        return predicate is ExperienceCompleted expected
            && expected.ActorId == evidence.ActorId
            && expected.ExperienceId == evidence.ExperienceId;
    }

    private static ActorId GetPredicateActorId(ResultPredicate predicate)
    {
        return predicate switch
        {
            InventoryAtLeast inventory => inventory.ActorId,
            BodyStateWithin body => body.ActorId,
            InteractionTargetReached reached => reached.ActorId,
            TargetTerminal terminal => terminal.ActorId,
            CommitmentStatusMatches commitment => commitment.Debtor,
            ExperienceCompleted experience => experience.ActorId,
            _ => throw new ArgumentException("Result predicate is outside this closed planning slice.", nameof(predicate))
        };
    }

    private static bool IsTargetReached(PlanResultEvidence evidence, InteractionTargetReached predicate)
    {
        if (!evidence.TryResolveTarget(predicate.TargetRef, out ActorVisibleTargetSpatialSnapshot? target) || target is null) return false;
        return InteractionTargetReachability.IsWithinRange(evidence.ActorPosition, target.Position, predicate.InteractionRange);
    }

    private static bool HasInventoryAtLeast(InventoryState inventory, InventoryAtLeast predicate)
    {
        long quantity = 0;
        foreach (InventoryEntry entry in inventory.Entries)
        {
            if (entry is StackEntry stack && stack.ItemTypeId == predicate.ItemTypeId)
            {
                checked
                {
                    quantity += stack.Quantity;
                }
            }
        }

        return quantity >= predicate.Quantity;
    }

    private static bool IsBodyStateWithin(ActorBodyState body, BodyStateWithin predicate)
    {
        return predicate.Metric switch
        {
            BodyMetric.Health => BodyGoalAssessment.IsWithinMinimum(predicate.Metric, body.Health.Current, body.Health.Maximum, predicate.MinimumAcceptableLevel),
            BodyMetric.Satiety => BodyGoalAssessment.IsWithinMinimum(predicate.Metric, body.Satiety.Value, 100, predicate.MinimumAcceptableLevel),
            BodyMetric.Spirit => BodyGoalAssessment.IsWithinMinimum(predicate.Metric, body.Spirit.Value, 100, predicate.MinimumAcceptableLevel),
            _ => throw new ArgumentOutOfRangeException(nameof(predicate))
        };
    }
}



/// <summary>Sole synchronous writer of execution status for one immutable NpcPlan.</summary>
public sealed class PlanRuntime
{
    private readonly NpcPlan _plan;
    private readonly ReadOnlyCollection<PlanStepRuntimeState> _steps;
    private PlanRuntimeStatus _status;
    private int _currentStepIndex;

    public PlanRuntime(NpcPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        PlanStepRuntimeState[] steps = CreateInitialSteps(plan);
        _plan = plan;
        _steps = Array.AsReadOnly(steps);
        _status = PlanRuntimeStatus.Active;
        _currentStepIndex = 0;
    }

    public static PlanRuntime RestoreProgress(
        NpcPlan plan,
        PlanRuntimeStatus status,
        int currentStepIndex,
        IReadOnlyList<PlanStepRuntimeStatus> stepStatuses)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(stepStatuses);
        if (status is not PlanRuntimeStatus.Active and not PlanRuntimeStatus.Suspended
            || currentStepIndex < 0
            || currentStepIndex >= plan.Steps.Count
            || stepStatuses.Count != plan.Steps.Count)
            throw new ArgumentException("Restored PlanRuntime progress is structurally invalid.");
        for (int index = 0; index < stepStatuses.Count; index++)
        {
            PlanStepRuntimeStatus expected = index < currentStepIndex
                ? PlanStepRuntimeStatus.Completed
                : index == currentStepIndex
                    ? status == PlanRuntimeStatus.Active
                        ? PlanStepRuntimeStatus.InProgress
                        : PlanStepRuntimeStatus.Interrupted
                    : PlanStepRuntimeStatus.Pending;
            if (stepStatuses[index] != expected)
                throw new ArgumentException("Restored PlanRuntime step progression is not contiguous.", nameof(stepStatuses));
        }
        var runtime = new PlanRuntime(plan)
        {
            _status = status,
            _currentStepIndex = currentStepIndex
        };
        for (int index = 0; index < runtime._steps.Count; index++)
            runtime._steps[index].SetStatus(stepStatuses[index]);
        return runtime;
    }

    public PlanId PlanId => _plan.PlanId;
    public PlanRuntimeStatus Status => _status;
    public int CurrentStepIndex => _currentStepIndex;
    public IReadOnlyList<PlanStepRuntimeState> Steps => _steps;
    public PlanStep? GetCurrentPlanStep() => Status == PlanRuntimeStatus.Active && _steps[CurrentStepIndex].Status == PlanStepRuntimeStatus.InProgress ? _plan.Steps[CurrentStepIndex] : null;
    public bool OwnsPlan(NpcPlan plan) => plan is not null && _plan.Equals(plan);

    public bool TryCompleteCurrentStep(SharedActorState actorState)
    {
        return TryCompleteCurrentStepCore(PlanResultPredicateEvaluator.Evaluate, actorState);
    }

    public bool TryCompleteCurrentStep(PlanResultEvidence evidence)
    {
        return TryCompleteCurrentStepCore(PlanResultPredicateEvaluator.Evaluate, evidence);
    }

    public bool TryCompleteCurrentStep(Commitment commitment)
    {
        return TryCompleteCurrentStepCore(PlanResultPredicateEvaluator.Evaluate, commitment);
    }

    public bool TryCompleteCurrentStep(ExperienceCompletionEvidence evidence)
    {
        return TryCompleteCurrentStepCore(PlanResultPredicateEvaluator.Evaluate, evidence);
    }

    private bool TryCompleteCurrentStepCore<T>(Func<ResultPredicate, T, bool> evaluator, T evidence)
    {
        EnsureActiveCurrentStep();
        bool complete = evaluator(_plan.Steps[CurrentStepIndex].DesiredResult, evidence);
        if (!complete)
        {
            return false;
        }

        _steps[CurrentStepIndex].SetStatus(PlanStepRuntimeStatus.Completed);
        if (CurrentStepIndex == _steps.Count - 1)
        {
            _status = PlanRuntimeStatus.Completed;
            return true;
        }

        _currentStepIndex++;
        _steps[_currentStepIndex].SetStatus(PlanStepRuntimeStatus.InProgress);
        return true;
    }

    public void Suspend()
    {
        EnsureActiveCurrentStep();
        _steps[CurrentStepIndex].SetStatus(PlanStepRuntimeStatus.Interrupted);
        _status = PlanRuntimeStatus.Suspended;
    }

    public void Resume()
    {
        if (Status != PlanRuntimeStatus.Suspended || _steps[CurrentStepIndex].Status != PlanStepRuntimeStatus.Interrupted)
        {
            throw new InvalidOperationException("Only a suspended runtime with an interrupted current step can resume.");
        }

        _steps[CurrentStepIndex].SetStatus(PlanStepRuntimeStatus.InProgress);
        _status = PlanRuntimeStatus.Active;
    }

    public void Cancel()
    {
        SetTerminalStatus(PlanRuntimeStatus.Cancelled);
    }

    public void Supersede()
    {
        SetTerminalStatus(PlanRuntimeStatus.Superseded);
    }

    public void InvalidateBoundAction(PlanStepId planStepId)
    {
        ArgumentNullException.ThrowIfNull(planStepId);
        int index = FindStepIndex(planStepId);
        if (index < 0 || _plan.Steps[index].Action is null || IsTerminalStep(_steps[index].Status))
        {
            throw new InvalidOperationException("Only a known non-terminal bound step can be invalidated.");
        }

        _steps[index].InvalidateBoundAction();
    }

    public GameActionSpec? GetCurrentBoundAction()
    {
        if (Status != PlanRuntimeStatus.Active || _steps[CurrentStepIndex].Status != PlanStepRuntimeStatus.InProgress || _steps[CurrentStepIndex].BoundActionInvalidated)
        {
            return null;
        }

        return _plan.Steps[CurrentStepIndex].Action;
    }

    public GameActionSpec? GetCurrentExecutableAction()
    {
        if (Status != PlanRuntimeStatus.Active || _steps[CurrentStepIndex].Status != PlanStepRuntimeStatus.InProgress)
        {
            return null;
        }

        GameActionSpec? boundAction = _plan.Steps[CurrentStepIndex].Action;
        if (boundAction is not null && !_steps[CurrentStepIndex].BoundActionInvalidated)
        {
            return boundAction;
        }

        return _steps[CurrentStepIndex].ResolvedAction;
    }

    public void ResolveCurrentAction(GameActionSpec action)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureActiveCurrentStep();
        if (GetCurrentExecutableAction() is not null)
        {
            throw new InvalidOperationException("The current step already has an executable action.");
        }

        PlanStep step = _plan.Steps[CurrentStepIndex];
        if (action.ActorId != _plan.ActorId || step.Target is not null && action.Binding.ContractRef.TargetRef != step.Target ||
            _steps[CurrentStepIndex].BoundActionInvalidated && action == step.Action)
        {
            throw new ArgumentException("Resolved action must belong to the plan actor and current step target.", nameof(action));
        }

        _steps[CurrentStepIndex].ResolveAction(action);
    }

    public void RecordCurrentDamageFailure(DamageActionOutcomeReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        EnsureActiveCurrentStep();
        if (receipt.Outcome != DamageActionOutcome.Rejected || receipt.PerceivedFailure is null)
        {
            throw new ArgumentException("Only rejected Damage action outcomes can be recorded as failures.", nameof(receipt));
        }

        PlanStep step = _plan.Steps[CurrentStepIndex];
        GameActionSpec? currentAction = GetCurrentExecutableAction();
        if (currentAction is null || receipt.AttemptedAction != currentAction || receipt.AttemptedAction.ActorId != _plan.ActorId ||
            step.Target is not null && receipt.AttemptedAction.Binding.ContractRef.TargetRef != step.Target)
        {
            throw new ArgumentException("Failure outcome must correlate to the current executable action.", nameof(receipt));
        }

        PlanStepRuntimeState state = _steps[CurrentStepIndex];
        state.RecordFailure(receipt);
        if (receipt.PerceivedFailure != DamageActionPerceivedFailure.AttemptFailed)
        {
            if (step.Action is not null)
            {
                state.InvalidateBoundAction();
            }

            state.ClearResolvedAction();
        }
    }

    public void RecordCurrentPickupFailure(PickupActionOutcomeReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        EnsureActiveCurrentStep();
        if (receipt.Outcome != PickupActionOutcome.Rejected || receipt.PerceivedFailure is null)
        {
            throw new ArgumentException("Only rejected Pickup action outcomes can be recorded as failures.", nameof(receipt));
        }

        PlanStep step = _plan.Steps[CurrentStepIndex];
        PlanStepRuntimeState state = _steps[CurrentStepIndex];
        if (step.Action is not null || state.ResolvedAction is null || state.ResolvedAction != receipt.AttemptedAction ||
            receipt.AttemptedAction.ActorId != _plan.ActorId || receipt.AttemptedAction.Arguments is not PickupActionArguments)
        {
            throw new ArgumentException("Pickup failure outcome must correlate to the current locally resolved Pickup action.", nameof(receipt));
        }

        state.RecordPickupFailure(receipt);
        if (receipt.PerceivedFailure != PickupActionPerceivedFailure.AttemptFailed)
        {
            state.ClearResolvedAction();
        }
    }

    private static PlanStepRuntimeState[] CreateInitialSteps(NpcPlan plan)
    {
        var steps = new PlanStepRuntimeState[plan.Steps.Count];
        for (int index = 0; index < plan.Steps.Count; index++)
        {
            steps[index] = new PlanStepRuntimeState(plan.Steps[index].PlanStepId, index == 0 ? PlanStepRuntimeStatus.InProgress : PlanStepRuntimeStatus.Pending);
        }

        return steps;
    }

    private void EnsureActiveCurrentStep()
    {
        if (Status != PlanRuntimeStatus.Active || _steps[CurrentStepIndex].Status != PlanStepRuntimeStatus.InProgress)
        {
            throw new InvalidOperationException("Only an active runtime with an in-progress current step can transition.");
        }
    }

    private void SetTerminalStatus(PlanRuntimeStatus status)
    {
        if (Status is not PlanRuntimeStatus.Active and not PlanRuntimeStatus.Suspended)
        {
            throw new InvalidOperationException("Only active or suspended runtimes can become terminal.");
        }

        foreach (PlanStepRuntimeState step in _steps)
        {
            if (step.Status is PlanStepRuntimeStatus.Pending or PlanStepRuntimeStatus.InProgress or PlanStepRuntimeStatus.Interrupted)
            {
                step.SetStatus(PlanStepRuntimeStatus.Cancelled);
            }
        }

        _status = status;
    }

    private int FindStepIndex(PlanStepId planStepId)
    {
        for (int index = 0; index < _steps.Count; index++)
        {
            if (_steps[index].PlanStepId == planStepId)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsTerminalStep(PlanStepRuntimeStatus status)
    {
        return status is PlanStepRuntimeStatus.Completed or PlanStepRuntimeStatus.Cancelled;
    }
}



/// <summary>Host-owned evidence that one actor completed one explicit lifestyle experience.</summary>
public sealed record ExperienceCompletionEvidence
{
    public ExperienceCompletionEvidence(ActorId actorId, ExperienceId experienceId)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(experienceId);
        ActorId = actorId;
        ExperienceId = experienceId;
    }

    public ActorId ActorId { get; }
    public ExperienceId ExperienceId { get; }
}
