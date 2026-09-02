using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Alice.Activities;
using Alice.Actors;
using Alice.Authority;
using Alice.Commitments;
using Alice.Interaction;
using Alice.Memory;
using Alice.ModelRuntime;
using Alice.Npc;
using Alice.ProductRuntime;
using Alice.Social;
using Alice.World;

namespace Alice.Cognition;

/// <summary>Result of a real Host/Validator/Authority terminalization over one live model response.</summary>
public sealed class FormalLiveTerminalSettlement
{
    internal FormalLiveTerminalSettlement(FormalTerminalOutcomeReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        Receipt = receipt;
    }

    public FormalTerminalOutcomeReceipt Receipt { get; }
}

/// <summary>
/// Scenario-owned terminalizer. Implementations receive the decoded decision and must use a typed
/// Host/Validator/Authority adapter that can issue a non-forgeable FormalTerminalOutcomeReceipt.
/// </summary>
public interface IFormalRemotePlannerTerminalizer
{
    FormalLiveTerminalSettlement Settle(
        RemotePlannerResponse response,
        FormalModelCallEvidence modelCall);
}

/// <summary>
/// One frozen local action expansion for a model-visible PlanStep. L2 selects plan semantics; this
/// catalogue owns the typed GameActionSpec that L0/L1 is allowed to resolve and execute.
/// </summary>
public sealed record FormalPlanningActionCandidate
{
    public FormalPlanningActionCandidate(
        string gameActionId,
        GoalObjective objective,
        TargetRef? target,
        ResultPredicate desiredResult,
        GameActionSpec action)
    {
        FormalExperimentCanonical.RequireIdentity(gameActionId, nameof(gameActionId));
        ArgumentNullException.ThrowIfNull(objective);
        ArgumentNullException.ThrowIfNull(desiredResult);
        ArgumentNullException.ThrowIfNull(action);
        if (target is not null && action.Binding.ContractRef.TargetRef != target)
            throw new ArgumentException("Formal action target must match its PlanStep target.", nameof(action));
        GameActionId = gameActionId;
        Objective = objective;
        Target = target;
        DesiredResult = desiredResult;
        Action = action;
    }

    public string GameActionId { get; }
    public GoalObjective Objective { get; }
    public TargetRef? Target { get; }
    public ResultPredicate DesiredResult { get; }
    public GameActionSpec Action { get; }
}

/// <summary>Deterministic plan-to-action expansion over one scenario's frozen candidate catalogue.</summary>
public sealed class FormalPlanningActionCatalogue
{
    private readonly IReadOnlyList<FormalPlanningActionCandidate> _candidates;

    public FormalPlanningActionCatalogue(
        ActorId actorId,
        IEnumerable<FormalPlanningActionCandidate> candidates)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(candidates);
        FormalPlanningActionCandidate[] snapshot = candidates.ToArray();
        if (snapshot.Length == 0
            || snapshot.Any(value => value is null || value.Action.ActorId != actorId)
            || snapshot.Select(value => value.GameActionId).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new ArgumentException("Formal planning action candidates must be non-empty, unique, and actor-owned.", nameof(candidates));
        for (int index = 0; index < snapshot.Length; index++)
        {
            for (int previous = 0; previous < index; previous++)
            {
                if (Matches(snapshot[index], snapshot[previous]))
                    throw new ArgumentException("Formal action candidates cannot duplicate PlanStep semantics.", nameof(candidates));
            }
        }
        ActorId = actorId;
        _candidates = Array.AsReadOnly(snapshot);
    }

    public ActorId ActorId { get; }
    public IReadOnlyList<FormalPlanningActionCandidate> Candidates => _candidates;

    internal FormalPlanningActionCandidate? Resolve(
        RemotePlannerHostSettlementOutcome outcome,
        NpcPlan currentPlan)
    {
        ArgumentNullException.ThrowIfNull(currentPlan);
        NpcPlan? acceptedPlan = outcome switch
        {
            RemotePlannerHostCreatePlanAccepted create => create.Plan,
            RemotePlannerHostRevisePlanAccepted revise => revise.Plan,
            RemotePlannerHostPlanlessCreatePlanAccepted create => create.Plan,
            RemotePlannerHostVerifyAccepted when currentPlan.ActorId == ActorId => currentPlan,
            _ => null
        };
        if (acceptedPlan is null || acceptedPlan.ActorId != ActorId || acceptedPlan.Steps.Count == 0)
            return null;
        PlanStep first = acceptedPlan.Steps[0];
        return Candidates.SingleOrDefault(value => Matches(value, first));
    }

    internal FormalPlanningActionCandidate? ResolvePlanless(
        RemotePlannerHostSettlementOutcome outcome)
    {
        NpcPlan? acceptedPlan = outcome is RemotePlannerHostPlanlessCreatePlanAccepted create
            ? create.Plan
            : null;
        if (acceptedPlan is null || acceptedPlan.ActorId != ActorId || acceptedPlan.Steps.Count == 0)
            return null;
        return Candidates.SingleOrDefault(value => Matches(value, acceptedPlan.Steps[0]));
    }

    private static bool Matches(FormalPlanningActionCandidate left, FormalPlanningActionCandidate right) =>
        left.Objective == right.Objective
        && left.Target == right.Target
        && left.DesiredResult == right.DesiredResult;

    private static bool Matches(FormalPlanningActionCandidate candidate, PlanStep step) =>
        candidate.Objective == step.Objective
        && candidate.Target == step.Target
        && candidate.DesiredResult == step.DesiredResult;
}

/// <summary>
/// Concrete planless settlement through RemotePlannerHostSettlement. A justified defer is a valid
/// terminal; an accepted plan becomes a world commit only when its frozen typed action passes the
/// shared execution pipeline and Authority.
/// </summary>
public sealed class FormalPlanlessStrategicTerminalizer : IFormalRemotePlannerTerminalizer
{
    private readonly DecisionNeedStore _store;
    private readonly DecisionNeed _need;
    private readonly ActorDecisionView _view;
    private readonly L2PlanlessStrategicContext _context;
    private readonly RemotePlannerRequest _request;
    private readonly NpcPlanningState _planning;
    private readonly SimTime _resolvedAt;
    private readonly FormalPlanningActionCatalogue? _actionCatalogue;
    private readonly IActorExecutionExecutor? _executor;

    public FormalPlanlessStrategicTerminalizer(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorDecisionView view,
        L2PlanlessStrategicContext context,
        RemotePlannerRequest request,
        NpcPlanningState planning,
        SimTime resolvedAt) : this(
            store,
            need,
            view,
            context,
            request,
            planning,
            resolvedAt,
            null,
            null,
            false)
    {
    }

    public FormalPlanlessStrategicTerminalizer(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorDecisionView view,
        L2PlanlessStrategicContext context,
        RemotePlannerRequest request,
        NpcPlanningState planning,
        SimTime resolvedAt,
        FormalPlanningActionCatalogue actionCatalogue,
        IActorExecutionExecutor executor) : this(
            store,
            need,
            view,
            context,
            request,
            planning,
            resolvedAt,
            actionCatalogue ?? throw new ArgumentNullException(nameof(actionCatalogue)),
            executor ?? throw new ArgumentNullException(nameof(executor)),
            true)
    {
    }

