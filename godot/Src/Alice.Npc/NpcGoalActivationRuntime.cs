using Alice.Actors;
using Alice.Commitments;
using Alice.Items;
using Alice.World;
using System.Collections.ObjectModel;

namespace Alice.Npc;



/// <summary>Sole per-NPC owner of Goal activation planning and mutable Plan runtime state.</summary>
public sealed class NpcGoalActivationRuntime
{
    private readonly ActorId _actorId;
    private readonly BodyGoalIdentitySet _bodyIdentities;
    private NpcPlanningState _planning;
    private PlanRuntime? _currentRuntime;
    private NpcPlan? _suspendedPriorPlan;
    private PlanRuntime? _suspendedPriorRuntime;
    private NpcPlan? _suspendedSatietyPlan;
    private PlanRuntime? _suspendedSatietyRuntime;

    public NpcGoalActivationRuntime(
        ActorId actorId,
        NpcPlanningState planning,
        PlanRuntime? currentPlanRuntime,
        BodyGoalIdentitySet bodyIdentities)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(planning);
        ArgumentNullException.ThrowIfNull(bodyIdentities);
        if (planning.CurrentPlan is null && currentPlanRuntime is not null ||
            planning.CurrentPlan is not null &&
            (currentPlanRuntime is null ||
             currentPlanRuntime.PlanId != planning.CurrentPlan.PlanId ||
             currentPlanRuntime.Status != PlanRuntimeStatus.Active ||
             !currentPlanRuntime.OwnsPlan(planning.CurrentPlan)))
        {
            throw new ArgumentException("Current planning and runtime must correlate exactly.", nameof(currentPlanRuntime));
        }

        if (planning.CurrentPlan is not null && planning.CurrentPlan.ActorId != actorId)
        {
            throw new ArgumentException("Current plan must belong to this NPC.", nameof(planning));
        }

