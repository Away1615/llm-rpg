using System.Collections.ObjectModel;
using Alice.ModelRuntime;

namespace Alice.ProductRuntime;

public sealed record ProviderSettlementReceipt(
    string SettlementOwnerId,
    ProviderWorkId WorkId,
    string Outcome);

public interface IDemoProviderSettlementOwner
{
    string SettlementOwnerId { get; }
    ProviderSettlementReceipt Settle(ProviderWorkSnapshot work, Alice.Activities.SimTime now);
}

public sealed record ProviderWorkId
{
    public ProviderWorkId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Provider work identity is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public sealed record ProviderRequestBudget
{
    public ProviderRequestBudget(int measuredContextTokens, int contextTokenCeiling, int outputTokenCeiling)
    {
        if (measuredContextTokens < 0 || contextTokenCeiling <= 0 || outputTokenCeiling <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(measuredContextTokens));
        }

        MeasuredContextTokens = measuredContextTokens;
        ContextTokenCeiling = contextTokenCeiling;
        OutputTokenCeiling = outputTokenCeiling;
    }

    public int MeasuredContextTokens { get; }
    public int ContextTokenCeiling { get; }
    public int OutputTokenCeiling { get; }
}

public enum ProviderWorkState
{
    Ready,
    InFlight,
    WaitingForRetry,
    Completed,
    Failed,
    Cancelled
}

public enum ProviderEnqueueOutcome
{
    Enqueued,
    Duplicate,
    CapacityBlocked,
    ContextBudgetExceeded,
    BudgetProfileMismatch,
    IdentityConflict
}

public sealed record ProviderEnqueueResult(
    ProviderEnqueueOutcome Outcome,
    ProviderWorkSnapshot? Work);

public sealed record ProviderWorkSnapshot
{
    internal ProviderWorkSnapshot(RemotePlannerWork work)
    {
        WorkId = work.WorkId;
        SettlementOwnerId = work.SettlementOwnerId;
        RequestId = work.Request.Binding.RequestId;
        ActorId = work.Request.Binding.ActorId.Value;
        NeedId = work.Request.Binding.NeedId.Value;
        State = work.State;
        AttemptCount = work.AttemptCount;
        RetryNotBefore = work.RetryNotBefore;
        FailureCode = work.FailureCode;
        Budget = work.Budget;
        Result = work.Result;
    }

    public ProviderWorkId WorkId { get; }
    public string SettlementOwnerId { get; }
    public RemotePlannerRequestId RequestId { get; }
    public string ActorId { get; }
    public string NeedId { get; }
    public ProviderWorkState State { get; }
    public int AttemptCount { get; }
    public DateTimeOffset? RetryNotBefore { get; }
    public string? FailureCode { get; }
    public ProviderRequestBudget Budget { get; }
    public ModelClientResult<RemotePlannerResponse>? Result { get; }
}

public sealed record ProviderQueueSnapshot
{
    internal ProviderQueueSnapshot(IEnumerable<RemotePlannerWork> work)
    {
        ProviderWorkSnapshot[] snapshots = work
            .Select(candidate => new ProviderWorkSnapshot(candidate))
            .ToArray();
        Work = new ReadOnlyCollection<ProviderWorkSnapshot>(snapshots);
    }

    public IReadOnlyList<ProviderWorkSnapshot> Work { get; }
    public int ReadyCount => Work.Count(entry => entry.State == ProviderWorkState.Ready);
    public int InFlightCount => Work.Count(entry => entry.State == ProviderWorkState.InFlight);
    public int WaitingForRetryCount => Work.Count(entry => entry.State == ProviderWorkState.WaitingForRetry);
}

internal sealed class RemotePlannerWork : IDisposable
{
    public RemotePlannerWork(
        long enqueueSequence,
        PlannerIntent intent)
    {
        EnqueueSequence = enqueueSequence;
        WorkId = intent.WorkId;
        SettlementOwnerId = intent.SettlementOwnerId;
        Request = intent.Request;
        Budget = intent.Budget;
        State = ProviderWorkState.Ready;
    }

    public long EnqueueSequence { get; }
    public ProviderWorkId WorkId { get; }
    public string SettlementOwnerId { get; }
    public RemotePlannerRequest Request { get; }
    public ProviderRequestBudget Budget { get; }
    public ProviderWorkState State { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? RetryNotBefore { get; set; }
    public string? FailureCode { get; set; }
    public ModelClientResult<RemotePlannerResponse>? Result { get; set; }
    public Task<ModelClientResult<RemotePlannerResponse>>? Operation { get; set; }
    public CancellationTokenSource? Cancellation { get; set; }

    public void Dispose()
    {
        Cancellation?.Cancel();
        Cancellation?.Dispose();
        Cancellation = null;
        Operation = null;
    }
}
/// <summary>
/// Product transport owner. Eligible DecisionNeeds remain outside this bounded queue until enqueue succeeds.
/// Every started attempt invokes the injected model-client port.
/// </summary>
public sealed class RemotePlannerQueue : IDisposable
{
    public const string TimeoutFailure = "timeout";
    public const string NetworkFailure = "network_failure";
    public const string HttpFailure = "http_failure";
    public const string ResponseBodyTooLargeFailure = "response_body_too_large";
    public const string OutputTokenLimitReachedFailure = "output_token_limit_reached";
    public const string InvalidResponseEnvelopeFailure = "invalid_response_envelope";
    public const string ClientExceptionFailure = "client_exception";
    public const string ClientUnavailableFailure = "client_unavailable";

