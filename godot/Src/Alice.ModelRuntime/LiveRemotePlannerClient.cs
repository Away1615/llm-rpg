using System.Diagnostics;
using System.Security.Cryptography;

namespace Alice.ModelRuntime;

public enum LiveRemoteTransportOutcome
{
    ResponseEnvelopeReceived,
    InvocationFailed
}

public enum LiveRemoteFailureKind
{
    Timeout,
    NetworkFailure,
    HttpFailure,
    ResponseBodyTooLarge,
    OutputTokenLimitReached,
    InvalidResponseEnvelope
}

public interface ILiveRemotePlannerExecutionEvidence
{
    LiveRemoteTransportOutcome Outcome { get; }
    LiveRemoteFailureKind? FailureKind { get; }
    int? HttpStatus { get; }
    bool ResponseEnvelopeReceived { get; }
    long DurationMilliseconds { get; }
    string? ResponseBodyHash { get; }
}

/// <summary>Formal-capable live transports expose only the sanitized, canonical call receipt.</summary>
public interface IFormalModelCallEvidenceCarrier
{
    FormalModelCallEvidence? FormalCallEvidence { get; }
}

/// <summary>Closed LiveRemote evidence without response body, tool arguments, credential or reasoning.</summary>
public sealed record LiveRemotePlannerExecutionEvidence : ModelClientExecutionEvidence,
    ILiveRemotePlannerExecutionEvidence
{
    private LiveRemotePlannerExecutionEvidence(
        OpenAiCompatibleProfileId profileId,
        OpenAiCompatibleModelId modelId,
        LiveRemoteTransportOutcome outcome,
        LiveRemoteFailureKind? failureKind,
        int? httpStatus,
        bool responseEnvelopeReceived,
        long durationMilliseconds,
        string? responseBodyHash)
        : base(ModelClientExecutionMode.LiveRemote)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(modelId);
        if (!Enum.IsDefined(outcome) || httpStatus is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        bool successStatus = httpStatus is >= 200 and <= 299;
        if (outcome == LiveRemoteTransportOutcome.ResponseEnvelopeReceived)
        {
            if (failureKind is not null || !successStatus || !responseEnvelopeReceived)
            {
                throw new ArgumentException("Received LiveRemote evidence is inconsistent.");
            }
        }
        else
        {
            if (failureKind is not LiveRemoteFailureKind failure || !Enum.IsDefined(failure) || responseEnvelopeReceived)
            {
                throw new ArgumentException("Failed LiveRemote evidence is inconsistent.");
            }

            if (failure == LiveRemoteFailureKind.HttpFailure && (httpStatus is null || successStatus) ||
                (failure is LiveRemoteFailureKind.ResponseBodyTooLarge
                    or LiveRemoteFailureKind.OutputTokenLimitReached
                    or LiveRemoteFailureKind.InvalidResponseEnvelope) && !successStatus ||
                failure is LiveRemoteFailureKind.Timeout or LiveRemoteFailureKind.NetworkFailure && httpStatus is not null && !successStatus)
            {
                throw new ArgumentException("LiveRemote failure kind and HTTP status are inconsistent.");
            }
        }

        ProfileId = profileId;
        ModelId = modelId;
        Outcome = outcome;
        FailureKind = failureKind;
        HttpStatus = httpStatus;
        ResponseEnvelopeReceived = responseEnvelopeReceived;
        DurationMilliseconds = durationMilliseconds;
        ResponseBodyHash = responseBodyHash;
    }

    public OpenAiCompatibleProfileId ProfileId { get; }
    public OpenAiCompatibleModelId ModelId { get; }
    public LiveRemoteTransportOutcome Outcome { get; }
    public LiveRemoteFailureKind? FailureKind { get; }
    public int? HttpStatus { get; }
    public bool ResponseEnvelopeReceived { get; }
    public long DurationMilliseconds { get; }
    public string? ResponseBodyHash { get; }

    internal static LiveRemotePlannerExecutionEvidence Received(
        OpenAiCompatibleProviderProfile profile,
        int httpStatus,
        long durationMilliseconds,
        string responseBodyHash)
    {
        return new LiveRemotePlannerExecutionEvidence(
            profile.ProfileId,
            profile.ModelId,
            LiveRemoteTransportOutcome.ResponseEnvelopeReceived,
            null,
            httpStatus,
            true,
            durationMilliseconds,
            responseBodyHash);
    }

    internal static LiveRemotePlannerExecutionEvidence Failed(
        OpenAiCompatibleProviderProfile profile,
        LiveRemoteFailureKind failureKind,
        int? httpStatus,
        long durationMilliseconds,
        string? responseBodyHash)
    {
        return new LiveRemotePlannerExecutionEvidence(
            profile.ProfileId,
            profile.ModelId,
            LiveRemoteTransportOutcome.InvocationFailed,
            failureKind,
            httpStatus,
            false,
            durationMilliseconds,
            responseBodyHash);
    }
}