        _actorId = actorId;
        _planning = planning;
        _currentRuntime = currentPlanRuntime;
        _bodyIdentities = bodyIdentities;
    }

    public NpcPlanningState Planning => _planning;
    public PlanRuntime? CurrentPlanRuntime => _currentRuntime;

    public GoalActivationDecision Reconcile(
        SharedActorState actorState,
        NpcKnowledgeState knowledge,
        IEnumerable<Commitment> currentOwnCommitments)
    {
        return ReconcileCore(actorState, knowledge, currentOwnCommitments, null, null);
    }

    public GoalActivationDecision Reconcile(
        SharedActorState actorState,
        NpcKnowledgeState knowledge,
        IEnumerable<Commitment> currentOwnCommitments,
        FreeWindowLifestyleContext lifestyleContext,
        NpcPersonalityState personality)
    {
        ArgumentNullException.ThrowIfNull(lifestyleContext);
        ArgumentNullException.ThrowIfNull(personality);
        if (lifestyleContext.ActorId != _actorId)
        {
            throw new ArgumentException("Free-window context must belong to this NPC.", nameof(lifestyleContext));
        }

        return ReconcileCore(actorState, knowledge, currentOwnCommitments, lifestyleContext, personality);
    }

    private GoalActivationDecision ReconcileCore(
        SharedActorState actorState,
        NpcKnowledgeState knowledge,
        IEnumerable<Commitment> currentOwnCommitments,
        FreeWindowLifestyleContext? lifestyleContext,
        NpcPersonalityState? personality)
    {
        ArgumentNullException.ThrowIfNull(actorState);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(currentOwnCommitments);
        if (actorState.Identity.ActorId != _actorId)
        {
            throw new ArgumentException("Shared Actor state must belong to this NPC.", nameof(actorState));
        }

        Commitment[] commitments = SnapshotCommitments(currentOwnCommitments);
        IReadOnlyList<BodyMetricAssessment> assessments = BodyGoalAssessment.Assess(actorState.Body);
        IReadOnlyList<AttendanceCandidate> readyAttendance = MaterializeReadyAttendance(commitments, knowledge);
        if (_currentRuntime is null &&
            _planning.CurrentPlan is null &&
            readyAttendance.Count > 1)
        {
            if (_suspendedSatietyRuntime is null &&
                TryActivateSurvivalForAttendanceConflict(actorState, knowledge, assessments))
            {
                return new GoalActivationDecision(GoalActivationDecisionKind.Activated, BodyMetric.Satiety);
            }

            return new GoalActivationDecision(GoalActivationDecisionKind.GoalConstraintConflictRequired, null);
        }

        NpcPlanningState reconciledBodyPlanning = BodyGoalSetReconciler.Reconcile(
            _planning,
            actorState.Body,
            _bodyIdentities);
        AutonomousGoalGenerationDecision? lifestyleDecision = null;
        if (lifestyleContext is not null &&
            _currentRuntime is null &&
            _planning.CurrentPlan is null &&
            _planning.Equals(reconciledBodyPlanning) &&
            FindExistingAttendanceGoalId() is null &&
            commitments.All(value => !IsActiveOwnAttendanceCommitment(value)) &&
            assessments.All(value => value.Band == BodyNeedBand.Acceptable))
        {
            lifestyleDecision = AutonomousGoalGenerator.Generate(lifestyleContext, personality!);
            if (lifestyleDecision.Kind == AutonomousGoalGenerationDecisionKind.GoalConstraintConflictRequired)
            {
                return new GoalActivationDecision(GoalActivationDecisionKind.GoalConstraintConflictRequired, null);
            }
        }

        bool currentSatietyPlan = IsSatietyPlan(_planning.CurrentPlan);
        ReplacePlanningIfChanged(reconciledBodyPlanning);
        ReplacePlanningIfChanged(AddActiveAttendanceGoals(_planning, commitments));

        GoalActivationDecision? attendanceEvidenceDecision = ReconcileCurrentAttendanceEvidence(commitments);
        if (attendanceEvidenceDecision?.Kind is GoalActivationDecisionKind.Completed or GoalActivationDecisionKind.Cancelled)
        {
            return attendanceEvidenceDecision;
        }

        GoalActivationDecision? bodyDecision = ReconcileBody(actorState, knowledge, assessments, currentSatietyPlan);
        if (bodyDecision?.Kind == GoalActivationDecisionKind.Preempted)
        {
            return bodyDecision;
        }

        if (attendanceEvidenceDecision?.Kind == GoalActivationDecisionKind.CommitmentEvidenceMissing)
        {
            return attendanceEvidenceDecision;
        }

        if (bodyDecision is not null)
        {
            return bodyDecision;
        }

        if (_currentRuntime is not null)
        {
            return attendanceEvidenceDecision ?? new GoalActivationDecision(GoalActivationDecisionKind.NoChange, null);
        }

        GoalActivationDecision? terminalInactiveGoal = RemoveTerminalAttendanceGoalWithoutRuntime(commitments);
        if (terminalInactiveGoal is not null)
        {
            return terminalInactiveGoal;
        }

        if (readyAttendance.Count == 1)
        {
            AttendanceCandidate candidate = readyAttendance[0];
            Activate(candidate.Plan);
            return new GoalActivationDecision(
                GoalActivationDecisionKind.Activated,
                null,
                candidate.Commitment.CommitmentId);
        }

        Commitment[] activeAttendance = commitments
            .Where(IsActiveOwnAttendanceCommitment)
            .ToArray();
        if (activeAttendance.Length > 0)
        {
            CommitmentId? missingId = activeAttendance.Length == 1
                ? activeAttendance[0].CommitmentId
                : null;
            return new GoalActivationDecision(GoalActivationDecisionKind.PlanMissing, null, missingId);
        }

        if (lifestyleDecision?.Kind == AutonomousGoalGenerationDecisionKind.Candidate)
        {
            NpcPlan plan = LifestylePlanMaterializer.Materialize(lifestyleContext!, lifestyleDecision.Opportunity!);
            AddLifestyleGoal(plan.Goal);
            Activate(plan);
            return new GoalActivationDecision(GoalActivationDecisionKind.Activated, null);
        }

        CommitmentId? existingAttendanceGoal = FindExistingAttendanceGoalId();
        return existingAttendanceGoal is not null
            ? new GoalActivationDecision(GoalActivationDecisionKind.CommitmentEvidenceMissing, null, existingAttendanceGoal)
            : new GoalActivationDecision(GoalActivationDecisionKind.NoChange, null);
    }

    private void AddLifestyleGoal(NpcGoal goal)
    {
        NpcGoal? existing = _planning.ActiveGoals.SingleOrDefault(value => value.GoalId == goal.GoalId);
        if (existing is not null && existing != goal)
        {
            throw new ArgumentException("Lifestyle Goal identity collides with a different objective.", nameof(goal));
        }

        if (existing is null)
        {
            _planning = new NpcPlanningState(_planning.ActiveGoals.Append(goal), _planning.CurrentPlan);
        }
    }

    private void ReplacePlanningIfChanged(NpcPlanningState planning)
    {
        if (!_planning.Equals(planning))
        {
            _planning = planning;
        }
    }

    private bool TryActivateSurvivalForAttendanceConflict(
        SharedActorState actorState,
        NpcKnowledgeState knowledge,
        IReadOnlyList<BodyMetricAssessment> assessments)
    {
        BodyMetricAssessment? highest = assessments.FirstOrDefault(value => value.Band != BodyNeedBand.Acceptable);
        if (highest?.Band != BodyNeedBand.Survival ||
            highest.Metric != BodyMetric.Satiety ||
            !SatietyConsumptionPlanMaterializer.TryMaterialize(
                actorState,
                knowledge,
                _bodyIdentities,
                out NpcPlan? plan))
        {
            return false;
        }

        _planning = BodyGoalSetReconciler.Reconcile(_planning, actorState.Body, _bodyIdentities);
        Activate(plan!);
        return true;
    }

    private GoalActivationDecision? ReconcileBody(
        SharedActorState actorState,
        NpcKnowledgeState knowledge,
        IReadOnlyList<BodyMetricAssessment> assessments,
        bool currentSatietyPlan)
    {
        if (currentSatietyPlan && assessments.All(value => value.Band == BodyNeedBand.Acceptable))
        {
            return CompleteSatietyIfSatisfied(actorState);
        }

        if (_currentRuntime is not null && _currentRuntime.Status == PlanRuntimeStatus.Completed)
        {
            return ClearCompletedCurrent();
        }

        BodyMetricAssessment? highest = assessments.FirstOrDefault(value => value.Band != BodyNeedBand.Acceptable);
        if (highest is null)
        {
            return null;
        }

        if (highest.Metric is BodyMetric.Health or BodyMetric.Spirit)
        {
            return new GoalActivationDecision(GoalActivationDecisionKind.PlanMissing, highest.Metric);
        }

        if (_currentRuntime is not null && IsSatietyPlan(_planning.CurrentPlan))
        {
            if (highest.Band != BodyNeedBand.Survival && _suspendedPriorRuntime is not null)
            {
                if (IsTerminal(_suspendedPriorRuntime))
                {
                    RemoveGoal(_suspendedPriorPlan?.Goal);
                    ClearSuspendedPrior();
                    return new GoalActivationDecision(GoalActivationDecisionKind.NoChange, BodyMetric.Satiety);
                }

                return ResumePrior();
            }

            return CompleteSatietyIfSatisfied(actorState);
        }

        if (_currentRuntime is not null)
        {
            if (highest.Band != BodyNeedBand.Survival)
            {
                return new GoalActivationDecision(GoalActivationDecisionKind.NoChange, BodyMetric.Satiety);
            }

            if (!SatietyConsumptionPlanMaterializer.TryMaterialize(
                    actorState,
                    knowledge,
                    _bodyIdentities,
                    out NpcPlan? plan))
            {
                return new GoalActivationDecision(GoalActivationDecisionKind.PlanMissing, BodyMetric.Satiety);
            }

            _currentRuntime.Suspend();
            _suspendedPriorPlan = _planning.CurrentPlan;
            _suspendedPriorRuntime = _currentRuntime;
            Activate(plan!);
            return new GoalActivationDecision(GoalActivationDecisionKind.Preempted, BodyMetric.Satiety);
        }

        if (_suspendedSatietyRuntime is not null)
        {
            if (IsTerminal(_suspendedSatietyRuntime))
            {
                ClearSuspendedSatiety();
                return new GoalActivationDecision(GoalActivationDecisionKind.NoChange, BodyMetric.Satiety);
            }

            _suspendedSatietyRuntime.Resume();
            _planning = new NpcPlanningState(_planning.ActiveGoals, _suspendedSatietyPlan);
            _currentRuntime = _suspendedSatietyRuntime;
            ClearSuspendedSatiety();
            return new GoalActivationDecision(GoalActivationDecisionKind.Resumed, BodyMetric.Satiety);
        }

        if (!SatietyConsumptionPlanMaterializer.TryMaterialize(
                actorState,
                knowledge,
                _bodyIdentities,
                out NpcPlan? activatedPlan))
        {
            return new GoalActivationDecision(GoalActivationDecisionKind.PlanMissing, BodyMetric.Satiety);
        }

        Activate(activatedPlan!);
        return new GoalActivationDecision(GoalActivationDecisionKind.Activated, BodyMetric.Satiety);
    }

    private GoalActivationDecision? ReconcileCurrentAttendanceEvidence(IReadOnlyList<Commitment> commitments)
    {
        if (!TryGetAttendanceCommitmentId(_planning.CurrentPlan, out CommitmentId currentId))
        {
            ReconcileSuspendedAttendanceCancellation(commitments);
            return null;
        }

        Commitment? current = FindExactOwnCommitment(commitments, currentId);
        if (current is null || current.Term is not PresenceWindowTerm)
        {
            return new GoalActivationDecision(GoalActivationDecisionKind.CommitmentEvidenceMissing, null, currentId);
        }

        if (current.Status == CommitmentStatus.Cancelled)
        {
            _currentRuntime!.Cancel();
            RemoveCurrentPlanAndGoal();
            ResumeSuspendedSatietyIfAvailable();
            return new GoalActivationDecision(GoalActivationDecisionKind.Cancelled, null, currentId);
        }

        if (current.Status != CommitmentStatus.Fulfilled)
        {
            return null;
        }

        PlanStep? currentStep = _currentRuntime!.GetCurrentPlanStep();
        if (currentStep?.DesiredResult is not CommitmentStatusMatches predicate ||
            predicate.Debtor != _actorId ||
            predicate.CommitmentId != currentId ||
            !PlanResultPredicateEvaluator.Evaluate(predicate, current) ||
            !_currentRuntime.TryCompleteCurrentStep(current))
        {
            return new GoalActivationDecision(GoalActivationDecisionKind.CommitmentEvidenceMissing, null, currentId);
        }

        RemoveCurrentPlanAndGoal();
        ResumeSuspendedSatietyIfAvailable();
        return new GoalActivationDecision(GoalActivationDecisionKind.Completed, null, currentId);
    }

    private void ReconcileSuspendedAttendanceCancellation(IReadOnlyList<Commitment> commitments)
    {
        if (!TryGetAttendanceCommitmentId(_suspendedPriorPlan, out CommitmentId suspendedId))
        {
            return;
        }

        Commitment? current = FindExactOwnCommitment(commitments, suspendedId);
        if (current?.Status != CommitmentStatus.Cancelled)
        {
            return;
        }

        _suspendedPriorRuntime!.Cancel();
        RemoveGoal(_suspendedPriorPlan!.Goal);
        ClearSuspendedPrior();
    }

    private GoalActivationDecision? RemoveTerminalAttendanceGoalWithoutRuntime(IReadOnlyList<Commitment> commitments)
    {
        foreach (NpcGoal goal in _planning.ActiveGoals)
        {
            if (goal.Objective is not FulfillCommitmentObjective objective)
            {
                continue;
            }

            Commitment? current = FindExactOwnCommitment(commitments, objective.CommitmentId);
            if (current?.Term is not PresenceWindowTerm ||
                current.Status is not CommitmentStatus.Fulfilled and not CommitmentStatus.Cancelled)
            {
                continue;
            }

            RemoveGoal(goal);
            GoalActivationDecisionKind kind = current.Status == CommitmentStatus.Fulfilled
                ? GoalActivationDecisionKind.Completed
                : GoalActivationDecisionKind.Cancelled;
            return new GoalActivationDecision(kind, null, current.CommitmentId);
        }

        return null;
    }

    private NpcPlanningState AddActiveAttendanceGoals(
        NpcPlanningState planning,
        IEnumerable<Commitment> commitments)
    {
        var goals = planning.ActiveGoals.ToList();
        foreach (Commitment commitment in commitments.Where(IsActiveOwnAttendanceCommitment))
        {
            AttendancePlanIdentitySet identities = AttendancePlanIdentitySet.Derive(commitment.CommitmentId);
            var expected = new NpcGoal(
                identities.GoalId,
                new FulfillCommitmentObjective(commitment.CommitmentId));
            NpcGoal? existing = goals.SingleOrDefault(goal => goal.GoalId == expected.GoalId);
            if (existing is not null && existing != expected)
            {
                throw new ArgumentException("Attendance Goal identity collides with a different objective.", nameof(commitments));
            }

            if (existing is null)
            {
                goals.Add(expected);
            }
        }

        return new NpcPlanningState(goals, planning.CurrentPlan);
    }

    private IReadOnlyList<AttendanceCandidate> MaterializeReadyAttendance(
        IEnumerable<Commitment> commitments,
        NpcKnowledgeState knowledge)
    {
        var candidates = new List<AttendanceCandidate>();
        foreach (Commitment commitment in commitments.Where(IsActiveOwnAttendanceCommitment))
        {
            if (!knowledge.TryResolveAttendanceDestination(
                    commitment.CommitmentId,
                    out KnownAttendanceDestination? destination) ||
                destination is null ||
                !AttendancePlanMaterializer.TryMaterialize(
                    _actorId,
                    commitment,
                    destination,
                    out NpcPlan? plan) ||
                plan is null)
            {
                continue;
            }

            candidates.Add(new AttendanceCandidate(commitment, plan));
        }

        return candidates;
    }

    private GoalActivationDecision CompleteSatietyIfSatisfied(SharedActorState actorState)
    {
        if (_currentRuntime is null)
        {
            return new GoalActivationDecision(GoalActivationDecisionKind.NoChange, null);
        }

        if (!_currentRuntime.TryCompleteCurrentStep(actorState))
        {
            return new GoalActivationDecision(GoalActivationDecisionKind.NoChange, BodyMetric.Satiety);
        }

        _planning = BodyGoalSetReconciler.Reconcile(_planning, actorState.Body, _bodyIdentities);
        _currentRuntime = null;
        if (_suspendedPriorRuntime is not null)
        {
            if (IsTerminal(_suspendedPriorRuntime))
            {
                RemoveGoal(_suspendedPriorPlan?.Goal);
                ClearSuspendedPrior();
                return new GoalActivationDecision(GoalActivationDecisionKind.Completed, BodyMetric.Satiety);
            }

            return ResumePrior();
        }

        return new GoalActivationDecision(GoalActivationDecisionKind.Completed, BodyMetric.Satiety);
    }

    private GoalActivationDecision ResumePrior()
    {
        _currentRuntime!.Suspend();
        _suspendedSatietyPlan = _planning.CurrentPlan;
        _suspendedSatietyRuntime = _currentRuntime;
        _suspendedPriorRuntime!.Resume();
        _planning = new NpcPlanningState(_planning.ActiveGoals, _suspendedPriorPlan);
        _currentRuntime = _suspendedPriorRuntime;
        _suspendedPriorPlan = null;
        _suspendedPriorRuntime = null;
        return new GoalActivationDecision(GoalActivationDecisionKind.Resumed, BodyMetric.Satiety);
    }

    private GoalActivationDecision ClearCompletedCurrent()
    {
        RemoveCurrentPlanAndGoal();
        if (_suspendedSatietyRuntime is not null)
        {
            if (IsTerminal(_suspendedSatietyRuntime))
            {
                ClearSuspendedSatiety();
                return new GoalActivationDecision(GoalActivationDecisionKind.Completed, null);
            }

            _suspendedSatietyRuntime.Resume();
            _planning = new NpcPlanningState(_planning.ActiveGoals, _suspendedSatietyPlan);
            _currentRuntime = _suspendedSatietyRuntime;
            ClearSuspendedSatiety();
            return new GoalActivationDecision(GoalActivationDecisionKind.Resumed, BodyMetric.Satiety);
        }

        return new GoalActivationDecision(GoalActivationDecisionKind.Completed, null);
    }

    private void Activate(NpcPlan plan)
    {
        _planning = new NpcPlanningState(_planning.ActiveGoals, plan);
        _currentRuntime = new PlanRuntime(plan);
    }

    private void RemoveCurrentPlanAndGoal()
    {
        NpcGoal? goal = _planning.CurrentPlan?.Goal;
        _planning = new NpcPlanningState(
            goal is null ? _planning.ActiveGoals : _planning.ActiveGoals.Where(value => value != goal),
            null);
        _currentRuntime = null;
    }

    private void RemoveGoal(NpcGoal? goal)
    {
        if (goal is null)
        {
            return;
        }

        bool removesCurrentPlan = _planning.CurrentPlan?.Goal == goal;
        NpcPlan? currentPlan = removesCurrentPlan ? null : _planning.CurrentPlan;
        _planning = new NpcPlanningState(_planning.ActiveGoals.Where(value => value != goal), currentPlan);
        if (removesCurrentPlan)
        {
            _currentRuntime = null;
        }
    }

    private void ResumeSuspendedSatietyIfAvailable()
    {
        if (_suspendedSatietyRuntime is null || IsTerminal(_suspendedSatietyRuntime))
        {
            ClearSuspendedSatiety();
            return;
        }

        _suspendedSatietyRuntime.Resume();
        _planning = new NpcPlanningState(_planning.ActiveGoals, _suspendedSatietyPlan);
        _currentRuntime = _suspendedSatietyRuntime;
        ClearSuspendedSatiety();
    }

    private Commitment[] SnapshotCommitments(IEnumerable<Commitment> commitments)
    {
        Commitment[] snapshot = commitments.ToArray();
        var ids = new HashSet<CommitmentId>();
        foreach (Commitment commitment in snapshot)
        {
            ArgumentNullException.ThrowIfNull(commitment);
            if (!ids.Add(commitment.CommitmentId))
            {
                throw new ArgumentException("Current Commitment snapshots must have unique exact identities.", nameof(commitments));
            }
        }

        return snapshot;
    }

    private bool IsActiveOwnAttendanceCommitment(Commitment commitment) =>
        commitment.Debtor == _actorId &&
        commitment.Status == CommitmentStatus.Active &&
        commitment.Term is PresenceWindowTerm;

    private Commitment? FindExactOwnCommitment(
        IEnumerable<Commitment> commitments,
        CommitmentId commitmentId) =>
        commitments.SingleOrDefault(value =>
            value.CommitmentId == commitmentId &&
            value.Debtor == _actorId);

    private CommitmentId? FindExistingAttendanceGoalId()
    {
        foreach (NpcGoal goal in _planning.ActiveGoals)
        {
            if (goal.Objective is FulfillCommitmentObjective objective)
            {
                return objective.CommitmentId;
            }
        }

        return null;
    }

    private bool IsSatietyPlan(NpcPlan? plan) =>
        plan is not null &&
        plan.PlanId == _bodyIdentities.SatietyPlanId &&
        plan.Goal.GoalId == _bodyIdentities.SatietyGoalId;

    private static bool TryGetAttendanceCommitmentId(NpcPlan? plan, out CommitmentId commitmentId)
    {
        if (plan?.Goal.Objective is FulfillCommitmentObjective objective)
        {
            commitmentId = objective.CommitmentId;
            return true;
        }

        commitmentId = default;
        return false;
    }

    private static bool IsTerminal(PlanRuntime runtime) =>
        runtime.Status is PlanRuntimeStatus.Cancelled or PlanRuntimeStatus.Superseded;

    private void ClearSuspendedPrior()
    {
        _suspendedPriorPlan = null;
        _suspendedPriorRuntime = null;
    }

    private void ClearSuspendedSatiety()
    {
        _suspendedSatietyPlan = null;
        _suspendedSatietyRuntime = null;
    }

    private sealed record AttendanceCandidate(Commitment Commitment, NpcPlan Plan);
}