    private FormalPlanlessStrategicTerminalizer(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorDecisionView view,
        L2PlanlessStrategicContext context,
        RemotePlannerRequest request,
        NpcPlanningState planning,
        SimTime resolvedAt,
        FormalPlanningActionCatalogue? actionCatalogue,
        IActorExecutionExecutor? executor,
        bool hasActionExecution)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(need);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(planning);
        if (request.Binding.Kind != RemotePlannerRequestKind.PlanlessStrategic
            || request.Binding.ActorId != need.NpcId
            || request.Binding.NeedId != need.NeedId
            || context.ActorId != need.NpcId
            || context.NeedId != need.NeedId)
            throw new ArgumentException("Formal planless terminalizer inputs are cross-wired.", nameof(request));
        if (hasActionExecution != (actionCatalogue is not null && executor is not null)
            || (actionCatalogue is null) != (executor is null)
            || actionCatalogue is not null
                && (actionCatalogue.ActorId != need.NpcId || executor!.ActorId != need.NpcId))
            throw new ArgumentException("Formal planless action execution must belong to the request actor.", nameof(actionCatalogue));
        _store = store;
        _need = need;
        _view = view;
        _context = context;
        _request = request;
        _planning = planning;
        _resolvedAt = resolvedAt;
        _actionCatalogue = actionCatalogue;
        _executor = executor;
    }

    public FormalLiveTerminalSettlement Settle(
        RemotePlannerResponse response,
        FormalModelCallEvidence modelCall)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(modelCall);
        if (!StringComparer.Ordinal.Equals(modelCall.CallId, _request.Binding.RequestId.Value))
            throw new ArgumentException("Formal model call does not match its Host request.", nameof(modelCall));
        RemotePlannerHostSettlementOutcome outcome = RemotePlannerHostSettlement.SettlePlanlessStrategic(
            _store,
            _need,
            _view,
            _context,
            _request,
            response,
            _planning,
            _resolvedAt);
        FormalPlanningActionCandidate? candidate = _actionCatalogue?.ResolvePlanless(outcome);
        if (candidate is not null)
            return SettleAction(outcome, candidate, modelCall);
        return Issue(_need, _request, _resolvedAt, outcome, modelCall);
    }

    private FormalLiveTerminalSettlement SettleAction(
        RemotePlannerHostSettlementOutcome outcome,
        FormalPlanningActionCandidate candidate,
        FormalModelCallEvidence modelCall)
    {
        var execution = new ActorExecutionRequest(
            new ActorExecutionId("formal-action:" + _need.NeedId.Value + ":" + _need.AttemptCount),
            _need.NpcId,
            ActorExecutionMode.Interact,
            new InteractExecutionPayload(_need.NpcId, candidate.Action),
            _resolvedAt,
            AutonomousNpcCognitionRoute.L2);
        ActorExecutionReceipt executionReceipt = ActorExecutionPipeline.Dispatch(execution, _executor!);
        byte[] sourceReceipt = SerializeActionReceipt(outcome, candidate, executionReceipt);
        FormalTerminalOutcomeReceipt receipt = executionReceipt is
            { Outcome: ActorExecutionOutcome.Completed, Result: AuthorityCommitExecutionResult }
            ? FormalTerminalOutcomeReceipt.FromAuthorityCommit(
                _need.NpcId.Value,
                _need.NeedId.Value,
                modelCall.CallId,
                candidate.GameActionId,
                sourceReceipt)
            : FormalTerminalOutcomeReceipt.FromValidatorRejection(
                _need.NpcId.Value,
                _need.NeedId.Value,
                modelCall.CallId,
                sourceReceipt);
        return new FormalLiveTerminalSettlement(receipt);
    }

    private byte[] SerializeActionReceipt(
        RemotePlannerHostSettlementOutcome outcome,
        FormalPlanningActionCandidate candidate,
        ActorExecutionReceipt receipt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-planless-authority-receipt.v1");
            writer.WriteString("actor_id", _need.NpcId.Value);
            writer.WriteString("need_id", _need.NeedId.Value);
            writer.WriteString("settlement_kind", outcome.GetType().Name);
            writer.WriteString("game_action_id", candidate.GameActionId);
            writer.WriteString("target_ref", candidate.Action.Binding.ContractRef.TargetRef.Value);
            writer.WriteString("contract_id", candidate.Action.Binding.ContractRef.ContractId);
            writer.WriteNumber("expected_contract_version", candidate.Action.Binding.ExpectedVersion.Value);
            writer.WriteString("capability", candidate.Action.Binding.Capability.Value);
            writer.WriteString("instrument_ref", candidate.Action.Binding.InstrumentRef?.Value);
            writer.WriteString("execution_id", receipt.ExecutionId.Value);
            writer.WriteString("execution_outcome", receipt.Outcome.ToString());
            writer.WriteString("execution_failure", receipt.Failure?.ToString());
            writer.WriteString("execution_evidence", receipt.Evidence);
            writer.WriteString(
                "authority_action_family",
                (receipt.Result as AuthorityCommitExecutionResult)?.ActionFamily);
            writer.WriteNumber("resolved_at_ticks", _resolvedAt.Ticks);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    internal static FormalLiveTerminalSettlement Issue(
        DecisionNeed need,
        RemotePlannerRequest request,
        SimTime resolvedAt,
        RemotePlannerHostSettlementOutcome outcome,
        FormalModelCallEvidence modelCall)
    {
        ArgumentNullException.ThrowIfNull(need);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(modelCall);
        byte[] sourceReceipt = SerializeHostReceipt(need, request, resolvedAt, outcome);
        FormalTerminalOutcomeReceipt receipt = outcome is RemotePlannerHostNoArtifactAccepted
            { ResolutionKind: DecisionNeedResolutionKind.Defer }
            ? FormalTerminalOutcomeReceipt.FromValidatedDefer(
                need.NpcId.Value,
                need.NeedId.Value,
                modelCall.CallId,
                sourceReceipt)
            : FormalTerminalOutcomeReceipt.FromValidatorRejection(
                need.NpcId.Value,
                need.NeedId.Value,
                modelCall.CallId,
                sourceReceipt);
        return new FormalLiveTerminalSettlement(receipt);
    }

    internal static FormalLiveTerminalSettlement IssueWithAction(
        DecisionNeed need,
        RemotePlannerRequest request,
        SimTime resolvedAt,
        RemotePlannerHostSettlementOutcome outcome,
        FormalModelCallEvidence modelCall,
        FormalPlanningActionCatalogue actionCatalogue,
        IActorExecutionExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(actionCatalogue);
        ArgumentNullException.ThrowIfNull(executor);
        if (actionCatalogue.ActorId != need.NpcId || executor.ActorId != need.NpcId)
            throw new ArgumentException("Formal planless action execution is cross-wired.", nameof(actionCatalogue));
        FormalPlanningActionCandidate? candidate = actionCatalogue.ResolvePlanless(outcome);
        if (candidate is null)
            return Issue(need, request, resolvedAt, outcome, modelCall);
        var execution = new ActorExecutionRequest(
            new ActorExecutionId("formal-action:" + need.NeedId.Value + ":" + need.AttemptCount),
            need.NpcId,
            ActorExecutionMode.Interact,
            new InteractExecutionPayload(need.NpcId, candidate.Action),
            resolvedAt,
            AutonomousNpcCognitionRoute.L2);
        ActorExecutionReceipt executionReceipt = ActorExecutionPipeline.Dispatch(execution, executor);
        byte[] sourceReceipt = SerializeStaticActionReceipt(
            need,
            resolvedAt,
            outcome,
            candidate,
            executionReceipt);
        FormalTerminalOutcomeReceipt receipt = executionReceipt is
            { Outcome: ActorExecutionOutcome.Completed, Result: AuthorityCommitExecutionResult }
            ? FormalTerminalOutcomeReceipt.FromAuthorityCommit(
                need.NpcId.Value,
                need.NeedId.Value,
                modelCall.CallId,
                candidate.GameActionId,
                sourceReceipt)
            : FormalTerminalOutcomeReceipt.FromValidatorRejection(
                need.NpcId.Value,
                need.NeedId.Value,
                modelCall.CallId,
                sourceReceipt);
        return new FormalLiveTerminalSettlement(receipt);
    }

    private static byte[] SerializeStaticActionReceipt(
        DecisionNeed need,
        SimTime resolvedAt,
        RemotePlannerHostSettlementOutcome outcome,
        FormalPlanningActionCandidate candidate,
        ActorExecutionReceipt receipt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-planless-authority-receipt.v1");
            writer.WriteString("actor_id", need.NpcId.Value);
            writer.WriteString("need_id", need.NeedId.Value);
            writer.WriteString("settlement_kind", outcome.GetType().Name);
            writer.WriteString("game_action_id", candidate.GameActionId);
            writer.WriteString("target_ref", candidate.Action.Binding.ContractRef.TargetRef.Value);
            writer.WriteString("contract_id", candidate.Action.Binding.ContractRef.ContractId);
            writer.WriteNumber("expected_contract_version", candidate.Action.Binding.ExpectedVersion.Value);
            writer.WriteString("capability", candidate.Action.Binding.Capability.Value);
            writer.WriteString("instrument_ref", candidate.Action.Binding.InstrumentRef?.Value);
            writer.WriteString("execution_id", receipt.ExecutionId.Value);
            writer.WriteString("execution_outcome", receipt.Outcome.ToString());
            writer.WriteString("execution_failure", receipt.Failure?.ToString());
            writer.WriteString("execution_evidence", receipt.Evidence);
            writer.WriteString(
                "authority_action_family",
                (receipt.Result as AuthorityCommitExecutionResult)?.ActionFamily);
            writer.WriteNumber("resolved_at_ticks", resolvedAt.Ticks);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static byte[] SerializeHostReceipt(
        DecisionNeed need,
        RemotePlannerRequest request,
        SimTime resolvedAt,
        RemotePlannerHostSettlementOutcome outcome)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-planless-host-receipt.v1");
            writer.WriteString("actor_id", need.NpcId.Value);
            writer.WriteString("need_id", need.NeedId.Value);
            writer.WriteString("settlement_kind", outcome.GetType().Name);
            writer.WriteString("host_rejection_reason", (outcome as RemotePlannerHostRejected)?.Reason.ToString());
            writer.WriteString("resolution_kind", need.ResolutionKind?.ToString());
            writer.WriteNumber("resolved_at_ticks", resolvedAt.Ticks);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}

/// <summary>Concrete current-plan settlement through the typed Remote Planner Host validator.</summary>
public sealed class FormalPlanningTerminalizer : IFormalRemotePlannerTerminalizer
{
    private readonly DecisionNeedStore _store;
    private readonly DecisionNeed _need;
    private readonly ActorCognitionView _view;
    private readonly L2PlanningContext _context;
    private readonly RemotePlannerRequest _request;
    private readonly NpcPlan _currentPlan;
    private readonly SimTime _resolvedAt;
    private readonly FormalPlanningActionCatalogue? _actionCatalogue;
    private readonly IActorExecutionExecutor? _executor;

    public FormalPlanningTerminalizer(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorCognitionView view,
        L2PlanningContext context,
        RemotePlannerRequest request,
        NpcPlan currentPlan,
        SimTime resolvedAt) : this(
            store,
            need,
            view,
            context,
            request,
            currentPlan,
            resolvedAt,
            null,
            null,
            false)
    {
    }

    public FormalPlanningTerminalizer(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorCognitionView view,
        L2PlanningContext context,
        RemotePlannerRequest request,
        NpcPlan currentPlan,
        SimTime resolvedAt,
        FormalPlanningActionCatalogue actionCatalogue,
        IActorExecutionExecutor executor) : this(
            store,
            need,
            view,
            context,
            request,
            currentPlan,
            resolvedAt,
            actionCatalogue ?? throw new ArgumentNullException(nameof(actionCatalogue)),
            executor ?? throw new ArgumentNullException(nameof(executor)),
            true)
    {
    }

    private FormalPlanningTerminalizer(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorCognitionView view,
        L2PlanningContext context,
        RemotePlannerRequest request,
        NpcPlan currentPlan,
        SimTime resolvedAt,
        FormalPlanningActionCatalogue? actionCatalogue,
        IActorExecutionExecutor? executor,
        bool hasActionExecution)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(need);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(currentPlan);
        if (request.Binding.Kind != RemotePlannerRequestKind.Planning
            || request.Binding.ActorId != need.NpcId
            || request.Binding.NeedId != need.NeedId
            || context.ActorId != need.NpcId
            || context.NeedId != need.NeedId)
            throw new ArgumentException("Formal planning terminalizer inputs are cross-wired.", nameof(request));
        if (hasActionExecution != (actionCatalogue is not null && executor is not null)
            || (actionCatalogue is null) != (executor is null)
            || actionCatalogue is not null
                && (actionCatalogue.ActorId != need.NpcId || executor!.ActorId != need.NpcId))
            throw new ArgumentException("Formal planning action execution must belong to the request actor.", nameof(actionCatalogue));
        _store = store;
        _need = need;
        _view = view;
        _context = context;
        _request = request;
        _currentPlan = currentPlan;
        _resolvedAt = resolvedAt;
        _actionCatalogue = actionCatalogue;
        _executor = executor;
    }

    public FormalLiveTerminalSettlement Settle(
        RemotePlannerResponse response,
        FormalModelCallEvidence modelCall)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(modelCall);
        if (!StringComparer.Ordinal.Equals(modelCall.CallId, _request.Binding.RequestId.Value))
            throw new ArgumentException("Formal model call does not match its planning Host request.", nameof(modelCall));
        RemotePlannerHostSettlementOutcome outcome = RemotePlannerHostSettlement.Settle(
            _store,
            _need,
            _view,
            _context,
            _request,
            response,
            _currentPlan,
            _resolvedAt);
        FormalPlanningActionCandidate? candidate = _actionCatalogue?.Resolve(outcome, _currentPlan);
        if (candidate is not null)
            return SettleAction(outcome, candidate, modelCall);
        byte[] sourceReceipt = SerializeHostReceipt(outcome);
        FormalTerminalOutcomeReceipt receipt = outcome is RemotePlannerHostNoArtifactAccepted
            { ResolutionKind: DecisionNeedResolutionKind.Defer }
            ? FormalTerminalOutcomeReceipt.FromValidatedDefer(
                _need.NpcId.Value,
                _need.NeedId.Value,
                modelCall.CallId,
                sourceReceipt)
            : FormalTerminalOutcomeReceipt.FromValidatorRejection(
                _need.NpcId.Value,
                _need.NeedId.Value,
                modelCall.CallId,
                sourceReceipt);
        return new FormalLiveTerminalSettlement(receipt);
    }

    private FormalLiveTerminalSettlement SettleAction(
        RemotePlannerHostSettlementOutcome outcome,
        FormalPlanningActionCandidate candidate,
        FormalModelCallEvidence modelCall)
    {
        var execution = new ActorExecutionRequest(
            new ActorExecutionId("formal-action:" + _need.NeedId.Value + ":" + _need.AttemptCount),
            _need.NpcId,
            ActorExecutionMode.Interact,
            new InteractExecutionPayload(_need.NpcId, candidate.Action),
            _resolvedAt,
            AutonomousNpcCognitionRoute.L2);
        ActorExecutionReceipt executionReceipt = ActorExecutionPipeline.Dispatch(execution, _executor!);
        byte[] sourceReceipt = SerializeActionReceipt(outcome, candidate, executionReceipt);
        FormalTerminalOutcomeReceipt receipt = executionReceipt is
            { Outcome: ActorExecutionOutcome.Completed, Result: AuthorityCommitExecutionResult }
            ? FormalTerminalOutcomeReceipt.FromAuthorityCommit(
                _need.NpcId.Value,
                _need.NeedId.Value,
                modelCall.CallId,
                candidate.GameActionId,
                sourceReceipt)
            : FormalTerminalOutcomeReceipt.FromValidatorRejection(
                _need.NpcId.Value,
                _need.NeedId.Value,
                modelCall.CallId,
                sourceReceipt);
        return new FormalLiveTerminalSettlement(receipt);
    }

    private byte[] SerializeHostReceipt(RemotePlannerHostSettlementOutcome outcome)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-planning-host-receipt.v1");
            writer.WriteString("actor_id", _need.NpcId.Value);
            writer.WriteString("need_id", _need.NeedId.Value);
            writer.WriteString("settlement_kind", outcome.GetType().Name);
            writer.WriteString("host_rejection_reason", (outcome as RemotePlannerHostRejected)?.Reason.ToString());
            writer.WriteString("resolution_kind", _need.ResolutionKind?.ToString());
            writer.WriteNumber("resolved_at_ticks", _resolvedAt.Ticks);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private byte[] SerializeActionReceipt(
        RemotePlannerHostSettlementOutcome outcome,
        FormalPlanningActionCandidate candidate,
        ActorExecutionReceipt receipt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-planning-authority-receipt.v1");
            writer.WriteString("actor_id", _need.NpcId.Value);
            writer.WriteString("need_id", _need.NeedId.Value);
            writer.WriteString("settlement_kind", outcome.GetType().Name);
            writer.WriteString("game_action_id", candidate.GameActionId);
            writer.WriteString("target_ref", candidate.Action.Binding.ContractRef.TargetRef.Value);
            writer.WriteString("contract_id", candidate.Action.Binding.ContractRef.ContractId);
            writer.WriteNumber("expected_contract_version", candidate.Action.Binding.ExpectedVersion.Value);
            writer.WriteString("capability", candidate.Action.Binding.Capability.Value);
            writer.WriteString("instrument_ref", candidate.Action.Binding.InstrumentRef?.Value);
            writer.WriteString("arguments_kind", candidate.Action.Arguments.GetType().Name);
            writer.WriteBase64String(
                "arguments",
                JsonSerializer.SerializeToUtf8Bytes(candidate.Action.Arguments, candidate.Action.Arguments.GetType()));
            writer.WriteString("execution_id", receipt.ExecutionId.Value);
            writer.WriteString("execution_outcome", receipt.Outcome.ToString());
            writer.WriteString("execution_failure", receipt.Failure?.ToString());
            writer.WriteString("execution_evidence", receipt.Evidence);
            writer.WriteString(
                "authority_action_family",
                (receipt.Result as AuthorityCommitExecutionResult)?.ActionFamily);
            writer.WriteNumber("resolved_at_ticks", _resolvedAt.Ticks);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}

