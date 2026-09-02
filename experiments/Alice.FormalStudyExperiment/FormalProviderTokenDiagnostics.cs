using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Alice.ModelRuntime;

internal sealed class FormalProviderAttemptCollector : IAnthropicMessagesProviderAttemptSink
{
    private readonly object _gate = new();
    private readonly List<AnthropicMessagesProviderAttemptTrace> _traces = [];

    public void Record(AnthropicMessagesProviderAttemptTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        lock (_gate) _traces.Add(trace);
    }

    public IReadOnlyList<AnthropicMessagesProviderAttemptTrace> Snapshot()
    {
        lock (_gate) return _traces.ToArray();
    }
}

internal sealed record FormalProviderAttemptCondition(
    string Treatment,
    string ScenarioId,
    string? Stratum,
    string? Tier,
    IReadOnlyList<AnthropicMessagesProviderAttemptTrace> Attempts);

internal static class FormalProviderAttemptSidecar
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void Write(
        string path,
        string rq,
        string pairId,
        IEnumerable<FormalProviderAttemptCondition> conditions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(rq);
        ArgumentException.ThrowIfNullOrWhiteSpace(pairId);
        ArgumentNullException.ThrowIfNull(conditions);
        using var output = new MemoryStream();
        foreach (FormalProviderAttemptCondition condition in conditions)
        {
            var attemptByRequest = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (AnthropicMessagesProviderAttemptTrace trace in condition.Attempts)
            {
                int attemptIndex = attemptByRequest.GetValueOrDefault(trace.RequestId) + 1;
                attemptByRequest[trace.RequestId] = attemptIndex;
                WriteRecord(output, rq, pairId, condition, trace, attemptIndex);
                output.WriteByte((byte)'\n');
            }
        }
        File.WriteAllBytes(path, output.ToArray());
    }

    private static void WriteRecord(
        Stream output,
        string rq,
        string pairId,
        FormalProviderAttemptCondition condition,
        AnthropicMessagesProviderAttemptTrace trace,
        int attemptIndex)
    {
        byte[] requestBody = trace.GetRequestBodyBytes();
        byte[]? responseBody = trace.GetResponseBodyBytes();
        long? inputTotal = trace.InputTokens is null
            ? null
            : trace.InputTokens.Value
                + (trace.CacheCreationInputTokens ?? 0)
                + (trace.CacheReadInputTokens ?? 0);
        long? total = inputTotal is null || trace.OutputTokens is null
            ? null
            : inputTotal.Value + trace.OutputTokens.Value;
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { SkipValidation = false });
        writer.WriteStartObject();
        writer.WriteString("schema_version", "alice.formal-provider-attempt.v1");
        writer.WriteString("rq", rq);
        writer.WriteString("pair_id", pairId);
        writer.WriteString("scenario_id", condition.ScenarioId);
        WriteNullableString(writer, "stratum", condition.Stratum);
        WriteNullableString(writer, "tier", condition.Tier);
        writer.WriteString("treatment", condition.Treatment);
        writer.WriteString("request_id", trace.RequestId);
        writer.WriteNumber("attempt_index", attemptIndex);
        writer.WriteString("outcome", trace.Outcome.ToString());
        WriteNullableString(writer, "failure_kind", trace.FailureKind?.ToString());
        WriteNullableNumber(writer, "http_status", trace.HttpStatus);
        writer.WriteNumber("duration_ms", trace.DurationMilliseconds);
        WriteNullableString(writer, "provider_response_id", trace.ProviderResponseId);
        WriteNullableNumber(writer, "input_tokens", trace.InputTokens);
        WriteNullableNumber(writer, "cache_creation_input_tokens", trace.CacheCreationInputTokens);
        WriteNullableNumber(writer, "cache_read_input_tokens", trace.CacheReadInputTokens);
        WriteNullableNumber(writer, "input_total_tokens", inputTotal);
        WriteNullableNumber(writer, "output_tokens_including_reasoning", trace.OutputTokens);
        WriteNullableNumber(writer, "reasoning_tokens", trace.ReasoningTokens);
        WriteNullableNumber(writer, "total_tokens_including_reasoning", total);
        writer.WriteBoolean(
            "output_limit_reached",
            trace.FailureKind == LiveRemoteFailureKind.OutputTokenLimitReached);
        writer.WriteString("request_body_sha256", Hash(requestBody));
        writer.WriteString("request_body_utf8", StrictUtf8.GetString(requestBody));
        if (responseBody is null)
        {
            writer.WriteNull("response_body_utf8");
            writer.WriteNull("response_body_sha256");
            writer.WritePropertyName("provider_returned_thinking");
            writer.WriteStartArray();
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteString("response_body_sha256", Hash(responseBody));
            writer.WriteString("response_body_utf8", StrictUtf8.GetString(responseBody));
            writer.WritePropertyName("provider_returned_thinking");
            writer.WriteStartArray();
            foreach (string thinking in ReadThinking(responseBody)) writer.WriteStringValue(thinking);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
        writer.Flush();
    }

    private static IReadOnlyList<string> ReadThinking(byte[] responseBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("content", out JsonElement content)
                || content.ValueKind != JsonValueKind.Array)
                return [];
            var values = new List<string>();
            foreach (JsonElement block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object
                    || !block.TryGetProperty("type", out JsonElement type)
                    || type.ValueKind != JsonValueKind.String
                    || !StringComparer.Ordinal.Equals(type.GetString(), "thinking"))
                    continue;
                if (block.TryGetProperty("thinking", out JsonElement thinking)
                    && thinking.ValueKind == JsonValueKind.String)
                    values.Add(thinking.GetString() ?? string.Empty);
            }
            return values;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, long? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteNumber(name, value.Value);
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteNumber(name, value.Value);
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal static class FormalProviderTokenReport
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static void WriteRq1(
        string outputRoot,
        IReadOnlyList<string> pairIds,
        IReadOnlyList<string> scenarioIds)
    {
        IReadOnlyList<TokenRow> rows = ReadRows(outputRoot);
        object[] scenarioTotals = scenarioIds.SelectMany(
            scenario => Treatments("AgentCentric", "EventCentric").Select(
                treatment => Summarize(
                    rows.Where(row => row.ScenarioId == scenario && row.Treatment == treatment),
                    new { scenario_id = scenario, treatment },
                    pairIds.Count)))
            .ToArray();
        object[] comparisons = pairIds.SelectMany(
            pair => scenarioIds.Select(scenario => Compare(
                pair,
                scenario,
                "AgentCentric",
                "EventCentric",
                rows)))
            .ToArray();
        WriteReport(
            Path.Combine(outputRoot, "rq1-token-usage.json"),
            "alice.formal-rq1-token-usage.v1",
            rows,
            scenarioTotals,
            comparisons,
            Treatments("AgentCentric", "EventCentric"));
    }

    public static void WriteRq2(string outputRoot)
    {
        IReadOnlyList<TokenRow> rows = ReadRows(outputRoot);
        string[] scenarios = rows.Select(row => row.ScenarioId).Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        string[] pairs = rows.Select(row => row.PairId).Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        object[] scenarioTotals = scenarios.SelectMany(
            scenario => Treatments("Verbatim", "Summary").Select(
                treatment => Summarize(
                    rows.Where(row => row.ScenarioId == scenario && row.Treatment == treatment),
                    new
                    {
                        scenario_id = scenario,
                        stratum = rows.First(row => row.ScenarioId == scenario).Stratum,
                        tier = rows.First(row => row.ScenarioId == scenario).Tier,
                        treatment
                    },
                    null)))
            .ToArray();
        object[] comparisons = pairs.Select(pair => Compare(
            pair,
            rows.First(row => row.PairId == pair).ScenarioId,
            "Verbatim",
            "Summary",
            rows)).ToArray();
        WriteReport(
            Path.Combine(outputRoot, "rq2-token-usage.json"),
            "alice.formal-rq2-token-usage.v1",
            rows,
            scenarioTotals,
            comparisons,
            Treatments("Verbatim", "Summary"));
    }

    private static void WriteReport(
        string path,
        string schema,
        IReadOnlyList<TokenRow> rows,
        object[] scenarioTotals,
        object[] comparisons,
        IReadOnlyList<string> treatments)
    {
        object[] treatmentTotals = treatments.Select(treatment => Summarize(
            rows.Where(row => row.Treatment == treatment),
            new { treatment },
            null)).ToArray();
        var report = new
        {
            schema_version = schema,
            token_semantics = new
            {
                input_total = "input_tokens + cache_creation_input_tokens + cache_read_input_tokens; absent cache fields count as zero",
                output = "Provider output_tokens; includes Provider-billed visible output and reasoning",
                total = "input_total + output; retries are included",
                unknown = "No-response attempts retain null usage and are never counted as zero"
            },
            treatment_totals = treatmentTotals,
            scenario_treatment_totals = scenarioTotals,
            matched_comparisons = comparisons
        };
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(report, Indented));
    }

    private static object Compare(
        string pairId,
        string scenarioId,
        string leftTreatment,
        string rightTreatment,
        IReadOnlyList<TokenRow> rows)
    {
        TokenTotals left = Totals(rows.Where(row =>
            row.PairId == pairId && row.ScenarioId == scenarioId && row.Treatment == leftTreatment));
        TokenTotals right = Totals(rows.Where(row =>
            row.PairId == pairId && row.ScenarioId == scenarioId && row.Treatment == rightTreatment));
        bool complete = left.UnknownUsageAttempts == 0 && right.UnknownUsageAttempts == 0;
        return new
        {
            pair_id = pairId,
            scenario_id = scenarioId,
            left_treatment = leftTreatment,
            right_treatment = rightTreatment,
            comparison_complete = complete,
            left_known_total_tokens = left.TotalTokens,
            right_known_total_tokens = right.TotalTokens,
            right_minus_left_tokens = complete
                ? right.TotalTokens - left.TotalTokens
                : (long?)null,
            right_to_left_ratio = !complete || left.TotalTokens == 0
                ? (double?)null
                : (double)right.TotalTokens / left.TotalTokens,
            left_missed_due_to_budget = left.Attempts == 0,
            right_missed_due_to_budget = right.Attempts == 0,
            left_unknown_usage_attempts = left.UnknownUsageAttempts,
            right_unknown_usage_attempts = right.UnknownUsageAttempts
        };
    }

    private static object Summarize(
        IEnumerable<TokenRow> source,
        object key,
        int? expectedOpportunities)
    {
        TokenRow[] rows = source.ToArray();
        TokenTotals totals = Totals(rows);
        int logicalRequests = rows.Select(row => row.PairId + "\n" + row.RequestId)
            .Distinct(StringComparer.Ordinal).Count();
        return new
        {
            key,
            expected_opportunities = expectedOpportunities,
            logical_requests = logicalRequests,
            missed_due_to_budget = expectedOpportunities is null
                ? (int?)null
                : Math.Max(0, expectedOpportunities.Value - logicalRequests),
            attempts = rows.Length,
            known_usage_attempts = totals.KnownUsageAttempts,
            unknown_usage_attempts = totals.UnknownUsageAttempts,
            input_total_tokens = totals.InputTokens,
            output_tokens_including_reasoning = totals.OutputTokens,
            reasoning_tokens_when_reported = totals.ReasoningTokens,
            reasoning_usage_attempts = totals.ReasoningUsageAttempts,
            total_tokens_including_reasoning = totals.TotalTokens,
            output_limit_attempts = rows.Count(row => row.OutputLimitReached),
            exhausted_output_limit_requests = rows.GroupBy(
                    row => row.PairId + "\n" + row.RequestId,
                    StringComparer.Ordinal)
                .Count(group => group.Count() == 3 && group.All(row => row.OutputLimitReached))
        };
    }

    private static TokenTotals Totals(IEnumerable<TokenRow> source)
    {
        TokenRow[] rows = source.ToArray();
        return new TokenTotals(
            rows.Length,
            rows.Count(row => row.TotalTokens is not null),
            rows.Count(row => row.TotalTokens is null),
            rows.Where(row => row.InputTotalTokens is not null).Sum(row => row.InputTotalTokens!.Value),
            rows.Where(row => row.OutputTokens is not null).Sum(row => row.OutputTokens!.Value),
            rows.Where(row => row.ReasoningTokens is not null).Sum(row => row.ReasoningTokens!.Value),
            rows.Count(row => row.ReasoningTokens is not null),
            rows.Where(row => row.TotalTokens is not null).Sum(row => row.TotalTokens!.Value));
    }

    private static IReadOnlyList<TokenRow> ReadRows(string outputRoot)
    {
        var rows = new List<TokenRow>();
        foreach (string path in Directory.GetFiles(outputRoot, "*-provider-attempts.jsonl"))
        {
            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                rows.Add(new TokenRow(
                    root.GetProperty("pair_id").GetString()!,
                    root.GetProperty("scenario_id").GetString()!,
                    NullableString(root, "stratum"),
                    NullableString(root, "tier"),
                    root.GetProperty("treatment").GetString()!,
                    root.GetProperty("request_id").GetString()!,
                    NullableInt64(root, "input_total_tokens"),
                    NullableInt64(root, "output_tokens_including_reasoning"),
                    NullableInt64(root, "reasoning_tokens"),
                    NullableInt64(root, "total_tokens_including_reasoning"),
                    root.GetProperty("output_limit_reached").GetBoolean()));
            }
        }
        return rows;
    }

    private static long? NullableInt64(JsonElement root, string propertyName)
    {
        JsonElement value = root.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetInt64();
    }

    private static string? NullableString(JsonElement root, string propertyName)
    {
        JsonElement value = root.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static string[] Treatments(string first, string second) => [first, second];

    private sealed record TokenRow(
        string PairId,
        string ScenarioId,
        string? Stratum,
        string? Tier,
        string Treatment,
        string RequestId,
        long? InputTotalTokens,
        long? OutputTokens,
        long? ReasoningTokens,
        long? TotalTokens,
        bool OutputLimitReached);

    private sealed record TokenTotals(
        int Attempts,
        int KnownUsageAttempts,
        int UnknownUsageAttempts,
        long InputTokens,
        long OutputTokens,
        long ReasoningTokens,
        int ReasoningUsageAttempts,
        long TotalTokens);
}
