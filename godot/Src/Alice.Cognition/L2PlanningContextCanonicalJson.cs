using System.Security.Cryptography;
using System.Text.Json;
using Alice.Actors;
using Alice.Capabilities;
using Alice.Commitments;
using Alice.Damage;
using Alice.Interaction;
using Alice.Items;
using Alice.Navigation;
using Alice.Npc;
using Alice.World;

namespace Alice.Cognition;

public static class L2PlanningContextCanonicalJson
{
    public const string ProtocolVersion = "l2-planning-context-v1";
    public const string InviteResponseProtocolVersion = "l2-invite-response-context-v1";
    public const string PlanlessStrategicProtocolVersion = "l2-planless-strategic-context-v1";

    public static byte[] SerializeShared(ActorCognitionView view, DecisionProblemDescriptor descriptor)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", ProtocolVersion);
            WriteIdentity(writer, view.Self.Identity);
            WritePersonality(writer, view.Personality);
            writer.WritePropertyName("current_problem");
            writer.WriteRawValue(descriptor.GetCanonicalBytes(), skipInputValidation: false);
            WriteSelf(writer, view.Self);
            WriteGoals(writer, view.ActiveGoals);
            WritePlan(writer, view.CurrentPlan);
            writer.WritePropertyName("current_step");
            WriteStep(writer, view.CurrentStep);
            WriteKnowledge(writer, view.Knowledge);
            writer.WritePropertyName("strategic_decision_kinds");
            writer.WriteStartArray();
            writer.WriteStringValue("create_plan"); writer.WriteStringValue("revise_plan"); writer.WriteStringValue("verify"); writer.WriteStringValue("defer"); writer.WriteStringValue("cancel");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    public static byte[] SerializeModelVisible(byte[] sharedBytes, byte[] packetBytes)
    {
        ArgumentNullException.ThrowIfNull(sharedBytes);
        ArgumentNullException.ThrowIfNull(packetBytes);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("shared_context"); writer.WriteRawValue(sharedBytes, skipInputValidation: false);
            writer.WritePropertyName("memory_packet"); writer.WriteRawValue(packetBytes, skipInputValidation: false);
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    public static byte[] SerializeInviteResponseShared(
        ActorDecisionView view,
        DecisionProblemDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(descriptor);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", InviteResponseProtocolVersion);
            WriteIdentity(writer, view.Self.Identity);
            WritePersonality(writer, view.Personality);
            writer.WritePropertyName("current_problem");
            writer.WriteRawValue(descriptor.GetCanonicalBytes(), skipInputValidation: false);
            WriteSelf(writer, view.Self);
            WriteGoals(writer, view.ActiveGoals);
            writer.WritePropertyName("current_plan");
            if (view.CurrentPlan is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                WritePlanValue(writer, view.CurrentPlan);
            }

            writer.WritePropertyName("current_step");
            if (view.CurrentStep is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                WriteStep(writer, view.CurrentStep);
            }

            WriteKnowledge(writer, view.Knowledge);
            writer.WritePropertyName("semantic_response_kinds");
            writer.WriteStartArray();
            writer.WriteStringValue("accept");
            writer.WriteStringValue("decline");
            writer.WriteStringValue("clarify");
            writer.WriteStringValue("counter_offer");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public static byte[] SerializePlanlessStrategicShared(
        ActorDecisionView view,
        PlanlessStrategicDecisionProblemDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(descriptor);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", PlanlessStrategicProtocolVersion);
            WriteIdentity(writer, view.Self.Identity);
            WritePersonality(writer, view.Personality);
            writer.WritePropertyName("current_problem");
            writer.WriteRawValue(descriptor.GetCanonicalBytes(), skipInputValidation: false);
            WriteSelf(writer, view.Self);
            WriteGoals(writer, view.ActiveGoals);
            writer.WriteNull("current_plan");
            writer.WriteNull("current_step");
            WriteKnowledge(writer, view.Knowledge);
            writer.WritePropertyName("strategic_decision_kinds");
            writer.WriteStartArray();
            writer.WriteStringValue("create_plan");
            writer.WriteStringValue("verify");
            writer.WriteStringValue("defer");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    internal static string ValidateSha256(string? value, string parameterName)
    {
        if (value is null || value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Value must be a lowercase SHA-256 hexadecimal string.", parameterName);
        return value;
    }

    private static void WriteIdentity(Utf8JsonWriter writer, ActorIdentity identity)
    {
        writer.WritePropertyName("identity"); writer.WriteStartObject();
        writer.WriteString("actor_id", identity.ActorId.Value); writer.WriteString("name", identity.Name.Value); writer.WriteNumber("age", identity.Age.WholeYears);
        writer.WriteEndObject();
    }

    private static void WritePersonality(Utf8JsonWriter writer, IPersonalityPriorView personality)
    {
        writer.WritePropertyName("personality"); writer.WriteStartObject();
        writer.WritePropertyName("cognitive_functions"); writer.WriteStartObject();
        writer.WriteNumber("se", personality.CognitiveStyle.Se); writer.WriteNumber("si", personality.CognitiveStyle.Si); writer.WriteNumber("ne", personality.CognitiveStyle.Ne); writer.WriteNumber("ni", personality.CognitiveStyle.Ni);
        writer.WriteNumber("te", personality.CognitiveStyle.Te); writer.WriteNumber("ti", personality.CognitiveStyle.Ti); writer.WriteNumber("fe", personality.CognitiveStyle.Fe); writer.WriteNumber("fi", personality.CognitiveStyle.Fi); writer.WriteEndObject();
        writer.WritePropertyName("traits"); writer.WriteStartArray(); foreach (var trait in personality.Traits) writer.WriteStringValue(trait.Value); writer.WriteEndArray();
        writer.WritePropertyName("weighted_values"); writer.WriteStartArray(); foreach (var value in personality.Values) { writer.WriteStartObject(); writer.WriteString("value_id", value.ValueIdentity.Value); writer.WriteNumber("weight", value.Weight); writer.WriteEndObject(); } writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSelf(Utf8JsonWriter writer, SharedActorState self)
    {
        writer.WritePropertyName("current_self"); writer.WriteStartObject();
        writer.WritePropertyName("body"); writer.WriteStartObject(); writer.WriteNumber("health_current", self.Body.Health.Current); writer.WriteNumber("health_maximum", self.Body.Health.Maximum); writer.WriteNumber("satiety", self.Body.Satiety.Value); writer.WriteNumber("spirit", self.Body.Spirit.Value); writer.WriteString("disease", DiseaseToken(self.Body.Disease)); writer.WriteEndObject();
        writer.WriteString("movement_mode", self.Traversal.MovementMode == MovementMode.Land ? "land" : "swimming");
        writer.WritePropertyName("inventory"); writer.WriteStartArray(); foreach (var entry in self.Inventory.Entries) WriteInventoryEntry(writer, entry); writer.WriteEndArray();
        writer.WritePropertyName("hand_equipment"); WriteHandItem(writer, self.Equipment.HandItemRef);
        writer.WriteEndObject();
    }

    private static void WriteGoals(Utf8JsonWriter writer, IReadOnlyList<NpcGoal> goals)
    {
        writer.WritePropertyName("active_goals"); writer.WriteStartArray(); foreach (var goal in goals) WriteGoal(writer, goal); writer.WriteEndArray();
    }

    private static void WritePlan(Utf8JsonWriter writer, CognitionPlanView plan)
    {
        writer.WritePropertyName("current_plan"); WritePlanValue(writer, plan);
    }

    private static void WritePlanValue(Utf8JsonWriter writer, CognitionPlanView plan) { writer.WriteStartObject(); writer.WritePropertyName("goal"); WriteGoal(writer, plan.Goal); writer.WritePropertyName("steps"); writer.WriteStartArray(); foreach (var step in plan.Steps) WriteStep(writer, step); writer.WriteEndArray(); writer.WriteEndObject(); }

    private static void WriteGoal(Utf8JsonWriter writer, NpcGoal goal)
    {
        writer.WriteStartObject(); writer.WriteString("goal_id", goal.GoalId.Value); writer.WritePropertyName("objective"); WriteObjective(writer, goal.Objective); writer.WriteEndObject();
    }

    private static void WriteStep(Utf8JsonWriter writer, PlanStep step)
    {
        writer.WriteStartObject(); writer.WriteString("plan_step_id", step.PlanStepId.Value); writer.WritePropertyName("objective"); WriteObjective(writer, step.Objective);
        if (step.Target is null) writer.WriteNull("target_ref"); else writer.WriteString("target_ref", step.Target.Value);
        writer.WritePropertyName("desired_result"); WriteResult(writer, step.DesiredResult);
        writer.WritePropertyName("action_binding"); if (step.Action is null) writer.WriteNullValue(); else WriteAction(writer, step.Action);
        writer.WriteEndObject();
    }

    private static void WriteKnowledge(Utf8JsonWriter writer, NpcKnowledgeState knowledge)
    {
        writer.WritePropertyName("known_facts"); writer.WriteStartObject();
        writer.WritePropertyName("targets"); writer.WriteStartArray(); foreach (var target in knowledge.KnownTargets.Snapshots) { writer.WriteStartObject(); writer.WriteString("target_ref", target.TargetRef.Value); writer.WriteString("target_kind", TargetKindToken(target.TargetKind)); writer.WritePropertyName("position"); writer.WriteStartObject(); writer.WriteNumber("x", target.Position.X); writer.WriteNumber("y", target.Position.Y); writer.WriteEndObject(); writer.WriteEndObject(); } writer.WriteEndArray();
        writer.WritePropertyName("damage_opportunities"); writer.WriteStartArray(); foreach (var opportunity in knowledge.KnownOpportunities.DamageOpportunities) WriteDamageOpportunity(writer, opportunity); writer.WriteEndArray();
        writer.WritePropertyName("consumption_opportunities"); writer.WriteStartArray(); foreach (var opportunity in knowledge.KnownOpportunities.ConsumptionOpportunities) WriteConsumptionOpportunity(writer, opportunity); writer.WriteEndArray();
        writer.WritePropertyName("pickup_opportunities"); writer.WriteStartArray(); foreach (var opportunity in knowledge.KnownOpportunities.PickupOpportunities) WritePickupOpportunity(writer, opportunity); writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteInventoryEntry(Utf8JsonWriter writer, InventoryEntry entry) { writer.WriteStartObject(); switch (entry) { case StackEntry stack: writer.WriteString("kind", "stack"); writer.WriteString("item_type_id", stack.ItemTypeId.Value); writer.WriteNumber("quantity", stack.Quantity); break; case InstanceEntry instance: writer.WriteString("kind", "instance"); writer.WriteString("item_instance_id", instance.ItemInstanceId.Value); break; default: throw new ArgumentException("Inventory entry is outside the closed PlanningContext domain."); } writer.WriteEndObject(); }
    private static void WriteHandItem(Utf8JsonWriter writer, HandItemRef? hand) { if (hand is null) { writer.WriteNullValue(); return; } writer.WriteStartObject(); switch (hand) { case StackHandItemRef stack: writer.WriteString("kind", "stack"); writer.WriteString("item_type_id", stack.ItemTypeId.Value); break; case InstanceHandItemRef instance: writer.WriteString("kind", "instance"); writer.WriteString("item_instance_id", instance.ItemInstanceId.Value); break; default: throw new ArgumentException("Hand item is outside the closed PlanningContext domain."); } writer.WriteEndObject(); }
    private static void WriteObjective(Utf8JsonWriter writer, GoalObjective objective) { writer.WriteStartObject(); switch (objective) { case AcquireItemObjective acquire: writer.WriteString("kind", "acquire_item"); writer.WriteString("item_type_id", acquire.ItemTypeId.Value); writer.WriteNumber("quantity", acquire.Quantity); break; case MaintainBodyObjective maintain: writer.WriteString("kind", "maintain_body"); writer.WriteString("metric", BodyMetricToken(maintain.Metric)); writer.WriteNumber("minimum_acceptable_level", maintain.MinimumAcceptableLevel); break; case KnowObjective know: writer.WriteString("kind", "know"); writer.WriteString("knowledge_fact_ref", know.KnowledgeFactRef.Value); break; case ReachTargetObjective reach: writer.WriteString("kind", "reach_target"); writer.WriteString("target_ref", reach.TargetRef.Value); break; case FulfillCommitmentObjective fulfill: writer.WriteString("kind", "fulfill_commitment"); writer.WriteString("commitment_id", fulfill.CommitmentId.Value); break; case ExperienceObjective experience: writer.WriteString("kind", "experience"); writer.WriteString("experience_id", experience.ExperienceId.Value); break; default: throw new ArgumentException("Goal objective is outside the closed PlanningContext domain."); } writer.WriteEndObject(); }
    private static void WriteResult(Utf8JsonWriter writer, ResultPredicate result) { writer.WriteStartObject(); switch (result) { case InventoryAtLeast inventory: writer.WriteString("kind", "inventory_at_least"); writer.WriteString("actor_id", inventory.ActorId.Value); writer.WriteString("item_type_id", inventory.ItemTypeId.Value); writer.WriteNumber("quantity", inventory.Quantity); break; case BodyStateWithin body: writer.WriteString("kind", "body_state_within"); writer.WriteString("actor_id", body.ActorId.Value); writer.WriteString("metric", BodyMetricToken(body.Metric)); writer.WriteNumber("minimum_acceptable_level", body.MinimumAcceptableLevel); break; case InteractionTargetReached reached: writer.WriteString("kind", "interaction_target_reached"); writer.WriteString("actor_id", reached.ActorId.Value); writer.WriteString("target_ref", reached.TargetRef.Value); writer.WriteNumber("interaction_range", reached.InteractionRange.Value); break; case TargetTerminal terminal: writer.WriteString("kind", "target_terminal"); writer.WriteString("actor_id", terminal.ActorId.Value); writer.WriteString("target_ref", terminal.TargetRef.Value); break; case CommitmentStatusMatches commitment: writer.WriteString("kind", "commitment_status_matches"); writer.WriteString("actor_id", commitment.Debtor.Value); writer.WriteString("commitment_id", commitment.CommitmentId.Value); writer.WriteString("status", "fulfilled"); break; case ExperienceCompleted experience: writer.WriteString("kind", "experience_completed"); writer.WriteString("actor_id", experience.ActorId.Value); writer.WriteString("experience_id", experience.ExperienceId.Value); break; default: throw new ArgumentException("Result predicate is outside the closed PlanningContext domain."); } writer.WriteEndObject(); }
    private static void WriteAction(Utf8JsonWriter writer, GameActionSpec action) { writer.WriteStartObject(); writer.WritePropertyName("contract_ref"); WriteContractRef(writer, action.Binding.ContractRef); writer.WriteNumber("expected_contract_version", action.Binding.ExpectedVersion.Value); writer.WriteString("capability", action.Binding.Capability.Value); if (action.Binding.InstrumentRef is null) writer.WriteNull("instrument_ref"); else writer.WriteString("instrument_ref", action.Binding.InstrumentRef.Value); writer.WritePropertyName("arguments"); writer.WriteStartObject(); switch (action.Arguments) { case DamageActionArguments damage: writer.WriteString("kind", "damage"); writer.WriteString("damage_type", DamageToken(damage.DamageType)); break; case ConsumptionActionArguments consumption: writer.WriteString("kind", "consumption"); writer.WriteString("source_item_type_id", consumption.SourceItemTypeId.Value); break; case PickupActionArguments pickup: writer.WriteString("kind", "pickup"); writer.WriteString("world_drop_id", pickup.WorldDropId.Value); break; default: throw new ArgumentException("Action arguments are outside the closed PlanningContext domain."); } writer.WriteEndObject(); writer.WriteEndObject(); }
    private static void WriteDamageOpportunity(Utf8JsonWriter writer, KnownDamageOpportunity opportunity) { writer.WriteStartObject(); WriteOpportunityBase(writer, opportunity.ContractRef, opportunity.ObservedVersion, opportunity.BelievedInteractionRange, opportunity.BelievedRequirement); writer.WritePropertyName("believed_yields"); writer.WriteStartArray(); foreach (var value in opportunity.BelievedYields) WriteYield(writer, value); writer.WriteEndArray(); writer.WriteEndObject(); }
    private static void WriteConsumptionOpportunity(Utf8JsonWriter writer, KnownConsumptionOpportunity opportunity) { writer.WriteStartObject(); WriteOpportunityBase(writer, opportunity.ContractRef, opportunity.ObservedVersion, opportunity.BelievedInteractionRange, opportunity.BelievedRequirement); writer.WriteString("source_item_type_id", opportunity.SourceItemTypeId.Value); writer.WriteNumber("quantity", opportunity.Quantity); writer.WriteNumber("believed_satiety_restore", opportunity.BelievedSatietyRestore); writer.WriteEndObject(); }
    private static void WritePickupOpportunity(Utf8JsonWriter writer, KnownPickupOpportunity opportunity) { writer.WriteStartObject(); WriteOpportunityBase(writer, opportunity.ContractRef, opportunity.ObservedVersion, opportunity.BelievedInteractionRange, opportunity.BelievedRequirement); writer.WriteString("world_drop_id", opportunity.WorldDropId.Value); writer.WritePropertyName("believed_items"); writer.WriteStartArray(); foreach (var value in opportunity.BelievedItems) WriteYield(writer, value); writer.WriteEndArray(); writer.WriteEndObject(); }
    private static void WriteOpportunityBase(Utf8JsonWriter writer, ContractRef contractRef, long observedVersion, InteractionRange range, KnownCapabilityRequirement requirement) { writer.WritePropertyName("contract_ref"); WriteContractRef(writer, contractRef); writer.WriteNumber("observed_version", observedVersion); writer.WriteNumber("interaction_range", range.Value); writer.WritePropertyName("capability_requirement"); writer.WriteStartObject(); writer.WriteString("capability", requirement.CapabilityIdentity.Value); writer.WriteNumber("minimum_value", requirement.MinimumValue); writer.WriteEndObject(); }
    private static void WriteContractRef(Utf8JsonWriter writer, ContractRef contractRef) { writer.WriteStartObject(); writer.WriteString("target_ref", contractRef.TargetRef.Value); writer.WriteString("contract_id", contractRef.ContractId); writer.WriteEndObject(); }
    private static void WriteYield(Utf8JsonWriter writer, KnownDestructionYield value) { writer.WriteStartObject(); writer.WriteString("item_type_id", value.ItemTypeId.Value); writer.WriteNumber("quantity", value.Quantity); writer.WriteEndObject(); }
    private static string DiseaseToken(Disease value) => value switch { Disease.Healthy => "healthy", Disease.Ill => "ill", Disease.SevereIllness => "severe_illness", Disease.Dead => "dead", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string BodyMetricToken(BodyMetric value) => value switch { BodyMetric.Health => "health", BodyMetric.Satiety => "satiety", BodyMetric.Spirit => "spirit", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string DamageToken(DamageType value) => value switch { DamageType.Slashing => "slashing", DamageType.Bludgeoning => "bludgeoning", DamageType.Piercing => "piercing", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string TargetKindToken(TargetKind value) => value switch
    {
        TargetKind.Tree => "tree",
        TargetKind.Npc => "npc",
        TargetKind.ResourceNode => "resource_node",
        TargetKind.PointOfInterest => "point_of_interest",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