/// <summary>
/// Concrete mandatory-Invite settlement. Accepted responses pass through the existing semantic Host and,
/// for Accept, the real InvitationAcceptance Authority before an AuthorityCommit receipt is issued.
/// </summary>
public sealed class FormalInviteResponseTerminalizer : IFormalRemotePlannerTerminalizer
{
    private readonly DecisionNeedStore _store;
    private readonly DecisionNeed _need;
    private readonly ActorDecisionView _view;
    private readonly L2InviteResponseContext _context;
    private readonly RoutineSemanticResponseContext _responseContext;
    private readonly RemotePlannerRequest _request;
    private readonly InvitationAcceptanceAuthorityRuntime _authority;
    private readonly SimTime _resolvedAt;

    public FormalInviteResponseTerminalizer(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorDecisionView view,
        L2InviteResponseContext context,
        RoutineSemanticResponseContext responseContext,
        RemotePlannerRequest request,
        InvitationAcceptanceAuthorityRuntime authority,
        SimTime resolvedAt)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(need);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(responseContext);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authority);
        if (request.Binding.Kind != RemotePlannerRequestKind.InviteResponse
            || request.Binding.ActorId != need.NpcId
            || request.Binding.NeedId != need.NeedId
            || context.ActorId != need.NpcId
            || context.NeedId != need.NeedId)
            throw new ArgumentException("Formal Invite terminalizer inputs are cross-wired.", nameof(request));
        _store = store;
        _need = need;
        _view = view;
        _context = context;
        _responseContext = responseContext;
        _request = request;
        _authority = authority;
        _resolvedAt = resolvedAt;
    }

    public FormalLiveTerminalSettlement Settle(
        RemotePlannerResponse response,
        FormalModelCallEvidence modelCall)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(modelCall);
        if (!StringComparer.Ordinal.Equals(modelCall.CallId, _request.Binding.RequestId.Value))
            throw new ArgumentException("Formal model call does not match its Invite Host request.", nameof(modelCall));
        RemotePlannerHostSettlementOutcome outcome = RemotePlannerHostSettlement.SettleInviteResponse(
            _store,
            _need,
            _view,
            _context,
            _responseContext,
            _request,
            response,
            _authority,
            _resolvedAt);
        return Issue(outcome, modelCall);
    }

    internal FormalLiveTerminalSettlement Issue(
        RemotePlannerHostSettlementOutcome outcome,
        FormalModelCallEvidence modelCall)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(modelCall);
        if (!StringComparer.Ordinal.Equals(modelCall.CallId, _request.Binding.RequestId.Value))
            throw new ArgumentException("Formal model call does not match its Invite Host result.", nameof(modelCall));
        byte[] sourceReceipt = SerializeAuthorityReceipt(outcome);
        FormalTerminalOutcomeReceipt receipt;
        if (outcome is RemotePlannerHostInviteResponseAccepted accepted)
        {
            if (accepted.RoutingResult.RecordedTurn is not SemanticDialogueTurn recordedTurn)
                throw new InvalidDataException("Accepted semantic response lacks its exact recorded turn.");
            if (accepted.RoutingResult.Outcome == ConversationSemanticResponseOutcome.InvitationAccepted)
            {
                Commitment commitment = accepted.RoutingResult.Commitment
                    ?? throw new InvalidDataException("Accepted Invite lacks its Authority Commitment.");
                if (!_authority.Commitments.Any(value => ReferenceEquals(value, commitment)))
                    throw new InvalidDataException("Formal Invite receipt is not owned by the exact Authority runtime.");
            }
            if (!ReferenceEquals(recordedTurn.Act, accepted.Act))
                throw new InvalidDataException("Formal semantic receipt is cross-wired from its recorded act.");
            receipt = FormalTerminalOutcomeReceipt.FromAuthorityCommit(
                _need.NpcId.Value,
                _need.NeedId.Value,
                modelCall.CallId,
                "semantic-act:" + accepted.Act.ActId.Value,
                sourceReceipt);
        }
        else
        {
            receipt = FormalTerminalOutcomeReceipt.FromValidatorRejection(
                _need.NpcId.Value,
                _need.NeedId.Value,
                modelCall.CallId,
                sourceReceipt);
        }
        return new FormalLiveTerminalSettlement(receipt);
    }

    private byte[] SerializeAuthorityReceipt(RemotePlannerHostSettlementOutcome outcome)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-invite-response-authority-receipt.v1");
            writer.WriteString("actor_id", _need.NpcId.Value);
            writer.WriteString("need_id", _need.NeedId.Value);
            writer.WriteString("request_id", _request.Binding.RequestId.Value);
            writer.WriteString("settlement_kind", outcome.GetType().Name);
            if (outcome is RemotePlannerHostInviteResponseAccepted accepted)
            {
                writer.WriteString("semantic_act_id", accepted.Act.ActId.Value);
                writer.WriteString("semantic_act_kind", accepted.Act.Kind.ToString());
                writer.WriteString("routing_outcome", accepted.RoutingResult.Outcome.ToString());
                writer.WriteNumber("recorded_turn_sequence", accepted.RoutingResult.RecordedTurn!.Sequence);
                Commitment? commitment = accepted.RoutingResult.Commitment;
                writer.WriteString("commitment_id", commitment?.CommitmentId.Value);
                writer.WriteString("commitment_status", commitment?.Status.ToString());
                writer.WriteString("commitment_debtor", commitment?.Debtor.Value);
                writer.WriteString("commitment_creditor", commitment?.Creditor.Value);
            }
            else
            {
                writer.WriteString(
                    "host_rejection_reason",
                    (outcome as RemotePlannerHostRejected)?.Reason.ToString());
            }
            writer.WriteNumber("resolved_at_ticks", _resolvedAt.Ticks);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}

