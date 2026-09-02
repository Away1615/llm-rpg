using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Alice.Activities;
using Alice.Actors;

namespace Alice.Memory;

public sealed record DecisionMemoryId
{
    public DecisionMemoryId(string value)
    {
        ValidateSha256(value, nameof(value));
        Value = value;
    }

    public string Value { get; }

    private static void ValidateSha256(string? value, string parameterName)
    {
        if (value is null || value.Length != 64)
        {
            throw new ArgumentException("Value must be a lowercase SHA-256 hexadecimal string.", parameterName);
        }

        foreach (char character in value)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                throw new ArgumentException("Value must be a lowercase SHA-256 hexadecimal string.", parameterName);
            }
        }
    }
}

public sealed record DecisionMemorySourceId
{
    public DecisionMemorySourceId(string value)
    {
        if (value is null || value.Length is < 1 or > 128 || char.IsWhiteSpace(value[0]) ||
            char.IsWhiteSpace(value[^1]))
        {
            throw new ArgumentException("Source identity must be 1-128 characters without edge whitespace.", nameof(value));
        }

        foreach (char character in value)
        {
            if (char.IsControl(character))
            {
                throw new ArgumentException("Source identity cannot contain control characters.", nameof(value));
            }
        }

        Value = value;
    }

    public string Value { get; }
}

public sealed record DecisionMemoryKind
{
    public DecisionMemoryKind(string value)
    {
        Value = CanonicalToken.Validate(value, nameof(value));
    }

    public string Value { get; }
}

public sealed record DecisionMemoryProjectorVersion
{
    public DecisionMemoryProjectorVersion(string value)
    {
        Value = CanonicalToken.Validate(value, nameof(value));
    }

    public string Value { get; }
}

public enum DecisionMemoryEvidenceStatus
{
    Current,
    Stale,
    Superseded,
    Uncertain
}

public sealed class DecisionMemorySlice
{
    private const string IdentityProtocolVersion = "decision-memory-id-v1";
    private const string SliceProtocolVersion = "decision-memory-slice-v1";
    private readonly ReadOnlyCollection<DecisionMemorySourceId> _sourceIds;
    private readonly ReadOnlyCollection<DecisionMemorySourceId> _supersedesSourceIds;
    private readonly ReadOnlyCollection<DecisionMemorySourceId> _conflictsWithSourceIds;
    private readonly byte[] _canonicalSourceBytes;

    private DecisionMemorySlice(
        DecisionMemoryId memoryId,
        ActorId actorId,
        DecisionMemoryKind kind,
        SimTime occurredAt,
        DecisionMemoryProjectorVersion projectorVersion,
        long constructionOrdinal,
        DecisionMemoryEvidenceStatus evidenceStatus,
        DecisionMemorySourceId[] sourceIds,
        DecisionMemorySourceId[] supersedesSourceIds,
        DecisionMemorySourceId[] conflictsWithSourceIds,
        byte[] canonicalSourceBytes)
    {
        MemoryId = memoryId;
        ActorId = actorId;
        Kind = kind;
        OccurredAt = occurredAt;
        ProjectorVersion = projectorVersion;
        ConstructionOrdinal = constructionOrdinal;
        EvidenceStatus = evidenceStatus;
        _sourceIds = Array.AsReadOnly(sourceIds);
        _supersedesSourceIds = Array.AsReadOnly(supersedesSourceIds);
        _conflictsWithSourceIds = Array.AsReadOnly(conflictsWithSourceIds);
        _canonicalSourceBytes = canonicalSourceBytes;
    }

    public DecisionMemoryId MemoryId { get; }
    public ActorId ActorId { get; }
    public DecisionMemoryKind Kind { get; }
    public SimTime OccurredAt { get; }
    public DecisionMemoryProjectorVersion ProjectorVersion { get; }
    public long ConstructionOrdinal { get; }
    public DecisionMemoryEvidenceStatus EvidenceStatus { get; }
    public IReadOnlyList<DecisionMemorySourceId> SourceIds => _sourceIds;
    public IReadOnlyList<DecisionMemorySourceId> SupersedesSourceIds => _supersedesSourceIds;
    public IReadOnlyList<DecisionMemorySourceId> ConflictsWithSourceIds => _conflictsWithSourceIds;