    private readonly object _sync = new();
    private readonly IModelClient<RemotePlannerResponse> _client;
    private readonly ProviderQueueConfiguration _configuration;
    private readonly bool _allowsAutomaticRetry;
    private readonly HashSet<string> _retryableFailureCodes;
    private readonly List<RemotePlannerWork> _work = [];
    private long _nextEnqueueSequence;
    private bool _disposed;

    public RemotePlannerQueue(
        IModelClient<RemotePlannerResponse> client,
        ProviderQueueConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(configuration);
        _client = client;
        _configuration = configuration;
        _allowsAutomaticRetry = client is not IAutomaticModelRetryPolicy policy
            || policy.AllowsAutomaticRetry;
        _retryableFailureCodes = new HashSet<string>(configuration.RetryableFailureCodes, StringComparer.Ordinal);
    }

    public ProviderEnqueueResult TryEnqueue(PlannerIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        lock (_sync)
        {
            ThrowIfDisposed();
            RemotePlannerWork? duplicate = Find(intent.WorkId);
            if (duplicate is not null)
            {
                return new ProviderEnqueueResult(
                    PlannerIntentIdentity.MatchesImmutableWork(duplicate, intent)
                        ? ProviderEnqueueOutcome.Duplicate
                        : ProviderEnqueueOutcome.IdentityConflict,
                    new ProviderWorkSnapshot(duplicate));
            }

            if (intent.Budget.ContextTokenCeiling != _configuration.MaxContextTokens
                || intent.Budget.OutputTokenCeiling != _configuration.MaxOutputTokens)
            {
                return new ProviderEnqueueResult(ProviderEnqueueOutcome.BudgetProfileMismatch, null);
            }

            if (intent.Budget.MeasuredContextTokens > intent.Budget.ContextTokenCeiling)
            {
                return new ProviderEnqueueResult(ProviderEnqueueOutcome.ContextBudgetExceeded, null);
            }

            int activeCount = 0;
            foreach (RemotePlannerWork existing in _work)
            {
                if (!IsTerminal(existing.State))
                {
                    activeCount++;
                }
            }

            if (activeCount >= _configuration.AdmittedCapacity)
            {
                return new ProviderEnqueueResult(ProviderEnqueueOutcome.CapacityBlocked, null);
            }

            long sequence = checked(++_nextEnqueueSequence);
            var added = new RemotePlannerWork(sequence, intent);
            _work.Add(added);
            return new ProviderEnqueueResult(
                ProviderEnqueueOutcome.Enqueued,
                new ProviderWorkSnapshot(added));
        }
    }

    public ProviderEnqueueResult TryEnqueue(
        ProviderWorkId workId,
        RemotePlannerRequest request,
        ProviderRequestBudget budget) =>
        TryEnqueue(new PlannerIntent(workId, "direct-queue-caller", request, budget));

    /// <summary>Polls terminal attempts, starts ready work up to capacity, then observes synchronous completions.</summary>
    public ProviderQueueSnapshot Pump(DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            SettleCompleted(now);
            PromoteRetries(now);
            StartReady(cancellationToken);
            SettleCompleted(now);
            return SnapshotUnsafe();
        }
    }

