using System.Security.Cryptography;
using System.Text.Json;
using Alice.Memory;

namespace Alice.Cognition;

public enum FormalRq2Treatment
{
    Verbatim,
    Summary
}

public enum FormalRq2RunPurpose
{
    EngineeringEvidence,
    FormalCollection
}

public enum FormalRq2ResolutionState
{
    Tbd,
    Resolved
}

public enum FormalRq2EmptyHistoryPolicy
{
    RejectPair
}

public sealed record FormalRq2IdentitySetting
{
    private FormalRq2IdentitySetting(
        FormalRq2ResolutionState state,
        string? tbdReason,
        string? version)
    {
        State = state;
        TbdReason = tbdReason;
        Version = version;
    }

    public FormalRq2ResolutionState State { get; }
    public string? TbdReason { get; }
    public string? Version { get; }
    public bool IsResolved => State == FormalRq2ResolutionState.Resolved;

    public static FormalRq2IdentitySetting Tbd(string reason)
    {
        DependencyContractIdentity.Validate(reason, nameof(reason));
        return new FormalRq2IdentitySetting(
            FormalRq2ResolutionState.Tbd,
            reason,
            null);
    }

    public static FormalRq2IdentitySetting Resolved(string version)
    {
        DependencyContractIdentity.Validate(version, nameof(version));
        return new FormalRq2IdentitySetting(
            FormalRq2ResolutionState.Resolved,
            null,
            version);
    }

    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("state", State == FormalRq2ResolutionState.Tbd ? "tbd" : "resolved");
        if (State == FormalRq2ResolutionState.Tbd)
        {
            writer.WriteString("reason", TbdReason);
        }
        else
        {
            writer.WriteString("version", Version);
        }

        writer.WriteEndObject();
    }
}

public sealed record FormalRq2PositiveIntSetting
{
    private FormalRq2PositiveIntSetting(
        FormalRq2ResolutionState state,
        string? tbdReason,
        int? value,
        string? evidenceId)
    {
        State = state;
        TbdReason = tbdReason;
        Value = value;
        EvidenceId = evidenceId;
    }

    public FormalRq2ResolutionState State { get; }
    public string? TbdReason { get; }
    public int? Value { get; }
    public string? EvidenceId { get; }
    public bool IsResolved => State == FormalRq2ResolutionState.Resolved;

    public static FormalRq2PositiveIntSetting Tbd(string reason)
    {
        DependencyContractIdentity.Validate(reason, nameof(reason));
        return new FormalRq2PositiveIntSetting(
            FormalRq2ResolutionState.Tbd,
            reason,
            null,
            null);
    }

    public static FormalRq2PositiveIntSetting Resolved(int value, string evidenceId)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        DependencyContractIdentity.Validate(evidenceId, nameof(evidenceId));
        return new FormalRq2PositiveIntSetting(
            FormalRq2ResolutionState.Resolved,
            null,
            value,
            evidenceId);
    }

    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("state", State == FormalRq2ResolutionState.Tbd ? "tbd" : "resolved");
        if (State == FormalRq2ResolutionState.Tbd)
        {
            writer.WriteString("reason", TbdReason);
        }
        else
        {
            writer.WriteNumber("value", Value!.Value);
            writer.WriteString("evidence_id", EvidenceId);
        }

        writer.WriteEndObject();
    }
}

public sealed record FormalRq2ConfigurationSetting
{
    private FormalRq2ConfigurationSetting(
        FormalRq2ResolutionState state,
        string? tbdReason,
        string? evidenceId)
    {
        State = state;
        TbdReason = tbdReason;
        EvidenceId = evidenceId;
    }

    public FormalRq2ResolutionState State { get; }
    public string? TbdReason { get; }
    public string? EvidenceId { get; }
    public bool IsResolved => State == FormalRq2ResolutionState.Resolved;

    public static FormalRq2ConfigurationSetting Tbd(string reason)
    {
        DependencyContractIdentity.Validate(reason, nameof(reason));
        return new FormalRq2ConfigurationSetting(
            FormalRq2ResolutionState.Tbd,
            reason,
            null);
    }

    public static FormalRq2ConfigurationSetting Resolved(string evidenceId)
    {
        DependencyContractIdentity.Validate(evidenceId, nameof(evidenceId));
        return new FormalRq2ConfigurationSetting(
            FormalRq2ResolutionState.Resolved,
            null,
            evidenceId);
    }

    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("state", State == FormalRq2ResolutionState.Tbd ? "tbd" : "resolved");
        if (State == FormalRq2ResolutionState.Tbd)
        {
            writer.WriteString("reason", TbdReason);
        }
        else
        {
            writer.WriteString("evidence_id", EvidenceId);
        }

