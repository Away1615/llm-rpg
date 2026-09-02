using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alice.Cognition;

public sealed record FormalVerifiedArtifactBinding
{
    internal FormalVerifiedArtifactBinding(string artifactId)
    {
        FormalExperimentCanonical.RequireIdentity(artifactId, nameof(artifactId));
        ArtifactId = artifactId;
    }

    public string ArtifactId { get; }
}

/// <summary>
/// Non-constructible formal admission issued only after an external freeze bundle, actual artifact bytes,
/// and the clean Git checkout all match. Engineering runners cannot manufacture one from flags or hashes.
/// </summary>
public sealed class FormalExperimentCollectionPermit
{
    private readonly ReadOnlyCollection<string> _artifactIds;
    private readonly ReadOnlyDictionary<string, byte[]> _artifactBytes;
    private readonly byte[] _canonicalBytes;

    internal FormalExperimentCollectionPermit(
        FormalExperimentRq rq,
        string preregistrationArtifactVersion,
        string repositoryRevision,
        string pairManifestHash,
        string runtimeVersion,
        string modelProfileId,
        string freezeBundleHash,
        FormalExperimentSuiteManifest suiteManifest,
        FormalExperimentSuitePairEntry suiteEntry,
        IEnumerable<FormalVerifiedArtifactBinding> artifacts,
        IReadOnlyDictionary<string, byte[]> actualArtifacts)
    {
        Rq = rq;
        PreregistrationArtifactVersion = preregistrationArtifactVersion;
        RepositoryRevision = repositoryRevision;
        PairManifestHash = pairManifestHash;
        RuntimeVersion = runtimeVersion;
        ModelProfileId = modelProfileId;
        FreezeBundleHash = freezeBundleHash;
        ArgumentNullException.ThrowIfNull(suiteManifest);
        ArgumentNullException.ThrowIfNull(suiteEntry);
        if (suiteManifest.Rq != rq
            || !StringComparer.Ordinal.Equals(
                suiteManifest.PreregistrationArtifactVersion,
                preregistrationArtifactVersion)
            || !StringComparer.Ordinal.Equals(suiteEntry.PairManifestHash, pairManifestHash))
            throw new ArgumentException("Collection permit suite binding does not match the requested pair.");
        SuiteManifestHash = suiteManifest.ManifestHash;
        SuiteId = suiteManifest.SuiteId;
        SuitePairId = suiteEntry.PairId;
        CandidateSetId = suiteEntry.CandidateSetId;
        SummaryArtifactId = suiteEntry.SummaryArtifactId;
        SummaryArtifactVersion = suiteEntry.SummaryArtifactVersion;
        _conditionOrder = Array.AsReadOnly(suiteEntry.ConditionOrder.ToArray());
        string[] artifactIds = artifacts.Select(GetArtifactId).Distinct(StringComparer.Ordinal).ToArray();
        if (artifactIds.Length == 0)
            throw new ArgumentException("Collection permit requires named artifacts.", nameof(artifacts));
        _artifactIds = Array.AsReadOnly(artifactIds.Order(StringComparer.Ordinal).ToArray());
        var exactBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach ((string id, byte[] bytes) in actualArtifacts)
        {
            if (!_artifactIds.Contains(id, StringComparer.Ordinal)
                || bytes is null
                || bytes.Length == 0)
                throw new ArgumentException(
                    "Collection permit artifacts must match the named catalogue.",
                    nameof(actualArtifacts));
            exactBytes.Add(id, bytes.ToArray());
        }
        if (exactBytes.Count != _artifactIds.Count)
            throw new ArgumentException(
                "Collection permit requires exact bytes for every verified artifact.",
                nameof(actualArtifacts));
        _artifactBytes = new ReadOnlyDictionary<string, byte[]>(exactBytes);
        _canonicalBytes = Serialize();
        PermitHash = FormalExperimentCanonical.Hash(_canonicalBytes);
    }

