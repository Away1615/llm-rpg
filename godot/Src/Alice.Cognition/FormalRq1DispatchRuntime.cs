using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Alice.Activities;
using Alice.ModelRuntime;

namespace Alice.Cognition;

public readonly record struct FormalRq1TransportFailureCode
{
    public FormalRq1TransportFailureCode(string value)
    {
        DependencyContractIdentity.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

/// <summary>An explicit versioned retry classification. The runtime contains no built-in status set.</summary>
public sealed class FormalRq1RetryClassificationPolicy
{
    private readonly ReadOnlyCollection<FormalRq1TransportFailureCode> _retryableFailureCodes;

    public FormalRq1RetryClassificationPolicy(
        string policyId,
        IEnumerable<FormalRq1TransportFailureCode> retryableFailureCodes)
    {
        DependencyContractIdentity.Validate(policyId, nameof(policyId));
        ArgumentNullException.ThrowIfNull(retryableFailureCodes);
        FormalRq1TransportFailureCode[] codes = retryableFailureCodes.ToArray();
        Array.Sort(codes, FailureCodeComparer.Instance);
        for (int index = 1; index < codes.Length; index++)
        {
            if (codes[index - 1] == codes[index])
            {
                throw new ArgumentException("Retryable transport failure codes must be unique.", nameof(retryableFailureCodes));
            }
        }

        PolicyId = policyId;
        _retryableFailureCodes = Array.AsReadOnly(codes);
        ContentHash = Hash(Serialize());
    }

    public string PolicyId { get; }
    public string ContentHash { get; }
    public IReadOnlyList<FormalRq1TransportFailureCode> RetryableFailureCodes => _retryableFailureCodes;

    public bool IsRetryable(FormalRq1TransportFailureCode failureCode)
    {
        foreach (FormalRq1TransportFailureCode code in _retryableFailureCodes)
        {
            if (code == failureCode)
            {
                return true;
            }
        }

        return false;
    }

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", "formal-rq1-retry-classification-v1");
            writer.WriteString("policy_id", PolicyId);
            writer.WritePropertyName("retryable_failure_codes");
            writer.WriteStartArray();
            foreach (FormalRq1TransportFailureCode code in _retryableFailureCodes)
            {
                writer.WriteStringValue(code.Value);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static string Hash(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed class FailureCodeComparer : IComparer<FormalRq1TransportFailureCode>
    {
        public static FailureCodeComparer Instance { get; } = new();

        public int Compare(FormalRq1TransportFailureCode left, FormalRq1TransportFailureCode right)
        {
            return StringComparer.Ordinal.Compare(left.Value, right.Value);
        }
    }
}

/// <summary>Externally supplied formal-RQ1 runtime values. This type defines no active defaults.</summary>
public sealed class FormalRq1DispatchConfiguration
{
    private readonly ReadOnlyCollection<TimeSpan> _retryBackoffs;

    public FormalRq1DispatchConfiguration(
        string configurationId,
        long starvationAgeTicks,
        int logicalSessionBudget,
        int maxProviderInFlight,
        IEnumerable<TimeSpan> retryBackoffs,
        FormalRq1RetryClassificationPolicy retryClassificationPolicy)
    {
        DependencyContractIdentity.Validate(configurationId, nameof(configurationId));
        if (starvationAgeTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(starvationAgeTicks));
        }

        if (logicalSessionBudget < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalSessionBudget));
        }

        if (maxProviderInFlight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxProviderInFlight));
        }

        ArgumentNullException.ThrowIfNull(retryBackoffs);
        ArgumentNullException.ThrowIfNull(retryClassificationPolicy);
        TimeSpan[] backoffs = retryBackoffs.ToArray();
        if (backoffs.Any(backoff => backoff < TimeSpan.Zero))
        {
            throw new ArgumentOutOfRangeException(nameof(retryBackoffs));
        }

        ConfigurationId = configurationId;
        StarvationAgeTicks = starvationAgeTicks;
        LogicalSessionBudget = logicalSessionBudget;
        MaxProviderInFlight = maxProviderInFlight;
        _retryBackoffs = Array.AsReadOnly(backoffs);
        RetryClassificationPolicy = retryClassificationPolicy;
    }

    public string ConfigurationId { get; }
    public long StarvationAgeTicks { get; }
    public int LogicalSessionBudget { get; }
    public int MaxProviderInFlight { get; }
    public int MaxTransportAttempts => checked(_retryBackoffs.Count + 1);
    public IReadOnlyList<TimeSpan> RetryBackoffs => _retryBackoffs;
    public FormalRq1RetryClassificationPolicy RetryClassificationPolicy { get; }
}

