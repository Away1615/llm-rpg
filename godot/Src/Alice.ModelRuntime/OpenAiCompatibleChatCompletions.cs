using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Alice.ModelRuntime;

internal sealed record BoundedResponseBodyReadResult(bool IsComplete, string? Body, byte[]? RawBytes)
{
    public static BoundedResponseBodyReadResult Complete(byte[] bytes) =>
        new(true, Encoding.UTF8.GetString(bytes), bytes.ToArray());
    public static BoundedResponseBodyReadResult TooLarge() => new(false, null, null);
}

/// <summary>Exact adopted LocalReasoner Chat Completions request/envelope boundary.</summary>
internal static class OpenAiCompatibleChatCompletions
{
    public static async ValueTask<BoundedResponseBodyReadResult> ReadResponseBodyAsync(
        HttpContent content,
        int maxResponseBodyBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (maxResponseBodyBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResponseBodyBytes));
        }

        if (content.Headers.ContentLength > maxResponseBodyBytes)
        {
            return BoundedResponseBodyReadResult.TooLarge();
        }

        await using Stream source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var body = new MemoryStream(Math.Min(maxResponseBodyBytes, 81920));
        byte[] chunk = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                int count = await source.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    return BoundedResponseBodyReadResult.Complete(body.ToArray());
                }

                if (body.Length + count > maxResponseBodyBytes)
                {
                    return BoundedResponseBodyReadResult.TooLarge();
                }

                await body.WriteAsync(chunk.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    public static HttpRequestMessage CreateStructuredRequest(
        OpenAiCompatibleProviderProfile profile,
        string systemPrompt,
        string userJson,
        string outputSchemaJson,
        string schemaName)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputSchemaJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        byte[] body = WriteStructuredRequestBody(
            profile, systemPrompt, userJson, outputSchemaJson, schemaName);
        var message = new HttpRequestMessage(HttpMethod.Post, profile.ChatCompletionsEndpoint.Value);
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        message.Content = content;
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return message;
    }

    public static HttpRequestMessage CreateRequest(
        OpenAiCompatibleProviderProfile profile,
        RemotePlannerRequest request,
        ProviderApiKey apiKey)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(apiKey);
        byte[] body = WriteRequestBody(profile, request);
        var message = new HttpRequestMessage(HttpMethod.Post, profile.ChatCompletionsEndpoint.Value);
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        message.Content = content;
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        apiKey.ApplyBearerCredential(message);
        return message;
    }

    public static bool TryReadAssistantContent(string responseBody, out string? content)
    {
        ArgumentNullException.ThrowIfNull(responseBody);
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("choices", out JsonElement choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() != 1)
            {
                content = null;
                return false;
            }

            JsonElement choice = choices[0];
            if (choice.ValueKind != JsonValueKind.Object ||
                !choice.TryGetProperty("finish_reason", out JsonElement finishReason) ||
                finishReason.ValueKind != JsonValueKind.String ||
                !StringComparer.Ordinal.Equals(finishReason.GetString(), "stop") ||
                !choice.TryGetProperty("message", out JsonElement message) ||
                message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("content", out JsonElement contentElement) ||
                contentElement.ValueKind != JsonValueKind.String)
            {
                content = null;
                return false;
            }

            content = contentElement.GetString();
            return content is not null;
        }
        catch (JsonException)
        {
            content = null;
            return false;
        }
    }

    public static bool TryReadRemotePlannerToolCalls(
        string responseBody,
        out IReadOnlyList<RemotePlannerToolCall>? toolCalls)
    {
        ArgumentNullException.ThrowIfNull(responseBody);
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("choices", out JsonElement choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() != 1)
            {
                toolCalls = null;
                return false;
            }

            JsonElement choice = choices[0];
            if (choice.ValueKind != JsonValueKind.Object ||
                !choice.TryGetProperty("finish_reason", out JsonElement finishReason) ||
                finishReason.ValueKind != JsonValueKind.String ||
                !StringComparer.Ordinal.Equals(finishReason.GetString(), "tool_calls") ||
                !choice.TryGetProperty("message", out JsonElement message) ||
                message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("tool_calls", out JsonElement calls) ||
                calls.ValueKind != JsonValueKind.Array)
            {
                toolCalls = null;
                return false;
            }

            var parsed = new List<RemotePlannerToolCall>();
            foreach (JsonElement call in calls.EnumerateArray())
            {
                if (call.ValueKind != JsonValueKind.Object ||
                    !call.TryGetProperty("function", out JsonElement function) ||
                    function.ValueKind != JsonValueKind.Object ||
                    !function.TryGetProperty("name", out JsonElement name) ||
                    name.ValueKind != JsonValueKind.String ||
                    !function.TryGetProperty("arguments", out JsonElement arguments) ||
                    arguments.ValueKind != JsonValueKind.String)
                {
                    toolCalls = null;
                    return false;
                }

                parsed.Add(new RemotePlannerToolCall(name.GetString()!, arguments.GetString()));
            }

            toolCalls = parsed.AsReadOnly();
            return true;
        }
        catch (JsonException)
        {
            toolCalls = null;
            return false;
        }
    }

    private static byte[] WriteStructuredRequestBody(
        OpenAiCompatibleProviderProfile profile,
        string systemPrompt,
        string userJson,
        string outputSchemaJson,
        string schemaName)
    {
        using var buffer = new MemoryStream();
        using JsonDocument schema = JsonDocument.Parse(outputSchemaJson);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("model", profile.ModelId.Value);
            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            WriteMessage(writer, "system", systemPrompt);
            WriteMessage(writer, "user", userJson);
            writer.WriteEndArray();
            writer.WritePropertyName("response_format");
            writer.WriteStartObject();
            writer.WriteString("type", "json_schema");
            writer.WritePropertyName("json_schema");
            writer.WriteStartObject();
            writer.WriteString("name", schemaName);
            writer.WriteBoolean("strict", true);
            writer.WritePropertyName("schema");
            schema.RootElement.WriteTo(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteNumber("max_tokens", profile.MaxTokens);
            writer.WriteBoolean("stream", false);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static byte[] WriteRequestBody(
        OpenAiCompatibleProviderProfile profile,
        RemotePlannerRequest request)
    {
        using var buffer = new MemoryStream();
        using JsonDocument tools = JsonDocument.Parse(request.GetToolCatalogueUtf8());
        string userContent = new UTF8Encoding(false, true).GetString(request.GetModelVisibleBytes());
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("model", profile.ModelId.Value);
            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            WriteMessage(writer, "system", request.SystemPrompt);
            WriteMessage(writer, "user", userContent);
            writer.WriteEndArray();
            writer.WritePropertyName("tools");
            tools.RootElement.WriteTo(writer);
            if (profile.ThinkingMode != OpenAiCompatibleThinkingMode.Enabled)
            {
                writer.WriteString("tool_choice", "required");
            }
            if (profile.ThinkingMode is OpenAiCompatibleThinkingMode.Enabled
                or OpenAiCompatibleThinkingMode.Disabled)
            {
                writer.WritePropertyName("thinking");
                writer.WriteStartObject();
                writer.WriteString(
                    "type",
                    profile.ThinkingMode == OpenAiCompatibleThinkingMode.Enabled
                        ? "enabled"
                        : "disabled");
                writer.WriteEndObject();
            }

            writer.WriteNumber("max_tokens", profile.MaxTokens);
            if (profile.Capabilities.SupportsParallelToolCalls)
            {
                writer.WriteBoolean("parallel_tool_calls", false);
            }

            writer.WriteBoolean("stream", false);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteMessage(Utf8JsonWriter writer, string role, string content)
    {
        writer.WriteStartObject();
        writer.WriteString("role", role);
        writer.WriteString("content", content);
        writer.WriteEndObject();
    }
}