    public FormalExperimentRq Rq { get; }
    public string PreregistrationArtifactVersion { get; }
    public string RepositoryRevision { get; }
    public string PairManifestHash { get; }
    public string RuntimeVersion { get; }
    public string ModelProfileId { get; }
    public string FreezeBundleHash { get; }
    public string SuiteManifestHash { get; }
    public string SuiteId { get; }
    public string SuitePairId { get; }
    public string? CandidateSetId { get; }
    public string? SummaryArtifactId { get; }
    public string? SummaryArtifactVersion { get; }
    public IReadOnlyList<string> ConditionOrder => _conditionOrder;
    public IReadOnlyList<string> ArtifactIds => _artifactIds;
    public string PermitHash { get; }

    public bool Matches(
        FormalExperimentRq rq,
        string preregistrationArtifactVersion,
        string pairManifestHash,
        string runtimeVersion,
        string modelProfileId)
    {
        return Rq == rq
            && StringComparer.Ordinal.Equals(PreregistrationArtifactVersion, preregistrationArtifactVersion)
            && StringComparer.Ordinal.Equals(PairManifestHash, pairManifestHash)
            && StringComparer.Ordinal.Equals(RuntimeVersion, runtimeVersion)
            && StringComparer.Ordinal.Equals(ModelProfileId, modelProfileId);
    }

    public bool MatchesAuthorization(FormalCollectionAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return authorization.State == FormalCollectionAuthorizationState.FrozenAuthorized
            && StringComparer.Ordinal.Equals(
                authorization.PreregistrationArtifactVersion,
                PreregistrationArtifactVersion)
            && StringComparer.Ordinal.Equals(authorization.FreezeRecordHash, FreezeBundleHash)
            && StringComparer.Ordinal.Equals(authorization.RepositoryRevision, RepositoryRevision)
            && authorization.AuthorizedRqs.Contains(Rq);
    }

    public bool MatchesConditionOrder(IEnumerable<string> conditionOrder)
    {
        ArgumentNullException.ThrowIfNull(conditionOrder);
        return ConditionOrder.SequenceEqual(conditionOrder, StringComparer.Ordinal);
    }

    public byte[] GetCanonicalBytes() => _canonicalBytes.ToArray();