        writer.WriteEndObject();
    }
}

/// <summary>Shared matched-pair configuration with explicit unresolved-value state.</summary>
public sealed class FormalRq2SharedConfigurationManifest
{
    private const string ProtocolVersion = "formal-rq2-shared-configuration-v1";
    private readonly byte[] _canonicalBytes;

    public FormalRq2SharedConfigurationManifest(
        string preregistrationArtifactVersion,
        string runtimeVersion,
        string modelProfileId,
        string requestProtocolVersion,
        FormalRq2IdentitySetting candidateSelectorIdentity,
        FormalRq2ConfigurationSetting candidateScorerConfiguration,
        FormalRq2IdentitySetting rendererIdentity,
        FormalRq2IdentitySetting tokenizerIdentity,
        FormalRq2PositiveIntSetting contextTokenCeiling,
        FormalRq2PositiveIntSetting outputTokenCeiling,
        FormalRq2IdentitySetting offlineOutcomeScorerIdentity,
        FormalRq2ConfigurationSetting offlineOutcomeScorerConfiguration,
        FormalRq2EmptyHistoryPolicy emptyHistoryPolicy)
    {
        DependencyContractIdentity.Validate(
            preregistrationArtifactVersion,
            nameof(preregistrationArtifactVersion));
        DependencyContractIdentity.Validate(runtimeVersion, nameof(runtimeVersion));
        DependencyContractIdentity.Validate(modelProfileId, nameof(modelProfileId));
        DependencyContractIdentity.Validate(requestProtocolVersion, nameof(requestProtocolVersion));
        ArgumentNullException.ThrowIfNull(candidateSelectorIdentity);
        ArgumentNullException.ThrowIfNull(candidateScorerConfiguration);
        ArgumentNullException.ThrowIfNull(rendererIdentity);
        ArgumentNullException.ThrowIfNull(tokenizerIdentity);
        ArgumentNullException.ThrowIfNull(contextTokenCeiling);
        ArgumentNullException.ThrowIfNull(outputTokenCeiling);
        ArgumentNullException.ThrowIfNull(offlineOutcomeScorerIdentity);
        ArgumentNullException.ThrowIfNull(offlineOutcomeScorerConfiguration);
        if (!Enum.IsDefined(emptyHistoryPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(emptyHistoryPolicy));
        }

        PreregistrationArtifactVersion = preregistrationArtifactVersion;
        RuntimeVersion = runtimeVersion;
        ModelProfileId = modelProfileId;
        RequestProtocolVersion = requestProtocolVersion;
        CandidateSelectorIdentity = candidateSelectorIdentity;
        CandidateScorerConfiguration = candidateScorerConfiguration;
        RendererIdentity = rendererIdentity;
        TokenizerIdentity = tokenizerIdentity;
        ContextTokenCeiling = contextTokenCeiling;
        OutputTokenCeiling = outputTokenCeiling;
        OfflineOutcomeScorerIdentity = offlineOutcomeScorerIdentity;
        OfflineOutcomeScorerConfiguration = offlineOutcomeScorerConfiguration;
        EmptyHistoryPolicy = emptyHistoryPolicy;
        _canonicalBytes = Serialize();
        ConfigurationHash = Hash(_canonicalBytes);
    }

    public string PreregistrationArtifactVersion { get; }
    public string RuntimeVersion { get; }
    public string ModelProfileId { get; }
    public string RequestProtocolVersion { get; }
    public FormalRq2IdentitySetting CandidateSelectorIdentity { get; }
    public FormalRq2ConfigurationSetting CandidateScorerConfiguration { get; }
    public FormalRq2IdentitySetting RendererIdentity { get; }
    public FormalRq2IdentitySetting TokenizerIdentity { get; }
    public FormalRq2PositiveIntSetting ContextTokenCeiling { get; }
    public FormalRq2PositiveIntSetting OutputTokenCeiling { get; }
    public FormalRq2IdentitySetting OfflineOutcomeScorerIdentity { get; }
    public FormalRq2ConfigurationSetting OfflineOutcomeScorerConfiguration { get; }
    public FormalRq2EmptyHistoryPolicy EmptyHistoryPolicy { get; }
    public string ConfigurationHash { get; }