/// <summary>One-attempt asynchronous Remote Planner LiveRemote HTTP transport.</summary>
public sealed class LiveRemotePlannerClient : IModelClient<RemotePlannerResponse>
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleProviderProfile _profile;
    private readonly ProviderApiKey _apiKey;

    public LiveRemotePlannerClient(
        HttpClient httpClient,
        OpenAiCompatibleProviderProfile profile,
        ProviderApiKey apiKey)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(apiKey);
        if (!profile.Capabilities.SupportsNativeTools ||
            !profile.Capabilities.SupportsStrictToolSchema ||
            profile.Capabilities.RequiresOpaqueReasoningReplay)
        {
            throw new ArgumentException(
                "LiveRemote Remote Planner requires strict native tools without opaque reasoning replay.",
                nameof(profile));
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(
                profile.ChatCompletionsEndpoint.Value.Scheme,
                Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "LiveRemote credentials require an HTTPS Chat Completions endpoint.",
                nameof(profile));
        }

        if (profile.CredentialReference is null ||
            profile.CredentialReference != apiKey.CredentialReference)
        {
            throw new ArgumentException(
                "LiveRemote credential source must exactly match the profile credential reference.",
                nameof(apiKey));
        }

        _httpClient = httpClient;
        _profile = profile.Snapshot();
        _apiKey = apiKey;
    }

    public async ValueTask<ModelClientResult<RemotePlannerResponse>> InvokeAsync(
        IModelRequest<RemotePlannerResponse> request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        if (request is not RemotePlannerRequest remoteRequest)
        {
            return ModelClientResult<RemotePlannerResponse>.Unavailable(
                ModelClientExecutionMode.LiveRemote,
                ModelClientUnavailableReason.UnsupportedRequestType);
        }

        using HttpRequestMessage httpRequest = OpenAiCompatibleChatCompletions.CreateRequest(
            _profile,
            remoteRequest,
            _apiKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_profile.Timeout);
        var stopwatch = Stopwatch.StartNew();
        int? responseStatus = null;
        try
        {
            using HttpResponseMessage httpResponse = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            responseStatus = (int)httpResponse.StatusCode;
            if (!httpResponse.IsSuccessStatusCode)
            {
                return InvocationFailure(
                    remoteRequest,
                    LiveRemoteFailureKind.HttpFailure,
                    responseStatus,
                    stopwatch.ElapsedMilliseconds,
                    null);
            }

            BoundedResponseBodyReadResult bodyRead = await OpenAiCompatibleChatCompletions.ReadResponseBodyAsync(
                httpResponse.Content,
                _profile.MaxResponseBodyBytes,
                timeout.Token).ConfigureAwait(false);
            if (!bodyRead.IsComplete || bodyRead.Body is null || bodyRead.RawBytes is null)
            {
                return InvocationFailure(
                    remoteRequest,
                    LiveRemoteFailureKind.ResponseBodyTooLarge,
                    responseStatus,
                    stopwatch.ElapsedMilliseconds,
                    null);
            }

            string responseBody = bodyRead.Body;
            string responseBodyHash = Hash(bodyRead.RawBytes);
            if (!OpenAiCompatibleChatCompletions.TryReadRemotePlannerToolCalls(responseBody, out IReadOnlyList<RemotePlannerToolCall>? calls) || calls is null)
            {
                return InvocationFailure(
                    remoteRequest,
                    LiveRemoteFailureKind.InvalidResponseEnvelope,
                    responseStatus,
                    stopwatch.ElapsedMilliseconds,
                    responseBodyHash);
            }

            RemotePlannerResponse response = RemotePlannerResponse.FromToolCalls(remoteRequest, calls);
            LiveRemotePlannerExecutionEvidence evidence =
                LiveRemotePlannerExecutionEvidence.Received(
                    _profile,
                    responseStatus.Value,
                    stopwatch.ElapsedMilliseconds,
                    responseBodyHash);
            return ModelClientResult<RemotePlannerResponse>.Produced(response, evidence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return InvocationFailure(
                remoteRequest,
                LiveRemoteFailureKind.Timeout,
                responseStatus,
                stopwatch.ElapsedMilliseconds,
                null);
        }
        catch (HttpRequestException)
        {
            return InvocationFailure(
                remoteRequest,
                LiveRemoteFailureKind.NetworkFailure,
                responseStatus,
                stopwatch.ElapsedMilliseconds,
                null);
        }
        catch (IOException)
        {
            return InvocationFailure(
                remoteRequest,
                LiveRemoteFailureKind.NetworkFailure,
                responseStatus,
                stopwatch.ElapsedMilliseconds,
                null);
        }
    }

    private ModelClientResult<RemotePlannerResponse> InvocationFailure(
        RemotePlannerRequest request,
        LiveRemoteFailureKind failureKind,
        int? httpStatus,
        long durationMilliseconds,
        string? responseBodyHash)
    {
        RemotePlannerResponse response = RemotePlannerResponse.InvocationFailed(request);
        LiveRemotePlannerExecutionEvidence evidence =
            LiveRemotePlannerExecutionEvidence.Failed(
                _profile,
                failureKind,
                httpStatus,
                durationMilliseconds,
                responseBodyHash);
        return ModelClientResult<RemotePlannerResponse>.Produced(response, evidence);
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