internal static class FormalTransportFailureTerminalizer
{
    public static FormalLiveTerminalSettlement Issue(
        RemotePlannerRequest request,
        ILiveRemotePlannerExecutionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Outcome != LiveRemoteTransportOutcome.InvocationFailed
            || evidence.ResponseEnvelopeReceived
            || evidence.FailureKind is not LiveRemoteFailureKind failureKind)
            throw new ArgumentException("Formal transport failure evidence is inconsistent.", nameof(evidence));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-live-transport-failure-receipt.v2");
            writer.WriteString("request_id", request.Binding.RequestId.Value);
            writer.WriteString("actor_id", request.Binding.ActorId.Value);
            writer.WriteString("need_id", request.Binding.NeedId.Value);
            writer.WriteString("request_protocol_version", request.ProtocolVersion);
            writer.WriteString("failure_kind", failureKind.ToString());
            if (evidence.HttpStatus is int status) writer.WriteNumber("http_status", status);
            else writer.WriteNull("http_status");
            writer.WriteNumber("duration_milliseconds", evidence.DurationMilliseconds);
            writer.WriteString("response_body_hash", evidence.ResponseBodyHash);
            writer.WriteBoolean("response_envelope_received", false);
            writer.WriteEndObject();
        }
        return new FormalLiveTerminalSettlement(FormalTerminalOutcomeReceipt.FromTransportFailure(
            request.Binding.ActorId.Value,
            request.Binding.NeedId.Value,
            request.Binding.RequestId.Value,
            stream.ToArray()));
    }
}