    public byte[] GetCanonicalBytes()
    {
        return _canonicalBytes.ToArray();
    }

    public IReadOnlyList<string> GetRuntimeRequiredTbdFields()
    {
        var result = new List<string>();
        AddIfTbd(result, "candidate_selector_identity", CandidateSelectorIdentity.IsResolved);
        AddIfTbd(result, "candidate_scorer_configuration", CandidateScorerConfiguration.IsResolved);
        AddIfTbd(result, "renderer_identity", RendererIdentity.IsResolved);
        AddIfTbd(result, "tokenizer_identity", TokenizerIdentity.IsResolved);
        AddIfTbd(result, "context_token_ceiling", ContextTokenCeiling.IsResolved);
        return result.AsReadOnly();
    }

    public IReadOnlyList<string> GetFormalRequiredTbdFields()
    {
        var result = new List<string>(GetRuntimeRequiredTbdFields());
        AddIfTbd(result, "output_token_ceiling", OutputTokenCeiling.IsResolved);
        AddIfTbd(result, "offline_outcome_scorer_identity", OfflineOutcomeScorerIdentity.IsResolved);
        AddIfTbd(
            result,
            "offline_outcome_scorer_configuration",
            OfflineOutcomeScorerConfiguration.IsResolved);
        return result.AsReadOnly();
    }

    private static void AddIfTbd(ICollection<string> fields, string name, bool isResolved)
    {
        if (!isResolved)
        {
            fields.Add(name);
        }
    }

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", ProtocolVersion);
            writer.WriteString("preregistration_artifact_version", PreregistrationArtifactVersion);
            writer.WriteString("runtime_version", RuntimeVersion);
            writer.WriteString("model_profile_id", ModelProfileId);
            writer.WriteString("request_protocol_version", RequestProtocolVersion);
            writer.WritePropertyName("candidate_selector_identity");
            CandidateSelectorIdentity.Write(writer);
            writer.WritePropertyName("candidate_scorer_configuration");
            CandidateScorerConfiguration.Write(writer);
            writer.WritePropertyName("renderer_identity");
            RendererIdentity.Write(writer);
            writer.WritePropertyName("tokenizer_identity");
            TokenizerIdentity.Write(writer);
            writer.WritePropertyName("context_token_ceiling");
            ContextTokenCeiling.Write(writer);
            writer.WritePropertyName("output_token_ceiling");
            OutputTokenCeiling.Write(writer);
            writer.WritePropertyName("offline_outcome_scorer_identity");
            OfflineOutcomeScorerIdentity.Write(writer);
            writer.WritePropertyName("offline_outcome_scorer_configuration");
            OfflineOutcomeScorerConfiguration.Write(writer);
            writer.WriteString("empty_history_policy", EmptyHistoryPolicyToken(EmptyHistoryPolicy));
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static string EmptyHistoryPolicyToken(FormalRq2EmptyHistoryPolicy policy)
    {
        return policy switch
        {
            FormalRq2EmptyHistoryPolicy.RejectPair => "reject_pair",
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };
    }

    private static string Hash(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}

public sealed class FormalRq2ConditionManifest
{
    private const string ProtocolVersion = "formal-rq2-condition-manifest-v1";
    private readonly byte[] _canonicalBytes;

    public FormalRq2ConditionManifest(
        string manifestId,
        FormalRq2Treatment treatment,
        FormalRq2SharedConfigurationManifest sharedConfiguration,
        string? summaryArtifactRegistryId,
        FrozenSummaryProfileVersion? summaryProfileVersion)
    {
        DependencyContractIdentity.Validate(manifestId, nameof(manifestId));
        if (!Enum.IsDefined(treatment))
        {
            throw new ArgumentOutOfRangeException(nameof(treatment));
        }

        ArgumentNullException.ThrowIfNull(sharedConfiguration);
        if (treatment == FormalRq2Treatment.Verbatim
            && (summaryArtifactRegistryId is not null || summaryProfileVersion is not null))
        {
            throw new ArgumentException(
                "The Verbatim condition cannot bind a Summary artifact registry.",
                nameof(summaryArtifactRegistryId));
        }

        if (treatment == FormalRq2Treatment.Summary
            && (summaryArtifactRegistryId is null || summaryProfileVersion is null))
        {
            throw new ArgumentException(
                "The Summary condition requires one artifact registry ID.",
                nameof(summaryArtifactRegistryId));
        }

        ManifestId = manifestId;
        Treatment = treatment;
        SharedConfiguration = sharedConfiguration;
        if (summaryArtifactRegistryId is not null)
            DependencyContractIdentity.Validate(summaryArtifactRegistryId, nameof(summaryArtifactRegistryId));
        SummaryArtifactRegistryId = summaryArtifactRegistryId;
        SummaryProfileVersion = summaryProfileVersion;
        _canonicalBytes = Serialize();
        ManifestHash = Convert.ToHexString(SHA256.HashData(_canonicalBytes)).ToLowerInvariant();
    }

