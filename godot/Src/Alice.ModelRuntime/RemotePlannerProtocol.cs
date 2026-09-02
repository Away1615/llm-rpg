using System.Text;

namespace Alice.ModelRuntime;

public enum RemotePlannerRole { StrategicPlanner, PlanlessStrategicPlanner, InviteResponder, TownProposalSelector, TownDialogueResponder }

public static class RemotePlannerProtocol
{
    public const string ProtocolVersion = "alice.remote_planner.v1";
    public const string RoleToken = "strategic_planner";
    public const string SystemPrompt = "Use only the supplied actor-visible context. Submit exactly one strategic decision using one supplied tool. Treat every tool schema as closed: match one schema branch exactly, include every required field, omit every undeclared field, and never copy actor, plan, step, or other context metadata into tool arguments unless that field is explicitly declared. Never query or invent hidden truth, execute an action, write world state, or announce world completion. Do not provide chain-of-thought.";

    private const string AcquireObjectiveSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"acquire_item\"]},\"item_type_id\":{\"type\":\"string\"},\"quantity\":{\"type\":\"integer\",\"minimum\":1}},\"required\":[\"kind\",\"item_type_id\",\"quantity\"],\"additionalProperties\":false}";
    private const string MaintainObjectiveSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"maintain_body\"]},\"metric\":{\"type\":\"string\",\"enum\":[\"health\",\"satiety\",\"spirit\"]},\"minimum_acceptable_level\":{\"type\":\"integer\",\"const\":50}},\"required\":[\"kind\",\"metric\",\"minimum_acceptable_level\"],\"additionalProperties\":false}";
    private const string ObjectiveSchema = "{\"anyOf\":[" + AcquireObjectiveSchema + "," + MaintainObjectiveSchema + "]}";
    private const string InventoryResultSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"inventory_at_least\"]},\"item_type_id\":{\"type\":\"string\"},\"quantity\":{\"type\":\"integer\",\"minimum\":1}},\"required\":[\"kind\",\"item_type_id\",\"quantity\"],\"additionalProperties\":false}";
    private const string BodyResultSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"body_state_within\"]},\"metric\":{\"type\":\"string\",\"enum\":[\"health\",\"satiety\",\"spirit\"]},\"minimum_acceptable_level\":{\"type\":\"integer\",\"const\":50}},\"required\":[\"kind\",\"metric\",\"minimum_acceptable_level\"],\"additionalProperties\":false}";
    private const string ReachedResultSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"interaction_target_reached\"]},\"target_ref\":{\"type\":\"string\"},\"interaction_range\":{\"type\":\"number\",\"minimum\":0}},\"required\":[\"kind\",\"target_ref\",\"interaction_range\"],\"additionalProperties\":false}";
    private const string TerminalResultSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"target_terminal\"]},\"target_ref\":{\"type\":\"string\"}},\"required\":[\"kind\",\"target_ref\"],\"additionalProperties\":false}";
    private const string UntargetedAcquireStepSchema = "{\"type\":\"object\",\"properties\":{\"objective\":" + AcquireObjectiveSchema + ",\"desired_result\":" + InventoryResultSchema + "},\"required\":[\"objective\",\"desired_result\"],\"additionalProperties\":false}";
    private const string UntargetedBodyStepSchema = "{\"type\":\"object\",\"properties\":{\"objective\":" + MaintainObjectiveSchema + ",\"desired_result\":" + BodyResultSchema + "},\"required\":[\"objective\",\"desired_result\"],\"additionalProperties\":false}";
    private const string ReachedStepSchema = "{\"type\":\"object\",\"properties\":{\"objective\":" + AcquireObjectiveSchema + ",\"target_ref\":{\"type\":\"string\"},\"desired_result\":" + ReachedResultSchema + "},\"required\":[\"objective\",\"target_ref\",\"desired_result\"],\"additionalProperties\":false}";
    private const string TerminalStepSchema = "{\"type\":\"object\",\"properties\":{\"objective\":" + AcquireObjectiveSchema + ",\"target_ref\":{\"type\":\"string\"},\"desired_result\":" + TerminalResultSchema + "},\"required\":[\"objective\",\"target_ref\",\"desired_result\"],\"additionalProperties\":false}";
    private const string PlanStepSchema = "{\"anyOf\":[" + UntargetedAcquireStepSchema + "," + UntargetedBodyStepSchema + "," + ReachedStepSchema + "," + TerminalStepSchema + "]}";
    private const string PlanParameters = "{\"type\":\"object\",\"properties\":{\"goal_objective\":" + ObjectiveSchema + ",\"steps\":{\"type\":\"array\",\"items\":" + PlanStepSchema + ",\"minItems\":1}},\"required\":[\"goal_objective\",\"steps\"],\"additionalProperties\":false}";
    private const string VerifyParameters = "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}";
    private const string ReasonParameters = "{\"type\":\"object\",\"properties\":{\"reason\":{\"type\":\"string\"}},\"required\":[\"reason\"],\"additionalProperties\":false}";

