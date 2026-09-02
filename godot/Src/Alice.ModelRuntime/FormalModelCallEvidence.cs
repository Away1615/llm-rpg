using System.Security.Cryptography;
using System.Text.Json;

namespace Alice.ModelRuntime;

public enum FormalModelCallEvidenceSource
{
    EngineeringFixture,
    LiveTransportReceipt
}

/// <summary>
/// Sanitized model-attempt evidence. Formal-capable instances are issued internally from a decoded Live transport
/// response; public engineering fixtures remain distinguishable and cannot satisfy formal pairing.
/// </summary>
public sealed class FormalModelCallEvidence
{
    private readonly byte[] _canonicalBytes;
    private readonly byte[]? _decodedToolCallBytes;

    private FormalModelCallEvidence(
        FormalModelCallEvidenceSource source,
        ModelClientExecutionMode executionMode,
        string callId,
        string requestHash,
        string responseHash,
        string? providerProtocolVersion,
        string? requestProtocolVersion,
        string? providerProfileId,
        string? modelId,
        string? requestBindingId,
        string? actorId,
        string? needId,
        string? decisionNeedFingerprint,
        string? problemDescriptorHash,
        string? candidateSetId,
        string? providerResponseId,
        long? inputTokens,
        long? outputTokens,
        long? cacheCreationInputTokens,
        long? cacheReadInputTokens,
        long? durationMilliseconds,
        byte[]? decodedToolCallBytes)
    {
        if (!Enum.IsDefined(source) || !Enum.IsDefined(executionMode))
            throw new ArgumentOutOfRangeException(nameof(source));
        RequireIdentity(callId, nameof(callId));
        Source = source;
        ExecutionMode = executionMode;
        CallId = callId;
        RequestHash = ValidateSha256(requestHash, nameof(requestHash));
        ResponseHash = ValidateSha256(responseHash, nameof(responseHash));
        ProviderProtocolVersion = NormalizeOptionalIdentity(providerProtocolVersion, nameof(providerProtocolVersion));
        RequestProtocolVersion = NormalizeOptionalIdentity(requestProtocolVersion, nameof(requestProtocolVersion));
        ProviderProfileId = NormalizeOptionalIdentity(providerProfileId, nameof(providerProfileId));
        ModelId = NormalizeOptionalIdentity(modelId, nameof(modelId));
        RequestBindingId = NormalizeOptionalIdentity(requestBindingId, nameof(requestBindingId));
        ActorId = NormalizeOptionalIdentity(actorId, nameof(actorId));
        NeedId = NormalizeOptionalIdentity(needId, nameof(needId));
        DecisionNeedFingerprint = NormalizeOptionalIdentity(
            decisionNeedFingerprint,
            nameof(decisionNeedFingerprint));
        ProblemDescriptorHash = NormalizeOptionalHash(problemDescriptorHash, nameof(problemDescriptorHash));
        CandidateSetId = NormalizeOptionalHash(candidateSetId, nameof(candidateSetId));
        ProviderResponseId = NormalizeOptionalIdentity(providerResponseId, nameof(providerResponseId));
        if (inputTokens is < 0
            || outputTokens is < 0
            || cacheCreationInputTokens is < 0
            || cacheReadInputTokens is < 0
            || durationMilliseconds is < 0)
            throw new ArgumentOutOfRangeException(nameof(inputTokens));
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CacheCreationInputTokens = cacheCreationInputTokens;
        CacheReadInputTokens = cacheReadInputTokens;
        DurationMilliseconds = durationMilliseconds;
        _decodedToolCallBytes = decodedToolCallBytes?.ToArray();
        DecodedToolCallHash = _decodedToolCallBytes is null ? null : Hash(_decodedToolCallBytes);
        _canonicalBytes = Serialize();
        EvidenceHash = Hash(_canonicalBytes);
    }

    public FormalModelCallEvidenceSource Source { get; }
    public ModelClientExecutionMode ExecutionMode { get; }
    public string CallId { get; }
    public string RequestHash { get; }
    public string ResponseHash { get; }
    public string? ProviderProtocolVersion { get; }
    public string? RequestProtocolVersion { get; }
    public string? ProviderProfileId { get; }
    public string? ModelId { get; }
    public string? RequestBindingId { get; }
    public string? ActorId { get; }
    public string? NeedId { get; }
    public string? DecisionNeedFingerprint { get; }
    public string? ProblemDescriptorHash { get; }
    public string? CandidateSetId { get; }
    public string? ProviderResponseId { get; }
    public long? InputTokens { get; }
    public long? OutputTokens { get; }
    public long? CacheCreationInputTokens { get; }
    public long? CacheReadInputTokens { get; }
    public long? DurationMilliseconds { get; }
    public string? DecodedToolCallHash { get; }
    public string EvidenceHash { get; }

    public bool IsFormalPairingComplete =>
        Source == FormalModelCallEvidenceSource.LiveTransportReceipt
        && ExecutionMode == ModelClientExecutionMode.LiveRemote
        && ProviderProtocolVersion is not null
        && RequestProtocolVersion is not null
        && ProviderProfileId is not null
        && ModelId is not null
        && RequestBindingId is not null
        && ActorId is not null
        && NeedId is not null
        && DecisionNeedFingerprint is not null
        && ProblemDescriptorHash is not null
        && CandidateSetId is not null
        && ProviderResponseId is not null
        && InputTokens is not null
        && OutputTokens is not null
        && DurationMilliseconds is not null
        && DecodedToolCallHash is not null;

