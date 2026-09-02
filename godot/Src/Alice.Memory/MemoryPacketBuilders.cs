using System.Text;
using System.Text.Json;

namespace Alice.Memory;

public static class MemoryPacketBuilders
{
    public static MemoryPacketBuildOutcome BuildVerbatim(
        DecisionMemoryCandidateSet set,
        IMemoryPacketTokenCounter counter,
        MemoryPacketTokenCeiling ceiling,
        MemoryPacketTokenizerVersion tokenizer)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(counter);
        ArgumentNullException.ThrowIfNull(tokenizer);
        var included = new List<DecisionMemorySlice>();
        var skipped = new List<MemoryPacketSkippedSlice>();
        var includedIds = new HashSet<DecisionMemoryId>();
        byte[] empty = RenderVerbatim(included);
        int emptyTokens = Count(counter, empty);
        if (emptyTokens > ceiling.Value) return new MemoryPacketEnvelopeOverCeiling(emptyTokens);

        for (int position = 0; position < set.RankedSlices.Count; position++)
        {
            DecisionMemorySlice slice = set.RankedSlices[position];
            int currentTokens = Count(counter, RenderVerbatim(included));
            int remainingTokens = ceiling.Value - currentTokens;
            int sliceTokens = Count(counter, RenderVerbatimSlice(slice));
            if (!includedIds.Add(slice.MemoryId))
            {
                skipped.Add(new MemoryPacketSkippedSlice(
                    slice.MemoryId,
                    position,
                    sliceTokens,
                    remainingTokens,
                    MemoryPacketSkipReason.Duplicate));
                continue;
            }

            byte[] trial = RenderVerbatim(included.Append(slice).ToArray());
            if (Count(counter, trial) > ceiling.Value)
            {
                MemoryPacketSkipReason reason = sliceTokens > ceiling.Value - emptyTokens
                    ? MemoryPacketSkipReason.OversizeSlice
                    : MemoryPacketSkipReason.WouldExceedCeiling;
                skipped.Add(new MemoryPacketSkippedSlice(
                    slice.MemoryId,
                    position,
                    sliceTokens,
                    remainingTokens,
                    reason));
                continue;
            }

            included.Add(slice);
        }

        byte[] final = RenderVerbatim(included);
        int used = Count(counter, final);
        var trace = new MemoryPacketPackingTrace(
            set.RankedSlices.Count,
            included.Count,
            skipped,
            used);
        return new MemoryPacketBuildSuccess(new MemoryPacket(
            MemoryPacketStrategy.Verbatim,
            set,
            tokenizer,
            used,
            ceiling.Value - used,
            included.Select(slice => slice.MemoryId),
            skipped.Select(value => value.MemoryId),
            trace,
            final));
    }

    public static MemoryPacketBuildOutcome BuildSummary(
        DecisionMemoryCandidateSet set,
        FrozenSummaryArtifact artifact,
        IMemoryPacketTokenCounter counter,
        MemoryPacketTokenCeiling ceiling,
        MemoryPacketTokenizerVersion tokenizer)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(counter);
        ArgumentNullException.ThrowIfNull(tokenizer);
        if (set.CandidateSetId != artifact.CandidateSet.CandidateSetId
            || !set.GetCanonicalBytes().AsSpan().SequenceEqual(artifact.CandidateSet.GetCanonicalBytes()))
        {
            throw new ArgumentException(
                "Artifact must bind to this exact canonical candidate set.",
                nameof(artifact));
        }

        byte[] empty = RenderSummary([]);
        int emptyTokens = Count(counter, empty);
        if (emptyTokens > ceiling.Value) return new MemoryPacketEnvelopeOverCeiling(emptyTokens);
        byte[] bytes = RenderSummary(artifact.Claims);
        int used = Count(counter, bytes);
        if (artifact.Claims.Count > 0 && used > ceiling.Value)
            return new FrozenSummaryOverCeiling(used);
        var trace = new MemoryPacketPackingTrace(0, 0, [], used);
        return new MemoryPacketBuildSuccess(new MemoryPacket(
            MemoryPacketStrategy.Summary,
            set,
            tokenizer,
            used,
            ceiling.Value - used,
            [],
            [],
            trace,
            bytes));
    }

    private static int Count(IMemoryPacketTokenCounter counter, byte[] bytes)
    {
        int value = counter.CountTokens(bytes);
        if (value < 0) throw new InvalidOperationException("Token counter returned a negative count.");
        return value;
    }

    private static byte[] RenderVerbatim(IReadOnlyList<DecisionMemorySlice> slices)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("memory");
            writer.WriteStartArray();
            foreach (DecisionMemorySlice slice in slices) WriteVerbatimSlice(writer, slice);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static byte[] RenderVerbatimSlice(DecisionMemorySlice slice)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) WriteVerbatimSlice(writer, slice);
        return buffer.ToArray();
    }

    private static void WriteVerbatimSlice(Utf8JsonWriter writer, DecisionMemorySlice slice)
    {
        writer.WriteStartObject();
        writer.WriteString("memory_id", slice.MemoryId.Value);
        writer.WriteNumber("occurred_at_ticks", slice.OccurredAt.Ticks);
        writer.WriteString("evidence_status", FrozenSummaryArtifact.Token(slice.EvidenceStatus));
        FrozenSummaryArtifact.WriteIds(writer, "source_ids", slice.SourceIds);
        FrozenSummaryArtifact.WriteIds(writer, "supersedes_source_ids", slice.SupersedesSourceIds);
        FrozenSummaryArtifact.WriteIds(writer, "conflicts_with_source_ids", slice.ConflictsWithSourceIds);
        writer.WriteString(
            "content",
            new UTF8Encoding(false, true).GetString(slice.GetCanonicalSourceBytes()));
        writer.WriteEndObject();
    }

    private static byte[] RenderSummary(IReadOnlyList<FrozenSummaryClaim> claims)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("memory");
            writer.WriteStartArray();
            foreach (FrozenSummaryClaim claim in claims)
            {
                writer.WriteStartObject();
                writer.WriteString("evidence_status", FrozenSummaryArtifact.Token(claim.EvidenceStatus));
                FrozenSummaryArtifact.WriteIds(writer, "source_ids", claim.SourceIds);
                FrozenSummaryArtifact.WriteIds(writer, "supersedes_source_ids", claim.SupersedesSourceIds);
                FrozenSummaryArtifact.WriteIds(writer, "conflicts_with_source_ids", claim.ConflictsWithSourceIds);
                writer.WriteString(
                    "content",
                    new UTF8Encoding(false, true).GetString(claim.GetContentBytes()));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }
}