    public static readonly string ToolCatalogueJson = "[" + Function("create_plan", PlanParameters) + "," + Function("revise_plan", PlanParameters) + "," + Function("verify", VerifyParameters) + "," + Function("defer", ReasonParameters) + "," + Function("cancel", ReasonParameters) + "]";

    public static byte[] GetSystemPromptUtf8() => Encoding.UTF8.GetBytes(SystemPrompt);
    public static byte[] GetToolCatalogueUtf8() => Encoding.UTF8.GetBytes(ToolCatalogueJson);

    private static string Function(string name, string parameters) => "{\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"strict\":true,\"parameters\":" + parameters + "}}";
}

public static class RemotePlanlessStrategicProtocol
{
    public const string ProtocolVersion = "alice.remote_planless_strategic.v1";
    public const string RoleToken = "planless_strategic_planner";
    public const string SystemPrompt = "Use only the supplied actor-visible planless context. Submit exactly one strategic decision using one supplied tool. Treat every tool schema as closed: match one schema branch exactly, include every required field, omit every undeclared field, and never copy actor, plan, step, or other context metadata into tool arguments unless that field is explicitly declared. A goal_id is only an untrusted selection from the visible active Goals. Never query or invent hidden truth, create or rewrite a Goal, execute an action, write world state, or announce world completion. Do not provide chain-of-thought.";