    internal byte[] GetFrozenArtifactBundleCanonicalBytes()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-frozen-artifact-bundle.v1");
            writer.WritePropertyName("artifacts");
            writer.WriteStartArray();
            foreach (string id in ArtifactIds)
            {
                writer.WriteStartObject();
                writer.WriteString("artifact_id", id);
                writer.WriteBase64String("canonical_bytes", _artifactBytes[id]);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    internal bool MatchesArtifactBytes(string artifactId, ReadOnlySpan<byte> bytes)
    {
        return _artifactBytes.TryGetValue(artifactId, out byte[]? frozenBytes)
            && !bytes.IsEmpty
            && StringComparer.Ordinal.Equals(
                FormalExperimentCanonical.Hash(bytes),
                FormalExperimentCanonical.Hash(frozenBytes));
    }

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-collection-permit.v2");
            writer.WriteString("rq", Rq == FormalExperimentRq.Rq1 ? "rq1" : "rq2");
            writer.WriteString("preregistration_artifact_version", PreregistrationArtifactVersion);
            writer.WriteString("repository_revision", RepositoryRevision);
            writer.WriteString("pair_manifest_hash", PairManifestHash);
            writer.WriteString("runtime_version", RuntimeVersion);
            writer.WriteString("model_profile_id", ModelProfileId);
            writer.WriteString("freeze_bundle_hash", FreezeBundleHash);
            writer.WriteString("suite_manifest_hash", SuiteManifestHash);
            writer.WriteString("suite_id", SuiteId);
            writer.WriteString("suite_pair_id", SuitePairId);
            writer.WriteString("candidate_set_id", CandidateSetId);
            writer.WriteString("summary_artifact_id", SummaryArtifactId);
            writer.WriteString("summary_artifact_version", SummaryArtifactVersion);
            writer.WritePropertyName("condition_order");
            writer.WriteStartArray();
            foreach (string condition in ConditionOrder) writer.WriteStringValue(condition);
            writer.WriteEndArray();
            writer.WritePropertyName("artifacts");
            writer.WriteStartArray();
            foreach (string id in ArtifactIds)
            {
                writer.WriteStartObject();
                writer.WriteString("artifact_id", id);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static string GetArtifactId(FormalVerifiedArtifactBinding value) => value.ArtifactId;

    private readonly ReadOnlyCollection<string> _conditionOrder;
}

public sealed record FormalCollectionFreezeGateResult(
    FormalExperimentPreflightReport Report,
    FormalExperimentCollectionPermit? Permit)
{
    public FormalCollectionAuthorization? Authorization { get; init; }
}

/// <summary>Strict external freeze authority. The checked-in TBD bundle can only produce blockers.</summary>
public static class FormalCollectionFreezeGate
{
    public const string ProtocolVersion = "alice.formal-collection-freeze-bundle.v1";

    private static readonly string[] RequiredRq1Artifacts =
    [
        "preregistration",
        "repository_source_manifest",
        "model_profile",
        "request_protocol_catalogue",
        "rq1_pair_manifest",
        "rq1_public_fixture",
        "rq1_world_configuration",
        "rq1_opportunity_ledger",
        "rq1_test_case_ledger",
        "rq1_opportunity_test_case_map",
        "rq1_hidden_test_cases",
        "rq1_outcome_evaluator",
        "rq1_suite_manifest"
    ];

    private static readonly string[] RequiredRq2Artifacts =
    [
        "preregistration",
        "repository_source_manifest",
        "model_profile",
        "request_protocol_catalogue",
        "rq2_pair_manifest",
        "rq2_pre_treatment_emotion",
        "rq2_public_fixture_bundle",
        "rq2_required_source_sets",
        "rq2_summary_registry",
        "rq2_summary_fidelity_validator",
        "rq2_hidden_predicates",
        "rq2_outcome_evaluator",
        "rq2_suite_manifest"
    ];

    public static FormalCollectionFreezeGateResult Verify(
        ReadOnlySpan<byte> freezeBundleBytes,
        ReadOnlySpan<byte> readinessBytes,
        string workspaceRoot,
        FormalExperimentRq rq,
        string pairManifestHash,
        string runtimeVersion,
        string modelProfileId,
        IReadOnlyDictionary<string, byte[]> actualArtifacts)
    {
        if (!Enum.IsDefined(rq)) throw new ArgumentOutOfRangeException(nameof(rq));
        FormalExperimentCanonical.ValidateSha256(pairManifestHash, nameof(pairManifestHash));
        FormalExperimentCanonical.RequireIdentity(runtimeVersion, nameof(runtimeVersion));
        FormalExperimentCanonical.RequireIdentity(modelProfileId, nameof(modelProfileId));
        ArgumentNullException.ThrowIfNull(actualArtifacts);

        byte[] freezeBytes = freezeBundleBytes.ToArray();
        byte[] readiness = readinessBytes.ToArray();
        FreezeBundle bundle = ParseFreezeBundle(freezeBytes);
        var blockers = new List<string>();
        if (!StringComparer.Ordinal.Equals(bundle.State, "frozen_authorized"))
        {
            blockers.Add("collection_freeze_bundle_tbd");
            foreach (string unresolved in bundle.UnresolvedInputIds)
                blockers.Add($"unresolved:{unresolved}");
            return new FormalCollectionFreezeGateResult(
                new FormalExperimentPreflightReport(blockers),
                null);
        }

        if (!ReadinessAllowsFormalCollection(
                readiness,
                rq,
                bundle.PreregistrationArtifactVersion!))
            blockers.Add("formal_readiness_not_frozen");

        FreezeRqBinding? rqBinding = bundle.RqBindings.SingleOrDefault(value => value.Rq == rq);
        FormalExperimentSuiteManifest? suiteManifest = null;
        FormalExperimentSuitePairEntry? suiteEntry = null;
        if (rqBinding is null || !bundle.AuthorizedRqs.Contains(rq))
        {
            blockers.Add("collection_rq_not_authorized");
        }
        else
        {
            if (!StringComparer.Ordinal.Equals(rqBinding.RuntimeVersion, runtimeVersion))
                blockers.Add("runtime_version_freeze_mismatch");
            if (!StringComparer.Ordinal.Equals(rqBinding.ModelProfileId, modelProfileId))
                blockers.Add("model_profile_freeze_mismatch");
            ValidateArtifacts(rq, rqBinding, actualArtifacts, blockers);
            if (!blockers.Contains("required_artifact_catalogue_mismatch", StringComparer.Ordinal))
            {
                try
                {
                    string suiteArtifactId = FormalExperimentSuiteManifest.SuiteArtifactId(rq);
                    suiteManifest = FormalExperimentSuiteManifest.Load(actualArtifacts[suiteArtifactId]);
                    if (suiteManifest.Rq != rq
                        || !StringComparer.Ordinal.Equals(
                            suiteManifest.ManifestHash,
                            rqBinding.SuiteManifestHash)
                        || !StringComparer.Ordinal.Equals(
                            suiteManifest.PreregistrationArtifactVersion,
                            bundle.PreregistrationArtifactVersion))
                        throw new InvalidDataException("Formal suite identity does not match the freeze bundle.");
                    string[] pairArtifacts = rqBinding.Artifacts
                        .Where(value => !StringComparer.Ordinal.Equals(value.ArtifactId, suiteArtifactId))
                        .Select(GetFreezeArtifactId)
                        .ToArray();
                    suiteEntry = suiteManifest.RequirePermitEntry(pairManifestHash, pairArtifacts);
                    string pairManifestArtifactId = FormalExperimentSuiteManifest.PairManifestArtifactId(rq);
                    if (!StringComparer.Ordinal.Equals(
                            pairManifestHash,
                            FormalExperimentCanonical.Hash(actualArtifacts[pairManifestArtifactId]))
                        || !suiteEntry.MatchesArtifactHashes(actualArtifacts))
                        throw new InvalidDataException("Formal pair artifact Hashes do not match the frozen suite entry.");
                    if (rq == FormalExperimentRq.Rq2)
                    {
                        suiteManifest.ValidateRq2FrozenAssets(
                            actualArtifacts["rq2_public_fixture_bundle"],
                            actualArtifacts["rq2_summary_registry"]);
                    }
                    else
                    {
                        suiteManifest.ValidateRq1FrozenAssets(actualArtifacts["rq1_public_fixture"]);
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidDataException or JsonException)
                {
                    blockers.Add("formal_suite_binding_mismatch");
                }
            }
        }

        if (blockers.Count == 0)
        {
            GitWorkspaceIdentity identity = InspectGitWorkspace(workspaceRoot);
            if (!identity.IsClean)
                blockers.Add("repository_worktree_not_clean");
            if (!StringComparer.Ordinal.Equals(identity.Revision, bundle.RepositoryRevision))
                blockers.Add("repository_revision_mismatch");
        }

        var report = new FormalExperimentPreflightReport(blockers);
        if (!report.IsReady || rqBinding is null || suiteManifest is null || suiteEntry is null)
            return new FormalCollectionFreezeGateResult(report, null);

        var verified = rqBinding.Artifacts.Select(value => new FormalVerifiedArtifactBinding(value.ArtifactId));
        string freezeBundleHash = FormalExperimentCanonical.Hash(freezeBytes);
        var permit = new FormalExperimentCollectionPermit(
                rq,
                bundle.PreregistrationArtifactVersion!,
                bundle.RepositoryRevision!,
                pairManifestHash,
                runtimeVersion,
                modelProfileId,
                freezeBundleHash,
                suiteManifest,
                suiteEntry,
                verified,
                actualArtifacts);
        return new FormalCollectionFreezeGateResult(report, permit)
        {
            Authorization = FormalCollectionAuthorization.Frozen(
                bundle.PreregistrationArtifactVersion!,
                freezeBundleHash,
                bundle.RepositoryRevision!,
                bundle.AuthorizedRqs)
        };
    }

    private static void ValidateArtifacts(
        FormalExperimentRq rq,
        FreezeRqBinding binding,
        IReadOnlyDictionary<string, byte[]> actual,
        ICollection<string> blockers)
    {
        string[] required = rq == FormalExperimentRq.Rq1 ? RequiredRq1Artifacts : RequiredRq2Artifacts;
        string[] boundIds = binding.Artifacts.Select(GetFreezeArtifactId).Order(StringComparer.Ordinal).ToArray();
        if (!boundIds.SequenceEqual(required.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || !new HashSet<string>(actual.Keys, StringComparer.Ordinal).SetEquals(required))
        {
            blockers.Add("required_artifact_catalogue_mismatch");
            return;
        }

        foreach (FreezeArtifactBinding expected in binding.Artifacts)
            if (actual[expected.ArtifactId] is not { Length: > 0 })
                blockers.Add($"artifact_missing_or_empty:{expected.ArtifactId}");
    }

    private static bool ReadinessAllowsFormalCollection(
        byte[] readinessBytes,
        FormalExperimentRq rq,
        string expectedPreregistrationArtifactVersion)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(readinessBytes);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("formal_collection_allowed", out JsonElement allowed)
                || allowed.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("demo_values_may_resolve_formal_tbd", out JsonElement demoValues)
                || demoValues.ValueKind != JsonValueKind.False
                || !root.TryGetProperty("preregistration_status", out JsonElement preregistration)
                || preregistration.ValueKind != JsonValueKind.String
                || !preregistration.GetString()!.StartsWith("Frozen / Preregistered", StringComparison.Ordinal)
                || !root.TryGetProperty("preregistration_artifact_version", out JsonElement preregistrationVersion)
                || preregistrationVersion.ValueKind != JsonValueKind.String
                || !StringComparer.Ordinal.Equals(
                    preregistrationVersion.GetString(),
                    expectedPreregistrationArtifactVersion)
                || !ArrayIsEmpty(root.GetProperty("shared_unresolved_input_ids")))
                return false;
            JsonElement rqRoot = root.GetProperty(rq == FormalExperimentRq.Rq1 ? "rq1" : "rq2");
            return ArrayIsEmpty(rqRoot.GetProperty("unresolved_input_ids"));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }

    private static bool ArrayIsEmpty(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0;

    private static GitWorkspaceIdentity InspectGitWorkspace(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            throw new ArgumentException("Formal workspace root must exist.", nameof(workspaceRoot));
        string revision = RunGit(workspaceRoot, "rev-parse", "HEAD").Trim();
        string status = RunGit(workspaceRoot, "status", "--porcelain=v1", "--untracked-files=all");
        FormalExperimentCanonical.RequireIdentity(revision, nameof(workspaceRoot));
        return new GitWorkspaceIdentity(revision, status.Length == 0);
    }

    private static string RunGit(string workspaceRoot, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start Git for formal workspace verification.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git formal workspace verification failed: {error.Trim()}");
        return output;
    }

    private static FreezeBundle ParseFreezeBundle(byte[] bytes)
    {
        ValidateNoDuplicateProperties(bytes);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        FreezeBundle bundle = JsonSerializer.Deserialize<FreezeBundle>(bytes, options)
            ?? throw new JsonException("Formal freeze bundle root is required.");
        if (!StringComparer.Ordinal.Equals(bundle.SchemaVersion, ProtocolVersion)
            || bundle.State is not ("tbd" or "frozen_authorized")
            || bundle.UnresolvedInputIds.Any(string.IsNullOrWhiteSpace)
            || bundle.UnresolvedInputIds.Distinct(StringComparer.Ordinal).Count() != bundle.UnresolvedInputIds.Length)
            throw new JsonException("Formal freeze bundle identity or unresolved catalogue is invalid.");
        if (bundle.State == "tbd")
        {
            if (string.IsNullOrWhiteSpace(bundle.TbdReason)
                || bundle.PreregistrationArtifactVersion is not null
                || bundle.RepositoryRevision is not null
                || bundle.AuthorizedRqs.Length != 0
                || bundle.RqBindings.Length != 0
                || bundle.UnresolvedInputIds.Length == 0)
                throw new JsonException("TBD formal freeze bundle must remain unresolved and unauthorized.");
            return bundle;
        }

        if (bundle.TbdReason is not null || bundle.UnresolvedInputIds.Length != 0
            || bundle.PreregistrationArtifactVersion is null
            || bundle.RepositoryRevision is null
            || bundle.AuthorizedRqs.Length == 0
            || bundle.RqBindings.Length != bundle.AuthorizedRqs.Length)
            throw new JsonException("Frozen formal bundle is incomplete.");
        FormalExperimentCanonical.RequireIdentity(bundle.PreregistrationArtifactVersion, "preregistration_artifact_version");
        FormalExperimentCanonical.RequireIdentity(bundle.RepositoryRevision, "repository_revision");
        if (bundle.AuthorizedRqs.Distinct().Count() != bundle.AuthorizedRqs.Length
            || bundle.RqBindings.Select(GetFreezeRq).Distinct().Count() != bundle.RqBindings.Length)
            throw new JsonException("Frozen RQ bindings must be unique.");
        foreach (FreezeRqBinding binding in bundle.RqBindings) ValidateRqBinding(binding);
        return bundle;
    }

    private static void ValidateNoDuplicateProperties(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        var objectProperties = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                objectProperties.Push(new HashSet<string>(StringComparer.Ordinal));
            }
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (objectProperties.Count == 0
                    || !objectProperties.Peek().Add(reader.GetString()!))
                    throw new JsonException("Formal freeze bundle contains a duplicate property.");
            }
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                objectProperties.Pop();
            }
        }
    }

    private static void ValidateRqBinding(FreezeRqBinding binding)
    {
        FormalExperimentCanonical.ValidateSha256(binding.SuiteManifestHash, "suite_manifest_hash");
        FormalExperimentCanonical.RequireIdentity(binding.RuntimeVersion, "runtime_version");
        FormalExperimentCanonical.RequireIdentity(binding.ModelProfileId, "model_profile_id");
        if (binding.Artifacts.Length == 0
            || binding.Artifacts.Select(GetFreezeArtifactId).Distinct(StringComparer.Ordinal).Count() != binding.Artifacts.Length)
            throw new JsonException("Frozen artifact bindings must be non-empty and unique.");
        foreach (FreezeArtifactBinding artifact in binding.Artifacts)
        {
            FormalExperimentCanonical.RequireIdentity(artifact.ArtifactId, "artifact_id");
        }
    }

    private static string GetFreezeArtifactId(FreezeArtifactBinding value) => value.ArtifactId;
    private static FormalExperimentRq GetFreezeRq(FreezeRqBinding value) => value.Rq;

    private sealed record GitWorkspaceIdentity(string Revision, bool IsClean);

    private sealed record FreezeBundle
    {
        [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyName("schema_version")]
        public string SchemaVersion { get; init; } = string.Empty;
        [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyName("state")]
        public string State { get; init; } = string.Empty;
        [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyName("tbd_reason")]
        public string? TbdReason { get; init; }
        [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyName("preregistration_artifact_version")]
        public string? PreregistrationArtifactVersion { get; init; }
        [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyName("repository_revision")]
        public string? RepositoryRevision { get; init; }
        [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyName("authorized_rqs")]
        public FormalExperimentRq[] AuthorizedRqs { get; init; } = [];
        [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyName("unresolved_input_ids")]
        public string[] UnresolvedInputIds { get; init; } = [];
        [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyName("rq_bindings")]
        public FreezeRqBinding[] RqBindings { get; init; } = [];
    }

    private sealed record FreezeRqBinding
    {
        [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyName("rq")]
        public FormalExperimentRq Rq { get; init; }
        [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyName("suite_manifest_hash")]
        public string SuiteManifestHash { get; init; } = string.Empty;
        [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyName("runtime_version")]
        public string RuntimeVersion { get; init; } = string.Empty;
        [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyName("model_profile_id")]
        public string ModelProfileId { get; init; } = string.Empty;
        [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyName("artifacts")]
        public FreezeArtifactBinding[] Artifacts { get; init; } = [];
    }

    private sealed record FreezeArtifactBinding
    {
        [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyName("artifact_id")]
        public string ArtifactId { get; init; } = string.Empty;
    }
}