public enum Rq1LogicalSessionState
{
    WaitingForProviderCapacity,
    InFlightTransportAttempt,
    WaitingForRetry,
    ResponseEnvelopeReceived,
    TransportOnlyAborted,
    DispatchPreparationFailed
}

/// <summary>One reserved logical-session unit shared by all transport attempts.</summary>
public sealed class Rq1LogicalSessionDispatch
{
    private readonly FormalRq1DispatchRuntime _owner;
    private IModelClient<RemotePlannerResponse>? _client;
    private RemotePlannerRequest? _request;
    private CancellationToken _callerCancellation;
    private bool _allowsAutomaticRetry = true;

    internal Rq1LogicalSessionDispatch(
        FormalRq1DispatchRuntime owner,
        Rq1DecisionNeedAdmissionEntry admissionEntry)
    {
        _owner = owner;
        AdmissionEntry = admissionEntry;
        State = Rq1LogicalSessionState.WaitingForProviderCapacity;
    }

    public Rq1DecisionNeedAdmissionEntry AdmissionEntry { get; }
    public DecisionNeed Need => AdmissionEntry.Need;
    public Rq1LogicalSessionState State { get; internal set; }
    public int TransportAttemptCount { get; internal set; }
    public DateTimeOffset? CurrentAttemptStartedAt { get; internal set; }
    public DateTimeOffset? RetryNotBefore { get; internal set; }
    public RemotePlannerInvocationSession? Invocation { get; internal set; }

    public void CompleteAttemptWithResponseEnvelope()
    {
        _owner.CompleteAttemptWithResponseEnvelope(this);
    }

    public void CompleteAttemptWithoutResponse(
        DateTimeOffset completedAt,
        FormalRq1TransportFailureCode failureCode)
    {
        _owner.CompleteAttemptWithoutResponse(this, completedAt, failureCode);
    }

    /// <summary>Releases retained request/client state after response settlement has completed.</summary>
    public void CompleteResponseSettlement()
    {
        _owner.CompleteResponseSettlement(this);
    }

    internal void AttachInitialInvocation(
        IModelClient<RemotePlannerResponse> client,
        RemotePlannerRequest request,
        CancellationToken callerCancellation)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        if (Invocation is not null || _request is not null || TransportAttemptCount != 1)
        {
            throw new InvalidOperationException("Only the first authorized transport attempt may attach initial invocation work.");
        }

