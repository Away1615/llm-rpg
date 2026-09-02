using Alice.Identity;

namespace Alice.ModelRuntime;

public sealed record OpenAiCompatibleProfileId
{
    public OpenAiCompatibleProfileId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Provider profile identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public sealed record OpenAiCompatibleModelId
{
    public OpenAiCompatibleModelId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Model identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public sealed record OpenAiCompatibleEndpoint
{
    public OpenAiCompatibleEndpoint(Uri value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.IsAbsoluteUri ||
            value.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(value.UserInfo) ||
            !string.IsNullOrEmpty(value.Fragment))
        {
            throw new ArgumentException(
                "Chat Completions endpoint must be an absolute HTTP(S) URI without user-info or fragment.",
                nameof(value));
        }

        Value = new Uri(value.OriginalString, UriKind.Absolute);
    }

    public Uri Value { get; }
}

/// <summary>Declared transport capabilities; this slice consumes only JSON Schema support.</summary>
public sealed record OpenAiCompatibleCapabilities(
    bool SupportsJsonSchemaStructuredOutput,
    bool SupportsNativeTools,
    bool SupportsStrictToolSchema,
    bool RequiresOpaqueReasoningReplay,
    bool SupportsParallelToolCalls);

public enum OpenAiCompatibleThinkingMode
{
    ProviderDefault,
    Enabled,
    Disabled
}

/// <summary>Immutable composition-owned OpenAI-compatible profile without credential values.</summary>
public sealed class OpenAiCompatibleProviderProfile : ProviderProfileBase
{

    public OpenAiCompatibleProviderProfile(
        OpenAiCompatibleProfileId profileId,
        OpenAiCompatibleEndpoint chatCompletionsEndpoint,
        OpenAiCompatibleModelId modelId,
        OpenAiCompatibleCapabilities capabilities,
        TimeSpan timeout,
        int maxTokens,
        int maxResponseBodyBytes,
        ProviderCredentialReference? credentialReference,
        OpenAiCompatibleThinkingMode thinkingMode = OpenAiCompatibleThinkingMode.ProviderDefault)
        : base(timeout, maxTokens, maxResponseBodyBytes, credentialReference)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(chatCompletionsEndpoint);
        ArgumentNullException.ThrowIfNull(modelId);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!Enum.IsDefined(thinkingMode))
        {
            throw new ArgumentOutOfRangeException(nameof(thinkingMode));
        }

        ProfileId = profileId;
        ChatCompletionsEndpoint = chatCompletionsEndpoint;
        ModelId = modelId;
        Capabilities = capabilities;
        ThinkingMode = thinkingMode;
    }

    public OpenAiCompatibleProfileId ProfileId { get; }
    public OpenAiCompatibleEndpoint ChatCompletionsEndpoint { get; }
    public OpenAiCompatibleModelId ModelId { get; }
    public OpenAiCompatibleCapabilities Capabilities { get; }
    public OpenAiCompatibleThinkingMode ThinkingMode { get; }

    internal OpenAiCompatibleProviderProfile Snapshot()
    {
        return new OpenAiCompatibleProviderProfile(
            new OpenAiCompatibleProfileId(ProfileId.Value),
            new OpenAiCompatibleEndpoint(ChatCompletionsEndpoint.Value),
            new OpenAiCompatibleModelId(ModelId.Value),
            new OpenAiCompatibleCapabilities(
                Capabilities.SupportsJsonSchemaStructuredOutput,
                Capabilities.SupportsNativeTools,
                Capabilities.SupportsStrictToolSchema,
                Capabilities.RequiresOpaqueReasoningReplay,
                Capabilities.SupportsParallelToolCalls),
            Timeout,
            MaxTokens,
            MaxResponseBodyBytes,
            CredentialReference is null
                ? null
                : new ProviderCredentialReference(CredentialReference.Value),
            ThinkingMode);
    }
}
