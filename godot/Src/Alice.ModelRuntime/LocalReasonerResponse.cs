using System.Text.Json;
using Alice.Cognition;

namespace Alice.ModelRuntime;

/// <summary>Strict decoder for Choose, Defer, and RequestEscalation.</summary>
public static class LocalReasonerResponseDecoder
{
    private static readonly string[] RequiredProperties =
        ["decision", "candidate_id", "reason_code", "evidence_refs"];

    public static LocalReasonerCallAttempt Decode(string? rawContent)
    {
        if (rawContent is null) return Invalid();
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                rawContent,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow
                });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Invalid();
            JsonProperty[] properties = root.EnumerateObject().ToArray();
            if (properties.Length != RequiredProperties.Length
                || properties.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length
                || RequiredProperties.Any(value => !root.TryGetProperty(value, out _)))
                return Invalid();

            if (root.GetProperty("decision").ValueKind != JsonValueKind.String
                || root.GetProperty("candidate_id").ValueKind != JsonValueKind.String
                || root.GetProperty("reason_code").ValueKind != JsonValueKind.String
                || root.GetProperty("evidence_refs").ValueKind != JsonValueKind.Array)
                return Invalid();

            string decision = root.GetProperty("decision").GetString()!;
            string candidateId = root.GetProperty("candidate_id").GetString()!;
            string reasonCode = root.GetProperty("reason_code").GetString()!;
            var evidenceRefs = new List<string>();
            foreach (JsonElement value in root.GetProperty("evidence_refs").EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) return Invalid();
                evidenceRefs.Add(value.GetString()!);
            }

            return decision switch
            {
                "choose" when !string.IsNullOrWhiteSpace(candidateId)
                    && reasonCode.Length == 0 && evidenceRefs.Count == 0 =>
                    new LocalReasonerChoiceProduced(new LocalReasonerChoice(new LocalCandidateId(candidateId))),
                "defer" when candidateId.Length == 0
                    && !string.IsNullOrWhiteSpace(reasonCode) && evidenceRefs.Count == 0 =>
                    new LocalReasonerDeferProduced(new LocalReasonerDefer(reasonCode)),
                "request_escalation" when candidateId.Length == 0
                    && !string.IsNullOrWhiteSpace(reasonCode) && evidenceRefs.Count > 0 =>
                    new LocalReasonerEscalationRequested(
                        new LocalReasonerEscalationRequest(reasonCode, evidenceRefs)),
                _ => Invalid()
            };
        }
        catch (JsonException) { return Invalid(); }
        catch (ArgumentException) { return Invalid(); }
    }

    private static LocalReasonerCallFailed Invalid() =>
        new(LocalReasonerCallFailureKind.InvalidStructuredOutput);
}

/// <summary>One immutable response correlated only through an existing canonical request.</summary>
public sealed class LocalReasonerResponse
{
    private LocalReasonerResponse(
        LocalReasonerRequestBinding binding,
        string? rawContent,
        LocalReasonerCallAttempt attempt)
    {
        Binding = binding;
        RawContent = rawContent;
        Attempt = attempt;
    }

    public LocalReasonerRequestBinding Binding { get; }
    public string? RawContent { get; }
    public LocalReasonerCallAttempt Attempt { get; }

    public static LocalReasonerResponse FromRawContent(LocalReasonerRequest request, string? rawContent)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new LocalReasonerResponse(request.Binding, rawContent, LocalReasonerResponseDecoder.Decode(rawContent));
    }

    public static LocalReasonerResponse InvocationFailed(LocalReasonerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new LocalReasonerResponse(
            request.Binding,
            null,
            new LocalReasonerCallFailed(LocalReasonerCallFailureKind.InvocationFailed));
    }
}
