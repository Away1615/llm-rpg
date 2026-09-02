using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Alice.Memory;

namespace Alice.LivingTown;

public enum LivingTownMemorySignificance
{
    RoutineMovement,
    Idle,
    UneventfulSleep,
    Failure,
    Injury,
    Promise,
    PlayerHelp,
    Social,
    Discovery,
    OtherSignificant
}

public enum LivingTownMemoryAdmissionStatus
{
    Admitted,
    RejectedInsignificant,
    Duplicate
}

public sealed record LivingTownMemoryAdmissionResult(
    LivingTownMemoryAdmissionStatus Status,
    LivingTownMemorySeed Memory);

public static class LivingTownMemoryAdmissionPolicy
{
    public static bool IsLongTermEligible(LivingTownMemorySignificance significance)
    {
        return significance switch
        {
            LivingTownMemorySignificance.RoutineMovement => false,
            LivingTownMemorySignificance.Idle => false,
            LivingTownMemorySignificance.UneventfulSleep => false,
            LivingTownMemorySignificance.Failure => true,
            LivingTownMemorySignificance.Injury => true,
            LivingTownMemorySignificance.Promise => true,
            LivingTownMemorySignificance.PlayerHelp => true,
            LivingTownMemorySignificance.Social => true,
            LivingTownMemorySignificance.Discovery => true,
            LivingTownMemorySignificance.OtherSignificant => true,
            _ => throw new ArgumentOutOfRangeException(nameof(significance))
        };
    }
}

public sealed record LivingTownMemoryRankingProfile
{
    public LivingTownMemoryRankingProfile(
        string profileId,
        double relevanceWeight,
        double recencyWeight,
        double emotionWeight,
        int resultLimit)
    {
        if (string.IsNullOrWhiteSpace(profileId)) throw new ArgumentException("Ranking profile identity is required.", nameof(profileId));
        ValidateWeight(relevanceWeight, nameof(relevanceWeight));
        ValidateWeight(recencyWeight, nameof(recencyWeight));
        ValidateWeight(emotionWeight, nameof(emotionWeight));
        if (relevanceWeight + recencyWeight + emotionWeight <= 0)
            throw new ArgumentException("At least one ranking signal weight must be positive.");
        if (resultLimit <= 0) throw new ArgumentOutOfRangeException(nameof(resultLimit));
        ProfileId = profileId;
        RelevanceWeight = relevanceWeight;
        RecencyWeight = recencyWeight;
        EmotionWeight = emotionWeight;
        ResultLimit = resultLimit;
    }

    public string ProfileId { get; }
    public double RelevanceWeight { get; }
    public double RecencyWeight { get; }
    public double EmotionWeight { get; }
    public int ResultLimit { get; }

    private static void ValidateWeight(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(parameterName);
    }
}

public sealed record LivingTownMemoryRankEvidence
{
    public LivingTownMemoryRankEvidence(string memoryId, double relevance, double recency)
    {
        if (string.IsNullOrWhiteSpace(memoryId)) throw new ArgumentException("Memory identity is required.", nameof(memoryId));
        CurrentEmotionState.ValidateEmotionValue(relevance, 0, 1, nameof(relevance));
        CurrentEmotionState.ValidateEmotionValue(recency, 0, 1, nameof(recency));
        MemoryId = memoryId;
        Relevance = relevance;
        Recency = recency;
    }

    public string MemoryId { get; }
    public double Relevance { get; }
    public double Recency { get; }
}

public sealed record RankedLivingTownMemory(LivingTownMemorySeed Memory, double Score);

public static class LivingTownMemoryRanker
{
    public static IReadOnlyList<RankedLivingTownMemory> Rank(
        IEnumerable<LivingTownMemorySeed> memories,
        IEnumerable<LivingTownMemoryRankEvidence> rankEvidence,
        LivingTownMemoryRankingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(memories);
        ArgumentNullException.ThrowIfNull(rankEvidence);
        ArgumentNullException.ThrowIfNull(profile);
        var memoryById = new Dictionary<string, LivingTownMemorySeed>(StringComparer.Ordinal);
        foreach (LivingTownMemorySeed memory in memories)
        {
            ArgumentNullException.ThrowIfNull(memory, nameof(memories));
            if (!memoryById.TryAdd(memory.MemoryId, memory))
                throw new ArgumentException("Memories must have unique identities.", nameof(memories));
        }
        var scored = new List<RankedLivingTownMemory>();
        var evidenceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (LivingTownMemoryRankEvidence evidence in rankEvidence)
        {
            ArgumentNullException.ThrowIfNull(evidence, nameof(rankEvidence));
            if (!evidenceIds.Add(evidence.MemoryId))
                throw new ArgumentException("Ranking evidence must have unique memory identities.", nameof(rankEvidence));
            if (!memoryById.TryGetValue(evidence.MemoryId, out LivingTownMemorySeed? memory))
                throw new ArgumentException("Ranking evidence references an unknown memory.", nameof(rankEvidence));
            double score = evidence.Relevance * profile.RelevanceWeight
                + evidence.Recency * profile.RecencyWeight
                + memory.Emotion.Intensity * profile.EmotionWeight;
            scored.Add(new RankedLivingTownMemory(memory, score));
        }
        scored.Sort(RankedLivingTownMemoryComparer.Instance);
        int count = Math.Min(profile.ResultLimit, scored.Count);
        return new ReadOnlyCollection<RankedLivingTownMemory>(scored.GetRange(0, count));
    }