        ValidateRequestBinding(request);
        _client = client;
        _request = request;
        _callerCancellation = callerCancellation;
        _allowsAutomaticRetry = client is not IAutomaticModelRetryPolicy policy
            || policy.AllowsAutomaticRetry;
        Invocation = RemotePlannerInvocationSession.Start(client, request, callerCancellation);
    }

    internal void RestartInvocation()
    {
        if (!_allowsAutomaticRetry)
        {
            throw new InvalidOperationException("This model client prohibits automatic retry.");
        }

        if (_client is null || _request is null || TransportAttemptCount <= 1)
        {
            throw new InvalidOperationException("A retry requires retained initial invocation work.");
        }

        ValidateRequestBinding(_request);
        Invocation?.Dispose();
        Invocation = RemotePlannerInvocationSession.Start(_client, _request, _callerCancellation);
    }

    internal void ReleaseInvocation(bool clearRetainedBinding)
    {
        Invocation?.Dispose();
        Invocation = null;
        if (clearRetainedBinding)
        {
            _client = null;
            _request = null;
            _callerCancellation = default;
            _allowsAutomaticRetry = true;
        }
    }

    internal bool AllowsAutomaticRetry => _allowsAutomaticRetry;

    private void ValidateRequestBinding(RemotePlannerRequest request)
    {
        RemotePlannerRequestBinding binding = request.Binding;
        if (Need.State != DecisionNeedState.InFlight
            || binding.ActorId != Need.NpcId
            || binding.NeedId != Need.NeedId
            || binding.Fingerprint != Need.Fingerprint
            || binding.ProblemDescriptorHash != Need.ProblemDescriptor.DescriptorHash)
        {
            throw new ArgumentException("Remote Planner request does not exactly bind the authorized in-flight Need.", nameof(request));
        }

        int? requestAttempt = binding.Kind switch
        {
            RemotePlannerRequestKind.PlanlessStrategic => binding.PlanlessStrategicBinding.AttemptCount,
            RemotePlannerRequestKind.InviteResponse => binding.InviteResponseBinding.AttemptCount,
            RemotePlannerRequestKind.Planning => null,
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
        if (requestAttempt is int attempt && attempt != Need.AttemptCount)
        {
            throw new ArgumentException("Remote Planner request attempt does not match the authorized Need attempt.", nameof(request));
        }
    }
}

/// <summary>Immutable evidence for one admission projection and its newly reserved sessions.</summary>
public sealed class FormalRq1DispatchAdmissionResult
{
    private readonly ReadOnlyCollection<Rq1LogicalSessionDispatch> _reservedSessions;
    private readonly ReadOnlyCollection<Rq1LogicalSessionDispatch> _alreadyReservedSessions;

    internal FormalRq1DispatchAdmissionResult(
        Rq1DecisionNeedAdmissionResult projection,
        Rq1LogicalSessionDispatch[] reservedSessions,
        Rq1LogicalSessionDispatch[] alreadyReservedSessions)
    {
        Projection = projection;
        _reservedSessions = Array.AsReadOnly(reservedSessions);
        _alreadyReservedSessions = Array.AsReadOnly(alreadyReservedSessions);
    }

    public Rq1DecisionNeedAdmissionResult Projection { get; }
    public IReadOnlyList<Rq1LogicalSessionDispatch> ReservedSessions => _reservedSessions;
    public IReadOnlyList<Rq1LogicalSessionDispatch> AlreadyReservedSessions => _alreadyReservedSessions;
}

/// <summary>
/// Owns formal-RQ1 session-budget reservation, deterministic capacity dispatch, and retry terminalization.
/// It reserves sessions and authorizes transport attempts; the condition runtime attaches exact invocation work.
/// </summary>
public sealed class FormalRq1DispatchRuntime : IDisposable
{
    private readonly object _sync = new();
    private readonly DecisionNeedStore _store;
    private readonly List<Rq1LogicalSessionDispatch> _sessions = [];
    private readonly Dictionary<DecisionNeedId, Rq1LogicalSessionDispatch> _activeSessionByNeedId = [];
    private bool _disposed;
    private int _reservedBudget;
    private int _consumedBudget;
    private int _providerInFlight;

    public FormalRq1DispatchRuntime(
        DecisionNeedStore store,
        FormalRq1DispatchConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(configuration);
        _store = store;
        Configuration = configuration;
    }

    public FormalRq1DispatchConfiguration Configuration { get; }

    public int ReservedSessionBudget
    {
        get { lock (_sync) { return _reservedBudget; } }
    }

    public int ConsumedSessionBudget
    {
        get { lock (_sync) { return _consumedBudget; } }
    }

    public int RemainingSessionBudget
    {
        get { lock (_sync) { return RemainingSessionBudgetUnsafe(); } }
    }