public sealed class FormalRq1PlanlessSettlementContext
{
    public FormalRq1PlanlessSettlementContext(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorDecisionView view,
        L2PlanlessStrategicContext context,
        NpcPlanningState planning,
        SimTime resolvedAt)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(need);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(planning);
        if (context.ActorId != need.NpcId || context.NeedId != need.NeedId)
            throw new ArgumentException("Formal planless settlement context is cross-wired.", nameof(context));
        Store = store;
        Need = need;
        View = view;
        Context = context;
        Planning = planning;
        ResolvedAt = resolvedAt;
    }

    public DecisionNeedStore Store { get; }
    public DecisionNeed Need { get; }
    public ActorDecisionView View { get; }
    public L2PlanlessStrategicContext Context { get; }
    public NpcPlanningState Planning { get; }
    public SimTime ResolvedAt { get; }
}

public interface IFormalRq1ScheduledInvocationOwner : IFormalRq1InvocationStarter
{
    DecisionNeed Need { get; }
    ValueTask<RemotePlannerInvocationResult> PollAndSettleAsync(
        RemotePlannerInvocationSession invocation);
    FormalLiveTerminalSettlement IssueTerminal(
        RemotePlannerInvocationResult result,
        FormalModelCallEvidence modelCall);
}

public sealed class FormalPlanlessRq1InvocationStarter : IFormalRq1ScheduledInvocationOwner
{
    private readonly DecisionNeedStore _store;
    private readonly DecisionNeed _need;
    private readonly ActorDecisionView _view;
    private readonly NpcPlanningState _planning;
    private readonly MemoryPacket _memoryPacket;
    private readonly RemotePlannerRequestId _requestId;
    private readonly string _contextBuilderVersion;
    private readonly SimTime _resolvedAt;
    private readonly FormalPlanningActionCatalogue? _actionCatalogue;
    private readonly IActorExecutionExecutor? _executor;
    private FormalRq1PlanlessSettlementContext? _settlementContext;
    private RemotePlannerRequest? _preparedRequest;

    public FormalPlanlessRq1InvocationStarter(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorDecisionView view,
        NpcPlanningState planning,
        MemoryPacket memoryPacket,
        RemotePlannerRequestId requestId,
        string contextBuilderVersion,
        SimTime resolvedAt) : this(
            store,
            need,
            view,
            planning,
            memoryPacket,
            requestId,
            contextBuilderVersion,
            resolvedAt,
            null,
            null,
            false)
    {
    }

    public FormalPlanlessRq1InvocationStarter(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorDecisionView view,
        NpcPlanningState planning,
        MemoryPacket memoryPacket,
        RemotePlannerRequestId requestId,
        string contextBuilderVersion,
        SimTime resolvedAt,
        FormalPlanningActionCatalogue actionCatalogue,
        IActorExecutionExecutor executor) : this(
            store,
            need,
            view,
            planning,
            memoryPacket,
            requestId,
            contextBuilderVersion,
            resolvedAt,
            actionCatalogue ?? throw new ArgumentNullException(nameof(actionCatalogue)),
            executor ?? throw new ArgumentNullException(nameof(executor)),
            true)
    {
    }

    private FormalPlanlessRq1InvocationStarter(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorDecisionView view,
        NpcPlanningState planning,
        MemoryPacket memoryPacket,
        RemotePlannerRequestId requestId,
        string contextBuilderVersion,
        SimTime resolvedAt,
        FormalPlanningActionCatalogue? actionCatalogue,
        IActorExecutionExecutor? executor,
        bool hasActionExecution)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(need);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(planning);
        ArgumentNullException.ThrowIfNull(memoryPacket);
        ArgumentNullException.ThrowIfNull(requestId);
        FormalExperimentCanonical.RequireIdentity(contextBuilderVersion, nameof(contextBuilderVersion));
        if (hasActionExecution != (actionCatalogue is not null && executor is not null)
            || (actionCatalogue is null) != (executor is null)
            || actionCatalogue is not null
                && (actionCatalogue.ActorId != need.NpcId || executor!.ActorId != need.NpcId))
            throw new ArgumentException("Formal RQ1 planless action execution must belong to the Need actor.", nameof(actionCatalogue));
        _store = store;
        _need = need;
        _view = view;
        _planning = planning;
        _memoryPacket = memoryPacket;
        _requestId = requestId;
        _contextBuilderVersion = contextBuilderVersion;
        _resolvedAt = resolvedAt;
        _actionCatalogue = actionCatalogue;
        _executor = executor;
    }

    public DecisionNeedId NeedId => _need.NeedId;
    public DecisionNeed Need => _need;
    public FormalRq1PlanlessSettlementContext SettlementContext => _settlementContext
        ?? throw new InvalidOperationException("Formal planless invocation has not been prepared.");

    public FormalRq1InvocationPreparation Prepare(DecisionNeed need)
    {
        if (!ReferenceEquals(need, _need) || _settlementContext is not null)
            throw new InvalidOperationException("Formal planless starter may prepare its exact Need once.");
        L2PlanlessStrategicContext context = PlanlessStrategicDecisionAdmission.Admit(
            _store,
            _need,
            _view,
            _planning,
            _memoryPacket);
        RemotePlannerRequest request = RemotePlannerRequest.Create(_requestId, context);
        _preparedRequest = request;
        _settlementContext = new FormalRq1PlanlessSettlementContext(
            _store,
            _need,
            _view,
            context,
            _planning,
            _resolvedAt);
        return new FormalRq1InvocationPreparation(
            request,
            _contextBuilderVersion,
            CancellationToken.None);
    }

    public ValueTask<RemotePlannerInvocationResult> PollAndSettleAsync(
        RemotePlannerInvocationSession invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        FormalRq1PlanlessSettlementContext context = SettlementContext;
        return invocation.PollAndSettlePlanlessStrategicAsync(
            context.Store,
            context.Need,
            context.View,
            context.Context,
            context.Planning,
            context.ResolvedAt);
    }

    public FormalLiveTerminalSettlement IssueTerminal(
        RemotePlannerInvocationResult result,
        FormalModelCallEvidence modelCall)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(modelCall);
        if (result.Settlement is not RemotePlannerHostSettlementOutcome settlement)
            throw new ArgumentException("Formal planless invocation lacks its Host settlement.", nameof(result));
        RemotePlannerRequest request = _preparedRequest
            ?? throw new InvalidOperationException("Formal planless invocation has not been prepared.");
        return _actionCatalogue is null
            ? FormalPlanlessStrategicTerminalizer.Issue(
                _need,
                request,
                _resolvedAt,
                settlement,
                modelCall)
            : FormalPlanlessStrategicTerminalizer.IssueWithAction(
                _need,
                request,
                _resolvedAt,
                settlement,
                modelCall,
                _actionCatalogue,
                _executor!);
    }
}

