using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Alice.CognitiveLodDialogueExperiment;

internal sealed class ProviderRecordingHandler : DelegatingHandler
{
    private readonly StudyArtifactWriter _artifacts;

    public ProviderRecordingHandler(HttpMessageHandler innerHandler, StudyArtifactWriter artifacts)
        : base(innerHandler)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        byte[] requestBody = request.Content is null
            ? []
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        string role = DetectRole(requestBody);
        string? modelId = ReadModelId(requestBody);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            byte[] responseBody = response.Content is null
                ? []
                : await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            ReplaceResponseContent(response, responseBody);
            TokenUsageSnapshot? usage = ReadUsage(responseBody);
            _artifacts.AddAttempt(
                role,
                modelId,
                response.IsSuccessStatusCode ? "response_received" : "http_failure",
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                usage,
                requestBody,
                responseBody,
                response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
            return response;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            _artifacts.AddAttempt(
                role,
                modelId,
                "transport_failure",
                null,
                stopwatch.ElapsedMilliseconds,
                null,
                requestBody,
                null,
                exception.GetType().Name);
            throw;
        }
    }

    private static void ReplaceResponseContent(HttpResponseMessage response, byte[] bytes)
    {
        if (response.Content is null) return;
        var replacement = new ByteArrayContent(bytes);
        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
        response.Content.Dispose();
        response.Content = replacement;
    }

    private static string DetectRole(ReadOnlySpan<byte> requestBody)
    {
        string text = System.Text.Encoding.UTF8.GetString(requestBody);
        if (text.Contains("town_l1_dialogue_route", StringComparison.Ordinal)) return "dialogue_l1";
        if (text.Contains("town_l1_decision", StringComparison.Ordinal)) return "autonomy_l1";
        if (text.Contains("respond_to_dialogue", StringComparison.Ordinal)) return "dialogue_l2";
        if (text.Contains("create_plan", StringComparison.Ordinal)
            || text.Contains("revise_plan", StringComparison.Ordinal)) return "autonomy_l2";
        return "other_model_role";
    }

    private static string? ReadModelId(byte[] requestBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(requestBody);
            return document.RootElement.TryGetProperty("model", out JsonElement model)
                && model.ValueKind == JsonValueKind.String
                ? model.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TokenUsageSnapshot? ReadUsage(byte[] responseBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
                return null;
            long? localInput = OptionalLong(usage, "prompt_tokens");
            long? localOutput = OptionalLong(usage, "completion_tokens");
            if (localInput is not null || localOutput is not null)
            {
                return new TokenUsageSnapshot(
                    localInput,
                    localOutput,
                    OptionalNestedLong(usage, "completion_tokens_details", "reasoning_tokens"),
                    OptionalNestedLong(usage, "prompt_tokens_details", "cached_tokens"),
                    null,
                    OptionalLong(usage, "total_tokens"));
            }
            long? input = OptionalLong(usage, "input_tokens");
            long? output = OptionalLong(usage, "output_tokens");
            long? cacheRead = OptionalLong(usage, "cache_read_input_tokens");
            long? cacheCreation = OptionalLong(usage, "cache_creation_input_tokens");
            long? reasoning = OptionalNestedLong(usage, "output_tokens_details", "reasoning_tokens")
                ?? OptionalNestedLong(usage, "output_tokens_details", "thinking_tokens");
            long? total = SumKnown(input, output, cacheRead, cacheCreation);
            return new TokenUsageSnapshot(input, output, reasoning, cacheRead, cacheCreation, total);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long? OptionalLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out long parsed)
            || parsed < 0)
            return null;
        return parsed;
    }

    private static long? OptionalNestedLong(JsonElement parent, string objectName, string name)
    {
        if (!parent.TryGetProperty(objectName, out JsonElement nested)
            || nested.ValueKind != JsonValueKind.Object)
            return null;
        return OptionalLong(nested, name);
    }

    private static long? SumKnown(params long?[] values)
    {
        long total = 0;
        bool any = false;
        foreach (long? value in values)
        {
            if (value is null) continue;
            any = true;
            total = checked(total + value.Value);
        }
        return any ? total : null;
    }
}
