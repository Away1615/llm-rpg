using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Alice.Activities;
using Alice.Actors;
using Alice.Memory;

namespace Alice.Cognition;

public enum FormalExperimentRq
{
    Rq1,
    Rq2
}

public enum FormalCollectionAuthorizationState
{
    Tbd,
    FrozenAuthorized
}

/// <summary>External switch from Draft evidence to an authorized collection state.</summary>
public sealed class FormalCollectionAuthorization
{
    private readonly ReadOnlyCollection<FormalExperimentRq> _authorizedRqs;
    private readonly byte[] _canonicalBytes;

    private FormalCollectionAuthorization(
        FormalCollectionAuthorizationState state,
        string? tbdReason,
        string? preregistrationArtifactVersion,
        string? freezeRecordHash,
        string? repositoryRevision,
        IEnumerable<FormalExperimentRq> authorizedRqs)
    {
        State = state;
        TbdReason = tbdReason;
        PreregistrationArtifactVersion = preregistrationArtifactVersion;
        FreezeRecordHash = freezeRecordHash;
        RepositoryRevision = repositoryRevision;
        FormalExperimentRq[] snapshot = authorizedRqs.Distinct().Order().ToArray();
        _authorizedRqs = Array.AsReadOnly(snapshot);
        _canonicalBytes = Serialize();
        AuthorizationHash = FormalExperimentCanonical.Hash(_canonicalBytes);
    }

    public FormalCollectionAuthorizationState State { get; }
    public string? TbdReason { get; }
    public string? PreregistrationArtifactVersion { get; }
    public string? FreezeRecordHash { get; }
    public string? RepositoryRevision { get; }
    public IReadOnlyList<FormalExperimentRq> AuthorizedRqs => _authorizedRqs;
    public string AuthorizationHash { get; }

    public static FormalCollectionAuthorization Tbd(string reason)
    {
        FormalExperimentCanonical.RequireIdentity(reason, nameof(reason));
        return new FormalCollectionAuthorization(
            FormalCollectionAuthorizationState.Tbd,
            reason,
            null,
            null,
            null,
            []);
    }

    internal static FormalCollectionAuthorization Frozen(
        string preregistrationArtifactVersion,
        string freezeRecordHash,
        string repositoryRevision,
        IEnumerable<FormalExperimentRq> authorizedRqs)
    {
        FormalExperimentCanonical.RequireIdentity(
            preregistrationArtifactVersion,
            nameof(preregistrationArtifactVersion));
        FormalExperimentCanonical.ValidateSha256(freezeRecordHash, nameof(freezeRecordHash));
        FormalExperimentCanonical.RequireIdentity(repositoryRevision, nameof(repositoryRevision));
        ArgumentNullException.ThrowIfNull(authorizedRqs);
        FormalExperimentRq[] supplied = authorizedRqs.ToArray();
        FormalExperimentRq[] snapshot = supplied.Distinct().Order().ToArray();
        if (snapshot.Length == 0
            || snapshot.Length != supplied.Length
            || snapshot.Any(IsUndefinedRq))
        {
            throw new ArgumentException(
                "Authorized RQs must be non-empty, defined, and unique.",
                nameof(authorizedRqs));
        }

        return new FormalCollectionAuthorization(
            FormalCollectionAuthorizationState.FrozenAuthorized,
            null,
            preregistrationArtifactVersion,
            freezeRecordHash,
            repositoryRevision,
            snapshot);
    }

    public static FormalCollectionAuthorization Load(ReadOnlySpan<byte> utf8)
    {
        using JsonDocument document = JsonDocument.Parse(utf8.ToArray());
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Formal collection authorization root must be an object.");
        }

        Dictionary<string, JsonElement> properties = SnapshotAuthorizationProperties(root);
        RequireString(properties, "schema_version", "alice.formal-collection-authorization.v1");
        string state = RequiredString(properties, "state");
        JsonElement authorizedRqs = properties["authorized_rqs"];
        if (authorizedRqs.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("authorized_rqs must be an array.");
        }

        if (StringComparer.Ordinal.Equals(state, "tbd"))
        {
            RequireNull(properties, "preregistration_artifact_version");
            RequireNull(properties, "freeze_record_hash");
            RequireNull(properties, "repository_revision");
            if (authorizedRqs.GetArrayLength() != 0)
            {
                throw new JsonException("TBD authorization cannot authorize an RQ.");
            }

            return Tbd(RequiredString(properties, "tbd_reason"));
        }

        if (!StringComparer.Ordinal.Equals(state, "frozen_authorized"))
        {
            throw new JsonException("Unknown formal collection authorization state.");
        }

        RequireNull(properties, "tbd_reason");
        var rqs = new List<FormalExperimentRq>();
        foreach (JsonElement rq in authorizedRqs.EnumerateArray())
        {
            if (rq.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("Authorized RQ identity must be a string.");
            }

            rqs.Add(rq.GetString() switch
            {
                "rq1" => FormalExperimentRq.Rq1,
                "rq2" => FormalExperimentRq.Rq2,
                _ => throw new JsonException("Unknown authorized RQ identity.")
            });
        }

        return Frozen(
            RequiredString(properties, "preregistration_artifact_version"),
            RequiredString(properties, "freeze_record_hash"),
            RequiredString(properties, "repository_revision"),
            rqs);
    }

    public static FormalCollectionAuthorization LoadFile(string path)
    {
        return Load(File.ReadAllBytes(path));
    }

    public IReadOnlyList<string> GetBlockers(
        FormalExperimentRq rq,
        string preregistrationArtifactVersion)
    {
        if (!Enum.IsDefined(rq))
        {
            throw new ArgumentOutOfRangeException(nameof(rq));
        }

        FormalExperimentCanonical.RequireIdentity(
            preregistrationArtifactVersion,
            nameof(preregistrationArtifactVersion));
        var blockers = new List<string>();
        if (State != FormalCollectionAuthorizationState.FrozenAuthorized)
        {
            blockers.Add("collection_authorization_tbd");
        }
        else
        {
            if (!StringComparer.Ordinal.Equals(
                    PreregistrationArtifactVersion,
                    preregistrationArtifactVersion))
            {
                blockers.Add("collection_authorization_preregistration_mismatch");
            }

            if (!AuthorizedRqs.Contains(rq))
            {
                blockers.Add("collection_authorization_rq_not_authorized");
            }
        }

        return blockers.AsReadOnly();
    }

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
            writer.WriteString("schema_version", "alice.formal-collection-authorization.v1");
            writer.WriteString(
                "state",
                State == FormalCollectionAuthorizationState.Tbd ? "tbd" : "frozen_authorized");
            writer.WriteString("tbd_reason", TbdReason);
            writer.WriteString("preregistration_artifact_version", PreregistrationArtifactVersion);
            writer.WriteString("freeze_record_hash", FreezeRecordHash);
            writer.WriteString("repository_revision", RepositoryRevision);
            writer.WritePropertyName("authorized_rqs");
            writer.WriteStartArray();
            foreach (FormalExperimentRq rq in AuthorizedRqs)
            {
                writer.WriteStringValue(rq == FormalExperimentRq.Rq1 ? "rq1" : "rq2");
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static bool IsUndefinedRq(FormalExperimentRq rq)
    {
        return !Enum.IsDefined(rq);
    }

    private static Dictionary<string, JsonElement> SnapshotAuthorizationProperties(JsonElement root)
    {
        string[] expected =
        [
            "schema_version",
            "state",
            "tbd_reason",
            "preregistration_artifact_version",
            "freeze_record_hash",
            "repository_revision",
            "authorized_rqs"
        ];
        var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!expectedSet.Contains(property.Name) || !result.TryAdd(property.Name, property.Value))
            {
                throw new JsonException("Formal authorization contains an unknown or duplicate property.");
            }
        }

        if (!expectedSet.SetEquals(result.Keys))
        {
            throw new JsonException("Formal authorization is missing a required property.");
        }

        return result;
    }

    private static string RequiredString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        JsonElement value = properties[name];
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{name} must be a string.");
        }

        return value.GetString()!;
    }

    private static void RequireString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        string expected)
    {
        if (!StringComparer.Ordinal.Equals(RequiredString(properties, name), expected))
        {
            throw new JsonException($"Unexpected {name}.");
        }
    }

    private static void RequireNull(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        if (properties[name].ValueKind != JsonValueKind.Null)
        {
            throw new JsonException($"{name} must be null.");
        }
    }
}

public sealed record FormalEvidenceArtifactBinding
{
    public FormalEvidenceArtifactBinding(string artifactId)
    {
        FormalExperimentCanonical.RequireIdentity(artifactId, nameof(artifactId));
        ArtifactId = artifactId;
    }

    public string ArtifactId { get; }
}

public sealed class FormalExperimentPreflightReport
{
    private readonly ReadOnlyCollection<string> _blockers;

    internal FormalExperimentPreflightReport(IEnumerable<string> blockers)
    {
        string[] snapshot = blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        _blockers = Array.AsReadOnly(snapshot);
    }

    public bool IsReady => _blockers.Count == 0;
    public IReadOnlyList<string> Blockers => _blockers;

    public byte[] GetCanonicalBytes()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-experiment-preflight.v1");
            writer.WriteBoolean("ready", IsReady);
            writer.WritePropertyName("blockers");
            writer.WriteStartArray();
            foreach (string blocker in Blockers)
            {
                writer.WriteStringValue(blocker);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}

/// <summary>One pre-Provider gate shared by both matched runners.</summary>
public static class FormalExperimentPreflight
{
    public static FormalExperimentPreflightReport Evaluate(
        FormalExperimentRq rq,
        bool formalCollection,
        string preregistrationArtifactVersion,
        string pairManifestHash,
        string runtimeVersion,
        string modelProfileId,
        FormalCollectionAuthorization authorization,
        IEnumerable<string> unresolvedInputIds,
        IEnumerable<FormalEvidenceArtifactBinding> requiredArtifacts,
        FormalExperimentCollectionPermit? collectionPermit)
    {
        if (!Enum.IsDefined(rq)) throw new ArgumentOutOfRangeException(nameof(rq));
        FormalExperimentCanonical.RequireIdentity(
            preregistrationArtifactVersion,
            nameof(preregistrationArtifactVersion));
        FormalExperimentCanonical.ValidateSha256(pairManifestHash, nameof(pairManifestHash));
        FormalExperimentCanonical.RequireIdentity(runtimeVersion, nameof(runtimeVersion));
        FormalExperimentCanonical.RequireIdentity(modelProfileId, nameof(modelProfileId));
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(unresolvedInputIds);
        ArgumentNullException.ThrowIfNull(requiredArtifacts);
        var blockers = new List<string>();
        if (formalCollection)
        {
            blockers.AddRange(authorization.GetBlockers(rq, preregistrationArtifactVersion));
            if (collectionPermit is null)
            {
                blockers.Add("formal_collection_permit_missing");
            }
            else if (!collectionPermit.Matches(
                rq,
                preregistrationArtifactVersion,
                pairManifestHash,
                runtimeVersion,
                modelProfileId))
            {
                blockers.Add("formal_collection_permit_mismatch");
            }
            else if (!collectionPermit.MatchesAuthorization(authorization))
            {
                blockers.Add("formal_collection_authorization_permit_mismatch");
            }
        }

        var unresolvedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? inputId in unresolvedInputIds)
        {
            FormalExperimentCanonical.RequireIdentity(inputId!, nameof(unresolvedInputIds));
            if (!unresolvedIds.Add(inputId!))
            {
                throw new ArgumentException(
                    "Unresolved formal input identities must be unique.",
                    nameof(unresolvedInputIds));
            }

            blockers.Add($"unresolved:{inputId}");
        }

