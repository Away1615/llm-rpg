using System.Net.Http.Headers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Alice.ModelRuntime;

public sealed record AnthropicMessagesRemotePlannerExecutionEvidence : ModelClientExecutionEvidence,
    ILiveRemotePlannerExecutionEvidence,
    IFormalModelCallEvidenceCarrier
{
    private AnthropicMessagesRemotePlannerExecutionEvidence(
        AnthropicMessagesProfileId profileId,
        AnthropicMessagesModelId modelId,
        LiveRemoteTransportOutcome outcome,
        LiveRemoteFailureKind? failureKind,
        int? httpStatus,
        bool responseEnvelopeReceived,
        long durationMilliseconds,
        string? responseBodyHash,
        FormalModelCallEvidence? formalCallEvidence)
        : base(ModelClientExecutionMode.LiveRemote)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(modelId);
        ProfileId = profileId;
        ModelId = modelId;
        Outcome = outcome;
        FailureKind = failureKind;
        HttpStatus = httpStatus;
        ResponseEnvelopeReceived = responseEnvelopeReceived;
        DurationMilliseconds = durationMilliseconds;
        ResponseBodyHash = responseBodyHash;
        FormalCallEvidence = formalCallEvidence;
    }

    public AnthropicMessagesProfileId ProfileId { get; }
    public AnthropicMessagesModelId ModelId { get; }
    public LiveRemoteTransportOutcome Outcome { get; }
    public LiveRemoteFailureKind? FailureKind { get; }
    public int? HttpStatus { get; }
    public bool ResponseEnvelopeReceived { get; }
    public long DurationMilliseconds { get; }
    public string? ResponseBodyHash { get; }
    public FormalModelCallEvidence? FormalCallEvidence { get; }

    internal static AnthropicMessagesRemotePlannerExecutionEvidence Received(
        AnthropicMessagesProviderProfile profile,
        int httpStatus,
        long durationMilliseconds,
        string responseBodyHash,
        FormalModelCallEvidence formalCallEvidence) => new(
            profile.ProfileId,
            profile.ModelId,
            LiveRemoteTransportOutcome.ResponseEnvelopeReceived,
            null,
            httpStatus,
            true,
            durationMilliseconds,
            responseBodyHash,
            formalCallEvidence);

    internal static AnthropicMessagesRemotePlannerExecutionEvidence Failed(
        AnthropicMessagesProviderProfile profile,
        LiveRemoteFailureKind failureKind,
        int? httpStatus,
        long durationMilliseconds,
        string? responseBodyHash) => new(
            profile.ProfileId,
            profile.ModelId,
            LiveRemoteTransportOutcome.InvocationFailed,
            failureKind,
            httpStatus,
            false,
            durationMilliseconds,
            responseBodyHash,
            null);
}

internal sealed record AnthropicMessagesResponseMetadata(
    string? ResponseId,
    long? InputTokens,
    long? OutputTokens,
    long? CacheCreationInputTokens,
    long? CacheReadInputTokens,
    long? ReasoningTokens);

internal static class AnthropicMessagesRemotePlannerProtocol
{
    public const string ProtocolVersion = "alice.deepseek-anthropic-remote-planner.v1";
    private const string AnthropicVersion = "2023-06-01";

    public static HttpRequestMessage CreateRequest(
        AnthropicMessagesProviderProfile profile,
        RemotePlannerRequest request,
        ProviderApiKey apiKey)
    {
        byte[] body = GetRequestBody(profile, request);
        return CreateRequest(profile, apiKey, body);
    }