    public ProviderQueueSnapshot Snapshot()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            return SnapshotUnsafe();
        }
    }

    public bool AcknowledgeTerminal(ProviderWorkId workId)
    {
        ArgumentNullException.ThrowIfNull(workId);
        lock (_sync)
        {
            ThrowIfDisposed();
            RemotePlannerWork? work = Find(workId);
            if (work is null || !IsTerminal(work.State))
            {
                return false;
            }

            work.Dispose();
            return _work.Remove(work);
        }
    }

    public bool Cancel(ProviderWorkId workId)
    {
        ArgumentNullException.ThrowIfNull(workId);
        lock (_sync)
        {
            ThrowIfDisposed();
            RemotePlannerWork? work = Find(workId);
            if (work is null || IsTerminal(work.State))
            {
                return false;
            }

            work.Dispose();
            work.State = ProviderWorkState.Cancelled;
            work.FailureCode = null;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            foreach (RemotePlannerWork work in _work)
            {
                work.Dispose();
            }

            _disposed = true;
        }
    }

    private void StartReady(CancellationToken callerCancellation)
    {
        int inFlight = 0;
        foreach (RemotePlannerWork existing in _work)
        {
            if (existing.State == ProviderWorkState.InFlight)
            {
                inFlight++;
            }
        }

        foreach (RemotePlannerWork work in OrderedWork())
        {
            if (inFlight >= _configuration.MaxInFlight)
            {
                return;
            }

            if (work.State != ProviderWorkState.Ready)
            {
                continue;
            }

            work.Cancellation?.Dispose();
            work.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
            work.AttemptCount = checked(work.AttemptCount + 1);
            work.State = ProviderWorkState.InFlight;
            work.RetryNotBefore = null;
            work.FailureCode = null;
            try
            {
                work.Operation = _client.InvokeAsync(work.Request, work.Cancellation.Token).AsTask();
            }
            catch (Exception exception)
            {
                work.Operation = Task.FromException<ModelClientResult<RemotePlannerResponse>>(exception);
            }

            inFlight++;
        }
    }

    private void SettleCompleted(DateTimeOffset now)
    {
        foreach (RemotePlannerWork work in OrderedWork())
        {
            if (work.State != ProviderWorkState.InFlight
                || work.Operation is not Task<ModelClientResult<RemotePlannerResponse>> operation
                || !operation.IsCompleted)
            {
                continue;
            }

            ModelClientResult<RemotePlannerResponse>? result = null;
            string? failureCode = null;
            try
            {
                result = operation.GetAwaiter().GetResult();
                failureCode = ClassifyFailure(result);
            }
            catch (OperationCanceledException) when (work.Cancellation?.IsCancellationRequested == true)
            {
                work.State = ProviderWorkState.Cancelled;
            }
            catch (Exception)
            {
                failureCode = ClientExceptionFailure;
            }

            work.Cancellation?.Dispose();
            work.Cancellation = null;
            work.Operation = null;
            if (work.State == ProviderWorkState.Cancelled)
            {
                continue;
            }

            if (failureCode is null)
            {
                work.Result = result;
                work.State = ProviderWorkState.Completed;
                continue;
            }

            work.FailureCode = failureCode;
            if (_allowsAutomaticRetry
                && _retryableFailureCodes.Contains(failureCode)
                && work.AttemptCount <= _configuration.RetryBackoffMilliseconds.Length)
            {
                int milliseconds = _configuration.RetryBackoffMilliseconds[work.AttemptCount - 1];
                work.RetryNotBefore = now.AddMilliseconds(milliseconds);
                work.State = ProviderWorkState.WaitingForRetry;
                continue;
            }

            work.Result = result;
            work.State = ProviderWorkState.Failed;
        }
    }

    private static string? ClassifyFailure(ModelClientResult<RemotePlannerResponse> result)
    {
        if (result.Status == ModelClientResultStatus.Unavailable)
        {
            return ClientUnavailableFailure;
        }

        if (result.ExecutionEvidence is not ILiveRemotePlannerExecutionEvidence remote
            || remote.ResponseEnvelopeReceived)
        {
            return null;
        }

        return remote.FailureKind switch
        {
            LiveRemoteFailureKind.Timeout => TimeoutFailure,
            LiveRemoteFailureKind.NetworkFailure => NetworkFailure,
            LiveRemoteFailureKind.HttpFailure => HttpFailure,
            LiveRemoteFailureKind.ResponseBodyTooLarge => ResponseBodyTooLargeFailure,
            LiveRemoteFailureKind.OutputTokenLimitReached => OutputTokenLimitReachedFailure,
            LiveRemoteFailureKind.InvalidResponseEnvelope => InvalidResponseEnvelopeFailure,
            _ => ClientExceptionFailure
        };
    }

    private void PromoteRetries(DateTimeOffset now)
    {
        foreach (RemotePlannerWork work in _work)
        {
            if (work.State == ProviderWorkState.WaitingForRetry
                && work.RetryNotBefore is DateTimeOffset retryAt
                && retryAt <= now)
            {
                work.State = ProviderWorkState.Ready;
                work.RetryNotBefore = null;
            }
        }
    }

    private RemotePlannerWork[] OrderedWork()
    {
        RemotePlannerWork[] ordered = _work.ToArray();
        Array.Sort(ordered, RemotePlannerWorkComparer.Instance);
        return ordered;
    }

    private RemotePlannerWork? Find(ProviderWorkId workId)
    {
        foreach (RemotePlannerWork work in _work)
        {
            if (work.WorkId == workId)
            {
                return work;
            }
        }

        return null;
    }

    private ProviderQueueSnapshot SnapshotUnsafe() => new(OrderedWork());

    private static bool IsTerminal(ProviderWorkState state) =>
        state is ProviderWorkState.Completed or ProviderWorkState.Failed or ProviderWorkState.Cancelled;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class RemotePlannerWorkComparer : IComparer<RemotePlannerWork>
    {
        public static RemotePlannerWorkComparer Instance { get; } = new();

        public int Compare(RemotePlannerWork? left, RemotePlannerWork? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            int sequence = left.EnqueueSequence.CompareTo(right.EnqueueSequence);
            return sequence != 0
                ? sequence
                : StringComparer.Ordinal.Compare(left.WorkId.Value, right.WorkId.Value);
        }
    }
}
