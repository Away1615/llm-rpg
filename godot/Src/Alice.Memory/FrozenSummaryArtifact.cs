using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Alice.Memory;

public sealed record FrozenSummaryArtifactId
{
    public FrozenSummaryArtifactId(string value) { Value = new DecisionMemoryId(value).Value; }
    public string Value { get; }
}
public sealed record FrozenSummaryProfileVersion
{
    public FrozenSummaryProfileVersion(string value) { Value = CanonicalToken.Validate(value, nameof(value)); }
    public string Value { get; }
}
public sealed record FrozenSummaryArtifactVersion
{
    public FrozenSummaryArtifactVersion(string value) { Value = CanonicalToken.Validate(value, nameof(value)); }
    public string Value { get; }
}

public sealed class FrozenSummaryClaim
{
    private readonly ReadOnlyCollection<DecisionMemorySourceId> _sourceIds;
    private readonly ReadOnlyCollection<DecisionMemorySourceId> _supersedesSourceIds;
    private readonly ReadOnlyCollection<DecisionMemorySourceId> _conflictsWithSourceIds;
    private readonly byte[] _content;
    public FrozenSummaryClaim(long ordinal, byte[] content, DecisionMemoryEvidenceStatus evidenceStatus, IEnumerable<DecisionMemorySourceId> sourceIds, IEnumerable<DecisionMemorySourceId> supersedesSourceIds, IEnumerable<DecisionMemorySourceId> conflictsWithSourceIds)
    {
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        ArgumentNullException.ThrowIfNull(content); if (content.Length == 0) throw new ArgumentException("Claim content is required.", nameof(content));
        _ = new UTF8Encoding(false, true).GetString(content);
        if (!Enum.IsDefined(evidenceStatus)) throw new ArgumentOutOfRangeException(nameof(evidenceStatus));
        Ordinal = ordinal; EvidenceStatus = evidenceStatus; _content = content.ToArray();
        _sourceIds = Array.AsReadOnly(Canonical(sourceIds, nameof(sourceIds), true));
        _supersedesSourceIds = Array.AsReadOnly(Canonical(supersedesSourceIds, nameof(supersedesSourceIds), false));
        _conflictsWithSourceIds = Array.AsReadOnly(Canonical(conflictsWithSourceIds, nameof(conflictsWithSourceIds), false));
    }
    public long Ordinal { get; } public DecisionMemoryEvidenceStatus EvidenceStatus { get; }
    public IReadOnlyList<DecisionMemorySourceId> SourceIds => _sourceIds;
    public IReadOnlyList<DecisionMemorySourceId> SupersedesSourceIds => _supersedesSourceIds;
    public IReadOnlyList<DecisionMemorySourceId> ConflictsWithSourceIds => _conflictsWithSourceIds;
    public byte[] GetContentBytes() => _content.ToArray();
    private static DecisionMemorySourceId[] Canonical(IEnumerable<DecisionMemorySourceId> input, string name, bool required)
    {
        ArgumentNullException.ThrowIfNull(input); var ids = input.ToArray(); if (required && ids.Length == 0) throw new ArgumentException("Claim requires source IDs.", name);
        foreach (var id in ids) ArgumentNullException.ThrowIfNull(id, name); Array.Sort(ids, (left, right) => StringComparer.Ordinal.Compare(left.Value, right.Value));
        for (int i=1;i<ids.Length;i++) if (ids[i-1].Value == ids[i].Value) throw new ArgumentException("Source IDs must be unique.", name); return ids;
    }
}