public enum BodyMetric
{
    Health,
    Satiety,
    Spirit
}

public abstract record GoalObjective
{
    private protected GoalObjective()
    {
    }
}

public sealed record AcquireItemObjective : GoalObjective
{
    public AcquireItemObjective(ItemTypeId itemTypeId, int quantity)
    {
        ArgumentNullException.ThrowIfNull(itemTypeId);
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        ItemTypeId = itemTypeId;
        Quantity = quantity;
    }

    public ItemTypeId ItemTypeId { get; }
    public int Quantity { get; }
}

public sealed record MaintainBodyObjective : GoalObjective
{
    public const int SupportedMinimumAcceptableLevel = 50;

    public MaintainBodyObjective(BodyMetric metric, int minimumAcceptableLevel)
    {
        if (!Enum.IsDefined(metric))
        {
            throw new ArgumentOutOfRangeException(nameof(metric));
        }

        if (minimumAcceptableLevel != SupportedMinimumAcceptableLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAcceptableLevel));
        }

        Metric = metric;
        MinimumAcceptableLevel = minimumAcceptableLevel;
    }

    public BodyMetric Metric { get; }
    public int MinimumAcceptableLevel { get; }
}

public sealed record KnowObjective : GoalObjective
{
    public KnowObjective(KnowledgeFactRef knowledgeFactRef)
    {
        ArgumentNullException.ThrowIfNull(knowledgeFactRef);
        KnowledgeFactRef = knowledgeFactRef;
    }