    public int ProviderInFlight
    {
        get { lock (_sync) { return _providerInFlight; } }
    }

    public FormalRq1DispatchAdmissionResult Admit(
        IEnumerable<Rq1DecisionNeedAdmissionCandidate> candidates,
        FormalRq1Treatment treatment,
        SimTime now)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            Rq1DecisionNeedAdmissionResult validated = Rq1DecisionNeedAdmissionScheduler.Project(
                _store,
                candidates,
                treatment,
                now,
                Configuration.StarvationAgeTicks,
                RemainingSessionBudgetUnsafe());

            var alreadyReserved = new List<Rq1LogicalSessionDispatch>();
            var eligibleEntries = new List<Rq1DecisionNeedAdmissionEntry>();
            foreach (Rq1DecisionNeedAdmissionEntry entry in validated.OrderedEntries)
            {
                if (_activeSessionByNeedId.TryGetValue(entry.Need.NeedId, out Rq1LogicalSessionDispatch? existing))
                {
                    if (!ReferenceEquals(existing.Need, entry.Need))
                    {
                        throw new InvalidOperationException("An active reservation NeedId cannot identify a different DecisionNeed instance.");
                    }

                    alreadyReserved.Add(existing);
                }
                else
                {
                    eligibleEntries.Add(entry);
                }
            }

            int selectedCount = Math.Min(RemainingSessionBudgetUnsafe(), eligibleEntries.Count);
            Rq1DecisionNeedAdmissionEntry[] ordered = eligibleEntries.ToArray();
            var selected = new Rq1DecisionNeedAdmissionEntry[selectedCount];
            var missed = new Rq1DecisionNeedAdmissionEntry[ordered.Length - selectedCount];
            Array.Copy(ordered, selected, selectedCount);
            Array.Copy(ordered, selectedCount, missed, 0, missed.Length);
            var projection = new Rq1DecisionNeedAdmissionResult(ordered, selected, missed);

            var reserved = new Rq1LogicalSessionDispatch[projection.SelectedForAdmission.Count];
            for (int index = 0; index < reserved.Length; index++)
            {
                Rq1DecisionNeedAdmissionEntry entry = projection.SelectedForAdmission[index];
                var session = new Rq1LogicalSessionDispatch(this, entry);
                _sessions.Add(session);
                _activeSessionByNeedId.Add(entry.Need.NeedId, session);
                reserved[index] = session;
                _reservedBudget = checked(_reservedBudget + 1);
            }

