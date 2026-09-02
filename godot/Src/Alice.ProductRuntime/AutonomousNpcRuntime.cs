using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Actors;
using Alice.Identity;
using Alice.Interaction;
using Alice.ModelRuntime;
using Alice.Navigation;
using Alice.Social;
using Alice.World;

namespace Alice.ProductRuntime;

public enum ActorExecutionMode
{
    Navigate,
    Interact,
    Communicate,
    Wait
}

/// <summary>Closed Phase 13 Authority mutation families; content nouns are configuration only.</summary>
public enum ProductActionFamily
{
    RegionOperation,
    Craft,
    Consumption,
    Rest,
    AssetTransfer,
    ListedExchange,
    ServiceExchange,
    PlaceStateChange,
    EquipmentChange
}

public enum ActorExecutionOutcome
{
    Completed,
    Rejected
}

public enum ActorExecutionFailure
{
    ForeignActor,
    Unsupported,
    Stale,
    Unavailable,
    AuthorityRejected
}

/// <summary>Typed provenance for the cognition layer that selected one execution intent.</summary>
public enum AutonomousNpcCognitionRoute
{
    None,
    L0,
    L1,
    L2
}

public sealed record ActorExecutionId
{
    public ActorExecutionId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Actor execution identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public abstract record ActorExecutionPayload
{
    private protected ActorExecutionPayload(ActorId actorId, ActorExecutionMode mode)
    {
        ActorIdentity.ValidateActorId(actorId);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ActorId = actorId;
        Mode = mode;
    }

    public ActorId ActorId { get; }
    public ActorExecutionMode Mode { get; }
}

public sealed record NavigateExecutionPayload : ActorExecutionPayload
{
    public NavigateExecutionPayload(ActorId actorId, TargetRef targetRef, ActivityId activityId)
        : base(actorId, ActorExecutionMode.Navigate)
    {
        ArgumentNullException.ThrowIfNull(targetRef);
        ArgumentNullException.ThrowIfNull(activityId);
        TargetRef = targetRef;
        ActivityId = activityId;
    }

    public TargetRef TargetRef { get; }
    public ActivityId ActivityId { get; }
}

public sealed record InteractExecutionPayload : ActorExecutionPayload
{
    public InteractExecutionPayload(ActorId actorId, GameActionSpec action)
        : base(actorId, ActorExecutionMode.Interact)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.ActorId != actorId)
            throw new ArgumentException("Interact payload must carry the same Actor's GameActionSpec.", nameof(action));
        Action = action;
    }

    public GameActionSpec Action { get; }
}

public sealed record CommunicateExecutionPayload : ActorExecutionPayload
{
    public CommunicateExecutionPayload(
        ActorId actorId,
        ConversationSessionId sessionId,
        SemanticDialogueActId sourceActId)
        : base(actorId, ActorExecutionMode.Communicate)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(sourceActId);
        SessionId = sessionId;
        SourceActId = sourceActId;
    }

    public ConversationSessionId SessionId { get; }
    public SemanticDialogueActId SourceActId { get; }
}

public sealed record WaitExecutionPayload : ActorExecutionPayload
{
    public WaitExecutionPayload(ActorId actorId, string reason)
        : base(actorId, ActorExecutionMode.Wait)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    public string Reason { get; }
}

public sealed record ActorExecutionIntent
{
    public ActorExecutionIntent(
        ActorId actorId,
        ActorExecutionMode mode,
        ActorExecutionPayload payload,
        string evidence,
        AutonomousNpcCognitionRoute cognitionRoute)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);
        if (!Enum.IsDefined(mode) || !Enum.IsDefined(cognitionRoute))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (payload.ActorId != actorId)
            throw new ArgumentException("Execution intent payload belongs to another Actor.", nameof(payload));
        if (payload.Mode != mode)
            throw new ArgumentException("Execution intent mode does not match its typed payload.", nameof(payload));
        ActorId = actorId;
        Mode = mode;
        Payload = payload;
        Evidence = evidence;
        CognitionRoute = cognitionRoute;
    }

    public ActorId ActorId { get; }
    public ActorExecutionMode Mode { get; }
    public ActorExecutionPayload Payload { get; }
    public string Evidence { get; }
    public AutonomousNpcCognitionRoute CognitionRoute { get; }

    public static ActorExecutionIntent Wait(
        ActorId actorId,
        string reason,
        AutonomousNpcCognitionRoute route = AutonomousNpcCognitionRoute.L0) =>
        new(actorId, ActorExecutionMode.Wait, new WaitExecutionPayload(actorId, reason), reason, route);
}