        var artifactIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (FormalEvidenceArtifactBinding? artifact in requiredArtifacts)
        {
            if (artifact is null || !artifactIds.Add(artifact.ArtifactId))
            {
                throw new ArgumentException(
                    "Required artifacts must be non-null and unique by identity.",
                    nameof(requiredArtifacts));
            }

        }

        return new FormalExperimentPreflightReport(blockers);
    }
}

public sealed class FormalExperimentEvidenceSeal
{
    private readonly byte[] _canonicalJsonLines;

    internal FormalExperimentEvidenceSeal(int recordCount, byte[] canonicalJsonLines)
    {
        RecordCount = recordCount;
        _canonicalJsonLines = canonicalJsonLines.ToArray();
        ArtifactHash = FormalExperimentCanonical.Hash(_canonicalJsonLines);
    }

    public int RecordCount { get; }
    public string ArtifactHash { get; }

    public byte[] GetCanonicalJsonLines()
    {
        return _canonicalJsonLines.ToArray();
    }

    public static FormalExperimentEvidenceSeal Load(ReadOnlySpan<byte> canonicalJsonLines)
    {
        byte[] bytes = canonicalJsonLines.ToArray();
        int recordCount = bytes.Count(value => value == (byte)'\n');
        if (recordCount == 0 || bytes[^1] != (byte)'\n')
            throw new InvalidDataException("Formal evidence must be non-empty canonical JSONL ending in a newline.");
        var seal = new FormalExperimentEvidenceSeal(recordCount, bytes);
        _ = FormalExperimentEvidenceReplayVerifier.Verify(seal);
        return seal;
    }
}

public sealed record FormalExperimentEvidenceReplayRecord(
    int Sequence,
    string RecordKind,
    string PayloadHash,
    byte[] Payload);

public sealed class FormalExperimentEvidenceReplayReport
{
    internal FormalExperimentEvidenceReplayReport(
        IEnumerable<FormalExperimentEvidenceReplayRecord> records)
    {
        Records = Array.AsReadOnly(records.ToArray());
    }

    public IReadOnlyList<FormalExperimentEvidenceReplayRecord> Records { get; }

    public FormalExperimentEvidenceReplayRecord Require(string recordKind)
    {
        FormalExperimentCanonical.RequireIdentity(recordKind, nameof(recordKind));
        return Records.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.RecordKind, recordKind))
            ?? throw new InvalidDataException($"Formal evidence record is missing: {recordKind}.");
    }
}

/// <summary>Strict structural verifier for one sealed, canonical JSONL evidence package.</summary>
public static class FormalExperimentEvidenceReplayVerifier
{
    private static readonly HashSet<string> AllowedRecordKinds = new(StringComparer.Ordinal)
    {
        "collection_authorization",
        "collection_permit",
        "frozen_artifact_bundle",
        "preflight_inputs",
        "preflight",
        "rq1_pair_manifest",
        "rq1_agent_centric_manifest",
        "rq1_event_centric_manifest",
        "rq1_public_fixture",
        "rq1_opportunity_ledger",
        "rq1_test_case_ledger",
        "rq1_opportunity_test_case_map",
        "rq1_hidden_test_cases",
        "rq1_agent_centric_result",
        "rq1_event_centric_result",
        "rq2_pair_manifest",
        "rq2_shared_configuration",
        "rq2_verbatim_manifest",
        "rq2_summary_manifest",
        "rq2_candidate_set",
        "rq2_candidate_scoring",
        "rq2_pre_treatment_emotion",
        "rq2_verbatim_packet",
        "rq2_verbatim_packing_trace",
        "rq2_summary_packet",
        "rq2_verbatim_context",
        "rq2_summary_context",
        "rq2_required_sources",
        "rq2_required_source_gate",
        "rq2_hidden_predicate",
        "rq2_summary_fidelity",
        "rq2_verbatim_result",
        "rq2_summary_result",
        "pair_evidence_invalid",
        "matched_score"
    };

    public static FormalExperimentEvidenceReplayReport Verify(FormalExperimentEvidenceSeal seal)
    {
        ArgumentNullException.ThrowIfNull(seal);
        return VerifyArtifact(
            seal.GetCanonicalJsonLines(),
            seal.RecordCount,
            seal.ArtifactHash);
    }

    public static FormalExperimentEvidenceReplayReport VerifyArtifact(
        ReadOnlySpan<byte> canonicalJsonLines,
        int expectedRecordCount,
        string expectedArtifactHash)
    {
        if (expectedRecordCount <= 0) throw new ArgumentOutOfRangeException(nameof(expectedRecordCount));
        FormalExperimentCanonical.ValidateSha256(expectedArtifactHash, nameof(expectedArtifactHash));
        byte[] jsonLines = canonicalJsonLines.ToArray();
        if (!StringComparer.Ordinal.Equals(FormalExperimentCanonical.Hash(jsonLines), expectedArtifactHash))
            throw new InvalidDataException("Formal evidence seal hash does not match its bytes.");
        if (jsonLines.Length == 0 || jsonLines[^1] != (byte)'\n' || jsonLines.Contains((byte)'\r'))
            throw new InvalidDataException("Formal evidence must be non-empty canonical LF-delimited JSONL.");

        var records = new List<FormalExperimentEvidenceReplayRecord>();
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        int start = 0;
        while (start < jsonLines.Length)
        {
            int relativeEnd = jsonLines.AsSpan(start).IndexOf((byte)'\n');
            if (relativeEnd < 0) throw new InvalidDataException("Formal evidence record is not LF terminated.");
            ReadOnlySpan<byte> line = jsonLines.AsSpan(start, relativeEnd);
            if (line.IsEmpty) throw new InvalidDataException("Formal evidence contains an empty record.");
            records.Add(ParseRecord(line, records.Count + 1, kinds));
            start += relativeEnd + 1;
        }

        if (records.Count != expectedRecordCount)
            throw new InvalidDataException("Formal evidence record count does not match its seal.");
        ValidatePackage(records);
        return new FormalExperimentEvidenceReplayReport(records);
    }

    private static FormalExperimentEvidenceReplayRecord ParseRecord(
        ReadOnlySpan<byte> line,
        int expectedSequence,
        ISet<string> kinds)
    {
        using JsonDocument document = JsonDocument.Parse(line.ToArray());
        JsonElement root = document.RootElement;
        string[] names = root.ValueKind == JsonValueKind.Object
            ? root.EnumerateObject().Select(value => value.Name).ToArray()
            : [];
        string[] expected = ["schema_version", "sequence", "record_kind", "payload_hash", "payload"];
        if (!names.SequenceEqual(expected, StringComparer.Ordinal)
            || root.GetProperty("schema_version").GetString() != "alice.formal-experiment-record.v1"
            || root.GetProperty("sequence").GetInt32() != expectedSequence)
            throw new InvalidDataException("Formal evidence record shape or sequence is invalid.");
        string recordKind = root.GetProperty("record_kind").GetString()
            ?? throw new InvalidDataException("Formal evidence record kind is missing.");
        if (!AllowedRecordKinds.Contains(recordKind) || !kinds.Add(recordKind))
            throw new InvalidDataException("Formal evidence record kind is unknown or duplicated.");
        string payloadHash = root.GetProperty("payload_hash").GetString()
            ?? throw new InvalidDataException("Formal evidence payload hash is missing.");
        byte[] payload = root.GetProperty("payload").GetBytesFromBase64();
        if (payload.Length == 0
            || !StringComparer.Ordinal.Equals(FormalExperimentCanonical.Hash(payload), payloadHash))
            throw new InvalidDataException("Formal evidence payload hash does not match its bytes.");
        ValidatePayloadIdentity(payload);
        return new FormalExperimentEvidenceReplayRecord(
            expectedSequence,
            recordKind,
            payloadHash,
            payload);
    }