public sealed class FrozenSummaryArtifact
{
    private readonly ReadOnlyCollection<FrozenSummaryClaim> _claims;
    private readonly ReadOnlyCollection<FrozenSummaryEmotionClaim> _emotionClaims;
    private readonly ReadOnlyCollection<DecisionMemoryId> _inputMemoryIds;
    private readonly ReadOnlyCollection<DecisionMemorySourceId> _inputSourceIds;
    private FrozenSummaryArtifact(FrozenSummaryArtifactId id, DecisionMemoryCandidateSet candidateSet, FrozenSummaryProfileVersion profileVersion, FrozenSummaryArtifactVersion artifactVersion, FrozenSummaryClaim[] claims, FrozenSummaryEmotionClaim[] emotionClaims)
    { ArtifactId=id; CandidateSet=candidateSet; ProfileVersion=profileVersion; ArtifactVersion=artifactVersion; _claims=Array.AsReadOnly(claims); _emotionClaims=Array.AsReadOnly(emotionClaims); _inputMemoryIds=Array.AsReadOnly(candidateSet.RankedSlices.Select(s=>s.MemoryId).ToArray()); _inputSourceIds=Array.AsReadOnly(candidateSet.SourceIds.ToArray()); }
    public FrozenSummaryArtifactId ArtifactId { get; } public DecisionMemoryCandidateSet CandidateSet { get; } public FrozenSummaryProfileVersion ProfileVersion { get; } public FrozenSummaryArtifactVersion ArtifactVersion { get; }
    public IReadOnlyList<FrozenSummaryClaim> Claims => _claims; public IReadOnlyList<FrozenSummaryEmotionClaim> EmotionClaims => _emotionClaims; public IReadOnlyList<DecisionMemoryId> InputMemoryIds => _inputMemoryIds; public IReadOnlyList<DecisionMemorySourceId> InputSourceIds => _inputSourceIds;
    public static FrozenSummaryArtifact Create(DecisionMemoryCandidateSet candidateSet, FrozenSummaryProfileVersion profileVersion, FrozenSummaryArtifactVersion artifactVersion, IEnumerable<FrozenSummaryClaim> claims)
    { return Create(candidateSet, profileVersion, artifactVersion, claims, Array.Empty<FrozenSummaryEmotionClaim>()); }
    public static FrozenSummaryArtifact Create(DecisionMemoryCandidateSet candidateSet, FrozenSummaryProfileVersion profileVersion, FrozenSummaryArtifactVersion artifactVersion, IEnumerable<FrozenSummaryClaim> claims, IEnumerable<FrozenSummaryEmotionClaim> emotionClaims)
    {
        ArgumentNullException.ThrowIfNull(candidateSet); ArgumentNullException.ThrowIfNull(profileVersion); ArgumentNullException.ThrowIfNull(artifactVersion); ArgumentNullException.ThrowIfNull(claims); ArgumentNullException.ThrowIfNull(emotionClaims);
        var ordered=claims.ToArray(); foreach(var claim in ordered) ArgumentNullException.ThrowIfNull(claim, nameof(claims)); Array.Sort(ordered,(a,b)=>a.Ordinal.CompareTo(b.Ordinal));
        for(int i=1;i<ordered.Length;i++) if(ordered[i-1].Ordinal==ordered[i].Ordinal) throw new ArgumentException("Claim ordinals must be unique.",nameof(claims));
        foreach(var claim in ordered) foreach(var id in claim.SourceIds.Concat(claim.SupersedesSourceIds).Concat(claim.ConflictsWithSourceIds)) if(!candidateSet.SourceIds.Any(source=>source.Value==id.Value)) throw new ArgumentException("Claim references a foreign source ID.",nameof(claims));
        FrozenSummaryEmotionClaim[] orderedEmotions=emotionClaims.ToArray(); foreach(var emotion in orderedEmotions) ArgumentNullException.ThrowIfNull(emotion,nameof(emotionClaims)); Array.Sort(orderedEmotions,FrozenSummaryEmotionClaimComparer.Instance);
        for(int i=0;i<orderedEmotions.Length;i++){FrozenSummaryEmotionClaim emotion=orderedEmotions[i];FrozenSummaryClaim? owner=ordered.FirstOrDefault(claim=>claim.Ordinal==emotion.ClaimOrdinal);if(owner is null||!owner.SourceIds.Any(source=>source==emotion.SourceId))throw new ArgumentException("Summary emotion must bind to a cited source of its exact claim.",nameof(emotionClaims));if(i>0&&orderedEmotions[i-1].ClaimOrdinal==emotion.ClaimOrdinal&&orderedEmotions[i-1].SourceId==emotion.SourceId)throw new ArgumentException("Summary emotion claim bindings must be unique.",nameof(emotionClaims));}
        byte[] bytes=Serialize(candidateSet,profileVersion,artifactVersion,ordered,orderedEmotions); var artifactId=new FrozenSummaryArtifactId(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()); return new FrozenSummaryArtifact(artifactId,candidateSet,profileVersion,artifactVersion,ordered,orderedEmotions);
    }
    public byte[] GetCanonicalBytes()=>Serialize(CandidateSet,ProfileVersion,ArtifactVersion,Claims,EmotionClaims);
    private static byte[] Serialize(DecisionMemoryCandidateSet set,FrozenSummaryProfileVersion profile,FrozenSummaryArtifactVersion version,IReadOnlyList<FrozenSummaryClaim> claims,IReadOnlyList<FrozenSummaryEmotionClaim> emotionClaims)
    { using var b=new MemoryStream(); using(var w=new Utf8JsonWriter(b)){w.WriteStartObject();w.WriteString("protocol_version","frozen-summary-artifact-v2");w.WriteString("candidate_set_id",set.CandidateSetId.Value);w.WriteString("profile_version",profile.Value);w.WriteString("artifact_version",version.Value);w.WritePropertyName("input_memory_ids");w.WriteStartArray();foreach(var id in set.RankedSlices)w.WriteStringValue(id.MemoryId.Value);w.WriteEndArray();w.WritePropertyName("input_source_ids");w.WriteStartArray();foreach(var id in set.SourceIds)w.WriteStringValue(id.Value);w.WriteEndArray();w.WritePropertyName("claims");w.WriteStartArray();foreach(var c in claims){w.WriteStartObject();w.WriteNumber("ordinal",c.Ordinal);w.WriteString("evidence_status",Token(c.EvidenceStatus));WriteIds(w,"source_ids",c.SourceIds);WriteIds(w,"supersedes_source_ids",c.SupersedesSourceIds);WriteIds(w,"conflicts_with_source_ids",c.ConflictsWithSourceIds);w.WriteString("content_base64",Convert.ToBase64String(c.GetContentBytes()));w.WriteEndObject();}w.WriteEndArray();w.WritePropertyName("emotion_claims");w.WriteStartArray();foreach(var emotion in emotionClaims){w.WriteStartObject();w.WriteNumber("claim_ordinal",emotion.ClaimOrdinal);w.WriteString("source_id",emotion.SourceId.Value);w.WriteString("kind",emotion.Kind.Value);w.WriteNumber("valence",emotion.Valence);w.WriteNumber("intensity",emotion.Intensity);w.WriteEndObject();}w.WriteEndArray();w.WriteEndObject();}return b.ToArray(); }
    internal static void WriteIds(Utf8JsonWriter w,string name,IReadOnlyList<DecisionMemorySourceId> ids){w.WritePropertyName(name);w.WriteStartArray();foreach(var id in ids)w.WriteStringValue(id.Value);w.WriteEndArray();}
    internal static string Token(DecisionMemoryEvidenceStatus s)=>s switch{DecisionMemoryEvidenceStatus.Current=>"current",DecisionMemoryEvidenceStatus.Stale=>"stale",DecisionMemoryEvidenceStatus.Superseded=>"superseded",DecisionMemoryEvidenceStatus.Uncertain=>"uncertain",_=>throw new ArgumentOutOfRangeException(nameof(s))};

    private sealed class FrozenSummaryEmotionClaimComparer : IComparer<FrozenSummaryEmotionClaim>
    {
        public static FrozenSummaryEmotionClaimComparer Instance { get; } = new();
        public int Compare(FrozenSummaryEmotionClaim? left, FrozenSummaryEmotionClaim? right)
        {
            if (left is null) return right is null ? 0 : -1;
            if (right is null) return 1;
            int ordinal = left.ClaimOrdinal.CompareTo(right.ClaimOrdinal);
            return ordinal != 0 ? ordinal : StringComparer.Ordinal.Compare(left.SourceId.Value, right.SourceId.Value);
        }
    }
}