    private const string AcquireObjectiveSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"acquire_item\"]},\"item_type_id\":{\"type\":\"string\"},\"quantity\":{\"type\":\"integer\",\"minimum\":1}},\"required\":[\"kind\",\"item_type_id\",\"quantity\"],\"additionalProperties\":false}";
    private const string MaintainObjectiveSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"maintain_body\"]},\"metric\":{\"type\":\"string\",\"enum\":[\"health\",\"satiety\",\"spirit\"]},\"minimum_acceptable_level\":{\"type\":\"integer\",\"const\":50}},\"required\":[\"kind\",\"metric\",\"minimum_acceptable_level\"],\"additionalProperties\":false}";
    private const string ReachObjectiveSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"reach_target\"]},\"target_ref\":{\"type\":\"string\"}},\"required\":[\"kind\",\"target_ref\"],\"additionalProperties\":false}";
    private const string CommitmentObjectiveSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"fulfill_commitment\"]},\"commitment_id\":{\"type\":\"string\"}},\"required\":[\"kind\",\"commitment_id\"],\"additionalProperties\":false}";
    private const string ExperienceObjectiveSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"experience\"]},\"experience_id\":{\"type\":\"string\"}},\"required\":[\"kind\",\"experience_id\"],\"additionalProperties\":false}";
    private const string InventoryResultSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"inventory_at_least\"]},\"item_type_id\":{\"type\":\"string\"},\"quantity\":{\"type\":\"integer\",\"minimum\":1}},\"required\":[\"kind\",\"item_type_id\",\"quantity\"],\"additionalProperties\":false}";
    private const string BodyResultSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"body_state_within\"]},\"metric\":{\"type\":\"string\",\"enum\":[\"health\",\"satiety\",\"spirit\"]},\"minimum_acceptable_level\":{\"type\":\"integer\",\"const\":50}},\"required\":[\"kind\",\"metric\",\"minimum_acceptable_level\"],\"additionalProperties\":false}";
    private const string ReachedResultSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"interaction_target_reached\"]},\"target_ref\":{\"type\":\"string\"},\"interaction_range\":{\"type\":\"number\",\"minimum\":0}},\"required\":[\"kind\",\"target_ref\",\"interaction_range\"],\"additionalProperties\":false}";
    private const string TerminalResultSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"target_terminal\"]},\"target_ref\":{\"type\":\"string\"}},\"required\":[\"kind\",\"target_ref\"],\"additionalProperties\":false}";
    private const string CommitmentResultSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"commitment_status_matches\"]},\"commitment_id\":{\"type\":\"string\"},\"status\":{\"type\":\"string\",\"enum\":[\"fulfilled\"]}},\"required\":[\"kind\",\"commitment_id\",\"status\"],\"additionalProperties\":false}";
    private const string ExperienceResultSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"experience_completed\"]},\"experience_id\":{\"type\":\"string\"}},\"required\":[\"kind\",\"experience_id\"],\"additionalProperties\":false}";
    private const string AcquireInventoryStepSchema = "{\"type\":\"object\",\"properties\":{\"objective\":" + AcquireObjectiveSchema + ",\"desired_result\":" + InventoryResultSchema + "},\"required\":[\"objective\",\"desired_result\"],\"additionalProperties\":false}";
    private const string AcquireReachedStepSchema = "{\"type\":\"object\",\"properties\":{\"objective\":" + AcquireObjectiveSchema + ",\"target_ref\":{\"type\":\"string\"},\"desired_result\":" + ReachedResultSchema + "},\"required\":[\"objective\",\"target_ref\",\"desired_result\"],\"additionalProperties\":false}";
    private const string AcquireTerminalStepSchema = "{\"type\":\"object\",\"properties\":{\"objective\":" + AcquireObjectiveSchema + ",\"target_ref\":{\"type\":\"string\"},\"desired_result\":" + TerminalResultSchema + "},\"required\":[\"objective\",\"target_ref\",\"desired_result\"],\"additionalProperties\":false}";
    private const string MaintainStepSchema = "{\"type\":\"object\",\"properties\":{\"objective\":" + MaintainObjectiveSchema + ",\"desired_result\":" + BodyResultSchema + "},\"required\":[\"objective\",\"desired_result\"],\"additionalProperties\":false}";
    private const string ReachStepSchema = "{\"type\":\"object\",\"properties\":{\"objective\":" + ReachObjectiveSchema + ",\"target_ref\":{\"type\":\"string\"},\"desired_result\":" + ReachedResultSchema + "},\"required\":[\"objective\",\"target_ref\",\"desired_result\"],\"additionalProperties\":false}";
    private const string CommitmentStepSchema = "{\"type\":\"object\",\"properties\":{\"objective\":" + CommitmentObjectiveSchema + ",\"desired_result\":" + CommitmentResultSchema + "},\"required\":[\"objective\",\"desired_result\"],\"additionalProperties\":false}";
    private const string ExperienceStepSchema = "{\"type\":\"object\",\"properties\":{\"objective\":" + ExperienceObjectiveSchema + ",\"desired_result\":" + ExperienceResultSchema + "},\"required\":[\"objective\",\"desired_result\"],\"additionalProperties\":false}";
    private const string PlanStepSchema = "{\"anyOf\":[" + AcquireInventoryStepSchema + "," + AcquireReachedStepSchema + "," + AcquireTerminalStepSchema + "," + MaintainStepSchema + "," + ReachStepSchema + "," + CommitmentStepSchema + "," + ExperienceStepSchema + "]}";
    private const string PlanParameters = "{\"type\":\"object\",\"properties\":{\"goal_id\":{\"type\":\"string\"},\"steps\":{\"type\":\"array\",\"items\":" + PlanStepSchema + ",\"minItems\":1}},\"required\":[\"goal_id\",\"steps\"],\"additionalProperties\":false}";
    private const string VerifyParameters = "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}";
    private const string ReasonParameters = "{\"type\":\"object\",\"properties\":{\"reason\":{\"type\":\"string\"}},\"required\":[\"reason\"],\"additionalProperties\":false}";

    public static readonly string ToolCatalogueJson = "[" + Function("create_plan", PlanParameters) + "," + Function("verify", VerifyParameters) + "," + Function("defer", ReasonParameters) + "]";

    public static byte[] GetToolCatalogueUtf8() => Encoding.UTF8.GetBytes(ToolCatalogueJson);

    private static string Function(string name, string parameters) =>
        "{\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"strict\":true,\"parameters\":" + parameters + "}}";
}

