using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace Alice.Memory;

public sealed record DecisionMemoryEmotionKind
{
    public DecisionMemoryEmotionKind(string value)
    {
        Value = CanonicalToken.Validate(value, nameof(value));
    }

    public string Value { get; }
}

public sealed record DecisionMemoryEmotion
{
    public DecisionMemoryEmotion(
        DecisionMemoryEmotionKind kind,
        double valence,
        double intensity,
        DecisionMemorySourceId sourceId,
        long capturedAtTicks)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(sourceId);
        ValidateValue(valence, -1, 1, nameof(valence));
        ValidateValue(intensity, 0, 1, nameof(intensity));
        if (capturedAtTicks < 0) throw new ArgumentOutOfRangeException(nameof(capturedAtTicks));
        Kind = kind;
        Valence = valence;
        Intensity = intensity;
        SourceId = sourceId;
        CapturedAtTicks = capturedAtTicks;
    }

    public DecisionMemoryEmotionKind Kind { get; }
    public double Valence { get; }
    public double Intensity { get; }
    public DecisionMemorySourceId SourceId { get; }
    public long CapturedAtTicks { get; }

    private static void ValidateValue(double value, double minimum, double maximum, string parameterName)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

public sealed record FormalRq2MemoryEmotionBinding(
    DecisionMemoryId MemoryId,
    DecisionMemoryEmotion? Emotion);

/// <summary>Exact ordered, pre-treatment emotion evidence shared by both formal RQ2 branches.</summary>
public sealed class FormalRq2PreTreatmentEmotionEvidence
{
    private readonly ReadOnlyCollection<FormalRq2MemoryEmotionBinding> _bindings;
    private readonly byte[] _canonicalBytes;

    private FormalRq2PreTreatmentEmotionEvidence(
        DecisionMemoryCandidateSetId candidateSetId,
        string evidenceId,
        FormalRq2MemoryEmotionBinding[] bindings,
        byte[] canonicalBytes)
    {
        CandidateSetId = candidateSetId;
        EvidenceId = evidenceId;
        _bindings = Array.AsReadOnly(bindings);
        _canonicalBytes = canonicalBytes;
    }

    public DecisionMemoryCandidateSetId CandidateSetId { get; }
    public string EvidenceId { get; }
    public IReadOnlyList<FormalRq2MemoryEmotionBinding> Bindings => _bindings;
    public byte[] GetCanonicalBytes() => _canonicalBytes.ToArray();

    public static FormalRq2PreTreatmentEmotionEvidence CreateNoEmotion(
        DecisionMemoryCandidateSet candidateSet)
    {
        ArgumentNullException.ThrowIfNull(candidateSet);
        var bindings = new List<FormalRq2MemoryEmotionBinding>();
        foreach (DecisionMemorySlice slice in candidateSet.RankedSlices)
            bindings.Add(new FormalRq2MemoryEmotionBinding(slice.MemoryId, null));
        return Create(candidateSet, bindings);
    }

    public static FormalRq2PreTreatmentEmotionEvidence Create(
        DecisionMemoryCandidateSet candidateSet,
        IEnumerable<FormalRq2MemoryEmotionBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(candidateSet);
        ArgumentNullException.ThrowIfNull(bindings);
        FormalRq2MemoryEmotionBinding[] snapshot = bindings.ToArray();
        if (snapshot.Length != candidateSet.RankedSlices.Count)
            throw new ArgumentException("Emotion evidence must cover the exact ordered candidate set.", nameof(bindings));
        for (int index = 0; index < snapshot.Length; index++)
        {
            FormalRq2MemoryEmotionBinding binding = snapshot[index]
                ?? throw new ArgumentException("Emotion evidence binding is required.", nameof(bindings));
            DecisionMemorySlice slice = candidateSet.RankedSlices[index];
            if (binding.MemoryId != slice.MemoryId)
                throw new ArgumentException("Emotion evidence order must exact-match pre-treatment candidate order.", nameof(bindings));
            if (binding.Emotion is not null && !ContainsSource(slice.SourceIds, binding.Emotion.SourceId))
                throw new ArgumentException("Memory emotion must bind to a source in its exact candidate slice.", nameof(bindings));
        }
        byte[] bytes = Serialize(candidateSet.CandidateSetId, snapshot);
        string evidenceId = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new FormalRq2PreTreatmentEmotionEvidence(candidateSet.CandidateSetId, evidenceId, snapshot, bytes);
    }