    public static DecisionMemorySlice Create(
        ActorId actorId,
        DecisionMemoryKind kind,
        SimTime occurredAt,
        DecisionMemoryProjectorVersion projectorVersion,
        long constructionOrdinal,
        DecisionMemoryEvidenceStatus evidenceStatus,
        IEnumerable<DecisionMemorySourceId> sourceIds,
        IEnumerable<DecisionMemorySourceId> supersedesSourceIds,
        IEnumerable<DecisionMemorySourceId> conflictsWithSourceIds,
        byte[] canonicalSourceBytes)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(projectorVersion);
        ArgumentNullException.ThrowIfNull(sourceIds);
        ArgumentNullException.ThrowIfNull(supersedesSourceIds);
        ArgumentNullException.ThrowIfNull(conflictsWithSourceIds);
        ArgumentNullException.ThrowIfNull(canonicalSourceBytes);
        if (constructionOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(constructionOrdinal));
        }

        if (!Enum.IsDefined(evidenceStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(evidenceStatus));
        }

        if (canonicalSourceBytes.Length == 0)
        {
            throw new ArgumentException("Canonical source bytes must be non-empty.", nameof(canonicalSourceBytes));
        }

        DecisionMemorySourceId[] canonicalSourceIds = CanonicalizeIds(sourceIds, nameof(sourceIds), true);
        DecisionMemorySourceId[] canonicalSupersedesIds = CanonicalizeIds(supersedesSourceIds, nameof(supersedesSourceIds), false);
        DecisionMemorySourceId[] canonicalConflictIds = CanonicalizeIds(conflictsWithSourceIds, nameof(conflictsWithSourceIds), false);
        RejectOwnRelations(canonicalSourceIds, canonicalSupersedesIds, nameof(supersedesSourceIds));
        RejectOwnRelations(canonicalSourceIds, canonicalConflictIds, nameof(conflictsWithSourceIds));
        byte[] sourceBytesSnapshot = canonicalSourceBytes.ToArray();
        DecisionMemoryId memoryId = CreateMemoryId(
            actorId,
            kind,
            occurredAt,
            projectorVersion,
            constructionOrdinal,
            evidenceStatus,
            canonicalSourceIds,
            canonicalSupersedesIds,
            canonicalConflictIds,
            sourceBytesSnapshot);
        return new DecisionMemorySlice(
            memoryId,
            actorId,
            kind,
            occurredAt,
            projectorVersion,
            constructionOrdinal,
            evidenceStatus,
            canonicalSourceIds,
            canonicalSupersedesIds,
            canonicalConflictIds,
            sourceBytesSnapshot);
    }

    public byte[] GetCanonicalSourceBytes()
    {
        return _canonicalSourceBytes.ToArray();
    }

    public byte[] GetCanonicalBytes()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", SliceProtocolVersion);
            writer.WriteString("memory_id", MemoryId.Value);
            WriteIdentity(writer, this);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static DecisionMemoryId CreateMemoryId(
        ActorId actorId,
        DecisionMemoryKind kind,
        SimTime occurredAt,
        DecisionMemoryProjectorVersion projectorVersion,
        long constructionOrdinal,
        DecisionMemoryEvidenceStatus evidenceStatus,
        IReadOnlyList<DecisionMemorySourceId> sourceIds,
        IReadOnlyList<DecisionMemorySourceId> supersedesSourceIds,
        IReadOnlyList<DecisionMemorySourceId> conflictsWithSourceIds,
        byte[] canonicalSourceBytes)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", IdentityProtocolVersion);
            WriteIdentity(
                writer,
                actorId,
                kind,
                occurredAt,
                projectorVersion,
                constructionOrdinal,
                evidenceStatus,
                sourceIds,
                supersedesSourceIds,
                conflictsWithSourceIds,
                canonicalSourceBytes);
            writer.WriteEndObject();
        }

        return new DecisionMemoryId(Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant());
    }

    private static void WriteIdentity(Utf8JsonWriter writer, DecisionMemorySlice slice)
    {
        WriteIdentity(
            writer,
            slice.ActorId,
            slice.Kind,
            slice.OccurredAt,
            slice.ProjectorVersion,
            slice.ConstructionOrdinal,
            slice.EvidenceStatus,
            slice.SourceIds,
            slice.SupersedesSourceIds,
            slice.ConflictsWithSourceIds,
            slice._canonicalSourceBytes);
    }

    private static void WriteIdentity(
        Utf8JsonWriter writer,
        ActorId actorId,
        DecisionMemoryKind kind,
        SimTime occurredAt,
        DecisionMemoryProjectorVersion projectorVersion,
        long constructionOrdinal,
        DecisionMemoryEvidenceStatus evidenceStatus,
        IReadOnlyList<DecisionMemorySourceId> sourceIds,
        IReadOnlyList<DecisionMemorySourceId> supersedesSourceIds,
        IReadOnlyList<DecisionMemorySourceId> conflictsWithSourceIds,
        byte[] canonicalSourceBytes)
    {
        writer.WriteString("actor_id", actorId.Value);
        writer.WriteString("kind", kind.Value);
        writer.WriteNumber("occurred_at_ticks", occurredAt.Ticks);
        writer.WriteString("projector_version", projectorVersion.Value);
        writer.WriteNumber("construction_ordinal", constructionOrdinal);
        writer.WriteString("evidence_status", EvidenceStatusToken(evidenceStatus));
        WriteSourceIds(writer, "source_ids", sourceIds);
        WriteSourceIds(writer, "supersedes_source_ids", supersedesSourceIds);
        WriteSourceIds(writer, "conflicts_with_source_ids", conflictsWithSourceIds);
        writer.WriteString("canonical_source_base64", Convert.ToBase64String(canonicalSourceBytes));
    }

    private static void WriteSourceIds(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyList<DecisionMemorySourceId> sourceIds)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (DecisionMemorySourceId sourceId in sourceIds)
        {
            writer.WriteStringValue(sourceId.Value);
        }

        writer.WriteEndArray();
    }

    private static string EvidenceStatusToken(DecisionMemoryEvidenceStatus evidenceStatus)
    {
        return evidenceStatus switch
        {
            DecisionMemoryEvidenceStatus.Current => "current",
            DecisionMemoryEvidenceStatus.Stale => "stale",
            DecisionMemoryEvidenceStatus.Superseded => "superseded",
            DecisionMemoryEvidenceStatus.Uncertain => "uncertain",
            _ => throw new ArgumentOutOfRangeException(nameof(evidenceStatus))
        };
    }

    private static DecisionMemorySourceId[] CanonicalizeIds(
        IEnumerable<DecisionMemorySourceId> sourceIds,
        string parameterName,
        bool requireOne)
    {
        DecisionMemorySourceId[] snapshot = sourceIds.ToArray();
        if (requireOne && snapshot.Length == 0)
        {
            throw new ArgumentException("At least one source identity is required.", parameterName);
        }

        foreach (DecisionMemorySourceId sourceId in snapshot)
        {
            ArgumentNullException.ThrowIfNull(sourceId, parameterName);
        }

        Array.Sort(snapshot, DecisionMemorySourceIdComparer.Instance);
        for (int index = 1; index < snapshot.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(snapshot[index - 1].Value, snapshot[index].Value))
            {
                throw new ArgumentException("Source identities must be unique.", parameterName);
            }
        }

        return snapshot;
    }

    private static void RejectOwnRelations(
        IReadOnlyList<DecisionMemorySourceId> sourceIds,
        IReadOnlyList<DecisionMemorySourceId> relationIds,
        string parameterName)
    {
        foreach (DecisionMemorySourceId relationId in relationIds)
        {
            foreach (DecisionMemorySourceId sourceId in sourceIds)
            {
                if (StringComparer.Ordinal.Equals(relationId.Value, sourceId.Value))
                {
                    throw new ArgumentException("A provenance relation cannot name this slice's own source identity.", parameterName);
                }
            }
        }
    }

    private sealed class DecisionMemorySourceIdComparer : IComparer<DecisionMemorySourceId>
    {
        public static DecisionMemorySourceIdComparer Instance { get; } = new();

        public int Compare(DecisionMemorySourceId? left, DecisionMemorySourceId? right)
        {
            return StringComparer.Ordinal.Compare(left?.Value, right?.Value);
        }
    }
}

internal static class CanonicalToken
{
    public static string Validate(string? value, string parameterName)
    {
        if (value is null || value.Length is < 1 or > 64 || value[0] < 'a' || value[0] > 'z')
        {
            throw new ArgumentException("Value must be a canonical lower-ASCII token.", parameterName);
        }

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_')
            {
                throw new ArgumentException("Value must be a canonical lower-ASCII token.", parameterName);
            }
        }

        return value;
    }
}