    public KnowledgeFactRef KnowledgeFactRef { get; }
}

public sealed record ReachTargetObjective : GoalObjective
{
    public ReachTargetObjective(TargetRef targetRef)
    {
        ArgumentNullException.ThrowIfNull(targetRef);
        TargetRef = targetRef;
    }

    public TargetRef TargetRef { get; }
}

public sealed record FulfillCommitmentObjective : GoalObjective
{
    public FulfillCommitmentObjective(CommitmentId commitmentId)
    {
        AttendancePlanningIdentity.Validate(commitmentId.Value, nameof(commitmentId));
        CommitmentId = commitmentId;
    }

    public CommitmentId CommitmentId { get; }
}

public sealed record ExperienceObjective : GoalObjective
{
    public ExperienceObjective(ExperienceId experienceId)
    {
        ArgumentNullException.ThrowIfNull(experienceId);
        ExperienceId = experienceId;
    }

    public ExperienceId ExperienceId { get; }
}

public sealed record NpcGoal
{
    public NpcGoal(GoalId goalId, GoalObjective objective)
    {
        ArgumentNullException.ThrowIfNull(goalId);
        ArgumentNullException.ThrowIfNull(objective);
        GoalId = goalId;
        Objective = objective;
    }

    public GoalId GoalId { get; }
    public GoalObjective Objective { get; }
}


