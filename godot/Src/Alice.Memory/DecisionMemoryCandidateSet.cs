using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Alice.Actors;

namespace Alice.Memory;

public sealed record DecisionMemoryCandidateSetId
{
    public DecisionMemoryCandidateSetId(string value) { Value = new DecisionMemoryId(value).Value; }
    public string Value { get; }
}

public sealed class DecisionMemoryCandidateSet
{
    private readonly ReadOnlyCollection<DecisionMemorySlice> _slices;
    private readonly ReadOnlyCollection<DecisionMemorySourceId> _sourceIds;

    private DecisionMemoryCandidateSet(DecisionMemoryCandidateSetId id, ActorId actorId, DecisionMemorySlice[] slices, DecisionMemorySourceId[] sourceIds)
    {
        CandidateSetId = id;
        ActorId = actorId;
        _slices = Array.AsReadOnly(slices);
        _sourceIds = Array.AsReadOnly(sourceIds);
    }

    public DecisionMemoryCandidateSetId CandidateSetId { get; }
    public ActorId ActorId { get; }
    public IReadOnlyList<DecisionMemorySlice> RankedSlices => _slices;
    public IReadOnlyList<DecisionMemorySourceId> SourceIds => _sourceIds;

    public static DecisionMemoryCandidateSet Create(IEnumerable<DecisionMemorySlice> rankedSlices)
    {
        ArgumentNullException.ThrowIfNull(rankedSlices);
        DecisionMemorySlice[] slices = rankedSlices.ToArray();
        if (slices.Length == 0) throw new ArgumentException("Candidate set requires at least one slice.", nameof(rankedSlices));
        foreach (DecisionMemorySlice slice in slices) ArgumentNullException.ThrowIfNull(slice, nameof(rankedSlices));
        ActorId actorId = slices[0].ActorId;
        for (int index = 0; index < slices.Length; index++)
        {
            if (slices[index].ActorId != actorId) throw new ArgumentException("Candidate slices must belong to one ActorId.", nameof(rankedSlices));
            for (int previous = 0; previous < index; previous++)
            {
                if (slices[index].MemoryId == slices[previous].MemoryId) throw new ArgumentException("Candidate slices must have unique MemoryIds.", nameof(rankedSlices));
            }
        }

        DecisionMemorySourceId[] sourceIds = slices.SelectMany(slice => slice.SourceIds).OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
        sourceIds = sourceIds.Where((id, index) => index == 0 || !StringComparer.Ordinal.Equals(id.Value, sourceIds[index - 1].Value)).ToArray();
        byte[] bytes = Serialize(actorId, slices);
        var id = new DecisionMemoryCandidateSetId(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        return new DecisionMemoryCandidateSet(id, actorId, slices, sourceIds);
    }

    public byte[] GetCanonicalBytes() => Serialize(ActorId, RankedSlices);

    private static byte[] Serialize(ActorId actorId, IReadOnlyList<DecisionMemorySlice> slices)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", "decision-memory-candidate-set-v1");
            writer.WriteString("actor_id", actorId.Value);
            writer.WritePropertyName("ranked_slices"); writer.WriteStartArray();
            for (int index = 0; index < slices.Count; index++)
            {
                writer.WriteStartObject(); writer.WriteNumber("rank", index);
                writer.WriteString("slice_base64", Convert.ToBase64String(slices[index].GetCanonicalBytes())); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        return buffer.ToArray();
    }
}
