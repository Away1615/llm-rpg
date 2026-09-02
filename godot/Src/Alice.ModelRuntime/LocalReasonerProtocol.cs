using System.Text;

namespace Alice.ModelRuntime;

/// <summary>The only model role admitted by the Phase 3 local-reasoner protocol.</summary>
public enum LocalReasonerRole
{
    LocalReasoner
}

/// <summary>Frozen transport-neutral tokens and model-visible instructions.</summary>
public static class LocalReasonerProtocol
{
    public const string ProtocolVersion = "alice.local_reasoner.v2";
    public const string RoleToken = "local_reasoner";
    public const string SystemPrompt =
        "The NPC's current goal and current plan step are fixed.\n" +
        "Choose one legal local option, defer, or request Host-validated strategic escalation.\n" +
        "Do not change the goal, create a long-term plan, create or cancel a major commitment, or invent facts.\n" +
        "Escalation requires an allowed reason code and actor-visible candidate IDs as evidence_refs; the Host alone creates DecisionNeed.\n" +
        "Return only one JSON object matching the supplied schema.";
    public const string OutputSchemaJson =
        "{\"type\":\"object\",\"properties\":{\"decision\":{\"type\":\"string\",\"enum\":[\"choose\",\"defer\",\"request_escalation\"]},\"candidate_id\":{\"type\":\"string\"},\"reason_code\":{\"type\":\"string\"},\"evidence_refs\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"decision\",\"candidate_id\",\"reason_code\",\"evidence_refs\"],\"additionalProperties\":false}";

    public static byte[] GetSystemPromptUtf8() => Encoding.UTF8.GetBytes(SystemPrompt);
    public static byte[] GetOutputSchemaUtf8() => Encoding.UTF8.GetBytes(OutputSchemaJson);

    internal static string RoleToToken(LocalReasonerRole role)
    {
        return role == LocalReasonerRole.LocalReasoner
            ? RoleToken
            : throw new ArgumentOutOfRangeException(nameof(role));
    }
}
