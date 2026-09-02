using System.Security.Cryptography;
using System.Text.Json;
using Alice.Actors;
using Alice.Npc;
using Alice.Social;

namespace Alice.Cognition;

public static class DecisionNeedCanonicalJson
{
    public const string ProblemDescriptorProtocolVersion = "decision-problem-descriptor-v1";
    public const string FingerprintProtocolVersion = "decision-need-fingerprint-v1";
    public const string NeedIdProtocolVersion = "decision-need-id-v1";

    public static byte[] SerializeProblemDescriptor(DecisionProblemDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", ProblemDescriptorProtocolVersion);
            switch (descriptor)
            {
                case CurrentStepDecisionProblemDescriptor currentStep:
                    WriteCurrentStepProblem(writer, currentStep);
                    break;
                case PlanlessStrategicDecisionProblemDescriptor planlessStrategic:
                    WritePlanlessStrategicProblem(writer, planlessStrategic);
                    break;
                case InviteResponseDecisionProblemDescriptor inviteResponse:
                    WriteInviteResponseProblem(writer, inviteResponse);
                    break;
                default:
                    throw new ArgumentException("Problem descriptor is outside the closed canonical domain.", nameof(descriptor));
            }

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public static DecisionProblemDescriptorHash HashProblemDescriptor(DecisionProblemDescriptor descriptor)
    {
        return new DecisionProblemDescriptorHash(HashBytes(SerializeProblemDescriptor(descriptor)));
    }

    internal static DecisionNeedFingerprint CreateFingerprint(
        ActorId actorId,
        PlanId? planId,
        PlanStepId? planStepId,
        DecisionNeedKind needKind,
        DecisionProblemDescriptorHash descriptorHash)
    {
        ArgumentNullException.ThrowIfNull(needKind);
        ArgumentNullException.ThrowIfNull(descriptorHash);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", FingerprintProtocolVersion);
            writer.WriteString("actor_id", actorId.Value);
            if (planId is null)
            {
                writer.WriteNull("plan_id");
            }
            else
            {
                writer.WriteString("plan_id", planId.Value);
            }

            if (planStepId is null)
            {
                writer.WriteNull("plan_step_id");
            }
            else
            {
                writer.WriteString("plan_step_id", planStepId.Value);
            }

            writer.WriteString("need_kind", needKind.Value);
            writer.WriteString("problem_descriptor_sha256", descriptorHash.Value);
            writer.WriteEndObject();
        }

        return new DecisionNeedFingerprint(HashBytes(buffer.ToArray()));
    }

    internal static DecisionNeedFingerprint CreateMandatoryResponseFingerprint(
        ActorId actorId,
        DecisionNeedKind needKind,
        DecisionProblemDescriptorHash descriptorHash,
        MandatoryResponseDecisionSubject subject)
    {
        ArgumentNullException.ThrowIfNull(needKind);
        ArgumentNullException.ThrowIfNull(descriptorHash);
        ArgumentNullException.ThrowIfNull(subject);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", FingerprintProtocolVersion);
            writer.WriteString("actor_id", actorId.Value);
            writer.WriteNull("plan_id");
            writer.WriteNull("plan_step_id");
            writer.WriteString("need_kind", needKind.Value);
            writer.WriteString("problem_descriptor_sha256", descriptorHash.Value);
            writer.WriteString("mandatory_response_subject", subject.CanonicalValue);
            writer.WriteEndObject();
        }

