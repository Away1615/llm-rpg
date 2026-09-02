namespace Alice.ModelRuntime;

public enum LocalReasonerInvocationState
{
    InFlight,
    CancellationPending,
    Admitted,
    Stale,
    Unavailable,
    Cancelled,
    ClientFaulted
}

/// <summary>Closed Host snapshot for one LocalReasoner invocation and later admission.</summary>
public sealed class LocalReasonerInvocationResult
{
    private LocalReasonerInvocationResult(
        LocalReasonerInvocationState state,
        ModelClientExecutionMode? mode,
        ModelClientExecutionEvidence? executionEvidence,
        ModelClientUnavailableReason? unavailableReason,
        LocalReasonerAdmissionResult? admission)
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
            LocalReasonerInvocationState.InFlight or
            LocalReasonerInvocationState.CancellationPending or
            LocalReasonerInvocationState.Cancelled or
            LocalReasonerInvocationState.ClientFaulted =>
                mode is null && executionEvidence is null && unavailableReason is null && admission is null,
            LocalReasonerInvocationState.Admitted =>
                hasProducedEvidence && unavailableReason is null &&
                admission is { Status: LocalReasonerAdmissionStatus.Accepted, Resolution: not null },
            LocalReasonerInvocationState.Stale =>
                hasProducedEvidence && unavailableReason is null &&
                admission is { Status: LocalReasonerAdmissionStatus.Stale, Resolution: null },
            LocalReasonerInvocationState.Unavailable =>
                hasUnavailableEvidence && executionEvidence is null && admission is null,
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException("LocalReasoner invocation snapshot evidence is inconsistent.");
        }

        State = state;
        Mode = mode;
        ExecutionEvidence = executionEvidence;
        UnavailableReason = unavailableReason;
        Admission = admission;
    }

    public LocalReasonerInvocationState State { get; }
    public ModelClientExecutionMode? Mode { get; }
    public ModelClientExecutionEvidence? ExecutionEvidence { get; }
    public ModelClientUnavailableReason? UnavailableReason { get; }
    public LocalReasonerAdmissionResult? Admission { get; }
    public bool IsTerminal => State is not (
        LocalReasonerInvocationState.InFlight or
        LocalReasonerInvocationState.CancellationPending);

    internal static LocalReasonerInvocationResult InFlight()
    {
        return new LocalReasonerInvocationResult(
            LocalReasonerInvocationState.InFlight,
            null,
            null,
            null,
            null);
    }

    internal static LocalReasonerInvocationResult CancellationPending()
    {
        return new LocalReasonerInvocationResult(
            LocalReasonerInvocationState.CancellationPending,
            null,
            null,
            null,
            null);
    }

    internal static LocalReasonerInvocationResult FromAdmission(
        ModelClientExecutionMode mode,
        ModelClientExecutionEvidence executionEvidence,
        LocalReasonerAdmissionResult admission)
    {
        ArgumentNullException.ThrowIfNull(executionEvidence);
        ArgumentNullException.ThrowIfNull(admission);
        LocalReasonerInvocationState state = admission.Status switch
        {
            LocalReasonerAdmissionStatus.Accepted => LocalReasonerInvocationState.Admitted,
            LocalReasonerAdmissionStatus.Stale => LocalReasonerInvocationState.Stale,
            _ => throw new ArgumentException("A session cannot expose duplicate admission.", nameof(admission))
        };
        return new LocalReasonerInvocationResult(state, mode, executionEvidence, null, admission);
    }

    internal static LocalReasonerInvocationResult Unavailable(
        ModelClientExecutionMode mode,
        ModelClientUnavailableReason reason)
    {
        return new LocalReasonerInvocationResult(
            LocalReasonerInvocationState.Unavailable,
            mode,
            null,
            reason,
            null);
    }

    internal static LocalReasonerInvocationResult Cancelled()
    {
        return new LocalReasonerInvocationResult(
            LocalReasonerInvocationState.Cancelled,
            null,
            null,
            null,
            null);
    }

    internal static LocalReasonerInvocationResult ClientFaulted()
    {
        return new LocalReasonerInvocationResult(
            LocalReasonerInvocationState.ClientFaulted,
            null,
            null,
            null,
            null);
    }
}