/// <summary>The shared, controller-neutral request accepted by Player and NPC executors.</summary>
public sealed record ActorExecutionRequest
{
    public ActorExecutionRequest(
        ActorExecutionId executionId,
        ActorId actorId,
        ActorExecutionMode mode,
        ActorExecutionPayload payload,
        SimTime sourceTime,
        AutonomousNpcCognitionRoute cognitionRoute)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(payload);
        if (!Enum.IsDefined(mode) || !Enum.IsDefined(cognitionRoute))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (payload.ActorId != actorId)
            throw new ArgumentException("Execution request payload belongs to another Actor.", nameof(payload));
        if (payload.Mode != mode)
            throw new ArgumentException("Execution request mode does not match its typed payload.", nameof(payload));
        ExecutionId = executionId;
        ActorId = actorId;
        Mode = mode;
        Payload = payload;
        SourceTime = sourceTime;
        CognitionRoute = cognitionRoute;
    }

    public ActorExecutionId ExecutionId { get; }
    public ActorId ActorId { get; }
    public ActorExecutionMode Mode { get; }
    public ActorExecutionPayload Payload { get; }
    public SimTime SourceTime { get; }
    public AutonomousNpcCognitionRoute CognitionRoute { get; }

    public static ActorExecutionRequest FromIntent(
        ActorExecutionId executionId,
        ActorExecutionIntent intent,
        SimTime sourceTime)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return new ActorExecutionRequest(
            executionId,
            intent.ActorId,
            intent.Mode,
            intent.Payload,
            sourceTime,
            intent.CognitionRoute);
    }
}

/// <summary>Optional closed family result attached to the shared receipt envelope.</summary>
public abstract record ActorExecutionResult
{
    private protected ActorExecutionResult()
    {
    }
}

public sealed record AuthorityCommitExecutionResult : ActorExecutionResult
{
    public AuthorityCommitExecutionResult(string actionFamily)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionFamily);
        ActionFamily = actionFamily;
    }

    public string ActionFamily { get; }
}

public sealed record ActorExecutionReceipt
{
    private ActorExecutionReceipt(
        ActorExecutionId executionId,
        ActorId actorId,
        ActorExecutionMode mode,
        ActorExecutionOutcome outcome,
        ActorExecutionFailure? failure,
        string evidence,
        SimTime sourceTime,
        AutonomousNpcCognitionRoute cognitionRoute,
        ActorExecutionResult? result)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        ActorIdentity.ValidateActorId(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);
        if (!Enum.IsDefined(mode)
            || !Enum.IsDefined(outcome)
            || !Enum.IsDefined(cognitionRoute)
            || outcome == ActorExecutionOutcome.Completed && failure is not null
            || outcome == ActorExecutionOutcome.Rejected && failure is null)
            throw new ArgumentException("Actor execution receipt is inconsistent.");
        ExecutionId = executionId;
        ActorId = actorId;
        Mode = mode;
        Outcome = outcome;
        Failure = failure;
        Evidence = evidence;
        SourceTime = sourceTime;
        CognitionRoute = cognitionRoute;
        Result = result;
    }

    public ActorExecutionId ExecutionId { get; }
    public ActorId ActorId { get; }
    public ActorExecutionMode Mode { get; }
    public ActorExecutionOutcome Outcome { get; }
    public ActorExecutionFailure? Failure { get; }
    public string Evidence { get; }
    public SimTime SourceTime { get; }
    public AutonomousNpcCognitionRoute CognitionRoute { get; }
    public ActorExecutionResult? Result { get; }

    public static ActorExecutionReceipt Completed(
        ActorExecutionRequest request,
        string evidence,
        ActorExecutionResult? result = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ActorExecutionReceipt(
            request.ExecutionId,
            request.ActorId,
            request.Mode,
            ActorExecutionOutcome.Completed,
            null,
            evidence,
            request.SourceTime,
            request.CognitionRoute,
            result);
    }

    public static ActorExecutionReceipt Rejected(
        ActorExecutionRequest request,
        ActorExecutionFailure failure,
        string evidence)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(failure)) throw new ArgumentOutOfRangeException(nameof(failure));
        return new ActorExecutionReceipt(
            request.ExecutionId,
            request.ActorId,
            request.Mode,
            ActorExecutionOutcome.Rejected,
            failure,
            evidence,
            request.SourceTime,
            request.CognitionRoute,
            null);
    }

    internal static ActorExecutionReceipt RejectedForExecutor(
        ActorExecutionRequest request,
        ActorId executorActorId)
    {
        ActorIdentity.ValidateActorId(executorActorId);
        return new ActorExecutionReceipt(
            request.ExecutionId,
            request.ActorId,
            request.Mode,
            ActorExecutionOutcome.Rejected,
            ActorExecutionFailure.ForeignActor,
            $"execution/foreign_executor/{executorActorId.Value}",
            request.SourceTime,
            request.CognitionRoute,
            null);
    }
}