        return new DecisionNeedFingerprint(HashBytes(buffer.ToArray()));
    }

    internal static DecisionNeedId CreateNeedId(
        DecisionNeedFingerprint fingerprint,
        DecisionNeedWorldRevision firstWorldRevision)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(firstWorldRevision);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", NeedIdProtocolVersion);
            writer.WriteString("fingerprint", fingerprint.Value);
            writer.WriteNumber("first_world_revision", firstWorldRevision.Value);
            writer.WriteEndObject();
        }

        return new DecisionNeedId(HashBytes(buffer.ToArray()));
    }

    private static void WriteCurrentStepProblem(
        Utf8JsonWriter writer,
        CurrentStepDecisionProblemDescriptor descriptor)
    {
        writer.WriteString("kind", "current_step");
        writer.WriteString("actor_id", descriptor.ActorId.Value);
        writer.WriteString("problem_code", descriptor.ProblemCode.Value);
        writer.WritePropertyName("current_goal");
        writer.WriteStartObject();
        writer.WriteString("goal_id", descriptor.CurrentGoalId.Value);
        writer.WritePropertyName("objective");
        WriteObjective(writer, descriptor.CurrentGoalObjective);
        writer.WriteEndObject();
        writer.WritePropertyName("current_step");
        writer.WriteStartObject();
        writer.WriteString("plan_step_id", descriptor.PlanStepId.Value);
        writer.WritePropertyName("objective");
        WriteObjective(writer, descriptor.StepObjective);
        if (descriptor.Target is null)
        {
            writer.WriteNull("target_ref");
        }
        else
        {
            writer.WriteString("target_ref", descriptor.Target.Value);
        }

        writer.WritePropertyName("desired_result");
        WriteDesiredResult(writer, descriptor.DesiredResult);
        writer.WriteEndObject();
    }

    private static void WriteInviteResponseProblem(
        Utf8JsonWriter writer,
        InviteResponseDecisionProblemDescriptor descriptor)
    {
        writer.WriteString("kind", "invite_response");
        writer.WriteString("actor_id", descriptor.ActorId.Value);
        writer.WriteString("problem_code", descriptor.ProblemCode.Value);
        writer.WriteString("original_speaker", descriptor.OriginalSpeaker.Value);
        writer.WriteString("gathering_ref", descriptor.GatheringRef.Value);
        writer.WriteNumber("expected_gathering_revision", descriptor.ExpectedGatheringRevision);
        if (descriptor.BelievedAuthorizationRef is null)
        {
            writer.WriteNull("believed_authorization_ref");
        }
        else
        {
            writer.WriteString("believed_authorization_ref", descriptor.BelievedAuthorizationRef.Value.Value);
        }

        if (descriptor.TopicRef is null)
        {
            writer.WriteNull("topic_ref");
        }
        else
        {
            writer.WriteString("topic_ref", descriptor.TopicRef.Value.Value);
        }

        writer.WritePropertyName("claim_references");
        writer.WriteStartArray();
        foreach (DialogueClaimReference claimReference in descriptor.ClaimReferences)
        {
            writer.WriteStartObject();
            writer.WriteString("claim_ref", claimReference.ClaimRef.Value);
            writer.WriteString("provenance_ref", claimReference.ProvenanceRef.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WritePlanlessStrategicProblem(
        Utf8JsonWriter writer,
        PlanlessStrategicDecisionProblemDescriptor descriptor)
    {
        writer.WriteString("kind", "planless_strategic");
        writer.WriteString("actor_id", descriptor.ActorId.Value);
        writer.WriteString("problem_code", descriptor.ProblemCode.Value);
        writer.WritePropertyName("active_goals");
        writer.WriteStartArray();
        foreach (NpcGoal goal in descriptor.ActiveGoals)
        {
            writer.WriteStartObject();
            writer.WriteString("goal_id", goal.GoalId.Value);
            writer.WritePropertyName("objective");
            WriteObjective(writer, goal.Objective);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
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
            case KnowObjective know:
                writer.WriteString("kind", "know");
                writer.WriteString("knowledge_fact_ref", PlanlessStrategicObjectiveCanonicalFields.KnowFactValue(know));
                break;
            case ReachTargetObjective reach:
                writer.WriteString("kind", "reach_target");
                writer.WriteString("target_ref", reach.TargetRef.Value);
                break;
            case FulfillCommitmentObjective fulfill:
                writer.WriteString("kind", "fulfill_commitment");
                writer.WriteString("commitment_id", fulfill.CommitmentId.Value);
                break;
            case ExperienceObjective experience:
                writer.WriteString("kind", "experience");
                writer.WriteString("experience_id", experience.ExperienceId.Value);
                break;
            default:
                throw new ArgumentException("Goal objective is outside the closed descriptor domain.", nameof(objective));
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
                throw new ArgumentException("Desired result is outside the closed descriptor domain.", nameof(desiredResult));
        }

        writer.WriteEndObject();
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

    private static string HashBytes(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
