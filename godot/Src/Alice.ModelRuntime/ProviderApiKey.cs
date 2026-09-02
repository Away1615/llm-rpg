using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alice.Identity;

namespace Alice.ModelRuntime;

public sealed record ProviderCredentialReference
{
    public ProviderCredentialReference(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Provider credential reference must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

/// <summary>Shared transport limits and optional credential reference for real Provider profiles.</summary>
public abstract class ProviderProfileBase
{
    private static readonly TimeSpan MaximumCancellationTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1u);

    protected ProviderProfileBase(
        TimeSpan timeout,
        int maxTokens,
        int maxResponseBodyBytes,
        ProviderCredentialReference? credentialReference)
    {
        if (timeout <= TimeSpan.Zero || timeout > MaximumCancellationTimeout)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (maxTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTokens));
        if (maxResponseBodyBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBodyBytes));
        Timeout = timeout;
        MaxTokens = maxTokens;
        MaxResponseBodyBytes = maxResponseBodyBytes;
        CredentialReference = credentialReference;
    }

    public TimeSpan Timeout { get; }
    public int MaxTokens { get; }
    public int MaxResponseBodyBytes { get; }
    public ProviderCredentialReference? CredentialReference { get; }
}

public enum ProviderApiKeyLoadStatus
{
    Loaded,
    MissingOrBlank
}

public sealed record ProviderApiKeyLoadResult
{
    public ProviderApiKeyLoadResult(
        ProviderCredentialReference credentialReference,
        ProviderApiKeyLoadStatus status,
        ProviderApiKey? apiKey)
    {
        ArgumentNullException.ThrowIfNull(credentialReference);
        if (!Enum.IsDefined(status) || (status == ProviderApiKeyLoadStatus.Loaded) != (apiKey is not null))
        {
            throw new ArgumentException("Provider API-key load evidence is inconsistent.");
        }

        CredentialReference = credentialReference;
        Status = status;
        ApiKey = apiKey;
    }

    public ProviderCredentialReference CredentialReference { get; }
    public ProviderApiKeyLoadStatus Status { get; }
    public ProviderApiKey? ApiKey { get; }
}

/// <summary>Non-serializable API-key value loaded only from its exact environment reference.</summary>
[JsonConverter(typeof(ProviderApiKeyJsonConverter))]
public sealed class ProviderApiKey
{
    private readonly string _value;

    private ProviderApiKey(ProviderCredentialReference credentialReference, string value)
    {
        CredentialReference = credentialReference;
        _value = value;
    }

    public ProviderCredentialReference CredentialReference { get; }

    public static ProviderApiKeyLoadResult LoadFromEnvironment(ProviderCredentialReference credentialReference)
    {
        ArgumentNullException.ThrowIfNull(credentialReference);
        string? value = Environment.GetEnvironmentVariable(credentialReference.Value);
        return string.IsNullOrWhiteSpace(value)
            ? new ProviderApiKeyLoadResult(
                credentialReference,
                ProviderApiKeyLoadStatus.MissingOrBlank,
                null)
            : new ProviderApiKeyLoadResult(
                credentialReference,
                ProviderApiKeyLoadStatus.Loaded,
                new ProviderApiKey(credentialReference, value));
    }

    internal void ApplyAnthropicCredential(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Add("x-api-key", _value);
    }

    internal void ApplyBearerCredential(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _value);
    }
}

internal sealed class ProviderApiKeyJsonConverter : JsonConverter<ProviderApiKey>
{
    public override ProviderApiKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Provider API keys cannot be deserialized.");

    public override void Write(Utf8JsonWriter writer, ProviderApiKey value, JsonSerializerOptions options) =>
        throw new NotSupportedException("Provider API keys cannot be serialized.");
}