/// <summary>Explicit host-supplied identities for the closed Body goal and Satiety plan slice.</summary>
public sealed class BodyGoalIdentitySet
{
    public BodyGoalIdentitySet(GoalId healthGoalId, GoalId satietyGoalId, GoalId spiritGoalId, PlanId satietyPlanId, PlanStepId satietyPlanStepId)
    {
        ArgumentNullException.ThrowIfNull(healthGoalId); ArgumentNullException.ThrowIfNull(satietyGoalId); ArgumentNullException.ThrowIfNull(spiritGoalId); ArgumentNullException.ThrowIfNull(satietyPlanId); ArgumentNullException.ThrowIfNull(satietyPlanStepId);
        if (healthGoalId == satietyGoalId || healthGoalId == spiritGoalId || satietyGoalId == spiritGoalId) throw new ArgumentException("Body GoalIds must be pairwise distinct.");
        HealthGoalId = healthGoalId; SatietyGoalId = satietyGoalId; SpiritGoalId = spiritGoalId; SatietyPlanId = satietyPlanId; SatietyPlanStepId = satietyPlanStepId;
    }
    public GoalId HealthGoalId { get; }
    public GoalId SatietyGoalId { get; }
    public GoalId SpiritGoalId { get; }
    public PlanId SatietyPlanId { get; }
    public PlanStepId SatietyPlanStepId { get; }
    public GoalId GetGoalId(BodyMetric metric) => metric switch { BodyMetric.Health => HealthGoalId, BodyMetric.Satiety => SatietyGoalId, BodyMetric.Spirit => SpiritGoalId, _ => throw new ArgumentOutOfRangeException(nameof(metric)) };
}