    public static FormalModelCallEvidence EngineeringFixture(
        string callId,
        string requestHash,
        string responseHash,
        string? providerResponseId,
        long? inputTokens,
        long? outputTokens)
    {
        return new FormalModelCallEvidence(
            FormalModelCallEvidenceSource.EngineeringFixture,
            ModelClientExecutionMode.DeterministicTest,
            callId,
            requestHash,
            responseHash,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            providerResponseId,
            inputTokens,
            outputTokens,
            null,
            null,
            null,
            null);
    }

    internal static FormalModelCallEvidence FromLiveTransportReceipt(
        string callId,
        string requestHash,
        string responseHash,
        string providerProtocolVersion,
        string requestProtocolVersion,
        string providerProfileId,
        string modelId,
        string requestBindingId,
        string actorId,
        string needId,
        string decisionNeedFingerprint,
        string problemDescriptorHash,
        string candidateSetId,
        string? providerResponseId,
        long? inputTokens,
        long? outputTokens,
        long? cacheCreationInputTokens,
        long? cacheReadInputTokens,
        long durationMilliseconds,
        byte[] decodedToolCallBytes)
    {
        return new FormalModelCallEvidence(
            FormalModelCallEvidenceSource.LiveTransportReceipt,
            ModelClientExecutionMode.LiveRemote,
            callId,
            requestHash,
            responseHash,
            providerProtocolVersion,
            requestProtocolVersion,
            providerProfileId,
            modelId,
            requestBindingId,
            actorId,
            needId,
            decisionNeedFingerprint,
            problemDescriptorHash,
            candidateSetId,
            providerResponseId,
            inputTokens,
            outputTokens,
            cacheCreationInputTokens,
            cacheReadInputTokens,
            durationMilliseconds,
            decodedToolCallBytes);
    }

    public byte[] GetCanonicalBytes() => _canonicalBytes.ToArray();
    public byte[]? GetDecodedToolCallBytes() => _decodedToolCallBytes?.ToArray();

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-model-call-evidence.v3");
            writer.WriteString("source", Source.ToString());
            writer.WriteString("execution_mode", ExecutionMode.ToString());
            writer.WriteString("call_id", CallId);
            writer.WriteString("request_hash", RequestHash);
            writer.WriteString("response_hash", ResponseHash);
            writer.WriteString("provider_protocol_version", ProviderProtocolVersion);
            writer.WriteString("request_protocol_version", RequestProtocolVersion);
            writer.WriteString("provider_profile_id", ProviderProfileId);
            writer.WriteString("model_id", ModelId);
            writer.WriteString("request_binding_id", RequestBindingId);
            writer.WriteString("actor_id", ActorId);
            writer.WriteString("need_id", NeedId);
            writer.WriteString("decision_need_fingerprint", DecisionNeedFingerprint);
            writer.WriteString("problem_descriptor_hash", ProblemDescriptorHash);
            writer.WriteString("candidate_set_id", CandidateSetId);
            writer.WriteString("provider_response_id", ProviderResponseId);
            if (InputTokens is long inputTokens) writer.WriteNumber("input_tokens", inputTokens);
            else writer.WriteNull("input_tokens");
            if (OutputTokens is long outputTokens) writer.WriteNumber("output_tokens", outputTokens);
            else writer.WriteNull("output_tokens");
            if (CacheCreationInputTokens is long cacheCreationInputTokens)
                writer.WriteNumber("cache_creation_input_tokens", cacheCreationInputTokens);
            else writer.WriteNull("cache_creation_input_tokens");
            if (CacheReadInputTokens is long cacheReadInputTokens)
                writer.WriteNumber("cache_read_input_tokens", cacheReadInputTokens);
            else writer.WriteNull("cache_read_input_tokens");
            if (DurationMilliseconds is long durationMilliseconds)
                writer.WriteNumber("duration_milliseconds", durationMilliseconds);
            else writer.WriteNull("duration_milliseconds");
            writer.WriteString("decoded_tool_call_hash", DecodedToolCallHash);
            if (_decodedToolCallBytes is null) writer.WriteNull("decoded_tool_call");
            else writer.WriteBase64String("decoded_tool_call", _decodedToolCallBytes);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static string? NormalizeOptionalIdentity(string? value, string name)
    {
        if (value is null) return null;
        RequireIdentity(value, name);
        return value;
    }

    private static string? NormalizeOptionalHash(string? value, string name) =>
        value is null ? null : ValidateSha256(value, name);

    private static void RequireIdentity(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty identity is required.", name);
    }

    private static string ValidateSha256(string value, string name)
    {
        if (value.Length != 64 || value.Any(IsNotLowerHex))
            throw new ArgumentException("A lowercase SHA-256 identity is required.", name);
        return value;
    }

    private static bool IsNotLowerHex(char value) =>
        value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f');

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
