using System.Net;

namespace Alice.ModelRuntime;

public enum LiveLocalTransportOutcome
{
    ContentReceived,
    InvocationFailed
}

public enum LiveLocalFailureKind
{
    Timeout,
    NetworkFailure,
    HttpFailure,
    ResponseBodyTooLarge,
    InvalidResponseEnvelope
}

/// <summary>Closed LiveLocal transport evidence without body, endpoint or Host correlation data.</summary>
public sealed record LiveLocalExecutionEvidence : ModelClientExecutionEvidence
{
    private LiveLocalExecutionEvidence(
        OpenAiCompatibleProfileId profileId,
        OpenAiCompatibleModelId modelId,
        LiveLocalTransportOutcome outcome,
        LiveLocalFailureKind? failureKind,
        int? httpStatus)
        : base(ModelClientExecutionMode.LiveLocal)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(modelId);
        if (!Enum.IsDefined(outcome) || httpStatus is < 100 or > 599)
            throw new ArgumentOutOfRangeException(nameof(outcome));

        bool successStatus = httpStatus is >= 200 and <= 299;
        if (outcome == LiveLocalTransportOutcome.ContentReceived)
        {
            if (failureKind is not null || !successStatus)
                throw new ArgumentException("Content-received LiveLocal evidence requires one success status and no failure kind.");
        }
        else
        {
            if (failureKind is not LiveLocalFailureKind failure || !Enum.IsDefined(failure))
                throw new ArgumentException("Invocation-failed LiveLocal evidence requires one defined failure kind.");

            if (failure == LiveLocalFailureKind.HttpFailure && (httpStatus is null || successStatus)
                || (failure is LiveLocalFailureKind.ResponseBodyTooLarge or LiveLocalFailureKind.InvalidResponseEnvelope)
                && !successStatus
                || failure is LiveLocalFailureKind.Timeout or LiveLocalFailureKind.NetworkFailure
                && httpStatus is not null && !successStatus)
                throw new ArgumentException("LiveLocal failure kind and HTTP status are inconsistent.");
        }

        ProfileId = profileId;
        ModelId = modelId;
        Outcome = outcome;
        FailureKind = failureKind;
        HttpStatus = httpStatus;
    }

    public OpenAiCompatibleProfileId ProfileId { get; }
    public OpenAiCompatibleModelId ModelId { get; }
    public LiveLocalTransportOutcome Outcome { get; }
    public LiveLocalFailureKind? FailureKind { get; }
    public int? HttpStatus { get; }

    internal static LiveLocalExecutionEvidence ContentReceived(
        OpenAiCompatibleProviderProfile profile,
        HttpStatusCode status) => new(
            profile.ProfileId,
            profile.ModelId,
            LiveLocalTransportOutcome.ContentReceived,
            null,
            (int)status);

    internal static LiveLocalExecutionEvidence InvocationFailed(
        OpenAiCompatibleProviderProfile profile,
        LiveLocalFailureKind failureKind,
        int? httpStatus) => new(
            profile.ProfileId,
            profile.ModelId,
            LiveLocalTransportOutcome.InvocationFailed,
            failureKind,
            httpStatus);
}