public sealed class FormalInviteRq1InvocationStarter : IFormalRq1ScheduledInvocationOwner
{
    private readonly DecisionNeedStore _store;
    private readonly DecisionNeed _need;
    private readonly RoutineSemanticResponseContext _responseContext;
    private readonly ActorDecisionView _view;
    private readonly MemoryPacket _memoryPacket;
    private readonly RemotePlannerRequestId _requestId;
    private readonly string _contextBuilderVersion;
    private readonly InvitationAcceptanceAuthorityRuntime _authority;
    private readonly SimTime _resolvedAt;
    private L2InviteResponseContext? _context;
    private RemotePlannerRequest? _request;
    private FormalInviteResponseTerminalizer? _terminalizer;

    public FormalInviteRq1InvocationStarter(
        DecisionNeedStore store,
        DecisionNeed need,
        RoutineSemanticResponseContext responseContext,
        ActorDecisionView view,
        MemoryPacket memoryPacket,
        RemotePlannerRequestId requestId,
        string contextBuilderVersion,
        InvitationAcceptanceAuthorityRuntime authority,
        SimTime resolvedAt)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(need);
        ArgumentNullException.ThrowIfNull(responseContext);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(memoryPacket);
        ArgumentNullException.ThrowIfNull(requestId);
        FormalExperimentCanonical.RequireIdentity(contextBuilderVersion, nameof(contextBuilderVersion));
        ArgumentNullException.ThrowIfNull(authority);
        _store = store;
        _need = need;
        _responseContext = responseContext;
        _view = view;
        _memoryPacket = memoryPacket;
        _requestId = requestId;
        _contextBuilderVersion = contextBuilderVersion;
        _authority = authority;
        _resolvedAt = resolvedAt;
    }

    public DecisionNeedId NeedId => _need.NeedId;
    public DecisionNeed Need => _need;

    public FormalRq1InvocationPreparation Prepare(DecisionNeed need)
    {
        if (!ReferenceEquals(need, _need) || _context is not null)
            throw new InvalidOperationException("Formal Invite starter may prepare its exact Need once.");
        _context = InviteResponseDecisionAdmission.Admit(
            _store,
            _need,
            _responseContext,
            _view,
            _memoryPacket);
        _request = RemotePlannerRequest.Create(_requestId, _context);
        _terminalizer = new FormalInviteResponseTerminalizer(
            _store,
            _need,
            _view,
            _context,
            _responseContext,
            _request,
            _authority,
            _resolvedAt);
        return new FormalRq1InvocationPreparation(
            _request,
            _contextBuilderVersion,
            CancellationToken.None);
    }

    public ValueTask<RemotePlannerInvocationResult> PollAndSettleAsync(
        RemotePlannerInvocationSession invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        L2InviteResponseContext context = _context
            ?? throw new InvalidOperationException("Formal Invite invocation has not been prepared.");
        return invocation.PollAndSettleInviteResponseAsync(
            _store,
            _need,
            _view,
            context,
            _responseContext,
            _authority,
            _resolvedAt);
    }

    public FormalLiveTerminalSettlement IssueTerminal(
        RemotePlannerInvocationResult result,
        FormalModelCallEvidence modelCall)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(modelCall);
        return (_terminalizer
                ?? throw new InvalidOperationException("Formal Invite invocation has not been prepared."))
            .Issue(result.Settlement
                ?? throw new ArgumentException("Formal Invite invocation lacks its Host settlement.", nameof(result)),
                modelCall);
    }
}

public sealed class FormalRq1ScheduledOpportunityTrial
{
    public FormalRq1ScheduledOpportunityTrial(
        Rq1OpportunityId opportunityId,
        SimTime discoveredAt,
        SimTime needCreatedAt,
        SimTime admittedAt,
        SimTime attemptedAt,
        bool wasStarvationPromoted,
        Rq1SessionId sessionId,
        FormalTerminalReceiptId receiptId,
        Rq1LogicalSessionDispatch session,
        IFormalRq1ScheduledInvocationOwner starter)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(starter);
        _ = new Rq1ActivationStageEvidence(
            opportunityId,
            discoveredAt,
            needCreatedAt,
            admittedAt,
            attemptedAt,
            wasStarvationPromoted);
        if (session.Need.NeedId != starter.NeedId)
            throw new ArgumentException("Formal scheduled trial does not bind one exact Need.", nameof(session));
        OpportunityId = opportunityId;
        DiscoveredAt = discoveredAt;
        NeedCreatedAt = needCreatedAt;
        AdmittedAt = admittedAt;
        AttemptedAt = attemptedAt;
        WasStarvationPromoted = wasStarvationPromoted;
        SessionId = sessionId;
        ReceiptId = receiptId;
        Session = session;
        Starter = starter;
    }

    public Rq1OpportunityId OpportunityId { get; }
    public SimTime DiscoveredAt { get; }
    public SimTime NeedCreatedAt { get; }
    public SimTime AdmittedAt { get; }
    public SimTime AttemptedAt { get; }
    public bool WasStarvationPromoted { get; }
    public Rq1SessionId SessionId { get; }
    public FormalTerminalReceiptId ReceiptId { get; }
    public Rq1LogicalSessionDispatch Session { get; }
    public IFormalRq1ScheduledInvocationOwner Starter { get; }
}

/// <summary>
/// Formal RQ1 production adapter over the unified condition scheduler. It does not bypass admission,
/// Provider capacity, invocation ownership, one-shot policy, or Host settlement.
/// </summary>
public sealed class FormalRq1ConditionRuntimeExecutor : IFormalRq1ConditionExecutor
{
    private readonly FormalRq1ConditionRuntime _conditionRuntime;
    private readonly ReadOnlyCollection<FormalRq1OpportunityRunEvidence> _nonAttemptedEvidence;
    private readonly ReadOnlyCollection<FormalRq1ScheduledOpportunityTrial> _trials;
    private readonly DateTimeOffset _dispatchWallTime;

    public FormalRq1ConditionRuntimeExecutor(
        string runtimeInstanceId,
        string providerSessionId,
        FormalRq1ConditionRuntime conditionRuntime,
        DateTimeOffset dispatchWallTime,
        IEnumerable<FormalRq1OpportunityRunEvidence> nonAttemptedEvidence,
        IEnumerable<FormalRq1ScheduledOpportunityTrial> trials)
    {
        FormalExperimentCanonical.RequireIdentity(runtimeInstanceId, nameof(runtimeInstanceId));
        FormalExperimentCanonical.RequireIdentity(providerSessionId, nameof(providerSessionId));
        ArgumentNullException.ThrowIfNull(conditionRuntime);
        ArgumentNullException.ThrowIfNull(nonAttemptedEvidence);
        ArgumentNullException.ThrowIfNull(trials);
        FormalRq1OpportunityRunEvidence[] missed = nonAttemptedEvidence.ToArray();
        FormalRq1ScheduledOpportunityTrial[] scheduled = trials.ToArray();
        if (scheduled.Length == 0
            || missed.Any(value => value.TerminalKind is not null)
            || missed.Select(value => value.OpportunityId).Concat(scheduled.Select(value => value.OpportunityId))
                .Distinct().Count() != missed.Length + scheduled.Length)
            throw new ArgumentException("Formal scheduled RQ1 observations are invalid.", nameof(trials));
        RuntimeInstanceId = runtimeInstanceId;
        ProviderSessionId = providerSessionId;
        _conditionRuntime = conditionRuntime;
        _dispatchWallTime = dispatchWallTime;
        _nonAttemptedEvidence = Array.AsReadOnly(missed);
        _trials = Array.AsReadOnly(scheduled);
    }

    public string RuntimeInstanceId { get; }
    public string ProviderSessionId { get; }