public interface IActorExecutionSelector
{
    ActorId ActorId { get; }
    ActorExecutionIntent Select(SimTime now);
}

public interface IActorExecutionExecutor
{
    ActorId ActorId { get; }
    ActorExecutionReceipt Execute(ActorExecutionRequest request);
}

/// <summary>Shared request validation used by both Player and NPC call sites.</summary>
public static class ActorExecutionPipeline
{
    public static ActorExecutionReceipt Dispatch(
        ActorExecutionRequest request,
        IActorExecutionExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(executor);
        if (request.ActorId != executor.ActorId)
            return ActorExecutionReceipt.RejectedForExecutor(request, executor.ActorId);
        ActorExecutionReceipt receipt = executor.Execute(request);
        if (receipt.ExecutionId != request.ExecutionId
            || receipt.ActorId != request.ActorId
            || receipt.Mode != request.Mode
            || receipt.SourceTime != request.SourceTime
            || receipt.CognitionRoute != request.CognitionRoute)
            throw new InvalidOperationException("Actor executor returned cross-request evidence.");
        return receipt;
    }
}

public interface IActorExecutionObserver
{
    void ObserveSelection(ActorExecutionIntent intent, SimTime now);
    void ObserveDispatch(ActorExecutionRequest request, ActorExecutionReceipt receipt);
}

/// <summary>Converts one NPC intent into the same shared request consumed by Player execution.</summary>
public sealed class UnifiedActorExecutionDispatcher
{
    private readonly IActorExecutionSelector _selector;
    private readonly IActorExecutionExecutor _executor;
    private readonly IActorExecutionObserver? _observer;

    public UnifiedActorExecutionDispatcher(
        IActorExecutionSelector selector,
        IActorExecutionExecutor executor,
        IActorExecutionObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(executor);
        if (selector.ActorId != executor.ActorId)
            throw new ArgumentException("Actor selector and executor must belong to one Actor.");
        _selector = selector;
        _executor = executor;
        _observer = observer;
    }

    public ActorId ActorId => _selector.ActorId;
    public IReadOnlyList<ActorExecutionMode> RegisteredModes { get; } =
        Array.AsReadOnly(Enum.GetValues<ActorExecutionMode>());

    public ActorExecutionReceipt Dispatch(SimTime now, ActorExecutionId executionId)
    {
        ActorExecutionIntent intent = _selector.Select(now);
        _observer?.ObserveSelection(intent, now);
        if (intent.ActorId != ActorId)
            throw new InvalidOperationException("Actor selector returned another Actor's intent.");
        ActorExecutionRequest request = ActorExecutionRequest.FromIntent(executionId, intent, now);
        ActorExecutionReceipt receipt = ActorExecutionPipeline.Dispatch(request, _executor);
        _observer?.ObserveDispatch(request, receipt);
        return receipt;
    }
}

public sealed class AutonomousNpc
{
    private readonly UnifiedActorExecutionDispatcher _dispatcher;
    private SimTime _nextDispatchAt;
    private long _dispatchSequence;

    public AutonomousNpc(UnifiedActorExecutionDispatcher dispatcher, SimTime firstDispatchAt)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        _nextDispatchAt = firstDispatchAt;
    }

    public ActorId ActorId => _dispatcher.ActorId;
    public SimTime NextDispatchAt => _nextDispatchAt;
    public long DispatchSequence => _dispatchSequence;

    public void RestoreDispatchState(SimTime nextDispatchAt, long dispatchSequence)
    {
        if (dispatchSequence < 0) throw new ArgumentOutOfRangeException(nameof(dispatchSequence));
        _nextDispatchAt = nextDispatchAt;
        _dispatchSequence = dispatchSequence;
    }

    internal ActorExecutionReceipt? Advance(SimTime now, long intervalTicks)
    {
        if (intervalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(intervalTicks));
        if (now.CompareTo(_nextDispatchAt) < 0) return null;
        long sequence = checked(_dispatchSequence + 1);
        var executionId = new ActorExecutionId($"demo/{ActorId.Value}/{sequence}");
        ActorExecutionReceipt receipt = _dispatcher.Dispatch(now, executionId);
        _dispatchSequence = sequence;
        _nextDispatchAt = now.Add(new SimDuration(intervalTicks));
        return receipt;
    }
}

public sealed record ActorExecutionBatch
{
    internal ActorExecutionBatch(SimTime now, IEnumerable<ActorExecutionReceipt> receipts)
    {
        Now = now;
        Receipts = new ReadOnlyCollection<ActorExecutionReceipt>(receipts.ToArray());
    }

    public SimTime Now { get; }
    public IReadOnlyList<ActorExecutionReceipt> Receipts { get; }
}