    public static HttpRequestMessage CreateRequest(
        AnthropicMessagesProviderProfile profile,
        ProviderApiKey apiKey,
        ReadOnlySpan<byte> requestBody)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, profile.MessagesEndpoint);
        var content = new ByteArrayContent(requestBody.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        message.Content = content;
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Add("anthropic-version", AnthropicVersion);
        apiKey.ApplyAnthropicCredential(message);
        return message;
    }

    public static bool TryReadToolCalls(
        string responseBody,
        out IReadOnlyList<RemotePlannerToolCall>? toolCalls)
    {
        return TryReadResponse(responseBody, out toolCalls, out _);
    }

    public static bool TryReadResponse(
        string responseBody,
        out IReadOnlyList<RemotePlannerToolCall>? toolCalls,
        out AnthropicMessagesResponseMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(responseBody);
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("stop_reason", out JsonElement stopReason)
                || stopReason.ValueKind != JsonValueKind.String
                || !StringComparer.Ordinal.Equals(stopReason.GetString(), "tool_use")
                || !root.TryGetProperty("content", out JsonElement content)
                || content.ValueKind != JsonValueKind.Array)
            {
                toolCalls = null;
                metadata = null;
                return false;
            }

            var parsed = new List<RemotePlannerToolCall>();
            foreach (JsonElement block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object
                    || !block.TryGetProperty("type", out JsonElement type)
                    || type.ValueKind != JsonValueKind.String)
                {
                    toolCalls = null;
                    metadata = null;
                    return false;
                }

                string? blockType = type.GetString();
                if (StringComparer.Ordinal.Equals(blockType, "thinking")
                    || StringComparer.Ordinal.Equals(blockType, "text"))
                {
                    continue;
                }

                if (!StringComparer.Ordinal.Equals(blockType, "tool_use")
                    || !block.TryGetProperty("name", out JsonElement name)
                    || name.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(name.GetString())
                    || !block.TryGetProperty("input", out JsonElement input)
                    || input.ValueKind != JsonValueKind.Object)
                {
                    toolCalls = null;
                    metadata = null;
                    return false;
                }

                parsed.Add(new RemotePlannerToolCall(name.GetString()!, input.GetRawText()));
            }

            if (parsed.Count != 1)
            {
                toolCalls = null;
                metadata = null;
                return false;
            }

            toolCalls = parsed.AsReadOnly();
            metadata = ReadMetadata(root);
            return true;
        }
        catch (JsonException)
        {
            toolCalls = null;
            metadata = null;
            return false;
        }
    }

    public static AnthropicMessagesResponseMetadata? TryReadMetadata(string responseBody)
    {
        ArgumentNullException.ThrowIfNull(responseBody);
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? ReadMetadata(document.RootElement)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsOutputTokenLimitReached(string responseBody)
    {
        ArgumentNullException.ThrowIfNull(responseBody);
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("stop_reason", out JsonElement stopReason)
                && stopReason.ValueKind == JsonValueKind.String
                && StringComparer.Ordinal.Equals(stopReason.GetString(), "max_tokens");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static byte[] GetRequestBody(
        AnthropicMessagesProviderProfile profile,
        RemotePlannerRequest request)
    {
        using var buffer = new MemoryStream();
        using JsonDocument sourceTools = JsonDocument.Parse(request.GetToolCatalogueUtf8());
        string userContent = new UTF8Encoding(false, true).GetString(request.GetModelVisibleBytes());
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", profile.ModelId.Value);
            writer.WriteNumber("max_tokens", profile.MaxTokens);
            writer.WriteString("system", request.SystemPrompt);
            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", userContent);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WritePropertyName("tools");
            writer.WriteStartArray();
            foreach (JsonElement sourceTool in sourceTools.RootElement.EnumerateArray())
            {
                JsonElement function = sourceTool.GetProperty("function");
                writer.WriteStartObject();
                writer.WriteString("name", function.GetProperty("name").GetString());
                writer.WritePropertyName("input_schema");
                function.GetProperty("parameters").WriteTo(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("tool_choice");
            writer.WriteStartObject();
            writer.WriteString("type", "any");
            writer.WriteBoolean("disable_parallel_tool_use", true);
            writer.WriteEndObject();
            writer.WritePropertyName("thinking");
            writer.WriteStartObject();
            writer.WriteString("type", profile.ThinkingEnabled ? "enabled" : "disabled");
            if (profile.ThinkingEnabled)
            {
                // DeepSeek's Anthropic compatibility contract ignores budget_tokens, but the
                // field is bound to the caller-supplied output ceiling instead of a hidden value.
                writer.WriteNumber("budget_tokens", profile.MaxTokens);
            }
            writer.WriteEndObject();
            if (profile.ThinkingEnabled)
            {
                writer.WritePropertyName("output_config");
                writer.WriteStartObject();
                writer.WriteString(
                    "effort",
                    profile.ThinkingEffort == AnthropicThinkingEffort.High ? "high" : "max");
                writer.WriteEndObject();
            }
            writer.WriteBoolean("stream", false);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static string? OptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value)) return null;
        return value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
    }

    private static long? OptionalNonNegativeInt64(
        JsonElement root,
        string objectName,
        string propertyName)
    {
        if (!root.TryGetProperty(objectName, out JsonElement parent)
            || parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out long parsed)
            || parsed < 0)
            return null;
        return parsed;
    }

    private static long? OptionalNestedNonNegativeInt64(
        JsonElement root,
        string objectName,
        string nestedObjectName,
        string propertyName)
    {
        if (!root.TryGetProperty(objectName, out JsonElement parent)
            || parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(nestedObjectName, out JsonElement nested)
            || nested.ValueKind != JsonValueKind.Object
            || !nested.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out long parsed)
            || parsed < 0)
            return null;
        return parsed;
    }

    private static AnthropicMessagesResponseMetadata ReadMetadata(JsonElement root) => new(
        OptionalString(root, "id"),
        OptionalNonNegativeInt64(root, "usage", "input_tokens"),
        OptionalNonNegativeInt64(root, "usage", "output_tokens"),
        OptionalNonNegativeInt64(root, "usage", "cache_creation_input_tokens"),
        OptionalNonNegativeInt64(root, "usage", "cache_read_input_tokens"),
        OptionalNestedNonNegativeInt64(root, "usage", "output_tokens_details", "reasoning_tokens")
            ?? OptionalNestedNonNegativeInt64(root, "usage", "output_tokens_details", "thinking_tokens"));
}

