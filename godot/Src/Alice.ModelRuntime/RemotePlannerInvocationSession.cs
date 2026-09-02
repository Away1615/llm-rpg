using Alice.Activities;
using Alice.Authority;
using Alice.Cognition;
using Alice.Npc;
using Alice.Social;

namespace Alice.ModelRuntime;

/// <summary>Host-owned lifecycle for one Remote Planner operation and one explicit later settlement.</summary>
public sealed partial class RemotePlannerInvocationSession : IDisposable
{
    private const int Undecided = 0;
    private const int CancellationDecision = 1;
    private const int SettlementDecision = 2;
    private readonly object _sync = new();
    private readonly RemotePlannerRequest _request;
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _callerCancellationRegistration;
    private readonly Task<ModelClientResult<RemotePlannerResponse>> _operation;
    private RemotePlannerInvocationResult? _terminalResult;
    private int _terminalDecision;
    private bool _disposed;

    private RemotePlannerInvocationSession(
        IModelClient<RemotePlannerResponse> client,
        RemotePlannerRequest request,
        CancellationToken callerCancellation)
    {
        _request = request;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
        try
        {
            _operation = client.InvokeAsync(request, _cancellation.Token).AsTask();
        }
        catch (Exception exception)
        {
            _operation = Task.FromException<ModelClientResult<RemotePlannerResponse>>(exception);
        }

        _callerCancellationRegistration = callerCancellation.Register(ClaimCancellation);
    }

    public RemotePlannerRequest Request => _request;
    public RemotePlannerRequestBinding Binding => _request.Binding;

    public RemotePlannerInvocationResult Current
    {
        get
        {
            lock (_sync)
            {
                ObserveCancellation();
                return _terminalResult ?? RemotePlannerInvocationResult.InFlight();
            }
        }
    }

    public static RemotePlannerInvocationSession Start(
        IModelClient<RemotePlannerResponse> client,
        RemotePlannerRequest request,
        CancellationToken callerCancellation)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return new RemotePlannerInvocationSession(client, request, callerCancellation);
    }

    public ValueTask<RemotePlannerInvocationResult> PollAndSettleAsync(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorCognitionView view,
        L2PlanningContext context,
        NpcPlan currentPlan,
        SimTime resolvedAt)
    {
        lock (_sync)
        {
            if (_terminalResult is not null)
            {
                return ValueTask.FromResult(_terminalResult);
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(need);
            ArgumentNullException.ThrowIfNull(view);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(currentPlan);
            ObserveCancellation();
            if (_terminalResult is not null)
            {
                return ValueTask.FromResult(_terminalResult);
            }

            if (!_operation.IsCompleted)
            {
                return ValueTask.FromResult(RemotePlannerInvocationResult.InFlight());
            }

            if (!TryClaimSettlement())
            {
                return ValueTask.FromResult(StoreTerminal(RemotePlannerInvocationResult.Cancelled()));
            }

            ModelClientResult<RemotePlannerResponse> clientResult;
            try
            {
                clientResult = _operation.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                return ValueTask.FromResult(StoreTerminal(RemotePlannerInvocationResult.ClientFaulted()));
            }

            if (clientResult.Status == ModelClientResultStatus.Unavailable)
            {
                return ValueTask.FromResult(StoreTerminal(RemotePlannerInvocationResult.Unavailable(
                    clientResult.Mode,
                    clientResult.UnavailableReason!.Value)));
            }

            RemotePlannerHostSettlementOutcome settlement = RemotePlannerHostSettlement.Settle(
                store,
                need,
                view,
                context,
                _request,
                clientResult.Output!,
                currentPlan,
                resolvedAt);
            return ValueTask.FromResult(StoreTerminal(RemotePlannerInvocationResult.Settled(
                clientResult.Mode,
                clientResult.ExecutionEvidence!,
                settlement,
                _request.Binding)));
        }
    }

    public async ValueTask WaitForTransportAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<RemotePlannerInvocationResult> PollAndSettleInviteResponseAsync(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorDecisionView view,
        L2InviteResponseContext context,
        RoutineSemanticResponseContext responseContext,
        InvitationAcceptanceAuthorityRuntime invitationAcceptanceAuthority,
        SimTime resolvedAt)
    {
        lock (_sync)
        {
            if (_terminalResult is not null)
            {
                return ValueTask.FromResult(_terminalResult);
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(need);
            ArgumentNullException.ThrowIfNull(view);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(responseContext);
            ArgumentNullException.ThrowIfNull(invitationAcceptanceAuthority);
            ObserveCancellation();
            if (_terminalResult is not null)
            {
                return ValueTask.FromResult(_terminalResult);
            }

            if (!_operation.IsCompleted)
            {
                return ValueTask.FromResult(RemotePlannerInvocationResult.InFlight());
            }

            if (!TryClaimSettlement())
            {
                return ValueTask.FromResult(StoreTerminal(RemotePlannerInvocationResult.Cancelled()));
            }

            ModelClientResult<RemotePlannerResponse> clientResult;
            try
            {
                clientResult = _operation.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                return ValueTask.FromResult(StoreTerminal(RemotePlannerInvocationResult.ClientFaulted()));
            }

            if (clientResult.Status == ModelClientResultStatus.Unavailable)
            {
                return ValueTask.FromResult(StoreTerminal(RemotePlannerInvocationResult.Unavailable(
                    clientResult.Mode,
                    clientResult.UnavailableReason!.Value)));
            }

            RemotePlannerHostSettlementOutcome settlement = RemotePlannerHostSettlement.SettleInviteResponse(
                store,
                need,
                view,
                context,
                responseContext,
                _request,
                clientResult.Output!,
                invitationAcceptanceAuthority,
                resolvedAt);
            return ValueTask.FromResult(StoreTerminal(RemotePlannerInvocationResult.Settled(
                clientResult.Mode,
                clientResult.ExecutionEvidence!,
                settlement,
                _request.Binding)));
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            if (_terminalResult is not null)
            {
                return;
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
            ClaimCancellationDecision();
            _terminalResult = RemotePlannerInvocationResult.Cancelled();
            _cancellation.Cancel();
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

            if (_terminalResult is null)
            {
                ClaimCancellationDecision();
                _terminalResult = RemotePlannerInvocationResult.Cancelled();
                _cancellation.Cancel();
            }

            _disposed = true;
            _callerCancellationRegistration.Dispose();
            _cancellation.Dispose();
        }
    }

    private bool TryClaimSettlement()
    {
        return Interlocked.CompareExchange(
            ref _terminalDecision,
            SettlementDecision,
            Undecided) == Undecided;
    }

    private void ObserveCancellation()
    {
        if (_cancellation.IsCancellationRequested)
        {
            ClaimCancellationDecision();
            if (Volatile.Read(ref _terminalDecision) == CancellationDecision)
            {
                _terminalResult ??= RemotePlannerInvocationResult.Cancelled();
            }
        }
    }

    private void ClaimCancellation()
    {
        ClaimCancellationDecision();
    }

    private void ClaimCancellationDecision()
    {
        _ = Interlocked.CompareExchange(
            ref _terminalDecision,
            CancellationDecision,
            Undecided);
    }

    private RemotePlannerInvocationResult StoreTerminal(RemotePlannerInvocationResult result)
    {
        _terminalResult = result;
        return result;
    }
}
