using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Alice.ModelRuntime;

internal sealed class FormalRq1ReplayPair
{
    private readonly string _pairId;
    private readonly Dictionary<string, IReadOnlyList<FormalRq1ReplayAttempt>> _attemptsByTreatment;
    private readonly List<FormalRq1ReplayClient> _clients = [];

    private FormalRq1ReplayPair(
        string pairId,
        Dictionary<string, IReadOnlyList<FormalRq1ReplayAttempt>> attemptsByTreatment)
    {
        _pairId = pairId;
        _attemptsByTreatment = attemptsByTreatment;
    }

    public static FormalRq1ReplayPair Load(string path, string pairId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(pairId);
        if (!File.Exists(path))
            throw new FileNotFoundException($"RQ1 replay sidecar is missing for {pairId}.", path);

        var byTreatment = new Dictionary<string, List<FormalRq1ReplayAttempt>>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            FormalRq1ReplayAttempt attempt = FormalRq1ReplayAttempt.Parse(line, pairId);
            if (!byTreatment.TryGetValue(attempt.Treatment, out List<FormalRq1ReplayAttempt>? records))
            {
                records = [];
                byTreatment.Add(attempt.Treatment, records);
            }
            records.Add(attempt);
        }

        string[] expectedTreatments = ["AgentCentric", "EventCentric"];
        if (byTreatment.Count != expectedTreatments.Length)
            throw new InvalidDataException($"RQ1 replay sidecar has the wrong treatment count: {pairId}.");
        var frozen = new Dictionary<string, IReadOnlyList<FormalRq1ReplayAttempt>>(StringComparer.Ordinal);
        foreach (string treatment in expectedTreatments)
        {
            if (!byTreatment.TryGetValue(treatment, out List<FormalRq1ReplayAttempt>? records))
                throw new InvalidDataException($"RQ1 replay sidecar lacks {treatment}: {pairId}.");
            ValidateTreatmentAttempts(records, pairId, treatment);
            frozen.Add(treatment, records.AsReadOnly());
        }
        return new FormalRq1ReplayPair(pairId, frozen);
    }

    public IModelClient<RemotePlannerResponse> CreateClient(
        string treatment,
        string profileId,
        IAnthropicMessagesProviderAttemptSink attemptSink)
    {
        if (!_attemptsByTreatment.TryGetValue(treatment, out IReadOnlyList<FormalRq1ReplayAttempt>? attempts))
            throw new InvalidDataException($"RQ1 replay sidecar lacks {treatment}: {_pairId}.");
        if (_clients.Any(client => StringComparer.Ordinal.Equals(client.Treatment, treatment)))
            throw new InvalidOperationException($"RQ1 replay client was already created: {_pairId}/{treatment}.");
        var client = new FormalRq1ReplayClient(_pairId, treatment, profileId, attempts, attemptSink);
        _clients.Add(client);
        return client;
    }

    public void RequireFullyConsumed()
    {
        if (_clients.Count != _attemptsByTreatment.Count)
            throw new InvalidDataException($"RQ1 replay did not create both treatment clients: {_pairId}.");
        foreach (FormalRq1ReplayClient client in _clients)
            client.RequireFullyConsumed();
    }

    private static void ValidateTreatmentAttempts(
        IReadOnlyList<FormalRq1ReplayAttempt> attempts,
        string pairId,
        string treatment)
    {
        var byRequest = new Dictionary<string, List<FormalRq1ReplayAttempt>>(StringComparer.Ordinal);
        foreach (FormalRq1ReplayAttempt attempt in attempts)
        {
            if (!byRequest.TryGetValue(attempt.RequestId, out List<FormalRq1ReplayAttempt>? records))
            {
                records = [];
                byRequest.Add(attempt.RequestId, records);
            }
            records.Add(attempt);
        }
        if (byRequest.Count != 4)
            throw new InvalidDataException($"RQ1 replay requires four logical calls: {pairId}/{treatment}.");
        foreach (KeyValuePair<string, List<FormalRq1ReplayAttempt>> request in byRequest)
        {
            request.Value.Sort(CompareAttemptIndex);
            for (int index = 0; index < request.Value.Count; index++)
            {
                if (request.Value[index].AttemptIndex != index + 1)
                    throw new InvalidDataException($"RQ1 replay attempts are not contiguous: {request.Key}.");
            }
            if (request.Value[^1].Outcome != LiveRemoteTransportOutcome.ResponseEnvelopeReceived)
                throw new InvalidDataException($"RQ1 replay logical call lacks a terminal response: {request.Key}.");
        }
    }

    private static int CompareAttemptIndex(FormalRq1ReplayAttempt left, FormalRq1ReplayAttempt right) =>
        left.AttemptIndex.CompareTo(right.AttemptIndex);
}
internal sealed class FormalRq1ReplayClient :
    IModelClient<RemotePlannerResponse>,
    IAutomaticModelRetryPolicy
{
    private readonly object _gate = new();
    private readonly string _pairId;
    private readonly AnthropicMessagesProviderProfile _profile;
    private readonly IAnthropicMessagesProviderAttemptSink _attemptSink;
    private readonly Dictionary<string, Queue<FormalRq1ReplayAttempt>> _attemptsByRequest;

    public FormalRq1ReplayClient(
        string pairId,
        string treatment,
        string profileId,
        IReadOnlyList<FormalRq1ReplayAttempt> attempts,
        IAnthropicMessagesProviderAttemptSink attemptSink)
    {
        _pairId = pairId;
        Treatment = treatment;
        _attemptSink = attemptSink;
        var credential = new ProviderCredentialReference("DEEPSEEK_API_KEY");
        _profile = new AnthropicMessagesProviderProfile(
            new AnthropicMessagesProfileId(profileId),
            new Uri("https://api.deepseek.com/anthropic/v1/messages"),
            new AnthropicMessagesModelId("deepseek-v4-pro"),
            TimeSpan.FromSeconds(300),
            16_384,
            1_048_576,
            credential,
            true,
            AnthropicThinkingEffort.High);
        _attemptsByRequest = new Dictionary<string, Queue<FormalRq1ReplayAttempt>>(StringComparer.Ordinal);
        foreach (FormalRq1ReplayAttempt attempt in attempts.OrderBy(value => value.AttemptIndex))
        {
            if (!_attemptsByRequest.TryGetValue(attempt.RequestId, out Queue<FormalRq1ReplayAttempt>? queue))
            {
                queue = new Queue<FormalRq1ReplayAttempt>();
                _attemptsByRequest.Add(attempt.RequestId, queue);
            }
            queue.Enqueue(attempt);
        }
    }

    public string Treatment { get; }
    public bool AllowsAutomaticRetry => true;

    public ValueTask<ModelClientResult<RemotePlannerResponse>> InvokeAsync(
        IModelRequest<RemotePlannerResponse> request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is not RemotePlannerRequest remoteRequest)
            return ValueTask.FromResult(ModelClientResult<RemotePlannerResponse>.Unavailable(
                ModelClientExecutionMode.LiveRemote,
                ModelClientUnavailableReason.UnsupportedRequestType));

        FormalRq1ReplayAttempt attempt;
        lock (_gate)
        {
            if (!_attemptsByRequest.TryGetValue(
                    remoteRequest.Binding.RequestId.Value,
                    out Queue<FormalRq1ReplayAttempt>? queue)
                || queue.Count == 0)
            {
                throw new InvalidDataException(
                    $"RQ1 replay has no remaining attempt: {_pairId}/{Treatment}/{remoteRequest.Binding.RequestId.Value}.");
            }
            attempt = queue.Dequeue();
        }

        byte[] requestBody = AnthropicMessagesRemotePlannerProtocol.GetRequestBody(_profile, remoteRequest);
        if (!requestBody.AsSpan().SequenceEqual(attempt.RequestBody)
            || !StringComparer.Ordinal.Equals(Hash(requestBody), attempt.RequestBodyHash))
        {
            throw new InvalidDataException(
                $"RQ1 replay request bytes differ from the frozen live attempt: {_pairId}/{Treatment}/{attempt.RequestId}.");
        }

        _attemptSink.Record(attempt.ToTrace());
        if (attempt.Outcome == LiveRemoteTransportOutcome.InvocationFailed)
        {
            LiveRemoteFailureKind failure = attempt.FailureKind
                ?? throw new InvalidDataException($"RQ1 replay failure lacks a failure kind: {attempt.RequestId}.");
            return ValueTask.FromResult(ModelClientResult<RemotePlannerResponse>.Produced(
                RemotePlannerResponse.InvocationFailed(remoteRequest),
                AnthropicMessagesRemotePlannerExecutionEvidence.Failed(
                    _profile,
                    failure,
                    attempt.HttpStatus,
                    attempt.DurationMilliseconds,
                    attempt.ResponseBody is null ? null : Hash(attempt.ResponseBody))));
        }

        byte[] responseBody = attempt.ResponseBody
            ?? throw new InvalidDataException($"RQ1 replay response is missing: {attempt.RequestId}.");
        if (!AnthropicMessagesRemotePlannerProtocol.TryReadResponse(
                Encoding.UTF8.GetString(responseBody),
                out IReadOnlyList<RemotePlannerToolCall>? calls,
                out AnthropicMessagesResponseMetadata? metadata)
            || calls is null
            || metadata is null
            || calls.Count != 1)
        {
            throw new InvalidDataException($"RQ1 replay response no longer satisfies the Provider protocol: {attempt.RequestId}.");
        }
        attempt.RequireMatchingMetadata(metadata);
        string responseHash = Hash(responseBody);
        var formalCallEvidence = FormalModelCallEvidence.FromLiveTransportReceipt(
            remoteRequest.Binding.RequestId.Value,
            attempt.RequestBodyHash,
            responseHash,
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
            attempt.DurationMilliseconds,
            SerializeDecodedToolCall(calls.Single()));
        return ValueTask.FromResult(ModelClientResult<RemotePlannerResponse>.Produced(
            RemotePlannerResponse.FromToolCalls(remoteRequest, calls),
            AnthropicMessagesRemotePlannerExecutionEvidence.Received(
                _profile,
                attempt.HttpStatus ?? 200,
                attempt.DurationMilliseconds,
                responseHash,
                formalCallEvidence)));
    }

    public void RequireFullyConsumed()
    {
        lock (_gate)
        {
            int remaining = _attemptsByRequest.Values.Sum(queue => queue.Count);
            if (remaining != 0)
                throw new InvalidDataException($"RQ1 replay left {remaining} unused attempt(s): {_pairId}/{Treatment}.");
        }
    }

    private static byte[] SerializeDecodedToolCall(RemotePlannerToolCall call)
    {
        using JsonDocument input = JsonDocument.Parse(call.ArgumentsJson ?? "{}");
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteString("name", call.Name);
            writer.WritePropertyName("input");
            input.RootElement.WriteTo(writer);
            writer.WriteEndObject();
        }
        return output.ToArray();
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed record FormalRq1ReplayAttempt(
    string PairId,
    string Treatment,
    string RequestId,
    int AttemptIndex,
    LiveRemoteTransportOutcome Outcome,
    LiveRemoteFailureKind? FailureKind,
    int? HttpStatus,
    long DurationMilliseconds,
    string? ProviderResponseId,
    long? InputTokens,
    long? OutputTokens,
    long? CacheCreationInputTokens,
    long? CacheReadInputTokens,
    long? ReasoningTokens,
    string RequestBodyHash,
    byte[] RequestBody,
    string? ResponseBodyHash,
    byte[]? ResponseBody)
{
    public static FormalRq1ReplayAttempt Parse(string line, string expectedPairId)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        string schema = RequiredString(root, "schema_version");
        if (!StringComparer.Ordinal.Equals(schema, "alice.formal-provider-attempt.v1"))
            throw new InvalidDataException($"RQ1 replay sidecar has an unknown schema: {schema}.");
        string pairId = RequiredString(root, "pair_id");
        if (!StringComparer.Ordinal.Equals(pairId, expectedPairId))
            throw new InvalidDataException($"RQ1 replay sidecar pair differs: {pairId}/{expectedPairId}.");
        string treatment = RequiredString(root, "treatment");
        string requestBodyText = RequiredString(root, "request_body_utf8");
        byte[] requestBody = Encoding.UTF8.GetBytes(requestBodyText);
        string requestHash = RequiredString(root, "request_body_sha256");
        RequireHash(requestBody, requestHash, $"{pairId}/{treatment}/request");
        string? responseText = OptionalString(root, "response_body_utf8");
        byte[]? responseBody = responseText is null ? null : Encoding.UTF8.GetBytes(responseText);
        string? responseHash = OptionalString(root, "response_body_sha256");
        if (responseBody is null != responseHash is null)
            throw new InvalidDataException($"RQ1 replay response body/hash presence differs: {pairId}/{treatment}.");
        if (responseBody is not null) RequireHash(responseBody, responseHash!, $"{pairId}/{treatment}/response");

        return new FormalRq1ReplayAttempt(
            pairId,
            treatment,
            RequiredString(root, "request_id"),
            root.GetProperty("attempt_index").GetInt32(),
            Enum.Parse<LiveRemoteTransportOutcome>(RequiredString(root, "outcome"), false),
            ParseOptionalEnum<LiveRemoteFailureKind>(root, "failure_kind"),
            OptionalInt32(root, "http_status"),
            root.GetProperty("duration_ms").GetInt64(),
            OptionalString(root, "provider_response_id"),
            OptionalInt64(root, "input_tokens"),
            OptionalInt64(root, "output_tokens_including_reasoning"),
            OptionalInt64(root, "cache_creation_input_tokens"),
            OptionalInt64(root, "cache_read_input_tokens"),
            OptionalInt64(root, "reasoning_tokens"),
            requestHash,
            requestBody,
            responseHash,
            responseBody);
    }

    public AnthropicMessagesProviderAttemptTrace ToTrace() => new(
        RequestId,
        Outcome,
        FailureKind,
        HttpStatus,
        DurationMilliseconds,
        ProviderResponseId,
        InputTokens,
        OutputTokens,
        CacheCreationInputTokens,
        CacheReadInputTokens,
        ReasoningTokens,
        RequestBody,
        ResponseBody);

    public void RequireMatchingMetadata(AnthropicMessagesResponseMetadata metadata)
    {
        if (!StringComparer.Ordinal.Equals(ProviderResponseId, metadata.ResponseId)
            || InputTokens != metadata.InputTokens
            || OutputTokens != metadata.OutputTokens
            || CacheCreationInputTokens != metadata.CacheCreationInputTokens
            || CacheReadInputTokens != metadata.CacheReadInputTokens
            || ReasoningTokens != metadata.ReasoningTokens)
        {
            throw new InvalidDataException($"RQ1 replay sidecar metadata differs from its response body: {RequestId}.");
        }
    }

    private static void RequireHash(byte[] bytes, string expected, string label)
    {
        string actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!StringComparer.Ordinal.Equals(actual, expected))
            throw new InvalidDataException($"RQ1 replay sidecar hash mismatch: {label}.");
    }

    private static string RequiredString(JsonElement root, string propertyName)
    {
        string? value = root.GetProperty(propertyName).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"RQ1 replay sidecar lacks {propertyName}.")
            : value;
    }

    private static string? OptionalString(JsonElement root, string propertyName)
    {
        JsonElement value = root.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static int? OptionalInt32(JsonElement root, string propertyName)
    {
        JsonElement value = root.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
    }

    private static long? OptionalInt64(JsonElement root, string propertyName)
    {
        JsonElement value = root.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetInt64();
    }

    private static T? ParseOptionalEnum<T>(JsonElement root, string propertyName) where T : struct
    {
        string? value = OptionalString(root, propertyName);
        return value is null ? null : Enum.Parse<T>(value, false);
    }
}
