using System.Text.Json;
using Alice.Actors;
using Alice.Cognition;
using Alice.Damage;
using Alice.Interaction;
using Alice.Items;
using Alice.Npc;
using Alice.World;

namespace Alice.ModelRuntime;

/// <summary>Closed explicit projection of the accepted model-visible context.</summary>
internal static class LocalReasonerCanonicalJson
{
    public static byte[] Serialize(LocalReasonerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", LocalReasonerProtocol.ProtocolVersion);
            WriteNpc(writer, context);
            writer.WritePropertyName("current_goal");
            WriteGoal(writer, context.CurrentGoal);
            writer.WritePropertyName("current_plan_step");
            WritePlanStep(writer, context.CurrentStep);
            writer.WritePropertyName("local_options");
            writer.WriteStartArray();
            foreach (LocalReasonerOption option in context.Options)
            {
                WriteOption(writer, option);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteNpc(Utf8JsonWriter writer, LocalReasonerContext context)
    {
        LocalReasonerSelfView self = context.Self;
        writer.WritePropertyName("npc");
        writer.WriteStartObject();
        writer.WriteString("actor_id", self.Identity.ActorId.Value);
        writer.WriteString("name", self.Identity.Name.Value);
        writer.WriteNumber("age_whole_years", self.Identity.Age.WholeYears);
        writer.WritePropertyName("health");
        writer.WriteStartObject();
        writer.WriteNumber("current", self.Body.Health.Current);
        writer.WriteNumber("maximum", self.Body.Health.Maximum);
        writer.WriteEndObject();
        writer.WriteNumber("satiety", self.Body.Satiety.Value);
        writer.WriteNumber("spirit", self.Body.Spirit.Value);
        writer.WriteString("disease", DiseaseToken(self.Body.Disease));
        writer.WriteString("movement_mode", MovementModeToken(self.Traversal.MovementMode));
        writer.WritePropertyName("inventory");
        writer.WriteStartArray();
        foreach (InventoryEntry entry in self.InventoryEntries)
        {
            WriteInventoryEntry(writer, entry);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("hand");
        WriteHand(writer, self.HandItem);
        writer.WritePropertyName("personality");
        WritePersonality(writer, context.Personality);
        writer.WriteEndObject();
    }

    private static void WriteInventoryEntry(Utf8JsonWriter writer, InventoryEntry entry)
    {
        writer.WriteStartObject();
        switch (entry)
        {
            case StackEntry stack:
                writer.WriteString("kind", "stack");
                writer.WriteString("item_type_id", stack.ItemTypeId.Value);
                writer.WriteNumber("quantity", stack.Quantity);
                break;
            case InstanceEntry instance:
                writer.WriteString("kind", "instance");
                writer.WriteString("item_instance_id", instance.ItemInstanceId.Value);
                break;
            default:
                throw new ArgumentException("Inventory entry is outside the closed local-reasoner domain.", nameof(entry));
        }

        writer.WriteEndObject();
    }

    private static void WriteHand(Utf8JsonWriter writer, HandItemRef? hand)
    {
        if (hand is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        switch (hand)
        {
            case StackHandItemRef stack:
                writer.WriteString("kind", "stack");
                writer.WriteString("item_type_id", stack.ItemTypeId.Value);
                break;
            case InstanceHandItemRef instance:
                writer.WriteString("kind", "instance");
                writer.WriteString("item_instance_id", instance.ItemInstanceId.Value);
                break;
            default:
                throw new ArgumentException("Hand reference is outside the closed local-reasoner domain.", nameof(hand));
        }

        writer.WriteEndObject();
    }

    private static void WritePersonality(Utf8JsonWriter writer, IPersonalityPriorView personality)
    {
        CognitiveFunctionProfile cognitive = personality.CognitiveStyle;
        writer.WriteStartObject();
        writer.WritePropertyName("cognitive_functions");
        writer.WriteStartObject();
        writer.WriteNumber("se", cognitive.Se);
        writer.WriteNumber("si", cognitive.Si);
        writer.WriteNumber("ne", cognitive.Ne);
        writer.WriteNumber("ni", cognitive.Ni);
        writer.WriteNumber("te", cognitive.Te);
        writer.WriteNumber("ti", cognitive.Ti);
        writer.WriteNumber("fe", cognitive.Fe);
        writer.WriteNumber("fi", cognitive.Fi);
        writer.WriteEndObject();
        writer.WritePropertyName("traits");
        writer.WriteStartArray();
        foreach (PersonalityTagId trait in personality.Traits)
        {
            writer.WriteStringValue(trait.Value);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("values");
        writer.WriteStartArray();
        foreach (WeightedPersonalityValue value in personality.Values)
        {
            writer.WriteStartObject();
            writer.WriteString("value_id", value.ValueIdentity.Value);
            writer.WriteNumber("weight", value.Weight);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteGoal(Utf8JsonWriter writer, NpcGoal goal)
    {
        writer.WriteStartObject();
        writer.WriteString("goal_id", goal.GoalId.Value);
        writer.WritePropertyName("objective");
        WriteObjective(writer, goal.Objective);
        writer.WriteEndObject();
    }

    private static void WritePlanStep(Utf8JsonWriter writer, PlanStep step)
    {
        writer.WriteStartObject();
        writer.WriteString("plan_step_id", step.PlanStepId.Value);
        writer.WritePropertyName("objective");
        WriteObjective(writer, step.Objective);
        writer.WritePropertyName("action");
        if (step.Action is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            WriteAction(writer, step.Action);
        }

        if (step.Target is null)
        {
            writer.WriteNull("target_ref");
        }
        else
        {
            writer.WriteString("target_ref", step.Target.Value);
        }

        writer.WritePropertyName("desired_result");
        WriteDesiredResult(writer, step.DesiredResult);
        writer.WriteEndObject();
    }

    private static void WriteObjective(Utf8JsonWriter writer, GoalObjective objective)
    {
        writer.WriteStartObject();
        switch (objective)
        {
            case AcquireItemObjective acquire:
                writer.WriteString("kind", "acquire_item");
                writer.WriteString("item_type_id", acquire.ItemTypeId.Value);
                writer.WriteNumber("quantity", acquire.Quantity);
                break;
            case MaintainBodyObjective maintain:
                writer.WriteString("kind", "maintain_body");
                writer.WriteString("metric", BodyMetricToken(maintain.Metric));
                writer.WriteNumber("minimum_acceptable_level", maintain.MinimumAcceptableLevel);
                break;
            default:
                throw new ArgumentException("Goal objective is outside the closed local-reasoner domain.", nameof(objective));
        }

        writer.WriteEndObject();
    }

    private static void WriteDesiredResult(Utf8JsonWriter writer, ResultPredicate desiredResult)
    {
        writer.WriteStartObject();
        switch (desiredResult)
        {
            case InventoryAtLeast inventory:
                writer.WriteString("kind", "inventory_at_least");
                writer.WriteString("actor_id", inventory.ActorId.Value);
                writer.WriteString("item_type_id", inventory.ItemTypeId.Value);
                writer.WriteNumber("quantity", inventory.Quantity);
                break;
            case BodyStateWithin body:
                writer.WriteString("kind", "body_state_within");
                writer.WriteString("actor_id", body.ActorId.Value);
                writer.WriteString("metric", BodyMetricToken(body.Metric));
                writer.WriteNumber("minimum_acceptable_level", body.MinimumAcceptableLevel);
                break;
            case InteractionTargetReached reached:
                writer.WriteString("kind", "interaction_target_reached");
                writer.WriteString("actor_id", reached.ActorId.Value);
                writer.WriteString("target_ref", reached.TargetRef.Value);
                writer.WriteNumber("interaction_range", reached.InteractionRange.Value);
                break;
            case TargetTerminal terminal:
                writer.WriteString("kind", "target_terminal");
                writer.WriteString("actor_id", terminal.ActorId.Value);
                writer.WriteString("target_ref", terminal.TargetRef.Value);
                break;
            default:
                throw new ArgumentException("Desired result is outside the closed local-reasoner domain.", nameof(desiredResult));
        }

        writer.WriteEndObject();
    }

    private static void WriteOption(Utf8JsonWriter writer, LocalReasonerOption option)
    {
        writer.WriteStartObject();
        writer.WriteString("candidate_id", option.CandidateId.Value);
        switch (option)
        {
            case LocalReasonerDamageOption damage:
                writer.WriteString("kind", "damage");
                writer.WritePropertyName("action");
                WriteAction(writer, damage.Action);
                writer.WritePropertyName("known_opportunity");
                WriteKnownDamage(writer, damage.KnownOpportunity);
                break;
            case LocalReasonerConsumptionOption consumption:
                writer.WriteString("kind", "consumption");
                writer.WritePropertyName("action");
                WriteAction(writer, consumption.Action);
                writer.WritePropertyName("known_opportunity");
                WriteKnownConsumption(writer, consumption.KnownOpportunity);
                break;
            case LocalReasonerPickupOption pickup:
                writer.WriteString("kind", "pickup");
                writer.WritePropertyName("action");
                WriteAction(writer, pickup.Action);
                writer.WritePropertyName("known_opportunity");
                WriteKnownPickup(writer, pickup.KnownOpportunity);
                break;
            default:
                throw new ArgumentException("Local option is outside the closed local-reasoner domain.", nameof(option));
        }

        writer.WriteEndObject();
    }

    private static void WriteAction(Utf8JsonWriter writer, GameActionSpec action)
    {
        writer.WriteStartObject();
        writer.WriteString("actor_id", action.ActorId.Value);
        writer.WritePropertyName("binding");
        WriteBinding(writer, action.Binding);
        writer.WritePropertyName("arguments");
        writer.WriteStartObject();
        switch (action.Arguments)
        {
            case DamageActionArguments damage:
                writer.WriteString("kind", "damage");
                writer.WriteString("damage_type", DamageTypeToken(damage.DamageType));
                break;
            case ConsumptionActionArguments consumption:
                writer.WriteString("kind", "consumption");
                writer.WriteString("source_item_type_id", consumption.SourceItemTypeId.Value);
                break;
            case PickupActionArguments pickup:
                writer.WriteString("kind", "pickup");
                writer.WriteString("world_drop_id", pickup.WorldDropId.Value);
                break;
            default:
                throw new ArgumentException("Action arguments are outside the closed local-reasoner domain.", nameof(action));
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteBinding(Utf8JsonWriter writer, InteractionBinding binding)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("contract_ref");
        WriteContractRef(writer, binding.ContractRef);
        writer.WriteNumber("expected_version", binding.ExpectedVersion.Value);
        writer.WriteString("capability", binding.Capability.Value);
        if (binding.InstrumentRef is null)
        {
            writer.WriteNull("instrument_ref");
        }
        else
        {
            writer.WriteString("instrument_ref", binding.InstrumentRef.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteContractRef(Utf8JsonWriter writer, ContractRef contractRef)
    {
        writer.WriteStartObject();
        writer.WriteString("target_ref", contractRef.TargetRef.Value);
        writer.WriteString("contract_id", contractRef.ContractId);
        writer.WriteEndObject();
    }

    private static void WriteKnownDamage(Utf8JsonWriter writer, KnownDamageOpportunity opportunity)
    {
        writer.WriteStartObject();
        WriteKnownCommon(writer, opportunity.ContractRef, opportunity.ObservedVersion, opportunity.BelievedInteractionRange, opportunity.BelievedRequirement);
        writer.WritePropertyName("believed_yields");
        WriteYields(writer, opportunity.BelievedYields);
        writer.WriteEndObject();
    }

    private static void WriteKnownConsumption(Utf8JsonWriter writer, KnownConsumptionOpportunity opportunity)
    {
        writer.WriteStartObject();
        WriteKnownCommon(writer, opportunity.ContractRef, opportunity.ObservedVersion, opportunity.BelievedInteractionRange, opportunity.BelievedRequirement);
        writer.WriteString("source_item_type_id", opportunity.SourceItemTypeId.Value);
        writer.WriteNumber("quantity", opportunity.Quantity);
        writer.WriteNumber("believed_satiety_restore", opportunity.BelievedSatietyRestore);
        writer.WriteEndObject();
    }

    private static void WriteKnownPickup(Utf8JsonWriter writer, KnownPickupOpportunity opportunity)
    {
        writer.WriteStartObject();
        WriteKnownCommon(writer, opportunity.ContractRef, opportunity.ObservedVersion, opportunity.BelievedInteractionRange, opportunity.BelievedRequirement);
        writer.WriteString("world_drop_id", opportunity.WorldDropId.Value);
        writer.WritePropertyName("believed_items");
        WriteYields(writer, opportunity.BelievedItems);
        writer.WriteEndObject();
    }

    private static void WriteKnownCommon(
        Utf8JsonWriter writer,
        ContractRef contractRef,
        long observedVersion,
        InteractionRange interactionRange,
        KnownCapabilityRequirement requirement)
    {
        writer.WritePropertyName("contract_ref");
        WriteContractRef(writer, contractRef);
        writer.WriteNumber("observed_version", observedVersion);
        writer.WriteNumber("believed_interaction_range", interactionRange.Value);
        writer.WritePropertyName("believed_requirement");
        writer.WriteStartObject();
        writer.WriteString("capability", requirement.CapabilityIdentity.Value);
        writer.WriteNumber("minimum_value", requirement.MinimumValue);
        writer.WriteEndObject();
    }

    private static void WriteYields(Utf8JsonWriter writer, IEnumerable<KnownDestructionYield> yields)
    {
        writer.WriteStartArray();
        foreach (KnownDestructionYield yield in yields)
        {
            writer.WriteStartObject();
            writer.WriteString("item_type_id", yield.ItemTypeId.Value);
            writer.WriteNumber("quantity", yield.Quantity);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static string DiseaseToken(Disease disease)
    {
        return disease switch
        {
            Disease.Healthy => "healthy",
            Disease.Ill => "ill",
            Disease.SevereIllness => "severe_illness",
            Disease.Dead => "dead",
            _ => throw new ArgumentOutOfRangeException(nameof(disease))
        };
    }

    private static string MovementModeToken(MovementMode movementMode)
    {
        return movementMode switch
        {
            MovementMode.Land => "land",
            MovementMode.Swimming => "swimming",
            _ => throw new ArgumentOutOfRangeException(nameof(movementMode))
        };
    }

    private static string BodyMetricToken(BodyMetric metric)
    {
        return metric switch
        {
            BodyMetric.Health => "health",
            BodyMetric.Satiety => "satiety",
            BodyMetric.Spirit => "spirit",
            _ => throw new ArgumentOutOfRangeException(nameof(metric))
        };
    }

    private static string DamageTypeToken(DamageType damageType)
    {
        return damageType switch
        {
            DamageType.Slashing => "slashing",
            DamageType.Bludgeoning => "bludgeoning",
            DamageType.Piercing => "piercing",
            _ => throw new ArgumentOutOfRangeException(nameof(damageType))
        };
    }
}
