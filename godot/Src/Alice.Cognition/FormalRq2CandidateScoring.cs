using System.Collections.ObjectModel;
using System.Text.Json;
using Alice.Activities;
using Alice.Memory;

namespace Alice.Cognition;

public sealed record FormalRq2CandidateScoreInput
{
    private readonly ReadOnlyCollection<string> _typedReferences;

    public FormalRq2CandidateScoreInput(
        DecisionMemorySlice slice,
        IEnumerable<string> typedReferences,
        int importance)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(typedReferences);
        if (importance is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(importance));
        string[] references = NormalizeReferences(typedReferences, nameof(typedReferences));
        Slice = slice;
        _typedReferences = Array.AsReadOnly(references);
        Importance = importance;
    }

    public DecisionMemorySlice Slice { get; }
    public IReadOnlyList<string> TypedReferences => _typedReferences;
    public int Importance { get; }

    internal static string[] NormalizeReferences(IEnumerable<string> values, string parameterName)
    {
        string[] references = values.ToArray();
        foreach (string value in references)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Length > 128
                || value.Any(char.IsControl))
            {
                throw new ArgumentException(
                    "Typed references must be non-empty bounded identities.",
                    parameterName);
            }
        }

        return references
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed record FormalRq2CandidateScoreRow(
    DecisionMemoryId MemoryId,
    int Rank,
    long AgeHours,
    double RelevanceRaw,
    double RecencyRaw,
    int ImportanceRaw,
    double RelevanceNormalized,
    double RecencyNormalized,
    double ImportanceNormalized,
    double TotalScore);

public sealed class FormalRq2CandidateScoringResult
{
    private readonly ReadOnlyCollection<FormalRq2CandidateScoreRow> _rows;
    private readonly byte[] _canonicalBytes;

    internal FormalRq2CandidateScoringResult(
        DecisionMemoryCandidateSet candidateSet,
        SimTime cueSimTime,
        long ticksPerHour,
        double recencyBase,
        FormalRq2ConfigurationSetting scorerConfiguration,
        IEnumerable<FormalRq2CandidateScoreRow> rows)
    {
        ArgumentNullException.ThrowIfNull(candidateSet);
        ArgumentNullException.ThrowIfNull(scorerConfiguration);
        ArgumentNullException.ThrowIfNull(rows);
        if (!scorerConfiguration.IsResolved)
            throw new ArgumentException("Scorer configuration must be resolved.", nameof(scorerConfiguration));
        FormalRq2CandidateScoreRow[] snapshot = rows.ToArray();
        if (snapshot.Length != candidateSet.RankedSlices.Count
            || snapshot.Where((row, index) => row.Rank != index
                || row.MemoryId != candidateSet.RankedSlices[index].MemoryId).Any())
        {
            throw new ArgumentException("Scoring rows must exactly bind candidate rank order.", nameof(rows));
        }

        CandidateSet = candidateSet;
        CueSimTime = cueSimTime;
        TicksPerHour = ticksPerHour;
        RecencyBase = recencyBase;
        ScorerConfiguration = scorerConfiguration;
        _rows = Array.AsReadOnly(snapshot);
        _canonicalBytes = Serialize();
    }

    public DecisionMemoryCandidateSet CandidateSet { get; }
    public SimTime CueSimTime { get; }
    public long TicksPerHour { get; }
    public double RecencyBase { get; }
    public FormalRq2ConfigurationSetting ScorerConfiguration { get; }
    public IReadOnlyList<FormalRq2CandidateScoreRow> Rows => _rows;
    public byte[] GetCanonicalBytes() => _canonicalBytes.ToArray();

    private byte[] Serialize()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-rq2-candidate-scoring.v1");
            writer.WriteString("candidate_set_id", CandidateSet.CandidateSetId.Value);
            writer.WriteNumber("cue_sim_time_ticks", CueSimTime.Ticks);
            writer.WriteNumber("ticks_per_hour", TicksPerHour);
            writer.WriteNumber("recency_base", RecencyBase);
            writer.WriteString("scorer_configuration_evidence_id", ScorerConfiguration.EvidenceId);
            writer.WritePropertyName("rows");
            writer.WriteStartArray();
            foreach (FormalRq2CandidateScoreRow row in Rows)
            {
                writer.WriteStartObject();
                writer.WriteString("memory_id", row.MemoryId.Value);
                writer.WriteNumber("rank", row.Rank);
                writer.WriteNumber("age_hours", row.AgeHours);
                writer.WriteNumber("relevance_raw", row.RelevanceRaw);
                writer.WriteNumber("recency_raw", row.RecencyRaw);
                writer.WriteNumber("importance_raw", row.ImportanceRaw);
                writer.WriteNumber("relevance_normalized", row.RelevanceNormalized);
                writer.WriteNumber("recency_normalized", row.RecencyNormalized);
                writer.WriteNumber("importance_normalized", row.ImportanceNormalized);
                writer.WriteNumber("total_score", row.TotalScore);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }
}