public sealed class AutonomousNpcScheduler
{
    private readonly ReadOnlyCollection<AutonomousNpc> _npcs;
    private readonly long _intervalTicks;

    public AutonomousNpcScheduler(IEnumerable<AutonomousNpc> npcs, long intervalTicks)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        if (intervalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(intervalTicks));
        AutonomousNpc[] snapshot = npcs.ToArray();
        Array.Sort(snapshot, AutonomousNpcComparer.Instance);
        if (snapshot.Select(GetActorId).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Autonomous NPC actor identities must be unique.", nameof(npcs));
        _npcs = Array.AsReadOnly(snapshot);
        _intervalTicks = intervalTicks;
    }

    public IReadOnlyList<AutonomousNpc> Npcs => _npcs;

    public ActorExecutionBatch Advance(SimTime now)
    {
        var receipts = new List<ActorExecutionReceipt>();
        foreach (AutonomousNpc npc in _npcs)
        {
            ActorExecutionReceipt? receipt = npc.Advance(now, _intervalTicks);
            if (receipt is not null) receipts.Add(receipt);
        }
        return new ActorExecutionBatch(now, receipts);
    }

    private static ActorId GetActorId(AutonomousNpc npc) => npc.ActorId;

    private sealed class AutonomousNpcComparer : IComparer<AutonomousNpc>
    {
        public static AutonomousNpcComparer Instance { get; } = new();
        public int Compare(AutonomousNpc? left, AutonomousNpc? right) =>
            StringComparer.Ordinal.Compare(left?.ActorId.Value, right?.ActorId.Value);
    }
}

public sealed record PlannerIntent
{
    public PlannerIntent(
        ProviderWorkId workId,
        string settlementOwnerId,
        RemotePlannerRequest request,
        ProviderRequestBudget budget)
    {
        ArgumentNullException.ThrowIfNull(workId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentException.ThrowIfNullOrWhiteSpace(settlementOwnerId);
        WorkId = workId;
        SettlementOwnerId = settlementOwnerId;
        Request = request;
        Budget = budget;
    }

    public ProviderWorkId WorkId { get; }
    public string SettlementOwnerId { get; }
    public RemotePlannerRequest Request { get; }
    public ProviderRequestBudget Budget { get; }
}

internal static class PlannerIntentIdentity
{
    public static bool MatchesImmutableWork(PlannerIntent left, PlannerIntent right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.WorkId == right.WorkId
            && StringComparer.Ordinal.Equals(left.SettlementOwnerId, right.SettlementOwnerId)
            && MatchesRequest(left.Request, right.Request)
            && left.Budget == right.Budget;
    }

    public static bool MatchesImmutableWork(RemotePlannerWork existing, PlannerIntent candidate)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidate);
        return existing.WorkId == candidate.WorkId
            && StringComparer.Ordinal.Equals(existing.SettlementOwnerId, candidate.SettlementOwnerId)
            && MatchesRequest(existing.Request, candidate.Request)
            && existing.Budget == candidate.Budget;
    }

    private static bool MatchesRequest(RemotePlannerRequest left, RemotePlannerRequest right) =>
        left.Binding.RequestId == right.Binding.RequestId
        && left.GetModelVisibleBytes().AsSpan().SequenceEqual(right.GetModelVisibleBytes());
}

/// <summary>Eligible planner intents retained outside the world-action pipeline until capacity is available.</summary>
public sealed class PlannerInbox
{
    private readonly List<PlannerIntent> _pending = [];

    public IReadOnlyList<PlannerIntent> Pending => _pending.AsReadOnly();

    public bool Add(PlannerIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        foreach (PlannerIntent existing in _pending)
        {
            if (existing.WorkId != intent.WorkId) continue;
            if (!PlannerIntentIdentity.MatchesImmutableWork(existing, intent))
                throw new InvalidOperationException($"Provider work identity {intent.WorkId.Value} was reused for different pending intent content.");
            return false;
        }
        _pending.Add(intent);
        return true;
    }

    public IReadOnlyList<ProviderEnqueueResult> Feed(RemotePlannerQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        var results = new List<ProviderEnqueueResult>();
        for (int index = 0; index < _pending.Count;)
        {
            PlannerIntent intent = _pending[index];
            ProviderEnqueueResult result = queue.TryEnqueue(intent);
            results.Add(result);
            if (result.Outcome is ProviderEnqueueOutcome.Enqueued or ProviderEnqueueOutcome.Duplicate)
            {
                _pending.RemoveAt(index);
                continue;
            }
            if (result.Outcome == ProviderEnqueueOutcome.CapacityBlocked) break;
            throw new InvalidOperationException($"A planner intent violates the active Provider budget profile: {result.Outcome}.");
        }
        return new ReadOnlyCollection<ProviderEnqueueResult>(results);
    }
}