public enum GoalActivationDecisionKind
{
    NoChange,
    Activated,
    Preempted,
    Resumed,
    Completed,
    PlanMissing,
    GoalConstraintConflictRequired,
    CommitmentEvidenceMissing,
    Cancelled
}

/// <summary>Closed public outcome for one bounded general Goal activation reconciliation.</summary>
public sealed class GoalActivationDecision
{
    public GoalActivationDecision(GoalActivationDecisionKind kind, BodyMetric? selectedMetric, CommitmentId? commitmentId = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind;
        SelectedMetric = selectedMetric;
        CommitmentId = commitmentId;
    }
    public GoalActivationDecisionKind Kind { get; }
    public BodyMetric? SelectedMetric { get; }
    public CommitmentId? CommitmentId { get; }
}



/// <summary>Non-owning Body compatibility surface over one caller-supplied general Goal owner.</summary>
public sealed class BodyGoalActivationRuntime
{
    private readonly NpcGoalActivationRuntime _owner;

    public BodyGoalActivationRuntime(NpcGoalActivationRuntime owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    public NpcGoalActivationRuntime Owner => _owner;
    public NpcPlanningState Planning => _owner.Planning;
    public PlanRuntime? CurrentPlanRuntime => _owner.CurrentPlanRuntime;

    public GoalActivationDecision Reconcile(SharedActorState actorState, NpcKnowledgeState knowledge)
    {
        return _owner.Reconcile(actorState, knowledge, []);
    }
}



public enum BodyNeedBand
{
    Acceptable,
    Maintain,
    Survival
}

public sealed record BodyMetricAssessment
{
    public BodyMetricAssessment(BodyMetric metric, int currentNumerator, int maximum, BodyNeedBand band)
    {
        if (!Enum.IsDefined(metric))
        {
            throw new ArgumentOutOfRangeException(nameof(metric));
        }

        if (currentNumerator < 0 || maximum <= 0 || currentNumerator > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(currentNumerator));
        }

