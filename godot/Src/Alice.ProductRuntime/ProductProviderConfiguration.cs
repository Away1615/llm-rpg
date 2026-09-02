using System.Text.Json.Serialization;
using Alice.ModelRuntime;

namespace Alice.ProductRuntime;

public sealed record ProviderQueueConfiguration
{
    [JsonRequired, JsonPropertyName("admitted_capacity")] public int AdmittedCapacity { get; init; }
    [JsonRequired, JsonPropertyName("max_in_flight")] public int MaxInFlight { get; init; }
    [JsonRequired, JsonPropertyName("retry_backoff_ms")] public int[] RetryBackoffMilliseconds { get; init; } = [];
    [JsonRequired, JsonPropertyName("retryable_failure_codes")] public string[] RetryableFailureCodes { get; init; } = [];
    [JsonRequired, JsonPropertyName("max_context_tokens")] public int MaxContextTokens { get; init; }
    [JsonRequired, JsonPropertyName("max_output_tokens")] public int MaxOutputTokens { get; init; }
}

public sealed record ProviderProfileConfiguration
{
    [JsonRequired, JsonPropertyName("transport_protocol")] public string TransportProtocol { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("profile_id")] public string ProfileId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("endpoint")] public string Endpoint { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("model_id")] public string ModelId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("timeout_ms")] public int TimeoutMilliseconds { get; init; }
    [JsonRequired, JsonPropertyName("max_response_body_bytes")] public int MaxResponseBodyBytes { get; init; }
    [JsonPropertyName("credential_environment_variable")] public string? CredentialEnvironmentVariable { get; init; }
    [JsonRequired, JsonPropertyName("disable_thinking")] public bool DisableThinking { get; init; }
    [JsonPropertyName("thinking_effort")] public string? ThinkingEffort { get; init; }
}

public sealed record ProviderProfilesConfiguration
{
    [JsonRequired, JsonPropertyName("local_reasoner")] public ProviderProfileConfiguration LocalReasoner { get; init; } = new();
    [JsonRequired, JsonPropertyName("remote_planner")] public ProviderProfileConfiguration RemotePlanner { get; init; } = new();
}

public sealed record ProductDialogueConfiguration
{
    [JsonRequired, JsonPropertyName("surface_profile_path")] public string SurfaceProfilePath { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("player_actor_id")] public string PlayerActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("npc_actor_id")] public string NpcActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("default_topic_ref")] public string DefaultTopicRef { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("max_session_turns")] public int? MaxSessionTurns { get; init; }
    [JsonRequired, JsonPropertyName("max_session_elapsed_ticks")] public long? MaxSessionElapsedTicks { get; init; }
}

public static class ProductModelClientComposition
{
    public static OpenAiCompatibleProviderProfile CreateLocalProfile(
        ProviderProfilesConfiguration profiles,
        ProviderQueueConfiguration queue) =>
        CreateProfile(profiles.LocalReasoner, queue.MaxOutputTokens, false);

    public static IModelClient<RemotePlannerResponse> CreateRemotePlanner(
        HttpClient httpClient,
        ProviderProfilesConfiguration profiles,
        ProviderQueueConfiguration queue,
        OpenAiCompatibleThinkingMode? thinkingMode = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ProviderProfileConfiguration source = profiles.RemotePlanner;
        if (source.TransportProtocol == "deepseek_anthropic_messages")
        {
            AnthropicMessagesProviderProfile profile = CreateAnthropicProfile(source, queue, thinkingMode);
            ProviderApiKeyLoadResult key = ProviderApiKey.LoadFromEnvironment(profile.CredentialReference);
            return key.ApiKey is null
                ? new FixedUnavailableModelClient<RemotePlannerResponse>(
                    ModelClientExecutionMode.LiveRemote,
                    ModelClientUnavailableReason.MissingCredential)
                : new AnthropicMessagesRemotePlannerClient(httpClient, profile, key.ApiKey);
        }

        OpenAiCompatibleProviderProfile openAiProfile = CreateProfile(
            source,
            queue.MaxOutputTokens,
            true,
            thinkingMode);
        ProviderApiKeyLoadResult openAiKey = ProviderApiKey.LoadFromEnvironment(
            openAiProfile.CredentialReference!);
        return openAiKey.ApiKey is null
            ? new FixedUnavailableModelClient<RemotePlannerResponse>(
                ModelClientExecutionMode.LiveRemote,
                ModelClientUnavailableReason.MissingCredential)
            : new LiveRemotePlannerClient(httpClient, openAiProfile, openAiKey.ApiKey);
    }