    private static bool ContainsSource(IReadOnlyList<DecisionMemorySourceId> sources, DecisionMemorySourceId expected)
    {
        foreach (DecisionMemorySourceId source in sources)
        {
            if (source == expected) return true;
        }
        return false;
    }

    private static byte[] Serialize(
        DecisionMemoryCandidateSetId candidateSetId,
        IReadOnlyList<FormalRq2MemoryEmotionBinding> bindings)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", "formal_rq2_pre_treatment_emotion_v1");
            writer.WriteString("candidate_set_id", candidateSetId.Value);
            writer.WritePropertyName("ordered_bindings");
            writer.WriteStartArray();
            foreach (FormalRq2MemoryEmotionBinding binding in bindings)
            {
                writer.WriteStartObject();
                writer.WriteString("memory_id", binding.MemoryId.Value);
                if (binding.Emotion is null)
                {
                    writer.WriteNull("emotion");
                }
                else
                {
                    writer.WritePropertyName("emotion");
                    writer.WriteStartObject();
                    writer.WriteString("kind", binding.Emotion.Kind.Value);
                    writer.WriteNumber("valence", binding.Emotion.Valence);
                    writer.WriteNumber("intensity", binding.Emotion.Intensity);
                    writer.WriteString("source_id", binding.Emotion.SourceId.Value);
                    writer.WriteNumber("captured_at_ticks", binding.Emotion.CapturedAtTicks);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }
}

public sealed record FrozenSummaryEmotionClaim
{
    public FrozenSummaryEmotionClaim(
        long claimOrdinal,
        DecisionMemorySourceId sourceId,
        DecisionMemoryEmotionKind kind,
        double valence,
        double intensity)
    {
        if (claimOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(claimOrdinal));
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(kind);
        _ = new DecisionMemoryEmotion(kind, valence, intensity, sourceId, 0);
        ClaimOrdinal = claimOrdinal;
        SourceId = sourceId;
        Kind = kind;
        Valence = valence;
        Intensity = intensity;
    }

    public long ClaimOrdinal { get; }
    public DecisionMemorySourceId SourceId { get; }
    public DecisionMemoryEmotionKind Kind { get; }
    public double Valence { get; }
    public double Intensity { get; }
}

public static class FormalRq2EmotionSourceGuard
{
    public static void Validate(
        FrozenSummaryArtifact artifact,
        FormalRq2PreTreatmentEmotionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(evidence);
        if (artifact.CandidateSet.CandidateSetId != evidence.CandidateSetId)
            throw new ArgumentException("Summary emotion evidence must bind to the exact pre-treatment candidate set.", nameof(evidence));
        foreach (FrozenSummaryEmotionClaim claim in artifact.EmotionClaims)
        {
            bool matched = false;
            foreach (FormalRq2MemoryEmotionBinding binding in evidence.Bindings)
            {
                DecisionMemoryEmotion? source = binding.Emotion;
                if (source is not null
                    && source.SourceId == claim.SourceId
                    && source.Kind == claim.Kind
                    && source.Valence.Equals(claim.Valence)
                    && claim.Intensity <= source.Intensity)
                {
                    matched = true;
                    break;
                }
            }
            if (!matched)
                throw new ArgumentException("Summary emotion must preserve a pre-treatment source kind and valence and may not amplify intensity.", nameof(artifact));
        }
    }
}