/// <summary>
/// Single-attempt DeepSeek V4 thinking transport. Retry ownership stays with an outer runtime,
/// while canonical model-call evidence exposes only the decoded tool call. An optional formal
/// diagnostic sink may retain the credential-free raw request and Provider response separately.
/// </summary>
public sealed class AnthropicMessagesRemotePlannerClient :
    IModelClient<RemotePlannerResponse>,
    IAutomaticModelRetryPolicy
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicMessagesProviderProfile _profile;
    private readonly ProviderApiKey _apiKey;
    private readonly IAnthropicMessagesProviderAttemptSink? _attemptSink;

    public AnthropicMessagesRemotePlannerClient(
        HttpClient httpClient,
        AnthropicMessagesProviderProfile profile,
        ProviderApiKey apiKey,
        IAnthropicMessagesProviderAttemptSink? attemptSink = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(apiKey);
        if (profile.CredentialReference != apiKey.CredentialReference)
        {
            throw new ArgumentException(
                "Anthropic Messages credential source must exactly match its profile.",
                nameof(apiKey));
        }

        _httpClient = httpClient;
        _profile = profile.Snapshot();
        _apiKey = apiKey;
        _attemptSink = attemptSink;
    }

    public bool AllowsAutomaticRetry => true;

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

        byte[] requestBody = AnthropicMessagesRemotePlannerProtocol.GetRequestBody(_profile, remoteRequest);
        var stopwatch = Stopwatch.StartNew();
        using HttpRequestMessage httpRequest = AnthropicMessagesRemotePlannerProtocol.CreateRequest(
            _profile,
            _apiKey,
            requestBody);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_profile.Timeout);
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
                BoundedResponseBodyReadResult errorBody = await OpenAiCompatibleChatCompletions.ReadResponseBodyAsync(
                    httpResponse.Content,
                    _profile.MaxResponseBodyBytes,
                    timeout.Token).ConfigureAwait(false);
                return InvocationFailure(
                    remoteRequest,
                    LiveRemoteFailureKind.HttpFailure,
                    responseStatus,
                    stopwatch.ElapsedMilliseconds,
                    requestBody,
                    errorBody.RawBytes,
                    errorBody.Body is null
                        ? null
                        : AnthropicMessagesRemotePlannerProtocol.TryReadMetadata(errorBody.Body));
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
                    requestBody,
                    null,
                    null);
            }

            if (!AnthropicMessagesRemotePlannerProtocol.TryReadResponse(
                    bodyRead.Body,
                    out IReadOnlyList<RemotePlannerToolCall>? calls,
                    out AnthropicMessagesResponseMetadata? metadata)
                || calls is null
                || metadata is null)
            {
                LiveRemoteFailureKind failureKind =
                    AnthropicMessagesRemotePlannerProtocol.IsOutputTokenLimitReached(bodyRead.Body)
                        ? LiveRemoteFailureKind.OutputTokenLimitReached
                        : LiveRemoteFailureKind.InvalidResponseEnvelope;
                AnthropicMessagesResponseMetadata? failureMetadata =
                    AnthropicMessagesRemotePlannerProtocol.TryReadMetadata(bodyRead.Body);
                return InvocationFailure(
                    remoteRequest,
                    failureKind,
                    responseStatus,
                    stopwatch.ElapsedMilliseconds,
                    requestBody,
                    bodyRead.RawBytes,
                    failureMetadata);
            }

            RemotePlannerResponse response = RemotePlannerResponse.FromToolCalls(remoteRequest, calls);
            byte[] decodedToolCallBytes = SerializeDecodedToolCall(calls.Single());
            long durationMilliseconds = stopwatch.ElapsedMilliseconds;
            string responseBodyHash = Hash(bodyRead.RawBytes);
            var formalCallEvidence = FormalModelCallEvidence.FromLiveTransportReceipt(
                remoteRequest.Binding.RequestId.Value,
                Hash(requestBody),
                responseBodyHash,
                AnthropicMessagesRemotePlannerProtocol.ProtocolVersion,
                remoteRequest.ProtocolVersion,
                _profile.ProfileId.Value,
                _profile.ModelId.Value,
                remoteRequest.Binding.RequestId.Value,
                remoteRequest.Binding.ActorId.Value,
                remoteRequest.Binding.NeedId.Value,
                remoteRequest.Binding.Fingerprint.Value,
                remoteRequest.Binding.ProblemDescriptorHash.Value,
                remoteRequest.Binding.CandidateSetId.Value,
                metadata.ResponseId,
                metadata.InputTokens,
                metadata.OutputTokens,
                metadata.CacheCreationInputTokens,
                metadata.CacheReadInputTokens,
                durationMilliseconds,
                decodedToolCallBytes);
            RecordAttempt(
                remoteRequest,
                LiveRemoteTransportOutcome.ResponseEnvelopeReceived,
                null,
                responseStatus,
                durationMilliseconds,
                requestBody,
                bodyRead.RawBytes,
                metadata);
            return ModelClientResult<RemotePlannerResponse>.Produced(
                response,
                AnthropicMessagesRemotePlannerExecutionEvidence.Received(
                    _profile,
                    responseStatus.Value,
                    durationMilliseconds,
                    responseBodyHash,
                    formalCallEvidence));
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
                requestBody,
                null,
                null);
        }
        catch (HttpRequestException)
        {
            return InvocationFailure(
                remoteRequest,
                LiveRemoteFailureKind.NetworkFailure,
                responseStatus,
                stopwatch.ElapsedMilliseconds,
                requestBody,
                null,
                null);
        }
        catch (IOException)
        {
            return InvocationFailure(
                remoteRequest,
                LiveRemoteFailureKind.NetworkFailure,
                responseStatus,
                stopwatch.ElapsedMilliseconds,
                requestBody,
                null,
                null);
        }
    }

    private ModelClientResult<RemotePlannerResponse> InvocationFailure(
        RemotePlannerRequest request,
        LiveRemoteFailureKind failureKind,
        int? httpStatus,
        long durationMilliseconds,
        byte[] requestBody,
        byte[]? responseBody,
        AnthropicMessagesResponseMetadata? metadata)
    {
        RecordAttempt(
            request,
            LiveRemoteTransportOutcome.InvocationFailed,
            failureKind,
            httpStatus,
            durationMilliseconds,
            requestBody,
            responseBody,
            metadata);
        return ModelClientResult<RemotePlannerResponse>.Produced(
            RemotePlannerResponse.InvocationFailed(request),
            AnthropicMessagesRemotePlannerExecutionEvidence.Failed(
                _profile,
                failureKind,
                httpStatus,
                durationMilliseconds,
                responseBody is null ? null : Hash(responseBody)));
    }

    private void RecordAttempt(
        RemotePlannerRequest request,
        LiveRemoteTransportOutcome outcome,
        LiveRemoteFailureKind? failureKind,
        int? httpStatus,
        long durationMilliseconds,
        byte[] requestBody,
        byte[]? responseBody,
        AnthropicMessagesResponseMetadata? metadata)
    {
        _attemptSink?.Record(new AnthropicMessagesProviderAttemptTrace(
            request.Binding.RequestId.Value,
            outcome,
            failureKind,
            httpStatus,
            durationMilliseconds,
            metadata?.ResponseId,
            metadata?.InputTokens,
            metadata?.OutputTokens,
            metadata?.CacheCreationInputTokens,
            metadata?.CacheReadInputTokens,
            metadata?.ReasoningTokens,
            requestBody,
            responseBody));
    }

    private static byte[] SerializeDecodedToolCall(RemotePlannerToolCall call)
    {
        using JsonDocument input = JsonDocument.Parse(call.ArgumentsJson ?? "{}");
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("name", call.Name);
            writer.WritePropertyName("input");
            input.RootElement.WriteTo(writer);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
