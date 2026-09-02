using System.Collections.ObjectModel;
using System.Text.Json;

namespace Alice.Memory;

public enum FrozenSummaryArtifactLookupKind
{
    Found,
    Missing,
    CandidateSetConflict
}

public sealed record FrozenSummaryArtifactLookupResult
{
    private FrozenSummaryArtifactLookupResult(
        FrozenSummaryArtifactLookupKind kind,
        FrozenSummaryArtifact? artifact)
    {
        Kind = kind;
        Artifact = artifact;
    }

    public FrozenSummaryArtifactLookupKind Kind { get; }
    public FrozenSummaryArtifact? Artifact { get; }

    internal static FrozenSummaryArtifactLookupResult Found(FrozenSummaryArtifact artifact)
    {
        return new FrozenSummaryArtifactLookupResult(FrozenSummaryArtifactLookupKind.Found, artifact);
    }

    internal static FrozenSummaryArtifactLookupResult Closed(FrozenSummaryArtifactLookupKind kind)
    {
        return new FrozenSummaryArtifactLookupResult(kind, null);
    }
}

/// <summary>Immutable offline Summary bundle indexed by stable candidate-set identity.</summary>
public sealed class FrozenSummaryArtifactRegistry
{
    private const string ProtocolVersion = "frozen-summary-artifact-registry-v1";
    private readonly ReadOnlyCollection<FrozenSummaryArtifact> _artifacts;
    private readonly Dictionary<string, FrozenSummaryArtifact> _artifactByCandidateSetId;
    private readonly byte[] _canonicalBytes;

    public FrozenSummaryArtifactRegistry(
        FrozenSummaryProfileVersion profileVersion,
        IEnumerable<FrozenSummaryArtifact> artifacts,
        string registryId)
    {
        ArgumentNullException.ThrowIfNull(profileVersion);
        ArgumentNullException.ThrowIfNull(artifacts);
        if (string.IsNullOrWhiteSpace(registryId) || !StringComparer.Ordinal.Equals(registryId, registryId.Trim()))
            throw new ArgumentException("Summary registry ID must be non-empty and trimmed.", nameof(registryId));

        FrozenSummaryArtifact[] snapshot = artifacts.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(IsNullArtifact))
        {
            throw new ArgumentException(
                "A frozen Summary registry requires at least one non-null artifact.",
                nameof(artifacts));
        }

        Array.Sort(snapshot, ArtifactComparer.Instance);
        _artifactByCandidateSetId = new Dictionary<string, FrozenSummaryArtifact>(StringComparer.Ordinal);
        foreach (FrozenSummaryArtifact artifact in snapshot)
        {
            if (artifact.ProfileVersion != profileVersion)
            {
                throw new ArgumentException(
                    "Every Summary artifact must use the registry's global profile version.",
                    nameof(artifacts));
            }

            if (!_artifactByCandidateSetId.TryAdd(artifact.CandidateSet.CandidateSetId.Value, artifact))
            {
                throw new ArgumentException(
                    "Summary artifacts must be unique by candidate-set identity.",
                    nameof(artifacts));
            }
        }

        ProfileVersion = profileVersion;
        RegistryId = registryId;
        _artifacts = Array.AsReadOnly(snapshot);
        _canonicalBytes = Serialize();
    }

    public FrozenSummaryProfileVersion ProfileVersion { get; }
    public string RegistryId { get; }
    public IReadOnlyList<FrozenSummaryArtifact> Artifacts => _artifacts;

    public byte[] GetCanonicalBytes()
    {
        return _canonicalBytes.ToArray();
    }

    public FrozenSummaryArtifactLookupResult Lookup(DecisionMemoryCandidateSet candidateSet)
    {
        ArgumentNullException.ThrowIfNull(candidateSet);
        if (!_artifactByCandidateSetId.TryGetValue(
                candidateSet.CandidateSetId.Value,
                out FrozenSummaryArtifact? artifact))
        {
            return FrozenSummaryArtifactLookupResult.Closed(FrozenSummaryArtifactLookupKind.Missing);
        }

        if (!candidateSet.GetCanonicalBytes().AsSpan().SequenceEqual(
                artifact.CandidateSet.GetCanonicalBytes()))
        {
            return FrozenSummaryArtifactLookupResult.Closed(
                FrozenSummaryArtifactLookupKind.CandidateSetConflict);
        }

        return FrozenSummaryArtifactLookupResult.Found(artifact);
    }

    private static bool IsNullArtifact(FrozenSummaryArtifact artifact)
    {
        return artifact is null;
    }

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", ProtocolVersion);
            writer.WriteString("registry_id", RegistryId);
            writer.WriteString("profile_version", ProfileVersion.Value);
            writer.WritePropertyName("bindings");
            writer.WriteStartArray();
            foreach (FrozenSummaryArtifact artifact in Artifacts)
            {
                writer.WriteStartObject();
                writer.WriteString("candidate_set_id", artifact.CandidateSet.CandidateSetId.Value);
                writer.WriteString("artifact_id", artifact.ArtifactId.Value);
                writer.WriteString("artifact_version", artifact.ArtifactVersion.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private sealed class ArtifactComparer : IComparer<FrozenSummaryArtifact>
    {
        public static ArtifactComparer Instance { get; } = new();

        public int Compare(FrozenSummaryArtifact? left, FrozenSummaryArtifact? right)
        {
            return StringComparer.Ordinal.Compare(
                left!.CandidateSet.CandidateSetId.Value,
                right!.CandidateSet.CandidateSetId.Value);
        }
    }
}
