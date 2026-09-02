using System.Collections.ObjectModel;
using System.Text.Json;

namespace Alice.Memory;

public enum MemoryPacketStrategy
{
    Verbatim,
    Summary
}

public sealed record MemoryPacketTokenizerVersion
{
    public MemoryPacketTokenizerVersion(string value)
    {
        Value = CanonicalToken.Validate(value, nameof(value));
    }

    public string Value { get; }
}

public readonly record struct MemoryPacketTokenCeiling
{
    public MemoryPacketTokenCeiling(int value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public int Value { get; }
}

public interface IMemoryPacketTokenCounter
{
    int CountTokens(ReadOnlySpan<byte> modelVisibleBytes);
}

public enum MemoryPacketSkipReason
{
    WouldExceedCeiling,
    OversizeSlice,
    Duplicate
}

public sealed record MemoryPacketSkippedSlice(
    DecisionMemoryId MemoryId,
    int OriginalPosition,
    int SliceTokens,
    int RemainingTokens,
    MemoryPacketSkipReason Reason);

public sealed class MemoryPacketPackingTrace
{
    private readonly ReadOnlyCollection<MemoryPacketSkippedSlice> _skippedSlices;
    private readonly byte[] _canonicalBytes;

    internal MemoryPacketPackingTrace(
        int consideredCount,
        int acceptedCount,
        IEnumerable<MemoryPacketSkippedSlice> skippedSlices,
        int finalPacketTokens)
    {
        ArgumentNullException.ThrowIfNull(skippedSlices);
        MemoryPacketSkippedSlice[] skipped = skippedSlices.ToArray();
        if (consideredCount < 0
            || acceptedCount < 0
            || finalPacketTokens < 0
            || consideredCount != acceptedCount + skipped.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(consideredCount));
        }

        ConsideredCount = consideredCount;
        AcceptedCount = acceptedCount;
        SkippedCount = skipped.Length;
        OversizeCount = skipped.Count(value => value.Reason == MemoryPacketSkipReason.OversizeSlice);
        FinalPacketTokens = finalPacketTokens;
        _skippedSlices = Array.AsReadOnly(skipped);
        _canonicalBytes = Serialize();
    }

    public int ConsideredCount { get; }
    public int AcceptedCount { get; }
    public int SkippedCount { get; }
    public int OversizeCount { get; }
    public int FinalPacketTokens { get; }
    public IReadOnlyList<MemoryPacketSkippedSlice> SkippedSlices => _skippedSlices;

    public byte[] GetCanonicalBytes() => _canonicalBytes.ToArray();

    private byte[] Serialize()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.memory-packet-packing-trace.v1");
            writer.WriteNumber("considered_count", ConsideredCount);
            writer.WriteNumber("accepted_count", AcceptedCount);
            writer.WriteNumber("skipped_count", SkippedCount);
            writer.WriteNumber("oversize_count", OversizeCount);
            writer.WriteNumber("final_packet_tokens", FinalPacketTokens);
            writer.WritePropertyName("skipped_slices");
            writer.WriteStartArray();
            foreach (MemoryPacketSkippedSlice skipped in SkippedSlices)
            {
                writer.WriteStartObject();
                writer.WriteString("memory_id", skipped.MemoryId.Value);
                writer.WriteNumber("original_position", skipped.OriginalPosition);
                writer.WriteNumber("slice_tokens", skipped.SliceTokens);
                writer.WriteNumber("remaining_tokens", skipped.RemainingTokens);
                writer.WriteString("reason", skipped.Reason.ToString());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }
}

public sealed class MemoryPacket
{
    private readonly byte[] _bytes;
    private readonly ReadOnlyCollection<DecisionMemoryId> _included;
    private readonly ReadOnlyCollection<DecisionMemoryId> _truncated;

    internal MemoryPacket(
        MemoryPacketStrategy strategy,
        DecisionMemoryCandidateSet set,
        MemoryPacketTokenizerVersion tokenizer,
        int consumed,
        int unspent,
        IEnumerable<DecisionMemoryId> included,
        IEnumerable<DecisionMemoryId> truncated,
        MemoryPacketPackingTrace packingTrace,
        byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(included);
        ArgumentNullException.ThrowIfNull(truncated);
        ArgumentNullException.ThrowIfNull(packingTrace);
        ArgumentNullException.ThrowIfNull(bytes);
        Strategy = strategy;
        CandidateSet = set;
        TokenizerVersion = tokenizer;
        ConsumedTokens = consumed;
        UnspentTokens = unspent;
        _included = Array.AsReadOnly(included.ToArray());
        _truncated = Array.AsReadOnly(truncated.ToArray());
        PackingTrace = packingTrace;
        _bytes = bytes.ToArray();
    }

    public MemoryPacketStrategy Strategy { get; }
    public DecisionMemoryCandidateSet CandidateSet { get; }
    public MemoryPacketTokenizerVersion TokenizerVersion { get; }
    public int ConsumedTokens { get; }
    public int UnspentTokens { get; }
    public IReadOnlyList<DecisionMemoryId> IncludedMemoryIds => _included;
    public IReadOnlyList<DecisionMemoryId> TruncatedMemoryIds => _truncated;
    public MemoryPacketPackingTrace PackingTrace { get; }
    public byte[] GetModelVisibleBytes() => _bytes.ToArray();
}

public abstract record MemoryPacketBuildOutcome
{
    private protected MemoryPacketBuildOutcome()
    {
    }
}

public sealed record MemoryPacketBuildSuccess(MemoryPacket Packet) : MemoryPacketBuildOutcome;
public sealed record MemoryPacketEnvelopeOverCeiling(int ConsumedTokens) : MemoryPacketBuildOutcome;
public sealed record FrozenSummaryOverCeiling(int ConsumedTokens) : MemoryPacketBuildOutcome;