    private sealed class RankedLivingTownMemoryComparer : IComparer<RankedLivingTownMemory>
    {
        public static RankedLivingTownMemoryComparer Instance { get; } = new();

        public int Compare(RankedLivingTownMemory? left, RankedLivingTownMemory? right)
        {
            if (left is null) return right is null ? 0 : 1;
            if (right is null) return -1;
            int score = right.Score.CompareTo(left.Score);
            return score != 0
                ? score
                : StringComparer.Ordinal.Compare(left.Memory.MemoryId, right.Memory.MemoryId);
        }
    }
}

public sealed record Rq2MemoryEmotionBinding(DecisionMemoryId MemoryId, MemoryEmotion Emotion);

public sealed class Rq2PreTreatmentEmotionEvidence
{
    private readonly ReadOnlyCollection<Rq2MemoryEmotionBinding> _bindings;

    private Rq2PreTreatmentEmotionEvidence(
        DecisionMemoryCandidateSetId candidateSetId,
        string evidenceId,
        Rq2MemoryEmotionBinding[] bindings)
    {
        CandidateSetId = candidateSetId;
        EvidenceId = evidenceId;
        _bindings = Array.AsReadOnly(bindings);
    }

    public DecisionMemoryCandidateSetId CandidateSetId { get; }
    public string EvidenceId { get; }
    public IReadOnlyList<Rq2MemoryEmotionBinding> Bindings => _bindings;

    public static Rq2PreTreatmentEmotionEvidence Create(
        DecisionMemoryCandidateSet candidateSet,
        IEnumerable<Rq2MemoryEmotionBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(candidateSet);
        ArgumentNullException.ThrowIfNull(bindings);
        Rq2MemoryEmotionBinding[] snapshot = bindings.ToArray();
        if (snapshot.Length != candidateSet.RankedSlices.Count)
            throw new ArgumentException("Emotion evidence must cover the exact ordered candidate set.", nameof(bindings));
        for (int index = 0; index < snapshot.Length; index++)
        {
            Rq2MemoryEmotionBinding binding = snapshot[index]
                ?? throw new ArgumentException("Emotion evidence binding is required.", nameof(bindings));
            DecisionMemorySlice slice = candidateSet.RankedSlices[index];
            if (binding.MemoryId != slice.MemoryId)
                throw new ArgumentException("Emotion evidence order must exact-match pre-treatment candidate order.", nameof(bindings));
            bool sourceBound = false;
            foreach (DecisionMemorySourceId sourceId in slice.SourceIds)
            {
                if (StringComparer.Ordinal.Equals(sourceId.Value, binding.Emotion.SourceEventId.Value)) sourceBound = true;
            }
            if (!sourceBound)
                throw new ArgumentException("Memory emotion must bind to a source in its exact candidate slice.", nameof(bindings));
        }
        byte[] bytes = Serialize(candidateSet.CandidateSetId, snapshot);
        string evidenceId = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new Rq2PreTreatmentEmotionEvidence(candidateSet.CandidateSetId, evidenceId, snapshot);
    }

    private static byte[] Serialize(
        DecisionMemoryCandidateSetId candidateSetId,
        IReadOnlyList<Rq2MemoryEmotionBinding> bindings)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", "rq2_pre_treatment_emotion_v1");
            writer.WriteString("candidate_set_id", candidateSetId.Value);
            writer.WritePropertyName("ordered_bindings");
            writer.WriteStartArray();
            foreach (Rq2MemoryEmotionBinding binding in bindings)
            {
                writer.WriteStartObject();
                writer.WriteString("memory_id", binding.MemoryId.Value);
                writer.WriteString("source_event_id", binding.Emotion.SourceEventId.Value);
                writer.WriteString("kind", binding.Emotion.Kind.ToString());
                writer.WriteNumber("valence", binding.Emotion.Valence);
                writer.WriteNumber("intensity", binding.Emotion.Intensity);
                writer.WriteNumber("captured_at_ticks", binding.Emotion.CapturedAtTicks);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }
}

public sealed record Rq2SummaryEmotionClaim
{
    public Rq2SummaryEmotionClaim(
        long claimOrdinal,
        SourceEventId sourceEventId,
        LivingTownEmotionKind kind,
        double valence,
        double intensity)
    {
        if (claimOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(claimOrdinal));
        ArgumentNullException.ThrowIfNull(sourceEventId);
        CurrentEmotionState.ValidateEmotionValue(valence, -1, 1, nameof(valence));
        CurrentEmotionState.ValidateEmotionValue(intensity, 0, 1, nameof(intensity));
        ClaimOrdinal = claimOrdinal;
        SourceEventId = sourceEventId;
        Kind = kind;
        Valence = valence;
        Intensity = intensity;
    }

    public long ClaimOrdinal { get; }
    public SourceEventId SourceEventId { get; }
    public LivingTownEmotionKind Kind { get; }
    public double Valence { get; }
    public double Intensity { get; }
}
