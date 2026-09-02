using Alice.Actors;
using Alice.Cognition;
using Alice.Npc;

namespace Alice.ModelRuntime;

/// <summary>Host-owned lifecycle for one invocation and one explicit later admission.</summary>
public sealed class LocalReasonerInvocationSession : IDisposable
{
    private const int Undecided = 0;
    private const int CancellationDecision = 1;
    private const int SettlementDecision = 2;
    private readonly object _sync = new();
    private readonly LocalReasonerRequest _request;
    private readonly LocalReasonerPendingRequest _pendingRequest;
    private readonly Cancellation\u0054oken _callerCancellation;
    private readonly Cancellation\u0054okenSource _cancellation;
    private readonly Cancellation\u0054okenRegistration _callerCancellationRegistration;
    private readonly Task <ModelClientResult<LocalReasonerResponse>> _operation;
    private LocalReasonerInvocationResult? _terminalResult;
    private int _settlementDecision;
    private bool _disposed;

    private LocalReasonerInvocationSession(
        IModelClient<LocalReasonerResponse> client,
        LocalReasonerRequest request,
        Cancellation\u0054oken callerCancellation)
    {
        _request = request;
        _pendingRequest = new LocalReasonerPendingRequest(request);
        _callerCancellation = callerCancellation;
        _cancellation = Cancellation\u0054okenSource.CreateLinkedTokenSource(callerCancellation);
        _callerCancellationRegistration = callerCancellation.Register(ClaimCancellation);
        try
        {
            _operation = client.InvokeAsync(request, _cancellation.Token).AsTask();
        }
        catch (Exception exception)
        {
            _operation = Task.FromException<ModelClientResult<LocalReasonerResponse>>(exception);
        }
    }

    public LocalReasonerRequest Request => _request;
    public LocalReasonerRequestBinding Binding => _request.Binding;

    public LocalReasonerInvocationResult Current
    {
        get
        {
            lock (_sync)
            {
                return _terminalResult ?? CurrentNonTerminal();
            }
        }
    }

    public static LocalReasonerInvocationSession Start(
        IModelClient<LocalReasonerResponse> client,
        LocalReasonerRequest request,
        Cancellation\u0054oken callerCancellation)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return new LocalReasonerInvocationSession(client, request, callerCancellation);
    }

    public ValueTask <LocalReasonerInvocationResult> PollAndAdmitAsync(
        SharedActorState actorState,
        NpcState npcState,
        PlanRuntime planRuntime,
        DecisionGateDecision decision)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(actorState);
            ArgumentNullException.ThrowIfNull(npcState);
            ArgumentNullException.ThrowIfNull(planRuntime);
            ArgumentNullException.ThrowIfNull(decision);
            if (_terminalResult is not null)
            {
                return ValueTask.FromResult(_terminalResult);
            }

            if (!_operation.IsCompleted)
            {
                return ValueTask.FromResult(CurrentNonTerminal());
            }

            _terminalResult = SettleCompletedOperation(actorState, npcState, planRuntime, decision);
            return ValueTask.FromResult(_terminalResult);
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_terminalResult is null && !_cancellation.IsCancellationRequested)
            {
                ClaimCancellation();
                _cancellation.Cancel();
            }
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

            if (_terminalResult is null && !_cancellation.IsCancellationRequested)
            {
                ClaimCancellation();
                _cancellation.Cancel();
            }

            _disposed = true;
            _callerCancellationRegistration.Dispose();
            _cancellation.Dispose();
        }
    }

    private LocalReasonerInvocationResult SettleCompletedOperation(
        SharedActorState actorState,
        NpcState npcState,
        PlanRuntime planRuntime,
        DecisionGateDecision decision)
    {
        if (CancellationWon())
        {
            return StoreTerminal(LocalReasonerInvocationResult.Cancelled());
        }

        ModelClientResult<LocalReasonerResponse> clientResult;
        try
        {
            clientResult = _operation.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return StoreTerminal(TryClaimSettlement()
                ? LocalReasonerInvocationResult.ClientFaulted()
                : LocalReasonerInvocationResult.Cancelled());
        }

        if (!TryClaimSettlement())
        {
            return StoreTerminal(LocalReasonerInvocationResult.Cancelled());
        }

        if (clientResult.Status == ModelClientResultStatus.Unavailable)
        {
            return StoreTerminal(LocalReasonerInvocationResult.Unavailable(
                clientResult.Mode,
                clientResult.UnavailableReason!.Value));
        }

        LocalReasonerAdmissionResult admission = _pendingRequest.Admit(
            clientResult.Output!,
            actorState,
            npcState,
            planRuntime,
            decision);
        return StoreTerminal(LocalReasonerInvocationResult.FromAdmission(
            clientResult.Mode,
            clientResult.ExecutionEvidence!,
            admission));
    }

    private LocalReasonerInvocationResult StoreTerminal(LocalReasonerInvocationResult result)
    {
        _terminalResult = result;
        return result;
    }

    private LocalReasonerInvocationResult CurrentNonTerminal()
    {
        return CancellationWon()
            ? LocalReasonerInvocationResult.CancellationPending()
            : LocalReasonerInvocationResult.InFlight();
    }

    private bool TryClaimSettlement()
    {
        if (CancellationWon())
        {
            return false;
        }

        return Interlocked.CompareExchange(
            ref _settlementDecision,
            SettlementDecision,
            Undecided) != CancellationDecision;
    }

    private bool CancellationWon()
    {
        if (_callerCancellation.IsCancellationRequested || _cancellation.IsCancellationRequested)
        {
            ClaimCancellation();
        }

        return Interlocked.CompareExchange(
            ref _settlementDecision,
            Undecided,
            Undecided) == CancellationDecision;
    }

    private void ClaimCancellation()
    {
        _ = Interlocked.CompareExchange(
            ref _settlementDecision,
            CancellationDecision,
            Undecided);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
