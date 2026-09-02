using Alice.Identity;

namespace Alice.ModelRuntime;

public sealed record AnthropicMessagesProfileId
{
    public AnthropicMessagesProfileId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Anthropic Messages profile identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public sealed record AnthropicMessagesModelId
{
    public AnthropicMessagesModelId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Anthropic Messages model identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public enum AnthropicThinkingEffort
{
    High,
    Max
}

/// <summary>One explicit DeepSeek Anthropic-Messages profile; it is not an OpenAI compatibility profile.</summary>
public sealed class AnthropicMessagesProviderProfile : ProviderProfileBase
{
    public AnthropicMessagesProviderProfile(
        AnthropicMessagesProfileId profileId,
        Uri messagesEndpoint,
        AnthropicMessagesModelId modelId,
        TimeSpan timeout,
        int maxTokens,
        int maxResponseBodyBytes,
        ProviderCredentialReference credentialReference,
        bool thinkingEnabled,
        AnthropicThinkingEffort thinkingEffort)
        : base(timeout, maxTokens, maxResponseBodyBytes, credentialReference)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(messagesEndpoint);
        ArgumentNullException.ThrowIfNull(modelId);
        ArgumentNullException.ThrowIfNull(credentialReference);
        if (!messagesEndpoint.IsAbsoluteUri
            || !StringComparer.OrdinalIgnoreCase.Equals(messagesEndpoint.Scheme, Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(messagesEndpoint.UserInfo)
            || !string.IsNullOrEmpty(messagesEndpoint.Fragment))
        {
            throw new ArgumentException(
                "Anthropic Messages endpoint must be an absolute HTTPS URI without user-info or fragment.",
                nameof(messagesEndpoint));
        }

        if (!Enum.IsDefined(thinkingEffort))
        {
            throw new ArgumentOutOfRangeException(nameof(thinkingEffort));
        }

        ProfileId = profileId;
        MessagesEndpoint = new Uri(messagesEndpoint.OriginalString, UriKind.Absolute);
        ModelId = modelId;
        ThinkingEnabled = thinkingEnabled;
        ThinkingEffort = thinkingEffort;
    }

    public AnthropicMessagesProfileId ProfileId { get; }
    public Uri MessagesEndpoint { get; }
    public AnthropicMessagesModelId ModelId { get; }
    public new ProviderCredentialReference CredentialReference => base.CredentialReference!;
    public bool ThinkingEnabled { get; }
    public AnthropicThinkingEffort ThinkingEffort { get; }

    internal AnthropicMessagesProviderProfile Snapshot() => new(
        new AnthropicMessagesProfileId(ProfileId.Value),
        MessagesEndpoint,
        new AnthropicMessagesModelId(ModelId.Value),
        Timeout,
        MaxTokens,
        MaxResponseBodyBytes,
        new ProviderCredentialReference(CredentialReference.Value),
        ThinkingEnabled,
        ThinkingEffort);
}