    private static void ValidatePayloadIdentity(byte[] payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Formal evidence payload root must be an object.");
        string[] names = root.EnumerateObject().Select(value => value.Name).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length
            || (!root.TryGetProperty("schema_version", out JsonElement schema)
                && !root.TryGetProperty("protocol_version", out schema))
            || schema.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(schema.GetString()))
            throw new InvalidDataException("Formal evidence payload identity is missing or ambiguous.");
    }

    private static void ValidatePackage(IReadOnlyList<FormalExperimentEvidenceReplayRecord> records)
    {
        HashSet<string> kinds = records.Select(value => value.RecordKind).ToHashSet(StringComparer.Ordinal);
        if (!kinds.Contains("collection_authorization")
            || !kinds.Contains("preflight_inputs")
            || !kinds.Contains("preflight"))
            throw new InvalidDataException("Formal evidence package lacks its preflight chain.");
        bool preflightReady = ReadPreflightReady(records.Single(value => value.RecordKind == "preflight").Payload);
        if (!preflightReady)
        {
            if (kinds.Contains("matched_score"))
                throw new InvalidDataException("A blocked preflight package cannot contain a matched score.");
            return;
        }

        bool rq1 = kinds.Contains("rq1_pair_manifest");
        bool rq2 = kinds.Contains("rq2_pair_manifest");
        if (rq1 == rq2) throw new InvalidDataException("Ready evidence must identify exactly one RQ package.");
        bool pairEvidenceInvalid = kinds.Contains("pair_evidence_invalid");
        bool formalCollection = ReadFormalCollectionPurpose(
            records.Single(value => value.RecordKind == "preflight_inputs").Payload);
        bool requireFormalPairing = formalCollection && !pairEvidenceInvalid;
        if (formalCollection)
            ValidateFormalAdmissionChain(records, rq1 ? FormalExperimentRq.Rq1 : FormalExperimentRq.Rq2);
        string[] publicExecutionRequired = rq1
            ? [
                "rq1_public_fixture", "rq1_agent_centric_result", "rq1_event_centric_result"]
            : [
                "rq2_candidate_set", "rq2_verbatim_packet", "rq2_verbatim_packing_trace", "rq2_summary_packet",
                "rq2_pre_treatment_emotion",
                "rq2_verbatim_context", "rq2_summary_context", "rq2_summary_fidelity",
                "rq2_verbatim_result", "rq2_summary_result"];
        if (publicExecutionRequired.Any(value => !kinds.Contains(value)))
            throw new InvalidDataException("Ready formal evidence lacks its public execution inputs or outputs.");
        if (rq2 && formalCollection && !kinds.Contains("rq2_candidate_scoring"))
            throw new InvalidDataException("Ready formal RQ2 evidence lacks candidate-score diagnostics.");

        string[] completedManifestRecords = rq1
            ? ["rq1_agent_centric_manifest", "rq1_event_centric_manifest"]
            : ["rq2_shared_configuration", "rq2_verbatim_manifest", "rq2_summary_manifest"];
        if (!pairEvidenceInvalid && completedManifestRecords.Any(value => !kinds.Contains(value)))
            throw new InvalidDataException("Completed formal evidence lacks its exact condition manifests.");

        if (rq1)
        {
            if (!pairEvidenceInvalid) ValidateRq1ManifestChain(records);
            ValidateRq1ConditionResult(
                records.Single(value => value.RecordKind == "rq1_agent_centric_result").Payload,
                pairEvidenceInvalid ? null : records.Single(value => value.RecordKind == "rq1_agent_centric_manifest").Payload,
                formalCollection,
                requireFormalPairing);
            ValidateRq1ConditionResult(
                records.Single(value => value.RecordKind == "rq1_event_centric_result").Payload,
                pairEvidenceInvalid ? null : records.Single(value => value.RecordKind == "rq1_event_centric_manifest").Payload,
                formalCollection,
                requireFormalPairing);
        }
        else
        {
            if (!pairEvidenceInvalid) ValidateRq2ManifestChain(records);
            ValidateRq2PackingTrace(records);
            if (formalCollection) ValidateRq2CandidateScoring(records);
            ValidateRq2PreTreatmentEmotion(records);
            ValidateRq2ConditionResult(
                records.Single(value => value.RecordKind == "rq2_verbatim_result").Payload,
                pairEvidenceInvalid ? null : records.Single(value => value.RecordKind == "rq2_shared_configuration").Payload,
                records.Single(value => value.RecordKind == "rq2_verbatim_context").Payload,
                records.Single(value => value.RecordKind == "rq2_candidate_set").Payload,
                formalCollection,
                requireFormalPairing);
            ValidateRq2ConditionResult(
                records.Single(value => value.RecordKind == "rq2_summary_result").Payload,
                pairEvidenceInvalid ? null : records.Single(value => value.RecordKind == "rq2_shared_configuration").Payload,
                records.Single(value => value.RecordKind == "rq2_summary_context").Payload,
                records.Single(value => value.RecordKind == "rq2_candidate_set").Payload,
                formalCollection,
                requireFormalPairing);
        }

        if (pairEvidenceInvalid)
        {
            if (kinds.Contains("matched_score"))
                throw new InvalidDataException("Invalid pair evidence cannot also contain a matched score.");
            return;
        }

        string[] hiddenScoringRequired = rq1
            ? [
                "rq1_opportunity_ledger", "rq1_test_case_ledger",
                "rq1_opportunity_test_case_map", "rq1_hidden_test_cases"]
            : ["rq2_required_sources", "rq2_required_source_gate", "rq2_hidden_predicate"];
        if (hiddenScoringRequired.Any(value => !kinds.Contains(value)))
            throw new InvalidDataException("Ready formal evidence lacks its offline scoring inputs.");
        if (!kinds.Contains("matched_score") && !kinds.Contains("pair_evidence_invalid"))
            throw new InvalidDataException("Ready formal evidence lacks a terminal pair outcome.");
        if (kinds.Contains("matched_score"))
            ValidateMatchedScore(records, rq1 ? FormalExperimentRq.Rq1 : FormalExperimentRq.Rq2);
    }

    private static bool ReadPreflightReady(byte[] payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("ready").GetBoolean();
    }

    private static bool ReadFormalCollectionPurpose(byte[] payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        string purpose = document.RootElement.GetProperty("run_purpose").GetString()
            ?? throw new InvalidDataException("Formal preflight run purpose is missing.");
        return purpose switch
        {
            "formal_collection" => true,
            "engineering_evidence" => false,
            _ => throw new InvalidDataException("Formal preflight run purpose is unknown.")
        };
    }

    private static void ValidateFormalAdmissionChain(
        IReadOnlyList<FormalExperimentEvidenceReplayRecord> records,
        FormalExperimentRq rq)
    {
        FormalExperimentEvidenceReplayRecord permitRecord = records.SingleOrDefault(
            value => value.RecordKind == "collection_permit")
            ?? throw new InvalidDataException("Ready formal evidence lacks its collection permit.");
        FormalCollectionAuthorization authorization = FormalCollectionAuthorization.Load(
            records.Single(value => value.RecordKind == "collection_authorization").Payload);
        using JsonDocument permitDocument = JsonDocument.Parse(permitRecord.Payload);
        using JsonDocument inputsDocument = JsonDocument.Parse(
            records.Single(value => value.RecordKind == "preflight_inputs").Payload);
        JsonElement permit = permitDocument.RootElement;
        JsonElement inputs = inputsDocument.RootElement;
        RequireExactProperties(
            permit,
            [
                "schema_version",
                "rq",
                "preregistration_artifact_version",
                "repository_revision",
                "pair_manifest_hash",
                "runtime_version",
                "model_profile_id",
                "freeze_bundle_hash",
                "suite_manifest_hash",
                "suite_id",
                "suite_pair_id",
                "candidate_set_id",
                "summary_artifact_id",
                "summary_artifact_version",
                "condition_order",
                "artifacts"
            ]);
        if (!StringComparer.Ordinal.Equals(
                RequiredString(permit, "schema_version"),
                "alice.formal-collection-permit.v2")
            || !StringComparer.Ordinal.Equals(
                RequiredString(inputs, "schema_version"),
                "alice.formal-preflight-inputs.v1"))
            throw new InvalidDataException("Formal admission evidence has an unknown schema.");
        string rqToken = rq == FormalExperimentRq.Rq1 ? "rq1" : "rq2";
        RequireSameString(permit, "rq", inputs, "rq", rqToken);
        RequireSameString(
            permit,
            "preregistration_artifact_version",
            inputs,
            "preregistration_artifact_version");
        RequireSameString(permit, "pair_manifest_hash", inputs, "pair_manifest_hash");
        RequireSameString(permit, "runtime_version", inputs, "runtime_version");
        RequireSameString(permit, "model_profile_id", inputs, "model_profile_id");
        string freezeHash = RequiredString(permit, "freeze_bundle_hash");
        string repositoryRevision = RequiredString(permit, "repository_revision");
        string preregistration = RequiredString(permit, "preregistration_artifact_version");
        if (authorization.State != FormalCollectionAuthorizationState.FrozenAuthorized
            || !StringComparer.Ordinal.Equals(authorization.FreezeRecordHash, freezeHash)
            || !StringComparer.Ordinal.Equals(authorization.RepositoryRevision, repositoryRevision)
            || !StringComparer.Ordinal.Equals(authorization.PreregistrationArtifactVersion, preregistration)
            || !authorization.AuthorizedRqs.Contains(rq))
            throw new InvalidDataException("Formal authorization does not match its collection permit.");

        HashSet<string> permitArtifacts = ReadArtifactIds(permit.GetProperty("artifacts"));
        HashSet<string> inputArtifacts = ReadArtifactIds(inputs.GetProperty("required_artifacts"));
        if (inputs.GetProperty("unresolved_input_ids").GetArrayLength() != 0
            || permitArtifacts.Count == 0
            || !permitArtifacts.SetEquals(inputArtifacts))
            throw new InvalidDataException("Formal preflight artifact catalogue does not match its permit.");

        FormalExperimentEvidenceReplayRecord artifactBundleRecord = records.SingleOrDefault(
            value => value.RecordKind == "frozen_artifact_bundle")
            ?? throw new InvalidDataException("Ready formal evidence lacks exact frozen artifact bytes.");
        IReadOnlyDictionary<string, byte[]> frozenArtifactBytes = ValidateFrozenArtifactBundle(
            artifactBundleRecord.Payload,
            permitArtifacts);

        string suiteArtifactId = FormalExperimentSuiteManifest.SuiteArtifactId(rq);
        if (!frozenArtifactBytes.TryGetValue(suiteArtifactId, out byte[]? suiteBytes))
            throw new InvalidDataException("Formal admission evidence lacks its frozen suite manifest.");
        FormalExperimentSuiteManifest suite = FormalExperimentSuiteManifest.Load(suiteBytes);
        string permitSuiteHash = RequiredString(permit, "suite_manifest_hash");
        string permitSuiteId = RequiredString(permit, "suite_id");
        string permitSuitePairId = RequiredString(permit, "suite_pair_id");
        if (!StringComparer.Ordinal.Equals(suite.ManifestHash, permitSuiteHash)
            || !StringComparer.Ordinal.Equals(suite.SuiteId, permitSuiteId)
            || suite.Rq != rq
            || !StringComparer.Ordinal.Equals(suite.PreregistrationArtifactVersion, preregistration))
            throw new InvalidDataException("Formal permit suite identity is inconsistent.");
        string[] permitConditionOrder = ReadStringArray(permit, "condition_order");
        string[] pairArtifacts = permitArtifacts
            .Where(value => !StringComparer.Ordinal.Equals(value, suiteArtifactId))
            .ToArray();
        FormalExperimentSuitePairEntry suiteEntry = suite.RequirePermitEntry(
            permitSuitePairId,
            RequiredString(permit, "pair_manifest_hash"),
            pairArtifacts,
            permitConditionOrder);
        if (!StringComparer.Ordinal.Equals(
                suiteEntry.CandidateSetId,
                RequiredNullableString(permit, "candidate_set_id"))
            || !StringComparer.Ordinal.Equals(
                suiteEntry.SummaryArtifactId,
                RequiredNullableString(permit, "summary_artifact_id"))
            || !StringComparer.Ordinal.Equals(
                suiteEntry.SummaryArtifactVersion,
                RequiredNullableString(permit, "summary_artifact_version")))
            throw new InvalidDataException("Formal permit RQ2 artifact identity is cross-wired.");
        if (rq == FormalExperimentRq.Rq2)
        {
            suite.ValidateRq2FrozenAssets(
                frozenArtifactBytes["rq2_public_fixture_bundle"],
                frozenArtifactBytes["rq2_summary_registry"]);
        }

        string pairKind = rq == FormalExperimentRq.Rq1 ? "rq1_pair_manifest" : "rq2_pair_manifest";
        string pairArtifactId = pairKind;
        FormalExperimentEvidenceReplayRecord pairRecord = records.Single(value => value.RecordKind == pairKind);
        if (!permitArtifacts.Contains(pairArtifactId)
            || !StringComparer.Ordinal.Equals(pairRecord.PayloadHash, RequiredString(inputs, "pair_manifest_hash")))
            throw new InvalidDataException("Formal pair-manifest evidence does not match its frozen artifact binding.");
        if (rq == FormalExperimentRq.Rq2)
        {
            FormalExperimentEvidenceReplayRecord emotionRecord = records.Single(
                value => value.RecordKind == "rq2_pre_treatment_emotion");
            if (!permitArtifacts.Contains("rq2_pre_treatment_emotion") || emotionRecord.Payload.Length == 0)
                throw new InvalidDataException("RQ2 pre-treatment emotion evidence is missing.");
        }
    }

    private static IReadOnlyDictionary<string, byte[]> ValidateFrozenArtifactBundle(
        byte[] payload,
        IReadOnlySet<string> permitArtifacts)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        if (!StringComparer.Ordinal.Equals(
                RequiredString(root, "schema_version"),
                "alice.formal-frozen-artifact-bundle.v1")
            || !root.TryGetProperty("artifacts", out JsonElement artifacts)
            || artifacts.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Frozen artifact evidence bundle is malformed.");
        var observedBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (JsonElement artifact in artifacts.EnumerateArray())
        {
            string id = RequiredString(artifact, "artifact_id");
            byte[] bytes = artifact.GetProperty("canonical_bytes").GetBytesFromBase64();
            if (bytes.Length == 0
                || !observedBytes.TryAdd(id, bytes))
                throw new InvalidDataException("Frozen artifact bytes or identity are invalid.");
        }
        if (!permitArtifacts.SetEquals(observedBytes.Keys))
            throw new InvalidDataException("Frozen artifact bytes do not cover the exact permit catalogue.");
        return new ReadOnlyDictionary<string, byte[]>(observedBytes);
    }

    private static string[] ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Formal evidence array is missing: {propertyName}.");
        var result = new List<string>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                throw new InvalidDataException($"Formal evidence array is invalid: {propertyName}.");
            result.Add(item.GetString()!);
        }
        return result.ToArray();
    }

    private static void RequireExactProperties(JsonElement root, IEnumerable<string> expectedProperties)
    {
        var expected = new HashSet<string>(expectedProperties, StringComparer.Ordinal);
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!actual.Add(property.Name))
                throw new InvalidDataException("Formal evidence contains a duplicate property.");
        }
        if (!expected.SetEquals(actual))
            throw new InvalidDataException("Formal evidence contains an unknown or missing property.");
    }

    private static void ValidateRq1ManifestChain(
        IReadOnlyList<FormalExperimentEvidenceReplayRecord> records)
    {
        using JsonDocument pairDocument = JsonDocument.Parse(
            records.Single(value => value.RecordKind == "rq1_pair_manifest").Payload);
        JsonElement pair = pairDocument.RootElement;
        string agentHash = FormalExperimentCanonical.Hash(
            records.Single(value => value.RecordKind == "rq1_agent_centric_manifest").Payload);
        string eventHash = FormalExperimentCanonical.Hash(
            records.Single(value => value.RecordKind == "rq1_event_centric_manifest").Payload);
        if (!StringComparer.Ordinal.Equals(RequiredString(pair, "agent_centric_manifest_hash"), agentHash)
            || !StringComparer.Ordinal.Equals(RequiredString(pair, "event_centric_manifest_hash"), eventHash))
            throw new InvalidDataException("RQ1 condition manifests do not match their paired manifest.");
    }

    private static void ValidateRq2ManifestChain(
        IReadOnlyList<FormalExperimentEvidenceReplayRecord> records)
    {
        using JsonDocument pairDocument = JsonDocument.Parse(
            records.Single(value => value.RecordKind == "rq2_pair_manifest").Payload);
        using JsonDocument verbatimDocument = JsonDocument.Parse(
            records.Single(value => value.RecordKind == "rq2_verbatim_manifest").Payload);
        using JsonDocument summaryDocument = JsonDocument.Parse(
            records.Single(value => value.RecordKind == "rq2_summary_manifest").Payload);
        JsonElement pair = pairDocument.RootElement;
        string sharedHash = FormalExperimentCanonical.Hash(
            records.Single(value => value.RecordKind == "rq2_shared_configuration").Payload);
        string verbatimHash = FormalExperimentCanonical.Hash(
            records.Single(value => value.RecordKind == "rq2_verbatim_manifest").Payload);
        string summaryHash = FormalExperimentCanonical.Hash(
            records.Single(value => value.RecordKind == "rq2_summary_manifest").Payload);
        if (!StringComparer.Ordinal.Equals(RequiredString(pair, "shared_configuration_hash"), sharedHash)
            || !StringComparer.Ordinal.Equals(RequiredString(pair, "verbatim_manifest_hash"), verbatimHash)
            || !StringComparer.Ordinal.Equals(RequiredString(pair, "summary_manifest_hash"), summaryHash)
            || !StringComparer.Ordinal.Equals(
                RequiredString(verbatimDocument.RootElement, "shared_configuration_hash"),
                sharedHash)
            || !StringComparer.Ordinal.Equals(
                RequiredString(summaryDocument.RootElement, "shared_configuration_hash"),
                sharedHash))
            throw new InvalidDataException("RQ2 shared/condition manifests do not match their paired manifest.");
    }

    private static void ValidateRq2PreTreatmentEmotion(
        IReadOnlyList<FormalExperimentEvidenceReplayRecord> records)
    {
        FormalExperimentEvidenceReplayRecord candidateRecord = records.Single(
            value => value.RecordKind == "rq2_candidate_set");
        FormalExperimentEvidenceReplayRecord emotionRecord = records.Single(
            value => value.RecordKind == "rq2_pre_treatment_emotion");
        using JsonDocument document = JsonDocument.Parse(emotionRecord.Payload);
        JsonElement root = document.RootElement;
        if (!StringComparer.Ordinal.Equals(
                RequiredString(root, "protocol_version"),
                "formal_rq2_pre_treatment_emotion_v1")
            || !StringComparer.Ordinal.Equals(
                RequiredString(root, "candidate_set_id"),
                candidateRecord.PayloadHash))
            throw new InvalidDataException("RQ2 emotion evidence does not bind the exact pre-treatment candidate set.");
    }

    private static void ValidateRq2CandidateScoring(
        IReadOnlyList<FormalExperimentEvidenceReplayRecord> records)
    {
        FormalExperimentEvidenceReplayRecord candidateRecord = records.Single(
            value => value.RecordKind == "rq2_candidate_set");
        FormalExperimentEvidenceReplayRecord scoringRecord = records.Single(
            value => value.RecordKind == "rq2_candidate_scoring");
        using JsonDocument candidateDocument = JsonDocument.Parse(candidateRecord.Payload);
        string[] expectedMemoryIds = candidateDocument.RootElement.GetProperty("ranked_slices")
            .EnumerateArray()
            .Select(ReadCandidateMemoryId)
            .ToArray();
        using JsonDocument document = JsonDocument.Parse(scoringRecord.Payload);
        JsonElement root = document.RootElement;
        using JsonDocument sharedDocument = JsonDocument.Parse(records.Single(
            value => value.RecordKind == "rq2_shared_configuration").Payload);
        JsonElement scorerConfiguration = sharedDocument.RootElement.GetProperty(
            "candidate_scorer_configuration");
        if (!StringComparer.Ordinal.Equals(
                RequiredString(root, "schema_version"),
                "alice.formal-rq2-candidate-scoring.v1")
            || !StringComparer.Ordinal.Equals(
                RequiredString(root, "candidate_set_id"),
                candidateRecord.PayloadHash)
            || !StringComparer.Ordinal.Equals(
                RequiredString(root, "scorer_configuration_evidence_id"),
                RequiredString(scorerConfiguration, "evidence_id"))
            || root.GetProperty("ticks_per_hour").GetInt64() <= 0
            || root.GetProperty("recency_base").GetDouble() != FormalRq2CandidateScorer.CanonicalRecencyBase)
        {
            throw new InvalidDataException("RQ2 candidate scoring does not bind its exact candidate set or scorer constants.");
        }

        _ = RequiredString(root, "scorer_configuration_evidence_id");
        int expectedRank = 0;
        foreach (JsonElement row in root.GetProperty("rows").EnumerateArray())
        {
            if (row.GetProperty("rank").GetInt32() != expectedRank++)
                throw new InvalidDataException("RQ2 candidate scoring ranks are not contiguous.");
            string memoryId = RequiredString(row, "memory_id");
            FormalExperimentCanonical.ValidateSha256(memoryId, "memory_id");
            if (expectedRank > expectedMemoryIds.Length
                || !StringComparer.Ordinal.Equals(memoryId, expectedMemoryIds[expectedRank - 1]))
                throw new InvalidDataException("RQ2 candidate scoring rows do not match candidate rank order.");
            if (row.GetProperty("age_hours").GetInt64() < 0
                || row.GetProperty("importance_raw").GetInt32() is < 1 or > 10)
                throw new InvalidDataException("RQ2 candidate scoring contains an invalid raw value.");
            ValidateUnitScore(row, "relevance_raw");
            ValidateUnitScore(row, "recency_raw");
            ValidateUnitScore(row, "relevance_normalized");
            ValidateUnitScore(row, "recency_normalized");
            ValidateUnitScore(row, "importance_normalized");
            double total = row.GetProperty("total_score").GetDouble();
            if (!double.IsFinite(total) || total is < 0d or > 3d)
                throw new InvalidDataException("RQ2 candidate total score is outside its canonical range.");
        }

        if (expectedRank == 0 || expectedRank != expectedMemoryIds.Length)
            throw new InvalidDataException("RQ2 candidate scoring must cover the exact candidate set.");
    }

    private static string ReadCandidateMemoryId(JsonElement rankedSlice)
    {
        byte[] sliceBytes = rankedSlice.GetProperty("slice_base64").GetBytesFromBase64();
        using JsonDocument sliceDocument = JsonDocument.Parse(sliceBytes);
        return RequiredString(sliceDocument.RootElement, "memory_id");
    }

    private static void ValidateRq2PackingTrace(
        IReadOnlyList<FormalExperimentEvidenceReplayRecord> records)
    {
        using JsonDocument document = JsonDocument.Parse(records.Single(
            value => value.RecordKind == "rq2_verbatim_packing_trace").Payload);
        JsonElement root = document.RootElement;
        if (!StringComparer.Ordinal.Equals(
                RequiredString(root, "schema_version"),
                "alice.memory-packet-packing-trace.v1"))
            throw new InvalidDataException("RQ2 packing trace schema is unknown.");
        int considered = root.GetProperty("considered_count").GetInt32();
        int accepted = root.GetProperty("accepted_count").GetInt32();
        int skipped = root.GetProperty("skipped_count").GetInt32();
        int oversize = root.GetProperty("oversize_count").GetInt32();
        JsonElement skippedSlices = root.GetProperty("skipped_slices");
        int observedOversize = 0;
        foreach (JsonElement slice in skippedSlices.EnumerateArray())
        {
            FormalExperimentCanonical.ValidateSha256(RequiredString(slice, "memory_id"), "memory_id");
            if (slice.GetProperty("original_position").GetInt32() < 0
                || slice.GetProperty("slice_tokens").GetInt32() < 0
                || slice.GetProperty("remaining_tokens").GetInt32() < 0)
                throw new InvalidDataException("RQ2 packing trace contains a negative value.");
            string reason = RequiredString(slice, "reason");
            if (reason is not (nameof(MemoryPacketSkipReason.WouldExceedCeiling)
                    or nameof(MemoryPacketSkipReason.OversizeSlice)
                    or nameof(MemoryPacketSkipReason.Duplicate)))
                throw new InvalidDataException("RQ2 packing trace contains an unknown skip reason.");
            if (reason == nameof(MemoryPacketSkipReason.OversizeSlice)) observedOversize++;
        }

        if (considered < 0
            || accepted < 0
            || skipped < 0
            || root.GetProperty("final_packet_tokens").GetInt32() < 0
            || considered != accepted + skipped
            || skipped != skippedSlices.GetArrayLength()
            || oversize != observedOversize)
            throw new InvalidDataException("RQ2 packing trace counts are inconsistent.");
    }

    private static void ValidateUnitScore(JsonElement row, string propertyName)
    {
        double value = row.GetProperty(propertyName).GetDouble();
        if (!double.IsFinite(value) || value is < 0d or > 1d)
            throw new InvalidDataException($"RQ2 candidate score {propertyName} is outside [0,1].");
    }

    private static void ValidateRq1ConditionResult(
        byte[] payload,
        byte[]? manifestPayload,
        bool formalCollection,
        bool requireFormalPairing)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        using JsonDocument? manifestDocument = manifestPayload is null ? null : JsonDocument.Parse(manifestPayload);
        JsonElement root = document.RootElement;
        if (!StringComparer.Ordinal.Equals(
                RequiredString(root, "schema_version"),
                "alice.formal-rq1-condition-result.v1"))
            throw new InvalidDataException("RQ1 condition result schema is unknown.");
        JsonElement runtimeDiagnostics = root.GetProperty("runtime_diagnostics");
        long logicalBudget = RequireNonNegativeInt64(runtimeDiagnostics, "logical_session_budget");
        long reservedBudget = RequireNonNegativeInt64(runtimeDiagnostics, "reserved_session_budget");
        long consumedBudget = RequireNonNegativeInt64(runtimeDiagnostics, "consumed_session_budget");
        long remainingBudget = RequireNonNegativeInt64(runtimeDiagnostics, "remaining_session_budget");
        if (logicalBudget != reservedBudget + consumedBudget + remainingBudget)
            throw new InvalidDataException("RQ1 runtime budget diagnostics are inconsistent.");
        RequireNonNegativeInt64(runtimeDiagnostics, "total_transport_attempts");
        RequireNonNegativeInt64(runtimeDiagnostics, "pressure_index_lookup_count");
        RequireNonNegativeInt64(runtimeDiagnostics, "pressure_evaluation_count");
        RequireNonNegativeInt64(runtimeDiagnostics, "pressure_state_change_count");
        var calls = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement callEntry in root.GetProperty("model_calls").EnumerateArray())
        {
            string outerHash = RequiredString(callEntry, "evidence_hash");
            byte[] callBytes = callEntry.GetProperty("canonical_evidence").GetBytesFromBase64();
            JsonElement call = ValidateModelCallEvidence(callBytes, outerHash, requireFormalPairing);
            if (requireFormalPairing && manifestDocument is not null)
                ValidateRq1CallManifestBinding(call, manifestDocument.RootElement);
            if (!calls.TryAdd(RequiredString(call, "call_id"), call))
                throw new InvalidDataException("RQ1 nested model-call identity is duplicated.");
        }

        foreach (JsonElement opportunity in root.GetProperty("opportunity_evidence").EnumerateArray())
        {
            string? terminalKind = RequiredNullableString(opportunity, "terminal_kind");
            JsonElement receiptElement = opportunity.GetProperty("terminal_receipt");
            if (receiptElement.ValueKind == JsonValueKind.Null)
            {
                if (RequiredNullableString(opportunity, "terminal_receipt_hash") is not null)
                    throw new InvalidDataException("RQ1 null terminal receipt cannot carry a receipt hash.");
                if (formalCollection && terminalKind is not null)
                    throw new InvalidDataException("A formal RQ1 terminal lacks its exact receipt.");
                continue;
            }
            string receiptHash = RequiredString(opportunity, "terminal_receipt_hash");
            byte[] receiptBytes = receiptElement.GetBytesFromBase64();
            JsonElement receipt = ValidateTerminalReceipt(receiptBytes, receiptHash);
            if (!StringComparer.Ordinal.Equals(
                    RequiredString(receipt, "kind"),
                    ExpectedReceiptKind(terminalKind, "RQ1")))
                throw new InvalidDataException("RQ1 terminal kind does not match its exact receipt kind.");
            string callId = RequiredString(opportunity, "model_call_id");
            if (!calls.TryGetValue(callId, out JsonElement call))
            {
                if (StringComparer.Ordinal.Equals(
                        terminalKind,
                        nameof(FormalRq1TerminalOutcomeKind.TransportFailure))
                    && StringComparer.Ordinal.Equals(RequiredString(receipt, "kind"), "transport_failure")
                    && StringComparer.Ordinal.Equals(callId, RequiredString(receipt, "model_call_id"))
                    && StringComparer.Ordinal.Equals(
                        RequiredNullableString(opportunity, "need_id"),
                        RequiredString(receipt, "need_id")))
                    continue;
                throw new InvalidDataException("RQ1 terminal receipt lacks its nested model call or transport failure.");
            }
            if (!StringComparer.Ordinal.Equals(callId, RequiredString(receipt, "model_call_id"))
                || requireFormalPairing && (!StringComparer.Ordinal.Equals(
                        RequiredString(call, "actor_id"),
                        RequiredString(receipt, "actor_id"))
                    || !StringComparer.Ordinal.Equals(
                        RequiredString(call, "need_id"),
                        RequiredNullableString(opportunity, "need_id")))
                || !StringComparer.Ordinal.Equals(
                    RequiredNullableString(opportunity, "need_id"),
                    RequiredString(receipt, "need_id"))
                || !StringComparer.Ordinal.Equals(
                    RequiredNullableString(opportunity, "terminal_evidence_hash"),
                    RequiredNullableString(receipt, "terminal_evidence_hash"))
                || !StringComparer.Ordinal.Equals(
                    RequiredNullableString(opportunity, "game_action_id"),
                    RequiredNullableString(receipt, "game_action_id")))
                throw new InvalidDataException("RQ1 terminal receipt is cross-wired from its nested model call or outcome.");
        }
    }

    private static void ValidateRq2ConditionResult(
        byte[] payload,
        byte[]? sharedConfigurationPayload,
        byte[]? contextBlobPayload,
        byte[]? candidateSetPayload,
        bool formalCollection,
        bool requireFormalPairing)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        using JsonDocument? sharedDocument = sharedConfigurationPayload is null
            ? null
            : JsonDocument.Parse(sharedConfigurationPayload);
        JsonElement root = document.RootElement;
        if (!StringComparer.Ordinal.Equals(
                RequiredString(root, "schema_version"),
                "alice.formal-rq2-condition-terminal-evidence.v1"))
            throw new InvalidDataException("RQ2 condition result schema is unknown.");
        string terminalKind = RequiredString(root, "terminal_kind");
        JsonElement callElement = root.GetProperty("model_call_evidence");
        if (callElement.ValueKind == JsonValueKind.Null)
        {
            if (!StringComparer.Ordinal.Equals(
                    terminalKind,
                    nameof(FormalRq2TerminalOutcomeKind.TransportFailure))
                || RequiredNullableString(root, "model_call_evidence_hash") is not null
                || RequiredNullableString(root, "terminal_evidence_hash") is not null)
                throw new InvalidDataException("Only an outcome-free RQ2 transport failure may omit model-call evidence.");
            JsonElement transportReceiptElement = root.GetProperty("terminal_receipt");
            if (transportReceiptElement.ValueKind == JsonValueKind.Null)
                throw new InvalidDataException("RQ2 transport failure lacks its sanitized receipt.");
            string transportReceiptHash = RequiredString(root, "terminal_receipt_hash");
            JsonElement transportReceipt = ValidateTerminalReceipt(
                transportReceiptElement.GetBytesFromBase64(),
                transportReceiptHash);
            if (!StringComparer.Ordinal.Equals(
                    RequiredString(transportReceipt, "kind"),
                    "transport_failure")
                || RequiredNullableString(transportReceipt, "terminal_evidence_hash") is not null)
                throw new InvalidDataException("RQ2 no-envelope terminal is not a transport-failure receipt.");
            if (contextBlobPayload is not null)
            {
                using JsonDocument contextBindingDocument = JsonDocument.Parse(contextBlobPayload);
                JsonElement contextBinding = contextBindingDocument.RootElement;
                using JsonDocument contextDocument = JsonDocument.Parse(ReadCanonicalBlob(contextBlobPayload));
                JsonElement context = contextDocument.RootElement;
                JsonElement sharedContext = context.GetProperty("shared_context");
                if (!StringComparer.Ordinal.Equals(
                        RequiredString(contextBinding, "actor_id"),
                        RequiredString(transportReceipt, "actor_id"))
                    || !StringComparer.Ordinal.Equals(
                        RequiredString(contextBinding, "need_id"),
                        RequiredString(transportReceipt, "need_id"))
                    || !StringComparer.Ordinal.Equals(
                        RequiredString(contextBinding, "actor_id"),
                        RequiredString(sharedContext.GetProperty("identity"), "actor_id")))
                    throw new InvalidDataException("RQ2 transport-failure receipt is cross-wired from its branch context.");
            }
            return;
        }
        string outerCallHash = RequiredString(root, "model_call_evidence_hash");
        byte[] callBytes = callElement.GetBytesFromBase64();
        JsonElement call = ValidateModelCallEvidence(callBytes, outerCallHash, requireFormalPairing);
        if (StringComparer.Ordinal.Equals(
                terminalKind,
                nameof(FormalRq2TerminalOutcomeKind.TransportFailure)))
            throw new InvalidDataException("An RQ2 transport failure cannot carry completed model-call evidence.");
        if (requireFormalPairing
            && sharedDocument is not null
            && contextBlobPayload is not null
            && candidateSetPayload is not null)
        {
            JsonElement shared = sharedDocument.RootElement;
            if (!StringComparer.Ordinal.Equals(
                RequiredString(call, "provider_profile_id"),
                RequiredString(shared, "model_profile_id"))
            || !StringComparer.Ordinal.Equals(
                RequiredString(call, "request_protocol_version"),
                RequiredString(shared, "request_protocol_version"))
            || !StringComparer.Ordinal.Equals(
                RequiredString(call, "candidate_set_id"),
                FormalExperimentCanonical.Hash(candidateSetPayload)))
                throw new InvalidDataException("RQ2 model-call evidence is not bound to its frozen manifest/context.");
        }
        JsonElement receiptElement = root.GetProperty("terminal_receipt");
        if (receiptElement.ValueKind == JsonValueKind.Null)
        {
            if (RequiredNullableString(root, "terminal_receipt_hash") is not null)
                throw new InvalidDataException("RQ2 null terminal receipt cannot carry a receipt hash.");
            if (formalCollection)
                throw new InvalidDataException("A formal RQ2 terminal lacks its exact receipt.");
            _ = ExpectedReceiptKind(terminalKind, "RQ2");
            return;
        }
        string receiptHash = RequiredString(root, "terminal_receipt_hash");
        JsonElement receipt = ValidateTerminalReceipt(receiptElement.GetBytesFromBase64(), receiptHash);
        if (!StringComparer.Ordinal.Equals(
                RequiredString(receipt, "kind"),
                ExpectedReceiptKind(terminalKind, "RQ2"))
            || !StringComparer.Ordinal.Equals(
                RequiredString(call, "call_id"),
                RequiredString(receipt, "model_call_id"))
            || requireFormalPairing && (!StringComparer.Ordinal.Equals(
                    RequiredString(call, "actor_id"),
                    RequiredString(receipt, "actor_id"))
                || !StringComparer.Ordinal.Equals(
                    RequiredString(call, "need_id"),
                    RequiredString(receipt, "need_id")))
            || !StringComparer.Ordinal.Equals(
                RequiredNullableString(root, "terminal_evidence_hash"),
                RequiredNullableString(receipt, "terminal_evidence_hash"))
            || !StringComparer.Ordinal.Equals(
                RequiredNullableString(root, "game_action_id"),
                RequiredNullableString(receipt, "game_action_id")))
            throw new InvalidDataException("RQ2 terminal receipt is cross-wired from its nested model call or outcome.");
    }

    private static string ExpectedReceiptKind(string? terminalKind, string rq) => terminalKind switch
    {
        nameof(FormalRq1TerminalOutcomeKind.AuthorityCommitted) => "authority_commit",
        nameof(FormalRq1TerminalOutcomeKind.JustifiedDefer) => "validated_defer",
        nameof(FormalRq1TerminalOutcomeKind.TransportFailure) => "transport_failure",
        nameof(FormalRq1TerminalOutcomeKind.InvalidDecision)
            or nameof(FormalRq1TerminalOutcomeKind.ValidatorRejected) => "validator_rejection",
        _ => throw new InvalidDataException($"{rq} terminal kind is missing or unknown.")
    };

    private static JsonElement ValidateModelCallEvidence(
        byte[] bytes,
        string outerHash,
        bool requireFormalPairing)
    {
        FormalExperimentCanonical.ValidateSha256(outerHash, nameof(outerHash));
        if (bytes.Length == 0
            || !StringComparer.Ordinal.Equals(FormalExperimentCanonical.Hash(bytes), outerHash))
            throw new InvalidDataException("Nested formal model-call evidence hash does not match its bytes.");
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement.Clone();
        if (!StringComparer.Ordinal.Equals(
                RequiredString(root, "schema_version"),
                "alice.formal-model-call-evidence.v3"))
            throw new InvalidDataException("Nested formal model-call evidence schema is unknown.");
        if (requireFormalPairing
            && (!StringComparer.Ordinal.Equals(RequiredString(root, "source"), "LiveTransportReceipt")
                || !StringComparer.Ordinal.Equals(RequiredString(root, "execution_mode"), "LiveRemote")))
            throw new InvalidDataException("Formal replay rejects Recorded/engineering model evidence.");
        string callId = RequiredString(root, "call_id");
        if (!requireFormalPairing) return root;
        if (!StringComparer.Ordinal.Equals(RequiredString(root, "request_binding_id"), callId))
            throw new InvalidDataException("Formal model-call request binding does not match its call identity.");
        string[] requiredIdentities =
        [
            "provider_protocol_version", "request_protocol_version", "provider_profile_id", "model_id",
            "actor_id", "need_id", "decision_need_fingerprint", "provider_response_id"
        ];
        foreach (string identity in requiredIdentities) _ = RequiredString(root, identity);
        string[] requiredHashes =
        [
            "request_hash", "response_hash", "problem_descriptor_hash", "candidate_set_id"
        ];
        foreach (string hash in requiredHashes)
            FormalExperimentCanonical.ValidateSha256(RequiredString(root, hash), hash);
        RequireNonNegativeInt64(root, "input_tokens");
        RequireNonNegativeInt64(root, "output_tokens");
        RequireNonNegativeInt64(root, "duration_milliseconds");
        ValidateOptionalNonNegativeInt64(root, "cache_creation_input_tokens");
        ValidateOptionalNonNegativeInt64(root, "cache_read_input_tokens");
        string decodedToolCallHash = RequiredString(root, "decoded_tool_call_hash");
        FormalExperimentCanonical.ValidateSha256(decodedToolCallHash, "decoded_tool_call_hash");
        byte[] decodedToolCall = root.GetProperty("decoded_tool_call").GetBytesFromBase64();
        if (decodedToolCall.Length == 0
            || !StringComparer.Ordinal.Equals(
                FormalExperimentCanonical.Hash(decodedToolCall),
                decodedToolCallHash))
            throw new InvalidDataException("Decoded tool-call evidence does not match its hash.");
        return root;
    }

    private static void ValidateRq1CallManifestBinding(JsonElement call, JsonElement manifest)
    {
        if (!StringComparer.Ordinal.Equals(
                RequiredString(call, "provider_profile_id"),
                RequiredString(manifest, "model_profile_id")))
            throw new InvalidDataException("RQ1 model-call profile does not match its condition manifest.");
        string protocolVersion = RequiredString(call, "request_protocol_version");
        bool matched = manifest.GetProperty("request_protocols").EnumerateArray().Any(protocol =>
            StringComparer.Ordinal.Equals(RequiredString(protocol, "protocol_version"), protocolVersion));
        if (!matched)
            throw new InvalidDataException("RQ1 model-call protocol/prompt/tool binding is not frozen by its manifest.");
    }

    private static JsonElement ValidateTerminalReceipt(byte[] bytes, string outerHash)
    {
        FormalExperimentCanonical.ValidateSha256(outerHash, nameof(outerHash));
        if (bytes.Length == 0
            || !StringComparer.Ordinal.Equals(FormalExperimentCanonical.Hash(bytes), outerHash))
            throw new InvalidDataException("Nested terminal receipt hash does not match its bytes.");
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement.Clone();
        if (!StringComparer.Ordinal.Equals(
                RequiredString(root, "schema_version"),
                "alice.formal-terminal-outcome-receipt.v2"))
            throw new InvalidDataException("Nested terminal receipt schema is unknown.");
        string sourceHash = RequiredString(root, "source_receipt_hash");
        byte[] sourceBytes = root.GetProperty("source_receipt").GetBytesFromBase64();
        if (sourceBytes.Length == 0
            || !StringComparer.Ordinal.Equals(FormalExperimentCanonical.Hash(sourceBytes), sourceHash))
            throw new InvalidDataException("Nested terminal receipt does not preserve its exact source bytes.");
        string kind = RequiredString(root, "kind");
        string? terminalHash = RequiredNullableString(root, "terminal_evidence_hash");
        bool grounded = kind is "authority_commit" or "validated_defer";
        if (grounded != (terminalHash is not null)
            || grounded && !StringComparer.Ordinal.Equals(terminalHash, sourceHash))
            throw new InvalidDataException("Nested terminal outcome truth was not derived from its source receipt.");
        _ = RequiredString(root, "actor_id");
        _ = RequiredString(root, "need_id");
        _ = RequiredString(root, "model_call_id");
        return root;
    }

    private static void ValidateMatchedScore(
        IReadOnlyList<FormalExperimentEvidenceReplayRecord> records,
        FormalExperimentRq rq)
    {
        FormalExperimentEvidenceReplayRecord scoreRecord = records.Single(
            value => value.RecordKind == "matched_score");
        byte[] recomputed = rq == FormalExperimentRq.Rq1
            ? RecomputeRq1Score(records)
            : RecomputeRq2Score(records);
        if (!scoreRecord.Payload.AsSpan().SequenceEqual(recomputed))
            throw new InvalidDataException("Formal matched score cannot be reproduced from the sealed evidence package.");
    }

    private static byte[] RecomputeRq1Score(IReadOnlyList<FormalExperimentEvidenceReplayRecord> records)
    {
        ActorOpportunityLedger opportunities = ParseRq1OpportunityLedger(
            records.Single(value => value.RecordKind == "rq1_opportunity_ledger").Payload);
        Rq1TestCaseLedger testCases = ParseRq1TestCaseLedger(
            records.Single(value => value.RecordKind == "rq1_test_case_ledger").Payload);
        FormalRq1OpportunityTestCaseMap mapping = ParseRq1OpportunityMap(
            opportunities,
            testCases,
            records.Single(value => value.RecordKind == "rq1_opportunity_test_case_map").Payload);
        FormalRq1HiddenTestCase[] hidden = ParseRq1HiddenCases(
            records.Single(value => value.RecordKind == "rq1_hidden_test_cases").Payload);
        ParsedRq1Condition agent = ParseRq1ConditionForScoring(
            records.Single(value => value.RecordKind == "rq1_agent_centric_result").Payload);
        ParsedRq1Condition eventCentric = ParseRq1ConditionForScoring(
            records.Single(value => value.RecordKind == "rq1_event_centric_result").Payload);
        FormalRq1EvaluatedConditionEvidence evaluatedAgent = FormalRq1OutcomeEvaluator.Evaluate(
            opportunities, testCases, mapping, hidden, agent.Opportunities);
        FormalRq1EvaluatedConditionEvidence evaluatedEvent = FormalRq1OutcomeEvaluator.Evaluate(
            opportunities, testCases, mapping, hidden, eventCentric.Opportunities);
        var score = new FormalRq1MatchedPairScore(
            opportunities.LedgerId,
            testCases.LedgerId,
            FormalRq1OfflineScorer.Score(
                opportunities,
                testCases,
                evaluatedAgent.ActivationEvidence,
                evaluatedAgent.TestCaseOutcomes,
                agent.Sessions),
            FormalRq1OfflineScorer.Score(
                opportunities,
                testCases,
                evaluatedEvent.ActivationEvidence,
                evaluatedEvent.TestCaseOutcomes,
                eventCentric.Sessions));
        return FormalRq1MatchedRunner.SerializeScore(score);
    }

    private static ActorOpportunityLedger ParseRq1OpportunityLedger(byte[] payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        var entries = new List<ActorOpportunityLedgerEntry>();
        foreach (JsonElement value in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            entries.Add(new ActorOpportunityLedgerEntry(
                new Rq1OpportunityId(RequiredString(value, "opportunity_id")),
                new ActorId(RequiredString(value, "actor_id")),
                new SimTime(value.GetProperty("eligible_at_ticks").GetInt64()),
                new SimTime(value.GetProperty("closes_at_ticks").GetInt64()),
                RequiredString(value, "shared_eligibility_evidence_hash"),
                value.GetProperty("baseline_dependency_degree").GetInt32()));
        }
        return new ActorOpportunityLedger(
            RequiredString(document.RootElement, "ledger_id"),
            entries);
    }

    private static Rq1TestCaseLedger ParseRq1TestCaseLedger(byte[] payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return new Rq1TestCaseLedger(
            RequiredString(document.RootElement, "ledger_id"),
            document.RootElement.GetProperty("entries").EnumerateArray().Select(
                value => new Rq1TestCaseLedgerEntry(
                    new Rq1TestCaseId(RequiredString(value, "test_case_id")))));
    }

    private static FormalRq1OpportunityTestCaseMap ParseRq1OpportunityMap(
        ActorOpportunityLedger opportunities,
        Rq1TestCaseLedger testCases,
        byte[] payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return new FormalRq1OpportunityTestCaseMap(
            opportunities,
            testCases,
            document.RootElement.GetProperty("entries").EnumerateArray().Select(
                value => new FormalRq1OpportunityTestCaseMapEntry(
                    new Rq1OpportunityId(RequiredString(value, "opportunity_id")),
                    new Rq1TestCaseId(RequiredString(value, "test_case_id")))));
    }

    private static FormalRq1HiddenTestCase[] ParseRq1HiddenCases(byte[] payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("test_cases").EnumerateArray().Select(
            value => new FormalRq1HiddenTestCase(
                new Rq1TestCaseId(RequiredString(value, "test_case_id")),
                Enum.Parse<FormalRq1TerminalOutcomeKind>(
                    RequiredString(value, "expected_terminal_kind"),
                    ignoreCase: false),
                RequiredNullableString(value, "expected_game_action_id"),
                RequiredNullableString(value, "expected_authority_action_family"))).ToArray();
    }

    private static ParsedRq1Condition ParseRq1ConditionForScoring(byte[] payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        var opportunities = new List<FormalRq1OpportunityRunEvidence>();
        foreach (JsonElement value in document.RootElement.GetProperty("opportunity_evidence").EnumerateArray())
        {
            string? terminalKindToken = RequiredNullableString(value, "terminal_kind");
            FormalTerminalOutcomeReceipt? terminalReceipt = ParseRq1TerminalReceipt(value);
            opportunities.Add(new FormalRq1OpportunityRunEvidence(
                new Rq1OpportunityId(RequiredString(value, "opportunity_id")),
                OptionalSimTime(value, "discovered_at_ticks"),
                OptionalSimTime(value, "need_created_at_ticks"),
                OptionalSimTime(value, "admitted_at_ticks"),
                OptionalSimTime(value, "attempted_at_ticks"),
                OptionalBoolean(value, "was_starvation_promoted"),
                OptionalDecisionNeedId(value, "need_id"),
                OptionalSessionId(value, "session_id"),
                OptionalReceiptId(value, "receipt_id"),
                terminalKindToken is null
                    ? null
                    : Enum.Parse<FormalRq1TerminalOutcomeKind>(terminalKindToken, ignoreCase: false),
                RequiredNullableString(value, "terminal_evidence_hash"),
                RequiredNullableString(value, "model_call_id"),
                terminalReceipt,
                RequiredNullableString(value, "game_action_id")));
        }
        var sessions = new List<Rq1SessionOutcome>();
        foreach (JsonElement value in document.RootElement.GetProperty("session_outcomes").EnumerateArray())
        {
            sessions.Add(new Rq1SessionOutcome(
                new Rq1SessionId(RequiredString(value, "session_id")),
                Enum.Parse<Rq1SessionProductivity>(RequiredString(value, "productivity"), ignoreCase: false),
                value.GetProperty("measured_tokens").GetInt64()));
        }
        return new ParsedRq1Condition(opportunities.ToArray(), sessions.ToArray());
    }

    private static FormalTerminalOutcomeReceipt? ParseRq1TerminalReceipt(JsonElement evidence)
    {
        string? receiptHash = RequiredNullableString(evidence, "terminal_receipt_hash");
        JsonElement encoded = evidence.GetProperty("terminal_receipt");
        if (encoded.ValueKind == JsonValueKind.Null)
        {
            if (receiptHash is not null)
                throw new InvalidDataException("RQ1 terminal receipt hash has no receipt bytes.");
            return null;
        }
        if (receiptHash is null || encoded.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("RQ1 terminal receipt bytes have no receipt hash.");

        byte[] canonicalBytes = encoded.GetBytesFromBase64();
        JsonElement receipt = ValidateTerminalReceipt(canonicalBytes, receiptHash);
        byte[] sourceBytes = receipt.GetProperty("source_receipt").GetBytesFromBase64();
        string actorId = RequiredString(receipt, "actor_id");
        string needId = RequiredString(receipt, "need_id");
        string modelCallId = RequiredString(receipt, "model_call_id");
        FormalTerminalOutcomeReceipt parsed = RequiredString(receipt, "kind") switch
        {
            "authority_commit" => FormalTerminalOutcomeReceipt.FromAuthorityCommit(
                actorId,
                needId,
                modelCallId,
                RequiredString(receipt, "game_action_id"),
                sourceBytes),
            "validated_defer" => FormalTerminalOutcomeReceipt.FromValidatedDefer(
                actorId,
                needId,
                modelCallId,
                sourceBytes),
            "validator_rejection" => FormalTerminalOutcomeReceipt.FromValidatorRejection(
                actorId,
                needId,
                modelCallId,
                sourceBytes),
            "transport_failure" => FormalTerminalOutcomeReceipt.FromTransportFailure(
                actorId,
                needId,
                modelCallId,
                sourceBytes),
            _ => throw new InvalidDataException("RQ1 terminal receipt kind is unknown.")
        };
        if (!parsed.GetCanonicalBytes().AsSpan().SequenceEqual(canonicalBytes))
            throw new InvalidDataException("RQ1 terminal receipt is not canonically reproducible.");
        return parsed;
    }

    private static byte[] RecomputeRq2Score(IReadOnlyList<FormalExperimentEvidenceReplayRecord> records)
    {
        using JsonDocument sourcesDocument = JsonDocument.Parse(
            records.Single(value => value.RecordKind == "rq2_required_sources").Payload);
        string[] requiredSources = sourcesDocument.RootElement.GetProperty("source_ids")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        using JsonDocument gateDocument = JsonDocument.Parse(
            records.Single(value => value.RecordKind == "rq2_required_source_gate").Payload);
        bool gateComplete = gateDocument.RootElement.GetProperty("observations").EnumerateArray()
            .All(value => value.GetProperty("first_candidate_rank").ValueKind == JsonValueKind.Number);
        using JsonDocument predicateDocument = JsonDocument.Parse(
            records.Single(value => value.RecordKind == "rq2_hidden_predicate").Payload);
        using JsonDocument fidelityDocument = JsonDocument.Parse(
            records.Single(value => value.RecordKind == "rq2_summary_fidelity").Payload);
        bool fidelityValid = fidelityDocument.RootElement.GetProperty("valid").GetBoolean();
        FormalRq2ConditionScore verbatim = RecomputeRq2Condition(
            FormalRq2Treatment.Verbatim,
            requiredSources,
            gateComplete,
            true,
            RequiredString(predicateDocument.RootElement, "expected_terminal_kind"),
            RequiredNullableString(predicateDocument.RootElement, "expected_game_action_id"),
            ReadCanonicalBlob(records.Single(value => value.RecordKind == "rq2_verbatim_packet").Payload),
            records.Single(value => value.RecordKind == "rq2_verbatim_result").Payload);
        FormalRq2ConditionScore summary = RecomputeRq2Condition(
            FormalRq2Treatment.Summary,
            requiredSources,
            gateComplete,
            fidelityValid,
            RequiredString(predicateDocument.RootElement, "expected_terminal_kind"),
            RequiredNullableString(predicateDocument.RootElement, "expected_game_action_id"),
            ReadCanonicalBlob(records.Single(value => value.RecordKind == "rq2_summary_packet").Payload),
            records.Single(value => value.RecordKind == "rq2_summary_result").Payload);
        var score = new FormalRq2MatchedPairScore(
            new FormalRq2TestCaseId(RequiredString(predicateDocument.RootElement, "test_case_id")),
            FormalExperimentCanonical.Hash(records.Single(value => value.RecordKind == "rq2_required_sources").Payload),
            verbatim,
            summary);
        return FormalRq2MatchedRunner.SerializeScore(score);
    }

    private static FormalRq2ConditionScore RecomputeRq2Condition(
        FormalRq2Treatment treatment,
        IReadOnlyCollection<string> requiredSources,
        bool gateComplete,
        bool fidelityValid,
        string expectedTerminalKind,
        string? expectedGameActionId,
        byte[] packetBytes,
        byte[] resultBytes)
    {
        using JsonDocument packetDocument = JsonDocument.Parse(packetBytes);
        var packetSources = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement memory in packetDocument.RootElement.GetProperty("memory").EnumerateArray())
            foreach (JsonElement source in memory.GetProperty("source_ids").EnumerateArray())
                packetSources.Add(source.GetString()!);
        using JsonDocument resultDocument = JsonDocument.Parse(resultBytes);
        string terminalKind = RequiredString(resultDocument.RootElement, "terminal_kind");
        bool decisionValid = terminalKind is "AuthorityCommitted" or "JustifiedDefer";
        bool terminalValid = decisionValid
            && StringComparer.Ordinal.Equals(terminalKind, expectedTerminalKind)
            && StringComparer.Ordinal.Equals(
                RequiredNullableString(resultDocument.RootElement, "game_action_id"),
                expectedGameActionId);
        return new FormalRq2ConditionScore(
            treatment,
            gateComplete,
            requiredSources.All(packetSources.Contains),
            fidelityValid,
            decisionValid,
            terminalValid,
            terminalValid);
    }

    private static byte[] ReadCanonicalBlob(byte[] payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        byte[] bytes = document.RootElement.GetProperty("canonical_bytes").GetBytesFromBase64();
        if (document.RootElement.TryGetProperty("content_hash", out JsonElement contentHash)
            && !StringComparer.Ordinal.Equals(
                FormalExperimentCanonical.Hash(bytes),
                contentHash.GetString()))
            throw new InvalidDataException("Canonical evidence blob hash does not match its bytes.");
        return bytes;
    }

    private static SimTime? OptionalSimTime(JsonElement root, string propertyName) =>
        root.GetProperty(propertyName).ValueKind == JsonValueKind.Null
            ? null
            : new SimTime(root.GetProperty(propertyName).GetInt64());

    private static bool? OptionalBoolean(JsonElement root, string propertyName) =>
        root.GetProperty(propertyName).ValueKind == JsonValueKind.Null
            ? null
            : root.GetProperty(propertyName).GetBoolean();

    private static DecisionNeedId? OptionalDecisionNeedId(JsonElement root, string propertyName)
    {
        string? value = RequiredNullableString(root, propertyName);
        return value is null ? null : new DecisionNeedId(value);
    }

    private static Rq1SessionId? OptionalSessionId(JsonElement root, string propertyName)
    {
        string? value = RequiredNullableString(root, propertyName);
        return value is null ? null : new Rq1SessionId(value);
    }

    private static FormalTerminalReceiptId? OptionalReceiptId(JsonElement root, string propertyName)
    {
        string? value = RequiredNullableString(root, propertyName);
        return value is null ? null : new FormalTerminalReceiptId(value);
    }

    private sealed record ParsedRq1Condition(
        FormalRq1OpportunityRunEvidence[] Opportunities,
        Rq1SessionOutcome[] Sessions);

    private static HashSet<string> ReadArtifactIds(JsonElement artifacts)
    {
        if (artifacts.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Formal artifact catalogue must be an array.");
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement artifact in artifacts.EnumerateArray())
        {
            string id = RequiredString(artifact, "artifact_id");
            if (!result.Add(id))
                throw new InvalidDataException("Formal artifact catalogue contains a duplicate identity.");
        }
        return result;
    }

    private static void RequireSameString(
        JsonElement first,
        string firstProperty,
        JsonElement second,
        string secondProperty,
        string? expected = null)
    {
        string firstValue = RequiredString(first, firstProperty);
        string secondValue = RequiredString(second, secondProperty);
        if (!StringComparer.Ordinal.Equals(firstValue, secondValue)
            || expected is not null && !StringComparer.Ordinal.Equals(firstValue, expected))
            throw new InvalidDataException("Formal admission identities are cross-wired.");
    }

    private static string RequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Formal evidence string is missing: {propertyName}.");
        return value.GetString()!;
    }

    private static string? RequiredNullableString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
            throw new InvalidDataException($"Formal evidence property is missing: {propertyName}.");
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Formal evidence optional string is malformed: {propertyName}.");
        return value.GetString();
    }

    private static long RequireNonNegativeInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out long parsed)
            || parsed < 0)
            throw new InvalidDataException($"Formal evidence non-negative integer is missing: {propertyName}.");
        return parsed;
    }

    private static void ValidateOptionalNonNegativeInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
            throw new InvalidDataException($"Formal evidence property is missing: {propertyName}.");
        if (value.ValueKind == JsonValueKind.Null) return;
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out long parsed)
            || parsed < 0)
            throw new InvalidDataException($"Formal evidence optional integer is malformed: {propertyName}.");
    }
}