    public static IPlayerUtteranceInterpreter CreateDialogueInterpreter(
        HttpClient httpClient,
        ProviderProfilesConfiguration profiles,
        ProviderQueueConfiguration queue,
        string unavailableMessage)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        var local = new LiveLocalPlayerUtteranceInterpreter(
            httpClient,
            CreateLocalProfile(profiles, queue),
            unavailableMessage);
        ProviderProfileConfiguration source = profiles.RemotePlanner;
        if (source.TransportProtocol != "deepseek_anthropic_messages") return local;
        AnthropicMessagesProviderProfile remoteProfile = CreateAnthropicProfile(source, queue, null);
        ProviderApiKeyLoadResult key = ProviderApiKey.LoadFromEnvironment(remoteProfile.CredentialReference);
        if (key.ApiKey is null) return local;
        return new FallbackPlayerUtteranceInterpreter(
            local,
            new LiveRemotePlayerUtteranceInterpreter(
                httpClient,
                remoteProfile,
                key.ApiKey,
                unavailableMessage));
    }

    private static AnthropicMessagesProviderProfile CreateAnthropicProfile(
        ProviderProfileConfiguration source,
        ProviderQueueConfiguration queue,
        OpenAiCompatibleThinkingMode? thinkingMode)
    {
        bool thinkingEnabled = (thinkingMode ?? (source.DisableThinking
            ? OpenAiCompatibleThinkingMode.Disabled
            : OpenAiCompatibleThinkingMode.Enabled)) != OpenAiCompatibleThinkingMode.Disabled;
        AnthropicThinkingEffort effort = source.ThinkingEffort switch
        {
            "high" => AnthropicThinkingEffort.High,
            "max" => AnthropicThinkingEffort.Max,
            _ => throw new InvalidOperationException("DeepSeek Anthropic thinking effort is not configured.")
        };
        return new AnthropicMessagesProviderProfile(
            new AnthropicMessagesProfileId(source.ProfileId),
            new Uri(source.Endpoint, UriKind.Absolute),
            new AnthropicMessagesModelId(source.ModelId),
            TimeSpan.FromMilliseconds(source.TimeoutMilliseconds),
            queue.MaxOutputTokens,
            source.MaxResponseBodyBytes,
            new ProviderCredentialReference(source.CredentialEnvironmentVariable!),
            thinkingEnabled,
            effort);
    }

    private static OpenAiCompatibleProviderProfile CreateProfile(
        ProviderProfileConfiguration source,
        int maxOutputTokens,
        bool remote,
        OpenAiCompatibleThinkingMode? thinkingMode = null)
    {
        ProviderCredentialReference? credential = remote
            ? new ProviderCredentialReference(source.CredentialEnvironmentVariable!)
            : null;
        return new OpenAiCompatibleProviderProfile(
            new OpenAiCompatibleProfileId(source.ProfileId),
            new OpenAiCompatibleEndpoint(new Uri(source.Endpoint, UriKind.Absolute)),
            new OpenAiCompatibleModelId(source.ModelId),
            remote
                ? new OpenAiCompatibleCapabilities(false, true, true, false, false)
                : new OpenAiCompatibleCapabilities(true, false, false, false, false),
            TimeSpan.FromMilliseconds(source.TimeoutMilliseconds),
            maxOutputTokens,
            source.MaxResponseBodyBytes,
            credential,
            thinkingMode ?? (source.DisableThinking
                ? OpenAiCompatibleThinkingMode.Disabled
                : remote
                    ? OpenAiCompatibleThinkingMode.Enabled
                    : OpenAiCompatibleThinkingMode.ProviderDefault));
    }
}
