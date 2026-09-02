namespace Alice.ModelRuntime;

/// <summary>
/// Receives one diagnostic record for every physical Anthropic Messages HTTP attempt.
/// The record intentionally excludes credentials and headers, but preserves the exact
/// JSON request and Provider response bodies used by the formal experiment.
/// </summary>
public interface IAnthropicMessagesProviderAttemptSink
{
    void Record(AnthropicMessagesProviderAttemptTrace trace);
}

public sealed class AnthropicMessagesProviderAttemptTrace
{
    private readonly byte[] _requestBody;
    private readonly byte[]? _responseBody;

    internal AnthropicMessagesProviderAttemptTrace(
        string requestId,
        LiveRemoteTransportOutcome outcome,
        LiveRemoteFailureKind? failureKind,
        int? httpStatus,
        long durationMilliseconds,
        string? providerResponseId,
        long? inputTokens,
        long? outputTokens,
        long? cacheCreationInputTokens,
        long? cacheReadInputTokens,
        long? reasoningTokens,
        ReadOnlySpan<byte> requestBody,
        byte[]? responseBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        if (durationMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));
        RequestId = requestId;
        Outcome = outcome;
        FailureKind = failureKind;
        HttpStatus = httpStatus;
        DurationMilliseconds = durationMilliseconds;
        ProviderResponseId = providerResponseId;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CacheCreationInputTokens = cacheCreationInputTokens;
        CacheReadInputTokens = cacheReadInputTokens;
        ReasoningTokens = reasoningTokens;
        _requestBody = requestBody.ToArray();
        _responseBody = responseBody?.ToArray();
    }

    public string RequestId { get; }
    public LiveRemoteTransportOutcome Outcome { get; }
    public LiveRemoteFailureKind? FailureKind { get; }
    public int? HttpStatus { get; }
    public long DurationMilliseconds { get; }
    public string? ProviderResponseId { get; }
    public long? InputTokens { get; }
    public long? OutputTokens { get; }
    public long? CacheCreationInputTokens { get; }
    public long? CacheReadInputTokens { get; }
    public long? ReasoningTokens { get; }

    public byte[] GetRequestBodyBytes() => _requestBody.ToArray();

    public byte[]? GetResponseBodyBytes() => _responseBody?.ToArray();
}