            return new FormalRq1DispatchAdmissionResult(projection, reserved, alreadyReserved.ToArray());
        }
    }

    /// <summary>Starts ready attempts in admission order up to externally configured Provider capacity.</summary>
    public IReadOnlyList<Rq1LogicalSessionDispatch> DispatchReady(DateTimeOffset now)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var dispatched = new List<Rq1LogicalSessionDispatch>();
            foreach (Rq1LogicalSessionDispatch session in _sessions)
            {
                if (_providerInFlight >= Configuration.MaxProviderInFlight)
                {
                    break;
                }

                bool ready = session.State == Rq1LogicalSessionState.WaitingForProviderCapacity
                    || (session.State == Rq1LogicalSessionState.WaitingForRetry
                        && session.RetryNotBefore is DateTimeOffset retryAt
                        && retryAt <= now);
                if (!ready)
                {
                    continue;
                }

                session.State = Rq1LogicalSessionState.InFlightTransportAttempt;
                session.TransportAttemptCount = checked(session.TransportAttemptCount + 1);
                session.CurrentAttemptStartedAt = now;
                session.RetryNotBefore = null;
                _providerInFlight = checked(_providerInFlight + 1);
                dispatched.Add(session);
            }

            return Array.AsReadOnly(dispatched.ToArray());
        }
    }

    internal void CompleteAttemptWithResponseEnvelope(Rq1LogicalSessionDispatch session)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            ValidateOwnedInFlight(session);
            session.State = Rq1LogicalSessionState.ResponseEnvelopeReceived;
            session.CurrentAttemptStartedAt = null;
            _providerInFlight--;
            _reservedBudget--;
            _consumedBudget = checked(_consumedBudget + 1);
            ReleaseReservationIdentity(session);
        }
    }

    internal void CompleteResponseSettlement(Rq1LogicalSessionDispatch session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_sessions.Contains(session))
            {
                throw new ArgumentException("The logical session is not owned by this runtime.", nameof(session));
            }

            if (session.State != Rq1LogicalSessionState.ResponseEnvelopeReceived)
            {
                throw new InvalidOperationException("Only a response-envelope session may complete response settlement.");
            }

            session.ReleaseInvocation(clearRetainedBinding: true);
        }
    }

    internal void CompleteAttemptWithoutResponse(
        Rq1LogicalSessionDispatch session,
        DateTimeOffset completedAt,
        FormalRq1TransportFailureCode failureCode)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            ValidateOwnedInFlight(session);
            bool retryable = session.AllowsAutomaticRetry
                && Configuration.RetryClassificationPolicy.IsRetryable(failureCode);
            if (retryable && session.TransportAttemptCount < Configuration.MaxTransportAttempts)
            {
                TimeSpan backoff = Configuration.RetryBackoffs[session.TransportAttemptCount - 1];
                DateTimeOffset retryNotBefore = completedAt + backoff;
                _providerInFlight--;
                session.ReleaseInvocation(clearRetainedBinding: false);
                session.CurrentAttemptStartedAt = null;
                session.RetryNotBefore = retryNotBefore;
                session.State = Rq1LogicalSessionState.WaitingForRetry;
                return;
            }

            if (session.Need.State is DecisionNeedState.Created or DecisionNeedState.Queued or DecisionNeedState.InFlight)
            {
                session.Need.Abort();
            }

            _providerInFlight--;
            session.ReleaseInvocation(clearRetainedBinding: true);
            session.CurrentAttemptStartedAt = null;
            session.RetryNotBefore = null;
            session.State = Rq1LogicalSessionState.TransportOnlyAborted;
            _reservedBudget--;
            ReleaseReservationIdentity(session);
        }
    }

    internal void FailDispatchPreparation(Rq1LogicalSessionDispatch session)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            ValidateOwnedInFlight(session);
            if (session.Invocation is not null)
            {
                throw new InvalidOperationException("Attached Provider work cannot be reclassified as a preparation failure.");
            }

            if (session.Need.State == DecisionNeedState.InFlight)
            {
                session.Need.Abort();
            }

            _providerInFlight--;
            _reservedBudget--;
            session.ReleaseInvocation(clearRetainedBinding: true);
            ReleaseReservationIdentity(session);
            session.CurrentAttemptStartedAt = null;
            session.State = Rq1LogicalSessionState.DispatchPreparationFailed;
        }
    }

    private int RemainingSessionBudgetUnsafe()
    {
        return checked(Configuration.LogicalSessionBudget - _reservedBudget - _consumedBudget);
    }

    private void ValidateOwnedInFlight(Rq1LogicalSessionDispatch session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_sessions.Contains(session))
        {
            throw new ArgumentException("The logical session is not owned by this runtime.", nameof(session));
        }

        if (session.State != Rq1LogicalSessionState.InFlightTransportAttempt)
        {
            throw new InvalidOperationException("Only an in-flight transport attempt can be completed.");
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

            foreach (Rq1LogicalSessionDispatch session in _sessions)
            {
                session.ReleaseInvocation(clearRetainedBinding: true);
            }

            _activeSessionByNeedId.Clear();
            _disposed = true;
        }
    }

    private void ReleaseReservationIdentity(Rq1LogicalSessionDispatch session)
    {
        if (!_activeSessionByNeedId.Remove(session.Need.NeedId, out Rq1LogicalSessionDispatch? retained)
            || !ReferenceEquals(retained, session))
        {
            throw new InvalidOperationException("Logical-session reservation identity was not retained exactly once.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