    public async ValueTask<FormalRq1ConditionExecutionResult> ExecuteAsync(
        FormalRq1ConditionExecutionInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Treatment != _conditionRuntime.Treatment
            || !StringComparer.Ordinal.Equals(input.Manifest.ManifestHash, _conditionRuntime.Manifest.ManifestHash))
            throw new ArgumentException("Formal RQ1 condition runtime does not match its exact input manifest.", nameof(input));
        var opportunities = new List<FormalRq1OpportunityRunEvidence>(_nonAttemptedEvidence);
        var sessions = new List<Rq1SessionOutcome>();
        var calls = new List<FormalModelCallEvidence>();
        foreach (FormalRq1ScheduledOpportunityTrial trial in _trials)
        {
            IFormalRq1ScheduledInvocationOwner owner = trial.Starter;
            DateTimeOffset dispatchTime = _dispatchWallTime;
            while (true)
            {
                FormalRq1InvocationStartBatch start = _conditionRuntime.DispatchReadyAndStart(
                    dispatchTime,
                    _trials.Select(value => value.Starter));
                if (start.Receipts.Count != 1
                    || !ReferenceEquals(start.Receipts[0].Session, trial.Session)
                    || start.Receipts[0].Outcome != FormalRq1InvocationStartOutcome.Started)
                {
                    string receiptSummary = string.Join(
                        ",",
                        start.Receipts.Select(receipt =>
                            receipt.Session.Need.NeedId.Value
                            + ":"
                            + receipt.Outcome
                            + (receipt.FailureType is null ? string.Empty : ":" + receipt.FailureType)));
                    throw new InvalidDataException(
                        "Formal RQ1 scheduler did not serially start the exact authorized trial. Receipts="
                        + receiptSummary);
                }
                RemotePlannerInvocationSession invocation = trial.Session.Invocation
                    ?? throw new InvalidDataException("Formal RQ1 scheduled session lacks invocation ownership.");
                await invocation.WaitForTransportAsync(cancellationToken).ConfigureAwait(false);
                RemotePlannerInvocationResult result = await owner.PollAndSettleAsync(invocation).ConfigureAwait(false);
                if (result.State == RemotePlannerInvocationState.Settled
                    && result.ExecutionEvidence is ILiveRemotePlannerExecutionEvidence
                    {
                        Outcome: LiveRemoteTransportOutcome.InvocationFailed,
                        FailureKind: LiveRemoteFailureKind failureKind
                    } transportFailure)
                {
                    DateTimeOffset completedAt = DateTimeOffset.UtcNow;
                    trial.Session.CompleteAttemptWithoutResponse(
                        completedAt,
                        new FormalRq1TransportFailureCode(failureKind.ToString()));
                    if (trial.Session.State == Rq1LogicalSessionState.WaitingForRetry)
                    {
                        DateTimeOffset retryAt = trial.Session.RetryNotBefore
                            ?? throw new InvalidDataException("Formal retry omitted its retry deadline.");
                        TimeSpan delay = retryAt - DateTimeOffset.UtcNow;
                        if (delay > TimeSpan.Zero)
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        dispatchTime = retryAt;
                        continue;
                    }
                    if (trial.Session.State != Rq1LogicalSessionState.TransportOnlyAborted)
                        throw new InvalidDataException("Formal transport failure reached an invalid scheduler state.");
                    FormalTerminalOutcomeReceipt failureReceipt = FormalTransportFailureTerminalizer.Issue(
                        invocation.Request,
                        transportFailure).Receipt;
                    opportunities.Add(new FormalRq1OpportunityRunEvidence(
                        trial.OpportunityId,
                        trial.DiscoveredAt,
                        trial.NeedCreatedAt,
                        trial.AdmittedAt,
                        trial.AttemptedAt,
                        trial.WasStarvationPromoted,
                        owner.Need.NeedId,
                        trial.SessionId,
                        trial.ReceiptId,
                        FormalRq1TerminalOutcomeKind.TransportFailure,
                        null,
                        invocation.Request.Binding.RequestId.Value,
                        failureReceipt));
                    sessions.Add(new Rq1SessionOutcome(
                        trial.SessionId,
                        Rq1SessionProductivity.Unproductive,
                        0));
                    break;
                }
                if (result.State != RemotePlannerInvocationState.Settled
                    || result.Settlement is null
                    || result.ExecutionEvidence is not IFormalModelCallEvidenceCarrier
                        { FormalCallEvidence: FormalModelCallEvidence modelCall }
                    || !modelCall.IsFormalPairingComplete)
                    throw new InvalidDataException("Formal RQ1 scheduled invocation lacks live terminal evidence.");
                trial.Session.CompleteAttemptWithResponseEnvelope();
                FormalLiveTerminalSettlement terminal = owner.IssueTerminal(result, modelCall);
                trial.Session.CompleteResponseSettlement();
                FormalTerminalOutcomeReceipt receipt = terminal.Receipt;
                FormalRq1TerminalOutcomeKind terminalKind = MapRq1TerminalKind(receipt.Kind);
                calls.Add(modelCall);
                opportunities.Add(new FormalRq1OpportunityRunEvidence(
                    trial.OpportunityId,
                    trial.DiscoveredAt,
                    trial.NeedCreatedAt,
                    trial.AdmittedAt,
                    trial.AttemptedAt,
                    trial.WasStarvationPromoted,
                    owner.Need.NeedId,
                    trial.SessionId,
                    trial.ReceiptId,
                    terminalKind,
                    receipt.TerminalEvidenceHash,
                    modelCall.CallId,
                    receipt));
                sessions.Add(new Rq1SessionOutcome(
                    trial.SessionId,
                    terminalKind is FormalRq1TerminalOutcomeKind.AuthorityCommitted
                            or FormalRq1TerminalOutcomeKind.JustifiedDefer
                        ? Rq1SessionProductivity.Productive
                        : Rq1SessionProductivity.Unproductive,
                    checked((modelCall.InputTokens ?? 0) + (modelCall.OutputTokens ?? 0))));
                break;
            }
        }
        return new FormalRq1ConditionExecutionResult(
            _conditionRuntime.Treatment,
            opportunities,
            sessions,
            calls,
            CaptureRuntimeDiagnostics());
    }

    private FormalRq1RuntimeDiagnostics CaptureRuntimeDiagnostics()
    {
        FormalRq1DispatchRuntime dispatch = _conditionRuntime.DispatchRuntime;
        PressureWorldRuntime pressure = _conditionRuntime.PressureCompositionRuntime.PressureRuntime;
        return new FormalRq1RuntimeDiagnostics(
            dispatch.Configuration.LogicalSessionBudget,
            dispatch.ReservedSessionBudget,
            dispatch.ConsumedSessionBudget,
            dispatch.RemainingSessionBudget,
            _trials.Sum(value => value.Session.TransportAttemptCount),
            pressure.IndexLookupCount,
            pressure.EvaluationCount,
            pressure.StateChangeCount);
    }

    private static FormalRq1TerminalOutcomeKind MapRq1TerminalKind(FormalTerminalOutcomeReceiptKind kind) =>
        kind switch
        {
            FormalTerminalOutcomeReceiptKind.AuthorityCommit => FormalRq1TerminalOutcomeKind.AuthorityCommitted,
            FormalTerminalOutcomeReceiptKind.ValidatedDefer => FormalRq1TerminalOutcomeKind.JustifiedDefer,
            FormalTerminalOutcomeReceiptKind.ValidatorRejection => FormalRq1TerminalOutcomeKind.ValidatorRejected,
            FormalTerminalOutcomeReceiptKind.TransportFailure => FormalRq1TerminalOutcomeKind.TransportFailure,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
}

public sealed class FormalRq1ConditionExecutorPairFactory : IFormalRq1ConditionExecutorFactory
{
    private readonly Dictionary<FormalRq1Treatment, IFormalRq1ConditionExecutor> _executors;

