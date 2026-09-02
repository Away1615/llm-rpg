using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Alice.Activities;
using Alice.Actors;

namespace Alice.Memory;

/// <summary>One immutable actor-owned evidence pointer to a shared semantic source.</summary>
public sealed class SubjectiveMemoryRecord
{
    private const string IdentityProtocolVersion = "subjective-memory-id-v1";
    private const string RecordProtocolVersion = "subjective-memory-record-v1";
    private const string KindToken = "semantic_dialogue_experience";
    private const string ProjectorVersionToken = "deterministic_semantic_dialogue_experience_v1";
    private readonly byte[] _canonicalBytes;

    private SubjectiveMemoryRecord(
        DecisionMemoryId memoryId,
        ActorId actorId,
        DecisionMemorySourceId sourceId,
        ActorExperienceRole role,
        SimTime sourceOccurredAt,
        byte[] canonicalBytes)
    {
        MemoryId = memoryId;
        ActorId = actorId;
        SourceId = sourceId;
        Role = role;
        SourceOccurredAt = sourceOccurredAt;
        EvidenceStatus = DecisionMemoryEvidenceStatus.Current;
        _canonicalBytes = canonicalBytes;
    }

    public DecisionMemoryId MemoryId { get; }
    public ActorId ActorId { get; }
    public DecisionMemorySourceId SourceId { get; }
    public ActorExperienceRole Role { get; }
    public SimTime SourceOccurredAt { get; }
    public DecisionMemoryEvidenceStatus EvidenceStatus { get; }

    public byte[] GetCanonicalBytes() => (byte[])_canonicalBytes.Clone();

    internal static SubjectiveMemoryRecord Create(
        SemanticDialogueSourceRecord sourceRecord,
        ActorExperienceReference experienceReference)
    {
        ArgumentNullException.ThrowIfNull(sourceRecord);
        ArgumentNullException.ThrowIfNull(experienceReference);
        if (sourceRecord.SourceId != experienceReference.SourceId
            || !sourceRecord.TryGetExpectedRole(experienceReference.ActorId, out ActorExperienceRole expectedRole)
            || expectedRole != experienceReference.Role)
        {
            throw new ArgumentException("Experience reference must exactly match the semantic source participants.",
                nameof(experienceReference));
        }

        DecisionMemoryId memoryId = CreateMemoryId(experienceReference.ActorId, sourceRecord.SourceId);
        byte[] sourceBytes = sourceRecord.GetCanonicalBytes();
        string sourceContentSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        byte[] canonicalBytes = CreateCanonicalBytes(
            memoryId,
            experienceReference,
            sourceRecord.OccurredAt,
            sourceContentSha256);
        return new SubjectiveMemoryRecord(
            memoryId,
            experienceReference.ActorId,
            sourceRecord.SourceId,
            experienceReference.Role,
            sourceRecord.OccurredAt,
            canonicalBytes);
    }

    internal bool HasExactContent(SubjectiveMemoryRecord other) =>
        MemoryId == other.MemoryId && _canonicalBytes.AsSpan().SequenceEqual(other._canonicalBytes);

    internal bool HasExactSource(SemanticDialogueSourceRecord sourceRecord)
    {
        ArgumentNullException.ThrowIfNull(sourceRecord);
        if (SourceId != sourceRecord.SourceId
            || SourceOccurredAt != sourceRecord.OccurredAt
            || !sourceRecord.TryGetExpectedRole(ActorId, out ActorExperienceRole expectedRole)
            || Role != expectedRole)
        {
            return false;
        }

        var experienceReference = new ActorExperienceReference(ActorId, SourceId, Role);
        SubjectiveMemoryRecord expectedRecord = Create(sourceRecord, experienceReference);
        return HasExactContent(expectedRecord);
    }

    private static DecisionMemoryId CreateMemoryId(ActorId actorId, DecisionMemorySourceId sourceId)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", IdentityProtocolVersion);
            writer.WriteString("actor_id", actorId.Value);
            writer.WriteString("source_id", sourceId.Value);
            writer.WriteString("projector_version", ProjectorVersionToken);
            writer.WriteEndObject();
        }

        return new DecisionMemoryId(Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant());
    }

    private static byte[] CreateCanonicalBytes(
        DecisionMemoryId memoryId,
        ActorExperienceReference experienceReference,
        SimTime sourceOccurredAt,
        string sourceContentSha256)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", RecordProtocolVersion);
            writer.WriteString("memory_id", memoryId.Value);
            writer.WriteString("actor_id", experienceReference.ActorId.Value);
            writer.WriteString("source_id", experienceReference.SourceId.Value);
            writer.WriteString("source_protocol_version", SemanticDialogueSourceRecord.ProtocolVersion);
            writer.WriteString("source_content_sha256", sourceContentSha256);
            writer.WriteString("experience_role", GetRoleToken(experienceReference.Role));
            writer.WriteNumber("source_sim_time_ticks", sourceOccurredAt.Ticks);
            writer.WriteString("kind", KindToken);
            writer.WriteString("projector_version", ProjectorVersionToken);
            writer.WriteString("evidence_status", "current");
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static string GetRoleToken(ActorExperienceRole role) => role switch
    {
        ActorExperienceRole.Caused => "caused",
        ActorExperienceRole.Received => "received",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