        if (!Enum.IsDefined(band))
        {
            throw new ArgumentOutOfRangeException(nameof(band));
        }

        Metric = metric;
        CurrentNumerator = currentNumerator;
        Maximum = maximum;
        Band = band;
    }

    public BodyMetric Metric { get; }
    public int CurrentNumerator { get; }
    public int Maximum { get; }
    public BodyNeedBand Band { get; }
}

public static class BodyGoalAssessment
{
    public static IReadOnlyList<BodyMetricAssessment> Assess(ActorBodyState body)
    {
        ArgumentNullException.ThrowIfNull(body);
        BodyMetricAssessment[] assessments =
        [
            CreateAssessment(BodyMetric.Health, body.Health.Current, body.Health.Maximum),
            CreateAssessment(BodyMetric.Satiety, body.Satiety.Value, 100),
            CreateAssessment(BodyMetric.Spirit, body.Spirit.Value, 100)
        ];
        Array.Sort(assessments, BodyMetricAssessmentComparer.Instance);
        return Array.AsReadOnly(assessments);
    }

    internal static bool IsWithinMinimum(BodyMetric metric, int currentNumerator, int maximum, int minimumAcceptableLevel)
    {
        if (!Enum.IsDefined(metric) || maximum <= 0 || minimumAcceptableLevel != 50)
        {
            throw new ArgumentOutOfRangeException();
        }

        return (long)currentNumerator * 100 >= (long)minimumAcceptableLevel * maximum;
    }