public static class FormalRq2CandidateScorer
{
    public const double CanonicalRecencyBase = 0.995d;

    public static FormalRq2CandidateScoringResult Score(
        IEnumerable<string> queryTypedReferences,
        SimTime cueSimTime,
        long ticksPerHour,
        FormalRq2ConfigurationSetting scorerConfiguration,
        IEnumerable<FormalRq2CandidateScoreInput> candidates)
    {
        ArgumentNullException.ThrowIfNull(queryTypedReferences);
        ArgumentNullException.ThrowIfNull(scorerConfiguration);
        ArgumentNullException.ThrowIfNull(candidates);
        if (ticksPerHour <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerHour));
        if (!scorerConfiguration.IsResolved)
            throw new ArgumentException("Scorer configuration must be resolved.", nameof(scorerConfiguration));
        string[] queryReferences = FormalRq2CandidateScoreInput.NormalizeReferences(
            queryTypedReferences,
            nameof(queryTypedReferences));
        FormalRq2CandidateScoreInput[] inputs = candidates.ToArray();
        if (inputs.Length == 0)
            throw new ArgumentException("RQ2 candidate scoring requires at least one candidate.", nameof(candidates));

        var raw = new List<RawScore>(inputs.Length);
        foreach (FormalRq2CandidateScoreInput input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input, nameof(candidates));
            long ageTicks = cueSimTime.Ticks - input.Slice.OccurredAt.Ticks;
            if (ageTicks < 0)
                throw new ArgumentException("Candidate memory cannot occur after the scoring cue.", nameof(candidates));
            long ageHours = ageTicks / ticksPerHour;
            raw.Add(new RawScore(
                input.Slice,
                ageHours,
                Cosine(queryReferences, input.TypedReferences),
                Math.Pow(CanonicalRecencyBase, ageHours),
                input.Importance));
        }

        double relevanceMin = raw.Min(value => value.Relevance);
        double relevanceMax = raw.Max(value => value.Relevance);
        double recencyMin = raw.Min(value => value.Recency);
        double recencyMax = raw.Max(value => value.Recency);
        double importanceMin = raw.Min(value => value.Importance);
        double importanceMax = raw.Max(value => value.Importance);
        RankedScore[] ranked = raw.Select(value =>
        {
            double relevance = Normalize(value.Relevance, relevanceMin, relevanceMax);
            double recency = Normalize(value.Recency, recencyMin, recencyMax);
            double importance = Normalize(value.Importance, importanceMin, importanceMax);
            return new RankedScore(value, relevance, recency, importance, relevance + recency + importance);
        }).OrderByDescending(value => value.Total)
            .ThenBy(value => value.Raw.Slice.MemoryId.Value, StringComparer.Ordinal)
            .ToArray();
        DecisionMemoryCandidateSet candidateSet = DecisionMemoryCandidateSet.Create(
            ranked.Select(value => value.Raw.Slice));
        FormalRq2CandidateScoreRow[] rows = ranked.Select((value, rank) =>
            new FormalRq2CandidateScoreRow(
                value.Raw.Slice.MemoryId,
                rank,
                value.Raw.AgeHours,
                value.Raw.Relevance,
                value.Raw.Recency,
                value.Raw.Importance,
                value.RelevanceNormalized,
                value.RecencyNormalized,
                value.ImportanceNormalized,
                value.Total)).ToArray();
        return new FormalRq2CandidateScoringResult(
            candidateSet,
            cueSimTime,
            ticksPerHour,
            CanonicalRecencyBase,
            scorerConfiguration,
            rows);
    }

    private static double Cosine(
        IReadOnlyCollection<string> queryReferences,
        IReadOnlyCollection<string> candidateReferences)
    {
        if (queryReferences.Count == 0 || candidateReferences.Count == 0) return 0d;
        int intersection = queryReferences.Intersect(candidateReferences, StringComparer.Ordinal).Count();
        return intersection / Math.Sqrt((double)queryReferences.Count * candidateReferences.Count);
    }

    private static double Normalize(double value, double minimum, double maximum) =>
        maximum == minimum ? 0d : (value - minimum) / (maximum - minimum);

    private sealed record RawScore(
        DecisionMemorySlice Slice,
        long AgeHours,
        double Relevance,
        double Recency,
        int Importance);

    private sealed record RankedScore(
        RawScore Raw,
        double RelevanceNormalized,
        double RecencyNormalized,
        double ImportanceNormalized,
        double Total);
}
