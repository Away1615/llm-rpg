namespace Alice.ModelRuntime;

/// <summary>Typed request marker declaring the output produced by a model client.</summary>
public interface IModelRequest<TOutput> where TOutput : class
{
}

/// <summary>Transport-neutral typed invocation port.</summary>
public interface IModelClient<TOutput> where TOutput : class
{
    ValueTask<ModelClientResult<TOutput>> InvokeAsync(
        IModelRequest<TOutput> request,
        CancellationToken cancellationToken);
}

/// <summary>Optional transport policy consumed by outer queues; one-shot clients prohibit a second invocation.</summary>
public interface IAutomaticModelRetryPolicy
{
    bool AllowsAutomaticRetry { get; }
}

public enum ModelClientExecutionMode
{
    DeterministicTest,
    Live\u004cocal,
    LiveRemote
}

public enum ModelClientResultStatus
{
    Produced,
    Unavailable
}

public enum ModelClientUnavailableReason
{
    UnsupportedRequestType,
    MissingCredential
}

/// <summary>Closed execution evidence attached only to a produced typed output.</summary>
public abstract record ModelClientExecutionEvidence
{
    private protected ModelClientExecutionEvidence(ModelClientExecutionMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        Mode = mode;
    }

    public ModelClientExecutionMode Mode { get; }
}

/// <summary>Immutable produced-or-unavailable result with no open payload.</summary>
public sealed class ModelClientResult<TOutput> where TOutput : class
{
    private ModelClientResult(
        ModelClientResultStatus status,
        ModelClientExecutionMode mode,
        TOutput? output,
        ModelClientExecutionEvidence? executionEvidence,
        ModelClientUnavailableReason? unavailableReason)
    {
        if (!Enum.IsDefined(status) || !Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (status == ModelClientResultStatus.Produced)
        {
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(executionEvidence);
            if (unavailableReason is not null || executionEvidence.Mode != mode)
            {
                throw new ArgumentException("Produced model-client evidence is inconsistent.");
            }
        }
        else
        {
            if (output is not null || executionEvidence is not null || unavailableReason is not ModelClientUnavailableReason reason || !Enum.IsDefined(reason))
            {
                throw new ArgumentException("Unavailable model-client evidence is inconsistent.");
            }
        }

        Status = status;
        Mode = mode;
        Output = output;
        ExecutionEvidence = executionEvidence;
        UnavailableReason = unavailableReason;
    }

    public ModelClientResultStatus Status { get; }
    public ModelClientExecutionMode Mode { get; }
    public TOutput? Output { get; }
    public ModelClientExecutionEvidence? ExecutionEvidence { get; }
    public ModelClientUnavailableReason? UnavailableReason { get; }

    public static ModelClientResult<TOutput> Produced(
        TOutput output,
        ModelClientExecutionEvidence executionEvidence)
    {
        ArgumentNullException.ThrowIfNull(executionEvidence);
        return new ModelClientResult<TOutput>(
            ModelClientResultStatus.Produced,
            executionEvidence.Mode,
            output,
            executionEvidence,
            null);
    }

    public static ModelClientResult<TOutput> Unavailable(
        ModelClientExecutionMode mode,
        ModelClientUnavailableReason reason)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        return new ModelClientResult<TOutput>(
            ModelClientResultStatus.Unavailable,
            mode,
            null,
            null,
            reason);
    }
}

/// <summary>A typed unavailable client used when an explicitly selected execution mode cannot be composed.</summary>
public sealed class FixedUnavailableModelClient<TOutput> : IModelClient<TOutput> where TOutput : class
{
    public FixedUnavailableModelClient(ModelClientExecutionMode mode, ModelClientUnavailableReason reason)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        Mode = mode;
        Reason = reason;
    }

    public ModelClientExecutionMode Mode { get; }
    public ModelClientUnavailableReason Reason { get; }

    public ValueTask<ModelClientResult<TOutput>> InvokeAsync(
        IModelRequest<TOutput> request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        return ValueTask.FromResult(ModelClientResult<TOutput>.Unavailable(Mode, Reason));
    }
}