    public string ManifestId { get; }
    public FormalRq2Treatment Treatment { get; }
    public FormalRq2SharedConfigurationManifest SharedConfiguration { get; }
    public string? SummaryArtifactRegistryId { get; }
    public FrozenSummaryProfileVersion? SummaryProfileVersion { get; }
    public string ManifestHash { get; }
    public string SharedConfigurationHash => SharedConfiguration.ConfigurationHash;

    public byte[] GetCanonicalBytes()
    {
        return _canonicalBytes.ToArray();
    }

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", ProtocolVersion);
            writer.WriteString("manifest_id", ManifestId);
            writer.WriteString("treatment", TreatmentToken(Treatment));
            writer.WriteString("shared_configuration_hash", SharedConfigurationHash);
            writer.WriteString("summary_artifact_registry_id", SummaryArtifactRegistryId);
            writer.WriteString("summary_profile_version", SummaryProfileVersion?.Value);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static string TreatmentToken(FormalRq2Treatment treatment)
    {
        return treatment switch
        {
            FormalRq2Treatment.Verbatim => "verbatim",
            FormalRq2Treatment.Summary => "summary",
            _ => throw new ArgumentOutOfRangeException(nameof(treatment))
        };
    }
}

public sealed class FormalRq2MatchedPairManifest
{
    public const string CurrentProtocolVersion = "formal-rq2-matched-pair-manifest-v1";
    private readonly byte[] _canonicalBytes;

    public FormalRq2MatchedPairManifest(
        FormalRq2ConditionManifest verbatim,
        FormalRq2ConditionManifest summary)
    {
        ArgumentNullException.ThrowIfNull(verbatim);
        ArgumentNullException.ThrowIfNull(summary);
        if (verbatim.Treatment != FormalRq2Treatment.Verbatim
            || summary.Treatment != FormalRq2Treatment.Summary)
        {
            throw new ArgumentException("A matched pair requires one Verbatim and one Summary manifest.");
        }

        if (!StringComparer.Ordinal.Equals(
                verbatim.SharedConfigurationHash,
                summary.SharedConfigurationHash))
        {
            throw new ArgumentException(
                "RQ2 matched conditions must have byte-identical shared configuration.");
        }

        Verbatim = verbatim;
        Summary = summary;
        _canonicalBytes = Serialize();
        PairManifestHash = Convert.ToHexString(SHA256.HashData(_canonicalBytes)).ToLowerInvariant();
    }

    public FormalRq2ConditionManifest Verbatim { get; }
    public FormalRq2ConditionManifest Summary { get; }
    public FormalRq2SharedConfigurationManifest SharedConfiguration => Verbatim.SharedConfiguration;
    public string PairManifestHash { get; }

    public byte[] GetCanonicalBytes()
    {
        return _canonicalBytes.ToArray();
    }

    public void ValidateRunPurpose(
        FormalRq2RunPurpose purpose,
        FormalCollectionAuthorization? authorization = null)
    {
        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        if (purpose == FormalRq2RunPurpose.FormalCollection)
        {
            if (authorization is null)
            {
                throw new InvalidOperationException(
                    "Formal RQ2 collection requires an external frozen authorization.");
            }

            IReadOnlyList<string> blockers = authorization.GetBlockers(
                FormalExperimentRq.Rq2,
                SharedConfiguration.PreregistrationArtifactVersion);
            if (blockers.Count != 0)
            {
                throw new InvalidOperationException(
                    $"Formal RQ2 collection is blocked: {string.Join(",", blockers)}.");
            }
        }
    }

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", CurrentProtocolVersion);
            writer.WriteString("shared_configuration_hash", Verbatim.SharedConfigurationHash);
            writer.WriteString("verbatim_manifest_hash", Verbatim.ManifestHash);
            writer.WriteString("summary_manifest_hash", Summary.ManifestHash);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