public interface IFormalExperimentRecorder
{
    bool IsSealed { get; }
    void Append(string recordKind, ReadOnlySpan<byte> canonicalPayload);
    FormalExperimentEvidenceSeal Seal();
}

internal static class FormalExperimentEvidencePayloads
{
    public static byte[] SerializePreflightInputs(
        FormalExperimentRq rq,
        bool formalCollection,
        string preregistrationArtifactVersion,
        string pairManifestHash,
        string runtimeVersion,
        string modelProfileId,
        IEnumerable<string> unresolvedInputIds,
        IEnumerable<FormalEvidenceArtifactBinding> requiredArtifacts)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-preflight-inputs.v1");
            writer.WriteString("rq", rq == FormalExperimentRq.Rq1 ? "rq1" : "rq2");
            writer.WriteString("run_purpose", formalCollection ? "formal_collection" : "engineering_evidence");
            writer.WriteString("preregistration_artifact_version", preregistrationArtifactVersion);
            writer.WriteString("pair_manifest_hash", pairManifestHash);
            writer.WriteString("runtime_version", runtimeVersion);
            writer.WriteString("model_profile_id", modelProfileId);
            writer.WritePropertyName("unresolved_input_ids");
            writer.WriteStartArray();
            foreach (string value in unresolvedInputIds.Order(StringComparer.Ordinal))
                writer.WriteStringValue(value);
            writer.WriteEndArray();
            writer.WritePropertyName("required_artifacts");
            writer.WriteStartArray();
            foreach (FormalEvidenceArtifactBinding artifact in requiredArtifacts.OrderBy(
                         value => value.ArtifactId,
                         StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("artifact_id", artifact.ArtifactId);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static byte[] SerializeCanonicalBlob(
        string blobSchemaVersion,
        string contentHash,
        ReadOnlySpan<byte> canonicalBytes)
    {
        FormalExperimentCanonical.RequireIdentity(blobSchemaVersion, nameof(blobSchemaVersion));
        FormalExperimentCanonical.ValidateSha256(contentHash, nameof(contentHash));
        if (canonicalBytes.IsEmpty)
            throw new ArgumentException("A canonical evidence blob cannot be empty.", nameof(canonicalBytes));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", blobSchemaVersion);
            writer.WriteString("content_hash", contentHash);
            writer.WriteBase64String("canonical_bytes", canonicalBytes);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static byte[] SerializeUnhashedBlob(
        string blobSchemaVersion,
        ReadOnlySpan<byte> canonicalBytes)
    {
        FormalExperimentCanonical.RequireIdentity(blobSchemaVersion, nameof(blobSchemaVersion));
        if (canonicalBytes.IsEmpty)
            throw new ArgumentException("A canonical evidence blob cannot be empty.", nameof(canonicalBytes));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", blobSchemaVersion);
            writer.WriteBase64String("canonical_bytes", canonicalBytes);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static byte[] SerializeBoundContextBlob(
        string blobSchemaVersion,
        string contentHash,
        string actorId,
        string needId,
        ReadOnlySpan<byte> canonicalBytes)
    {
        FormalExperimentCanonical.RequireIdentity(blobSchemaVersion, nameof(blobSchemaVersion));
        FormalExperimentCanonical.ValidateSha256(contentHash, nameof(contentHash));
        FormalExperimentCanonical.RequireIdentity(actorId, nameof(actorId));
        FormalExperimentCanonical.RequireIdentity(needId, nameof(needId));
        if (canonicalBytes.IsEmpty)
            throw new ArgumentException("A canonical evidence blob cannot be empty.", nameof(canonicalBytes));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", blobSchemaVersion);
            writer.WriteString("content_hash", contentHash);
            writer.WriteString("actor_id", actorId);
            writer.WriteString("need_id", needId);
            writer.WriteBase64String("canonical_bytes", canonicalBytes);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}

/// <summary>Deterministic append-only evidence owner. Temporary runs remain caller-owned until explicitly sealed.</summary>
public sealed class CanonicalFormalExperimentRecorder : IFormalExperimentRecorder
{
    private readonly List<byte[]> _records = [];
    private FormalExperimentEvidenceSeal? _seal;

    public bool IsSealed => _seal is not null;

    public void Append(string recordKind, ReadOnlySpan<byte> canonicalPayload)
    {
        if (IsSealed)
        {
            throw new InvalidOperationException("A sealed formal evidence recorder is immutable.");
        }

        FormalExperimentCanonical.RequireIdentity(recordKind, nameof(recordKind));
        if (canonicalPayload.IsEmpty)
        {
            throw new ArgumentException("Formal evidence payload cannot be empty.", nameof(canonicalPayload));
        }

        byte[] payload = canonicalPayload.ToArray();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-experiment-record.v1");
            writer.WriteNumber("sequence", _records.Count + 1);
            writer.WriteString("record_kind", recordKind);
            writer.WriteString("payload_hash", FormalExperimentCanonical.Hash(payload));
            writer.WriteBase64String("payload", payload);
            writer.WriteEndObject();
        }

        byte[] line = new byte[checked(stream.Length + 1)];
        stream.GetBuffer().AsSpan(0, checked((int)stream.Length)).CopyTo(line);
        line[^1] = (byte)'\n';
        _records.Add(line);
    }

    public FormalExperimentEvidenceSeal Seal()
    {
        if (_seal is not null)
        {
            return _seal;
        }

        int length = _records.Sum(GetLength);
        byte[] bytes = new byte[length];
        int offset = 0;
        foreach (byte[] record in _records)
        {
            record.CopyTo(bytes, offset);
            offset += record.Length;
        }

        _seal = new FormalExperimentEvidenceSeal(_records.Count, bytes);
        return _seal;
    }

    private static int GetLength(byte[] bytes)
    {
        return bytes.Length;
    }
}

internal static class FormalExperimentCanonical
{
    public static void RequireIdentity(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty identity is required.", name);
        }
    }

    public static string ValidateSha256(string value, string name)
    {
        return L2PlanningContextCanonicalJson.ValidateSha256(value, name);
    }

    public static string Hash(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
