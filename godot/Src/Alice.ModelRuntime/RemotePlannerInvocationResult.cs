using Alice.Cognition;

namespace Alice.ModelRuntime;

public enum RemotePlannerInvocationState
{
    InFlight,
    Settled,
    Unavailable,
    Cancelled,
    ClientFaulted
}

/// <summary>Closed Host snapshot for one Remote Planner invocation and explicit settlement.</summary>
public sealed class RemotePlannerInvocationResult
{
    private RemotePlannerInvocationResult(
        RemotePlannerInvocationState state,
        ModelClientExecutionMode? mode,
        ModelClientExecutionEvidence? executionEvidence,
        ModelClientUnavailableReason? unavailableReason,
        RemotePlannerHostSettlementOutcome? settlement,
        RemotePlannerRequestBinding? requestBinding)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        bool hasProducedEvidence = mode is ModelClientExecutionMode producedMode &&
            executionEvidence is not null &&
            executionEvidence.Mode == producedMode;
        bool hasUnavailableEvidence = mode is ModelClientExecutionMode unavailableMode &&
            Enum.IsDefined(unavailableMode) &&
            unavailableReason is ModelClientUnavailableReason reason &&
            Enum.IsDefined(reason);
        bool valid = state switch
        {
            RemotePlannerInvocationState.InFlight or
            RemotePlannerInvocationState.Cancelled or
            RemotePlannerInvocationState.ClientFaulted =>
                mode is null && executionEvidence is null && unavailableReason is null && settlement is null && requestBinding is null,
            RemotePlannerInvocationState.Settled =>
                hasProducedEvidence && unavailableReason is null && settlement is not null && requestBinding is not null,
            RemotePlannerInvocationState.Unavailable =>
                hasUnavailableEvidence && executionEvidence is null && settlement is null && requestBinding is null,
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException("Remote Planner invocation snapshot evidence is inconsistent.");
        }

        State = state;
        Mode = mode;
        ExecutionEvidence = executionEvidence;
        UnavailableReason = unavailableReason;
        Settlement = settlement;
        RequestBinding = requestBinding;
    }

    public RemotePlannerInvocationState State { get; }
    public ModelClientExecutionMode? Mode { get; }
    public ModelClientExecutionEvidence? ExecutionEvidence { get; }
    public ModelClientUnavailableReason? UnavailableReason { get; }
    public RemotePlannerHostSettlementOutcome? Settlement { get; }
    public RemotePlannerRequestBinding? RequestBinding { get; }
    public bool IsTerminal => State != RemotePlannerInvocationState.InFlight;

    internal static RemotePlannerInvocationResult InFlight()
    {
        return new RemotePlannerInvocationResult(
            RemotePlannerInvocationState.InFlight,
            null,
            null,
            null,
            null,
            null);
    }

    internal static RemotePlannerInvocationResult Settled(
        ModelClientExecutionMode mode,
        ModelClientExecutionEvidence executionEvidence,
        RemotePlannerHostSettlementOutcome settlement,
        RemotePlannerRequestBinding requestBinding)
    {
        return new RemotePlannerInvocationResult(
            RemotePlannerInvocationState.Settled,
            mode,
            executionEvidence,
            null,
            settlement,
            requestBinding);
    }

    internal static RemotePlannerInvocationResult Unavailable(
        ModelClientExecutionMode mode,
        ModelClientUnavailableReason reason)
    {
        return new RemotePlannerInvocationResult(
            RemotePlannerInvocationState.Unavailable,
            mode,
            null,
            reason,
            null,
            null);
    }

    internal static RemotePlannerInvocationResult Cancelled()
    {
        return new RemotePlannerInvocationResult(
            RemotePlannerInvocationState.Cancelled,
            null,
            null,
            null,
            null,
            null);
    }

    internal static RemotePlannerInvocationResult ClientFaulted()
    {
        return new RemotePlannerInvocationResult(
            RemotePlannerInvocationState.ClientFaulted,
            null,
            null,
            null,
            null,
            null);
    }
}