    private static BodyMetricAssessment CreateAssessment(BodyMetric metric, int currentNumerator, int maximum)
    {
        BodyNeedBand band = IsWithinMinimum(metric, currentNumerator, maximum, 50)
            ? BodyNeedBand.Acceptable
            : (long)currentNumerator * 100 <= 10L * maximum
                ? BodyNeedBand.Survival
                : BodyNeedBand.Maintain;
        return new BodyMetricAssessment(metric, currentNumerator, maximum, band);
    }

    private sealed class BodyMetricAssessmentComparer : IComparer<BodyMetricAssessment>
    {
        public static BodyMetricAssessmentComparer Instance { get; } = new();

        public int Compare(BodyMetricAssessment? left, BodyMetricAssessment? right)
        {
            long leftScaled = (long)(left?.CurrentNumerator ?? 0) * (right?.Maximum ?? 1);
            long rightScaled = (long)(right?.CurrentNumerator ?? 0) * (left?.Maximum ?? 1);
            int ratioComparison = leftScaled.CompareTo(rightScaled);
            return ratioComparison != 0 ? ratioComparison : ((int)(left?.Metric ?? 0)).CompareTo((int)(right?.Metric ?? 0));
        }
    }
}



/// <summary>Pure reconciliation of the three closed Body Maintain Goals from current Body truth.</summary>
public static class BodyGoalSetReconciler
{
    public static NpcPlanningState Reconcile(NpcPlanningState planning, ActorBodyState body, BodyGoalIdentitySet identities)
    {
        ArgumentNullException.ThrowIfNull(planning); ArgumentNullException.ThrowIfNull(body); ArgumentNullException.ThrowIfNull(identities);
        IReadOnlyList<BodyMetricAssessment> assessments = BodyGoalAssessment.Assess(body);
        var goals = new List<NpcGoal>();
        foreach (NpcGoal goal in planning.ActiveGoals)
        {
            BodyMetric? metric = FindMetric(goal.GoalId, identities);
            if (metric is null) { goals.Add(goal); continue; }
            if (goal.Objective is not MaintainBodyObjective maintain || maintain.Metric != metric || maintain.MinimumAcceptableLevel != 50) throw new ArgumentException("Body Goal identity collides with a different objective.", nameof(planning));
        }
        foreach (BodyMetricAssessment assessment in assessments)
        {
            if (assessment.Band != BodyNeedBand.Acceptable) goals.Add(new NpcGoal(identities.GetGoalId(assessment.Metric), new MaintainBodyObjective(assessment.Metric, 50)));
        }
        NpcPlan? currentPlan = planning.CurrentPlan;
        if (currentPlan is not null && !goals.Any(goal => goal == currentPlan.Goal)) currentPlan = null;
        return new NpcPlanningState(goals, currentPlan);
    }

    private static BodyMetric? FindMetric(GoalId goalId, BodyGoalIdentitySet identities)
    {
        if (goalId == identities.HealthGoalId) return BodyMetric.Health;
        if (goalId == identities.SatietyGoalId) return BodyMetric.Satiety;
        if (goalId == identities.SpiritGoalId) return BodyMetric.Spirit;
        return null;
    }
}