    public FormalRq1ConditionExecutorPairFactory(
        IFormalRq1ConditionExecutor agentCentric,
        IFormalRq1ConditionExecutor eventCentric)
    {
        ArgumentNullException.ThrowIfNull(agentCentric);
        ArgumentNullException.ThrowIfNull(eventCentric);
        _executors = new Dictionary<FormalRq1Treatment, IFormalRq1ConditionExecutor>
        {
            [FormalRq1Treatment.AgentCentric] = agentCentric,
            [FormalRq1Treatment.EventCentric] = eventCentric
        };
    }

    public IFormalRq1ConditionExecutor Create(FormalRq1Treatment treatment) =>
        _executors.Remove(treatment, out IFormalRq1ConditionExecutor? executor)
            ? executor
            : throw new InvalidOperationException("Each formal RQ1 condition requires one fresh executor.");
}

/// <summary>Production RQ2 executor over the exact branch context and one real Provider call.</summary>
public sealed class FormalRq2LiveConditionExecutor : IFormalRq2ConditionExecutor
{
    private readonly IModelClient<RemotePlannerResponse> _client;
    private readonly RemotePlannerRequest _request;
    private readonly IFormalRemotePlannerTerminalizer _terminalizer;

    public FormalRq2LiveConditionExecutor(
        FormalRq2Treatment treatment,
        string runtimeInstanceId,
        string providerSessionId,
        IModelClient<RemotePlannerResponse> client,
        RemotePlannerRequest request,
        IFormalRemotePlannerTerminalizer terminalizer)
    {
        if (!Enum.IsDefined(treatment)) throw new ArgumentOutOfRangeException(nameof(treatment));
        FormalExperimentCanonical.RequireIdentity(runtimeInstanceId, nameof(runtimeInstanceId));
        FormalExperimentCanonical.RequireIdentity(providerSessionId, nameof(providerSessionId));
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(terminalizer);
        Treatment = treatment;
        RuntimeInstanceId = runtimeInstanceId;
        ProviderSessionId = providerSessionId;
        _client = client;
        _request = request;
        _terminalizer = terminalizer;
    }

    public FormalRq2Treatment Treatment { get; }
    public string RuntimeInstanceId { get; }
    public string ProviderSessionId { get; }

    public async ValueTask<FormalRq2ConditionTerminalEvidence> ExecuteAsync(
        FormalRq2ConditionExecutionInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Treatment != Treatment
            || _request.Binding.ActorId != input.Composition.Context.ActorId
            || _request.Binding.NeedId != input.Composition.Context.NeedId
            || _request.Binding.CandidateSetId != input.Composition.Context.CandidateSetId
            || !_request.GetModelVisibleBytes().AsSpan().SequenceEqual(
                input.Composition.Context.GetModelVisibleBytes()))
            throw new ArgumentException("Formal RQ2 request does not bind the exact branch context.", nameof(input));
        FormalLiveCallResult call = await FormalLiveRemotePlannerCall.InvokeAsync(
            _client,
            _request,
            _terminalizer,
            cancellationToken).ConfigureAwait(false);
        FormalTerminalOutcomeReceipt receipt = call.Settlement.Receipt;
        return new FormalRq2ConditionTerminalEvidence(
            Treatment,
            MapRq2TerminalKind(receipt.Kind),
            receipt.TerminalEvidenceHash,
            call.ModelCall,
            receipt,
            transportAttemptCount: call.TransportAttemptCount);
    }

    private static FormalRq2TerminalOutcomeKind MapRq2TerminalKind(FormalTerminalOutcomeReceiptKind kind) =>
        kind switch
        {
            FormalTerminalOutcomeReceiptKind.AuthorityCommit => FormalRq2TerminalOutcomeKind.AuthorityCommitted,
            FormalTerminalOutcomeReceiptKind.ValidatedDefer => FormalRq2TerminalOutcomeKind.JustifiedDefer,
            FormalTerminalOutcomeReceiptKind.ValidatorRejection => FormalRq2TerminalOutcomeKind.ValidatorRejected,
            FormalTerminalOutcomeReceiptKind.TransportFailure => FormalRq2TerminalOutcomeKind.TransportFailure,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
}

public sealed class FormalRq2ConditionExecutorPairFactory : IFormalRq2ConditionExecutorFactory
{
    private readonly Dictionary<FormalRq2Treatment, IFormalRq2ConditionExecutor> _executors;

    public FormalRq2ConditionExecutorPairFactory(
        IFormalRq2ConditionExecutor verbatim,
        IFormalRq2ConditionExecutor summary)
    {
        ArgumentNullException.ThrowIfNull(verbatim);
        ArgumentNullException.ThrowIfNull(summary);
        _executors = new Dictionary<FormalRq2Treatment, IFormalRq2ConditionExecutor>
        {
            [FormalRq2Treatment.Verbatim] = verbatim,
            [FormalRq2Treatment.Summary] = summary
        };
    }

    public IFormalRq2ConditionExecutor Create(FormalRq2Treatment treatment) =>
        _executors.Remove(treatment, out IFormalRq2ConditionExecutor? executor)
            ? executor
            : throw new InvalidOperationException("Each formal RQ2 condition requires one fresh executor.");
}

internal sealed record FormalLiveCallResult(
    FormalModelCallEvidence? ModelCall,
    FormalLiveTerminalSettlement Settlement,
    int TransportAttemptCount);

internal static class FormalLiveRemotePlannerCall
{
    public static async ValueTask<FormalLiveCallResult> InvokeAsync(
        IModelClient<RemotePlannerResponse> client,
        RemotePlannerRequest request,
        IFormalRemotePlannerTerminalizer terminalizer,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= FormalTransientTransportRetry.MaxAttempts; attempt++)
        {
            ModelClientResult<RemotePlannerResponse> result = await client.InvokeAsync(
                request,
                cancellationToken).ConfigureAwait(false);
            if (result.Status != ModelClientResultStatus.Produced || result.Output is null)
                throw new InvalidDataException(
                    "Formal execution requires typed live Provider execution evidence.");
            if (result.ExecutionEvidence is ILiveRemotePlannerExecutionEvidence
                {
                    Outcome: LiveRemoteTransportOutcome.InvocationFailed,
                    FailureKind: LiveRemoteFailureKind failureKind
                } transportFailure)
            {
                if (attempt < FormalTransientTransportRetry.MaxAttempts
                    && FormalTransientTransportRetry.IsRetryable(failureKind))
                {
                    await Task.Delay(
                        FormalTransientTransportRetry.Backoffs[attempt - 1],
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }
                return new FormalLiveCallResult(
                    null,
                    FormalTransportFailureTerminalizer.Issue(request, transportFailure),
                    attempt);
            }
            if (result.ExecutionEvidence is not IFormalModelCallEvidenceCarrier
                { FormalCallEvidence: FormalModelCallEvidence modelCall })
                throw new InvalidDataException(
                    "Formal execution requires a live Provider response with canonical model-call evidence.");
            if (!modelCall.IsFormalPairingComplete)
                throw new InvalidDataException("Formal live model-call evidence is incomplete.");
            FormalLiveTerminalSettlement settlement = terminalizer.Settle(result.Output, modelCall)
                ?? throw new InvalidDataException("Formal terminalizer returned no settlement.");
            if (!StringComparer.Ordinal.Equals(settlement.Receipt.ModelCallId, modelCall.CallId))
                throw new InvalidDataException("Formal terminal settlement is cross-wired from its model call.");
            return new FormalLiveCallResult(modelCall, settlement, attempt);
        }
        throw new InvalidOperationException("Formal transport retry loop exhausted without a terminal result.");
    }
}

internal static class FormalTransientTransportRetry
{
    public const int MaxAttempts = 3;
    public static IReadOnlyList<TimeSpan> Backoffs { get; } =
        Array.AsReadOnly(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15) });

    public static bool IsRetryable(LiveRemoteFailureKind failureKind) => Enum.IsDefined(failureKind);
}