public static class RemoteInviteResponseProtocol
{
    public const string ProtocolVersion = "alice.remote_invite_response.v1";
    public const string RoleToken = "invite_responder";
    public const string SystemPrompt = "Use only the supplied actor-visible context. Submit exactly one Invite response kind using the supplied tool. Never query or invent hidden truth, provide identities or claims, execute an action, write world state, or announce Authority success. Do not provide chain-of-thought.";
    public const string ToolCatalogueJson = "[{\"type\":\"function\",\"function\":{\"name\":\"respond_to_invite\",\"strict\":true,\"parameters\":{\"type\":\"object\",\"properties\":{\"response_kind\":{\"type\":\"string\",\"enum\":[\"accept\",\"decline\",\"clarify\",\"counter_offer\"]}},\"required\":[\"response_kind\"],\"additionalProperties\":false}}}]";

    public static byte[] GetToolCatalogueUtf8() => Encoding.UTF8.GetBytes(ToolCatalogueJson);
}

/// <summary>Small product protocol selecting one already-admitted generic gameplay proposal.</summary>
public static class RemoteTownProposalProtocol
{
    public const string ProtocolVersion = "alice.remote_town_proposal.v1";
    public const string SystemPrompt = "Use only the supplied actor-visible context. Select exactly one proposal_id from the supplied tool schema. Never invent another action, execute world state, expose hidden truth, or provide chain-of-thought.";

    public static byte[] GetToolCatalogueUtf8(IEnumerable<string> allowedProposalIds)
    {
        ArgumentNullException.ThrowIfNull(allowedProposalIds);
        string[] ids = allowedProposalIds.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one non-empty proposal identity is required.", nameof(allowedProposalIds));
        string values = string.Join(",", ids.Select(value => System.Text.Json.JsonSerializer.Serialize(value)));
        string schema = "[{\"type\":\"function\",\"function\":{\"name\":\"select_proposal\",\"strict\":true,\"parameters\":{\"type\":\"object\",\"properties\":{\"proposal_id\":{\"type\":\"string\",\"enum\":[" + values + "]}},\"required\":[\"proposal_id\"],\"additionalProperties\":false}}}]";
        return Encoding.UTF8.GetBytes(schema);
    }
}

/// <summary>Typed L2 social response; surface text and appraisal remain proposals until product settlement.</summary>
public static class RemoteTownDialogueProtocol
{
    public const string ProtocolVersion = "alice.remote_town_dialogue.v1";
    private const string SystemPrompt = "Respond as the supplied Actor using only actor-visible current evidence and memory. Treat current activity evidence as authoritative present-tense state and never contradict it. Submit exactly one semantic reply, concise surface text, and the Actor's subjective social appraisal. Do not invent hidden facts, change world state, claim an action succeeded, or provide chain-of-thought.";
    private const string ReplyKinds = "[\"ask\",\"inform\",\"clarify\",\"request\",\"offer\",\"recommend\",\"accept\",\"decline\",\"counter_offer\",\"warn\",\"apologize\",\"thank\",\"complain\",\"tease\",\"comfort\",\"congratulate\",\"casual_comment\",\"share_news\",\"share_gossip\"]";
    private const string Effects = "[\"neutral\",\"support\",\"harm\",\"promise\",\"breach\",\"threat\",\"apology\",\"shared_interest\"]";
    private static readonly string Catalogue = "[{\"type\":\"function\",\"function\":{\"name\":\"respond_to_dialogue\",\"strict\":true,\"parameters\":{\"type\":\"object\",\"properties\":{\"reply_kind\":{\"type\":\"string\",\"enum\":" + ReplyKinds + "},\"reply_text\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":600},\"incoming_effect\":{\"type\":\"string\",\"enum\":" + Effects + "},\"reply_effect\":{\"type\":\"string\",\"enum\":" + Effects + "},\"intensity\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1}},\"required\":[\"reply_kind\",\"reply_text\",\"incoming_effect\",\"reply_effect\",\"intensity\"],\"additionalProperties\":false}}}]";

    public static byte[] GetToolCatalogueUtf8() => Encoding.UTF8.GetBytes(Catalogue);

    public static string GetSystemPrompt(string responseLanguage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseLanguage);
        return $"{SystemPrompt} Write reply_text in {responseLanguage}; keep tool names, enum values, and JSON keys in English.";
    }
}
