using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alice.Cognition;

public sealed record FormalExperimentSuiteArtifactBinding
{
    public FormalExperimentSuiteArtifactBinding(string artifactId, string contentHash)
    {
        FormalExperimentCanonical.RequireIdentity(artifactId, nameof(artifactId));
        ArtifactId = artifactId;
        ContentHash = FormalExperimentCanonical.ValidateSha256(contentHash, nameof(contentHash));
    }

    public string ArtifactId { get; }
    public string ContentHash { get; }
}

public sealed class FormalExperimentSuitePairEntry
{
    private readonly ReadOnlyCollection<string> _conditionOrder;
    private readonly ReadOnlyCollection<FormalExperimentSuiteArtifactBinding> _artifacts;
    private readonly ReadOnlyCollection<string> _artifactIds;

    public FormalExperimentSuitePairEntry(
        string pairId,
        string fixtureId,
        string stratum,
        string? tierId,
        string? candidateSetId,
        string? summaryArtifactId,
        string? summaryArtifactVersion,
        string repeatId,
        string pairManifestHash,
        IEnumerable<string> conditionOrder,
        IEnumerable<FormalExperimentSuiteArtifactBinding> artifacts)
    {
        FormalExperimentCanonical.RequireIdentity(pairId, nameof(pairId));
        FormalExperimentCanonical.RequireIdentity(fixtureId, nameof(fixtureId));
        FormalExperimentCanonical.RequireIdentity(stratum, nameof(stratum));
        FormalExperimentCanonical.RequireIdentity(repeatId, nameof(repeatId));
        ArgumentNullException.ThrowIfNull(conditionOrder);
        ArgumentNullException.ThrowIfNull(artifacts);
        string[] order = conditionOrder.ToArray();
        if (order.Length != 2 || order.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A formal suite pair requires two named conditions.", nameof(conditionOrder));
        FormalExperimentSuiteArtifactBinding[] bindings = artifacts.ToArray();
        if (bindings.Length == 0 || bindings.Any(IsNullBinding))
            throw new ArgumentException("A formal suite pair requires artifact bindings.", nameof(artifacts));
        var artifactIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (FormalExperimentSuiteArtifactBinding binding in bindings)
        {
            if (!artifactIds.Add(binding.ArtifactId))
                throw new ArgumentException("Formal suite artifact identities must be unique.", nameof(artifacts));
        }

        PairId = pairId;
        FixtureId = fixtureId;
        Stratum = stratum;
        TierId = tierId;
        CandidateSetId = candidateSetId;
        SummaryArtifactId = summaryArtifactId;
        SummaryArtifactVersion = summaryArtifactVersion;
        RepeatId = repeatId;
        PairManifestHash = FormalExperimentCanonical.ValidateSha256(pairManifestHash, nameof(pairManifestHash));
        _conditionOrder = Array.AsReadOnly(order);
        FormalExperimentSuiteArtifactBinding[] orderedBindings = bindings
            .OrderBy(value => value.ArtifactId, StringComparer.Ordinal)
            .ToArray();
        _artifacts = Array.AsReadOnly(orderedBindings);
        _artifactIds = Array.AsReadOnly(orderedBindings.Select(value => value.ArtifactId).ToArray());
    }

    public string PairId { get; }
    public string FixtureId { get; }
    public string Stratum { get; }
    public string? TierId { get; }
    public string? CandidateSetId { get; }
    public string? SummaryArtifactId { get; }
    public string? SummaryArtifactVersion { get; }
    public string RepeatId { get; }
    public string PairManifestHash { get; }
    public IReadOnlyList<string> ConditionOrder => _conditionOrder;
    public IReadOnlyList<FormalExperimentSuiteArtifactBinding> Artifacts => _artifacts;
    public IReadOnlyList<string> ArtifactIds => _artifactIds;

    internal bool MatchesConditionOrder(IEnumerable<string> conditionOrder)
    {
        ArgumentNullException.ThrowIfNull(conditionOrder);
        return ConditionOrder.SequenceEqual(conditionOrder, StringComparer.Ordinal);
    }

    internal bool MatchesArtifactHashes(IReadOnlyDictionary<string, byte[]> actualArtifacts)
    {
        ArgumentNullException.ThrowIfNull(actualArtifacts);
        return Artifacts.All(binding =>
            actualArtifacts.TryGetValue(binding.ArtifactId, out byte[]? bytes)
            && bytes.Length > 0
            && StringComparer.Ordinal.Equals(
                binding.ContentHash,
                FormalExperimentCanonical.Hash(bytes)));
    }

    private static bool IsNullBinding(FormalExperimentSuiteArtifactBinding binding) => binding is null;
}

/// <summary>
/// Canonical, outcome-blind declaration of every formal fixture/pair and its exact pair-scoped artifacts.
/// It enforces the preregistered fixture and repeat coverage without resolving experimental outcomes.
/// </summary>
public sealed class FormalExperimentSuiteManifest
{
    public const string ProtocolVersion = "alice.formal-experiment-suite-manifest.v1";

    private const string Rq1FixtureStratum = "balanced_multi_domain";

    private static readonly string[] Rq2Strata =
    [
        "simple_current_state",
        "stale_state",
        "conflicting_reports",
        "commitment_lifecycle",
        "failed_plan_revision",
        "salient_distraction"
    ];

    private static readonly string[] Rq2Tiers = ["T1", "T2", "T3", "T4", "T5", "T6"];

    private static readonly string[] Rq1RepeatIds = ["repeat-01"];
    private static readonly string[] Rq2RepeatIds =
        ["repeat-01", "repeat-02", "repeat-03", "repeat-04", "repeat-05", "repeat-06", "repeat-07", "repeat-08"];

    private static readonly string[] Rq1PairArtifactIds =
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
        "rq1_outcome_evaluator"
    ];

    private static readonly string[] Rq2PairArtifactIds =
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
        "rq2_outcome_evaluator"
    ];

    private readonly ReadOnlyCollection<FormalExperimentSuitePairEntry> _pairs;
    private readonly byte[] _canonicalBytes;

    public FormalExperimentSuiteManifest(
        string suiteId,
        FormalExperimentRq rq,
        string preregistrationArtifactVersion,
        IEnumerable<FormalExperimentSuitePairEntry> pairs)
    {
        FormalExperimentCanonical.RequireIdentity(suiteId, nameof(suiteId));
        if (!Enum.IsDefined(rq)) throw new ArgumentOutOfRangeException(nameof(rq));
        FormalExperimentCanonical.RequireIdentity(
            preregistrationArtifactVersion,
            nameof(preregistrationArtifactVersion));
        ArgumentNullException.ThrowIfNull(pairs);
        FormalExperimentSuitePairEntry[] snapshot = pairs.ToArray();
        ValidatePairs(rq, snapshot);
        Array.Sort(snapshot, PairComparer.Instance);
        SuiteId = suiteId;
        Rq = rq;
        PreregistrationArtifactVersion = preregistrationArtifactVersion;
        _pairs = Array.AsReadOnly(snapshot);
        _canonicalBytes = Serialize();
        ManifestHash = FormalExperimentCanonical.Hash(_canonicalBytes);
    }

    public string SuiteId { get; }
    public FormalExperimentRq Rq { get; }
    public string PreregistrationArtifactVersion { get; }
    public IReadOnlyList<FormalExperimentSuitePairEntry> Pairs => _pairs;
    public string ManifestHash { get; }

    public byte[] GetCanonicalBytes() => _canonicalBytes.ToArray();

    public static FormalExperimentSuiteManifest Load(ReadOnlySpan<byte> canonicalBytes)
    {
        byte[] bytes = canonicalBytes.ToArray();
        ValidateNoDuplicateProperties(bytes);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        SuiteDocument document = JsonSerializer.Deserialize<SuiteDocument>(bytes, options)
            ?? throw new JsonException("Formal suite manifest root is required.");
        if (!StringComparer.Ordinal.Equals(document.SchemaVersion, ProtocolVersion))
            throw new JsonException("Formal suite manifest protocol is invalid.");
        FormalExperimentRq rq = document.Rq switch
        {
            "rq1" => FormalExperimentRq.Rq1,
            "rq2" => FormalExperimentRq.Rq2,
            _ => throw new JsonException("Formal suite RQ identity is invalid.")
        };
        FormalExperimentSuitePairEntry[] entries = document.Pairs.Select(CreateEntry).ToArray();
        var manifest = new FormalExperimentSuiteManifest(
            document.SuiteId,
            rq,
            document.PreregistrationArtifactVersion,
            entries);
        if (!bytes.AsSpan().SequenceEqual(manifest.GetCanonicalBytes()))
            throw new JsonException("Formal suite manifest bytes are not canonical.");
        return manifest;
    }

    internal FormalExperimentSuitePairEntry RequirePermitEntry(
        string pairManifestHash,
        IEnumerable<string> pairArtifactIds)
    {
        FormalExperimentCanonical.ValidateSha256(pairManifestHash, nameof(pairManifestHash));
        ArgumentNullException.ThrowIfNull(pairArtifactIds);
        FormalExperimentSuitePairEntry entry = Pairs.SingleOrDefault(
            value => StringComparer.Ordinal.Equals(value.PairManifestHash, pairManifestHash))
            ?? throw new InvalidDataException("The pair manifest is absent from the frozen formal suite.");
        if (!new HashSet<string>(entry.ArtifactIds, StringComparer.Ordinal).SetEquals(pairArtifactIds))
            throw new InvalidDataException("The pair artifact catalogue does not match its frozen suite entry.");
        return entry;
    }

    internal FormalExperimentSuitePairEntry RequirePermitEntry(
        string suitePairId,
        string pairManifestHash,
        IEnumerable<string> pairArtifactIds,
        IEnumerable<string> conditionOrder)
    {
        FormalExperimentCanonical.RequireIdentity(suitePairId, nameof(suitePairId));
        FormalExperimentSuitePairEntry entry = RequirePermitEntry(pairManifestHash, pairArtifactIds);
        if (!StringComparer.Ordinal.Equals(entry.PairId, suitePairId)
            || !entry.MatchesConditionOrder(conditionOrder))
            throw new InvalidDataException("The permit does not match its frozen suite pair identity/order.");
        return entry;
    }

    internal static string SuiteArtifactId(FormalExperimentRq rq) => rq switch
    {
        FormalExperimentRq.Rq1 => "rq1_suite_manifest",
        FormalExperimentRq.Rq2 => "rq2_suite_manifest",
        _ => throw new ArgumentOutOfRangeException(nameof(rq))
    };

    internal static IReadOnlyList<string> PairArtifactIds(FormalExperimentRq rq) => rq switch
    {
        FormalExperimentRq.Rq1 => Rq1PairArtifactIds,
        FormalExperimentRq.Rq2 => Rq2PairArtifactIds,
        _ => throw new ArgumentOutOfRangeException(nameof(rq))
    };

    internal static string PairManifestArtifactId(FormalExperimentRq rq) => rq switch
    {
        FormalExperimentRq.Rq1 => "rq1_pair_manifest",
        FormalExperimentRq.Rq2 => "rq2_pair_manifest",
        _ => throw new ArgumentOutOfRangeException(nameof(rq))
    };

    internal static string[] ConditionTokens(FormalExperimentRq rq) => rq switch
    {
        FormalExperimentRq.Rq1 => ["agent_centric", "event_centric"],
        FormalExperimentRq.Rq2 => ["verbatim", "summary"],
        _ => throw new ArgumentOutOfRangeException(nameof(rq))
    };

    private static FormalExperimentSuitePairEntry CreateEntry(SuitePairDocument pair)
    {
        return new FormalExperimentSuitePairEntry(
            pair.PairId,
            pair.FixtureId,
            pair.Stratum,
            pair.TierId,
            pair.CandidateSetId,
            pair.SummaryArtifactId,
            pair.SummaryArtifactVersion,
            pair.RepeatId,
            pair.PairManifestHash,
            pair.ConditionOrder,
            pair.Artifacts.Select(CreateBinding));
    }

    private static FormalExperimentSuiteArtifactBinding CreateBinding(SuiteArtifactDocument artifact)
    {
        return new FormalExperimentSuiteArtifactBinding(artifact.ArtifactId, artifact.ContentHash);
    }

    private static void ValidatePairs(FormalExperimentRq rq, FormalExperimentSuitePairEntry[] pairs)
    {
        if (pairs.Length == 0 || pairs.Any(IsNullPair))
            throw new ArgumentException("A formal suite requires at least one non-null pair.", nameof(pairs));
        if (pairs.Select(GetPairId).Distinct(StringComparer.Ordinal).Count() != pairs.Length
            || pairs.Select(GetPairManifestHash).Distinct(StringComparer.Ordinal).Count() != pairs.Length)
            throw new ArgumentException("Formal suite pair IDs and manifest hashes must be unique.", nameof(pairs));

        string[] expectedArtifacts = PairArtifactIds(rq).Order(StringComparer.Ordinal).ToArray();
        string[] expectedConditions = ConditionTokens(rq).Order(StringComparer.Ordinal).ToArray();
        foreach (FormalExperimentSuitePairEntry pair in pairs)
        {
            string[] actualArtifacts = pair.ArtifactIds.Order(StringComparer.Ordinal).ToArray();
            if (!actualArtifacts.SequenceEqual(expectedArtifacts, StringComparer.Ordinal))
                throw new ArgumentException("Formal suite pair artifact catalogue is incomplete.", nameof(pairs));
            if (!pair.ConditionOrder.Order(StringComparer.Ordinal).SequenceEqual(
                    expectedConditions,
                    StringComparer.Ordinal))
                throw new ArgumentException("Formal suite pair condition order is invalid.", nameof(pairs));
        }

        if (rq == FormalExperimentRq.Rq1) ValidateRq1Coverage(pairs);
        else ValidateRq2Coverage(pairs);
    }

    private static void ValidateRq1Coverage(FormalExperimentSuitePairEntry[] pairs)
    {
        if (pairs.Any(value => value.TierId is not null
                || value.CandidateSetId is not null
                || value.SummaryArtifactId is not null
                || value.SummaryArtifactVersion is not null
                || !StringComparer.Ordinal.Equals(value.Stratum, Rq1FixtureStratum)))
            throw new ArgumentException("RQ1 suite entries must use the balanced multi-domain fixture stratum and no tier.", nameof(pairs));

        IGrouping<string, FormalExperimentSuitePairEntry>[] fixtures = pairs
            .GroupBy(GetFixtureId, StringComparer.Ordinal)
            .ToArray();
        if (fixtures.Length != 30 || pairs.Length != 30)
            throw new ArgumentException("RQ1 suite requires thirty distinct 10-case block fixtures with one matched pair each.", nameof(pairs));
        foreach (IGrouping<string, FormalExperimentSuitePairEntry> fixture in fixtures)
            ValidateRepeats(fixture, Rq1RepeatIds, nameof(pairs));
    }

    private static void ValidateRq2Coverage(FormalExperimentSuitePairEntry[] pairs)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (string stratum in Rq2Strata)
        {
            foreach (string tier in Rq2Tiers) expected.Add(CellKey(stratum, tier));
        }

        var fixtureByCell = new Dictionary<string, string>(StringComparer.Ordinal);
        var candidateByFixture = new Dictionary<string, string>(StringComparer.Ordinal);
        var artifactByFixture = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (FormalExperimentSuitePairEntry pair in pairs)
        {
            if (pair.TierId is null || !Rq2Strata.Contains(pair.Stratum, StringComparer.Ordinal)
                || !Rq2Tiers.Contains(pair.TierId, StringComparer.Ordinal)
                || string.IsNullOrWhiteSpace(pair.CandidateSetId)
                || string.IsNullOrWhiteSpace(pair.SummaryArtifactId)
                || string.IsNullOrWhiteSpace(pair.SummaryArtifactVersion))
                throw new ArgumentException("RQ2 suite contains an invalid stratum/tier.", nameof(pairs));
            FormalExperimentCanonical.ValidateSha256(pair.CandidateSetId, nameof(pair.CandidateSetId));
            FormalExperimentCanonical.ValidateSha256(pair.SummaryArtifactId, nameof(pair.SummaryArtifactId));
            FormalExperimentCanonical.RequireIdentity(
                pair.SummaryArtifactVersion,
                nameof(pair.SummaryArtifactVersion));
            string key = CellKey(pair.Stratum, pair.TierId);
            if (fixtureByCell.TryGetValue(key, out string? fixtureId))
            {
                if (!StringComparer.Ordinal.Equals(fixtureId, pair.FixtureId))
                    throw new ArgumentException("RQ2 suite cell maps to multiple fixtures.", nameof(pairs));
            }
            else
            {
                fixtureByCell.Add(key, pair.FixtureId);
            }
            AddOrMatch(candidateByFixture, pair.FixtureId, pair.CandidateSetId!, "candidate set");
            AddOrMatch(
                artifactByFixture,
                pair.FixtureId,
                pair.SummaryArtifactId! + "\u001f" + pair.SummaryArtifactVersion!,
                "Summary artifact");
        }
        if (!expected.SetEquals(fixtureByCell.Keys)
            || fixtureByCell.Values.Distinct(StringComparer.Ordinal).Count() != expected.Count
            || candidateByFixture.Values.Distinct(StringComparer.Ordinal).Count() != expected.Count
            || artifactByFixture.Values.Distinct(StringComparer.Ordinal).Count() != expected.Count)
            throw new ArgumentException("RQ2 suite requires one unique fixture for every 6 x 6 cell.", nameof(pairs));

        IGrouping<string, FormalExperimentSuitePairEntry>[] fixtures = pairs
            .GroupBy(GetFixtureId, StringComparer.Ordinal)
            .ToArray();
        if (fixtures.Length != 36 || pairs.Length != 288)
            throw new ArgumentException("RQ2 suite requires 36 fixtures with eight repeats each.", nameof(pairs));
        foreach (IGrouping<string, FormalExperimentSuitePairEntry> fixture in fixtures)
            ValidateRepeats(fixture, Rq2RepeatIds, nameof(pairs));
    }

    private static void ValidateRepeats(
        IGrouping<string, FormalExperimentSuitePairEntry> fixture,
        IReadOnlyCollection<string> expectedRepeatIds,
        string parameterName)
    {
        if (fixture.Count() != expectedRepeatIds.Count
            || !new HashSet<string>(fixture.Select(GetRepeatId), StringComparer.Ordinal).SetEquals(expectedRepeatIds))
            throw new ArgumentException(
                $"Formal suite fixture {fixture.Key} does not contain its exact repeat set.",
                parameterName);
    }

    internal void ValidateRq2FrozenAssets(
        ReadOnlySpan<byte> fixtureBundleBytes,
        ReadOnlySpan<byte> summaryRegistryBytes)
    {
        if (Rq != FormalExperimentRq.Rq2)
            throw new InvalidOperationException("Only an RQ2 suite owns RQ2 frozen assets.");
        ValidateRq2FixtureBundle(fixtureBundleBytes);
        ValidateRq2SummaryRegistry(summaryRegistryBytes);
    }

    internal void ValidateRq1FrozenAssets(ReadOnlySpan<byte> fixtureBytes)
    {
        if (Rq != FormalExperimentRq.Rq1)
            throw new InvalidOperationException("Only an RQ1 suite owns RQ1 block fixtures.");
        using JsonDocument document = JsonDocument.Parse(fixtureBytes.ToArray());
        JsonElement root = document.RootElement;
        if (!StringComparer.Ordinal.Equals(
                RequiredString(root, "schema_version"),
                "alice.rq1-30-block-public-fixture.v1")
            || root.GetProperty("logical_l2_budget").GetInt32() != 4
            || !root.TryGetProperty("block_id", out JsonElement blockId)
            || string.IsNullOrWhiteSpace(blockId.GetString())
            || !root.TryGetProperty("fixture_id", out JsonElement fixtureId)
            || string.IsNullOrWhiteSpace(fixtureId.GetString())
            || !root.TryGetProperty("authority_setup", out JsonElement authoritySetup)
            || authoritySetup.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("cases", out JsonElement cases)
            || cases.ValueKind != JsonValueKind.Array
            || cases.GetArrayLength() != 10)
            throw new InvalidDataException("RQ1 frozen block fixture identity is invalid.");
        JsonElement[] caseRecords = cases.EnumerateArray().ToArray();
        if (caseRecords.Select(value => RequiredString(value, "pressure_id")).Distinct(StringComparer.Ordinal).Count() != 10
            || caseRecords.Select(value => RequiredString(value, "actor_id")).Distinct(StringComparer.Ordinal).Count() != 10
            || caseRecords.Any(value => !value.TryGetProperty("selection_inputs", out JsonElement selection)
                || selection.ValueKind != JsonValueKind.Object
                || !selection.TryGetProperty("actor_local_evidence", out JsonElement actorEvidence)
                || actorEvidence.ValueKind != JsonValueKind.Object
                || !actorEvidence.TryGetProperty("rank_band", out JsonElement rankBand)
                || rankBand.ValueKind != JsonValueKind.String
                || !selection.TryGetProperty("event_dependency", out JsonElement eventDependency)
                || eventDependency.ValueKind != JsonValueKind.Object
                || !eventDependency.TryGetProperty("source_kind", out JsonElement sourceKind)
                || sourceKind.ValueKind != JsonValueKind.String
                || !eventDependency.TryGetProperty("source_id", out JsonElement sourceId)
                || sourceId.ValueKind != JsonValueKind.String
                || !eventDependency.TryGetProperty("edge_kind", out JsonElement edgeKind)
                || edgeKind.ValueKind != JsonValueKind.String
                || !eventDependency.TryGetProperty("affected_node", out JsonElement affectedNode)
                || affectedNode.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty("actor_decision_view", out JsonElement view)
                || view.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty("action_catalogue", out JsonElement catalogue)
                || catalogue.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty("authority_execution_state", out JsonElement state)
                || state.ValueKind != JsonValueKind.Object))
            throw new InvalidDataException("RQ1 frozen block fixture cases are incomplete or not actor-distinct.");
    }

    private void ValidateRq2FixtureBundle(ReadOnlySpan<byte> bytes)
    {
        using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
        JsonElement root = document.RootElement;
        if (!StringComparer.Ordinal.Equals(RequiredString(root, "schema_version"), "alice.formal-frozen-fixture-bundle.v1")
            || !StringComparer.Ordinal.Equals(RequiredString(root, "rq"), "rq2")
            || !StringComparer.Ordinal.Equals(
                RequiredString(root, "preregistration_artifact_version"),
                PreregistrationArtifactVersion)
            || !root.TryGetProperty("fixture_records", out JsonElement records)
            || records.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("RQ2 frozen fixture bundle identity is invalid.");
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonElement record in records.EnumerateArray())
        {
            string fixtureId = RequiredString(record, "fixture_id");
            string stratum = RequiredString(record, "stratum");
            string tier = RequiredString(record, "tier_id");
            if (!actual.TryAdd(fixtureId, CellKey(stratum, tier)))
                throw new InvalidDataException("RQ2 frozen fixture IDs must be unique.");
        }
        Dictionary<string, string> expected = Pairs
            .GroupBy(GetFixtureId, StringComparer.Ordinal)
            .ToDictionary(
                GetFixtureGroupKey,
                GetFixtureGroupCell,
                StringComparer.Ordinal);
        if (!MapsEqual(expected, actual))
            throw new InvalidDataException("RQ2 frozen fixture bundle does not cover the exact suite matrix.");
    }

    private void ValidateRq2SummaryRegistry(ReadOnlySpan<byte> bytes)
    {
        using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
        JsonElement root = document.RootElement;
        if (!StringComparer.Ordinal.Equals(
                RequiredString(root, "protocol_version"),
                "frozen-summary-artifact-registry-v1")
            || !root.TryGetProperty("bindings", out JsonElement bindings)
            || bindings.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("RQ2 Summary registry identity is invalid.");
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonElement binding in bindings.EnumerateArray())
        {
            string candidateSetId = RequiredString(binding, "candidate_set_id");
            string artifact = RequiredString(binding, "artifact_id")
                + "\u001f"
                + RequiredString(binding, "artifact_version");
            if (!actual.TryAdd(candidateSetId, artifact))
                throw new InvalidDataException("RQ2 Summary registry candidate-set IDs must be unique.");
        }
        Dictionary<string, string> expected = Pairs
            .GroupBy(GetFixtureId, StringComparer.Ordinal)
            .ToDictionary(
                GetFixtureCandidateSetId,
                GetFixtureArtifactIdentity,
                StringComparer.Ordinal);
        if (!MapsEqual(expected, actual))
            throw new InvalidDataException("RQ2 Summary registry does not bind the exact 36 suite candidate sets.");
    }

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", ProtocolVersion);
            writer.WriteString("suite_id", SuiteId);
            writer.WriteString("rq", Rq == FormalExperimentRq.Rq1 ? "rq1" : "rq2");
            writer.WriteString("preregistration_artifact_version", PreregistrationArtifactVersion);
            writer.WritePropertyName("pairs");
            writer.WriteStartArray();
            foreach (FormalExperimentSuitePairEntry pair in Pairs)
            {
                writer.WriteStartObject();
                writer.WriteString("pair_id", pair.PairId);
                writer.WriteString("fixture_id", pair.FixtureId);
                writer.WriteString("stratum", pair.Stratum);
                writer.WriteString("tier_id", pair.TierId);
                writer.WriteString("candidate_set_id", pair.CandidateSetId);
                writer.WriteString("summary_artifact_id", pair.SummaryArtifactId);
                writer.WriteString("summary_artifact_version", pair.SummaryArtifactVersion);
                writer.WriteString("repeat_id", pair.RepeatId);
                writer.WriteString("pair_manifest_hash", pair.PairManifestHash);
                writer.WritePropertyName("condition_order");
                writer.WriteStartArray();
                foreach (string condition in pair.ConditionOrder) writer.WriteStringValue(condition);
                writer.WriteEndArray();
                writer.WritePropertyName("artifacts");
                writer.WriteStartArray();
                foreach (FormalExperimentSuiteArtifactBinding artifact in pair.Artifacts)
                {
                    writer.WriteStartObject();
                    writer.WriteString("artifact_id", artifact.ArtifactId);
                    writer.WriteString("content_hash", artifact.ContentHash);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void ValidateNoDuplicateProperties(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        var properties = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                properties.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.PropertyName
                     && (properties.Count == 0 || !properties.Peek().Add(reader.GetString()!)))
                throw new JsonException("Formal suite manifest contains a duplicate property.");
            else if (reader.TokenType == JsonTokenType.EndObject)
                properties.Pop();
        }
    }

    private static bool MapsEqual(
        IReadOnlyDictionary<string, string> first,
        IReadOnlyDictionary<string, string> second)
    {
        return first.Count == second.Count
            && first.All(value => second.TryGetValue(value.Key, out string? hash)
                && StringComparer.Ordinal.Equals(value.Value, hash));
    }

    private static string CellKey(string stratum, string tier) => stratum + "\u001f" + tier;
    private static void AddOrMatch(
        IDictionary<string, string> values,
        string key,
        string value,
        string name)
    {
        if (values.TryGetValue(key, out string? existing))
        {
            if (!StringComparer.Ordinal.Equals(existing, value))
                throw new ArgumentException($"RQ2 fixture {name} drifts across repeats.");
        }
        else
        {
            values.Add(key, value);
        }
    }

    private static string RequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Formal suite asset identity is missing: {propertyName}.");
        return value.GetString()!;
    }

    private static string GetFixtureGroupKey(IGrouping<string, FormalExperimentSuitePairEntry> value) => value.Key;
    private static string GetFixtureGroupCell(IGrouping<string, FormalExperimentSuitePairEntry> value)
    {
        FormalExperimentSuitePairEntry first = value.First();
        return CellKey(first.Stratum, first.TierId!);
    }

    private static string GetFixtureCandidateSetId(IGrouping<string, FormalExperimentSuitePairEntry> value) =>
        value.First().CandidateSetId!;

    private static string GetFixtureArtifactIdentity(IGrouping<string, FormalExperimentSuitePairEntry> value)
    {
        FormalExperimentSuitePairEntry first = value.First();
        return first.SummaryArtifactId! + "\u001f" + first.SummaryArtifactVersion!;
    }
    private static string GetPairId(FormalExperimentSuitePairEntry value) => value.PairId;
    private static string GetPairManifestHash(FormalExperimentSuitePairEntry value) => value.PairManifestHash;
    private static string GetFixtureId(FormalExperimentSuitePairEntry value) => value.FixtureId;
    private static string GetStratum(FormalExperimentSuitePairEntry value) => value.Stratum;
    private static string GetRepeatId(FormalExperimentSuitePairEntry value) => value.RepeatId;
    private static bool IsNullPair(FormalExperimentSuitePairEntry value) => value is null;

    private sealed class PairComparer : IComparer<FormalExperimentSuitePairEntry>
    {
        public static PairComparer Instance { get; } = new();
        public int Compare(FormalExperimentSuitePairEntry? left, FormalExperimentSuitePairEntry? right) =>
            StringComparer.Ordinal.Compare(left!.PairId, right!.PairId);
    }

    private sealed record SuiteDocument
    {
        [JsonRequired, JsonPropertyName("schema_version")]
        public string SchemaVersion { get; init; } = string.Empty;
        [JsonRequired, JsonPropertyName("suite_id")]
        public string SuiteId { get; init; } = string.Empty;
        [JsonRequired, JsonPropertyName("rq")]
        public string Rq { get; init; } = string.Empty;
        [JsonRequired, JsonPropertyName("preregistration_artifact_version")]
        public string PreregistrationArtifactVersion { get; init; } = string.Empty;
        [JsonRequired, JsonPropertyName("pairs")]
        public SuitePairDocument[] Pairs { get; init; } = [];
    }

    private sealed record SuitePairDocument
    {
        [JsonRequired, JsonPropertyName("pair_id")]
        public string PairId { get; init; } = string.Empty;
        [JsonRequired, JsonPropertyName("fixture_id")]
        public string FixtureId { get; init; } = string.Empty;
        [JsonRequired, JsonPropertyName("stratum")]
        public string Stratum { get; init; } = string.Empty;
        [JsonRequired, JsonPropertyName("tier_id")]
        public string? TierId { get; init; }
        [JsonRequired, JsonPropertyName("candidate_set_id")]
        public string? CandidateSetId { get; init; }
        [JsonRequired, JsonPropertyName("summary_artifact_id")]
        public string? SummaryArtifactId { get; init; }
        [JsonRequired, JsonPropertyName("summary_artifact_version")]
        public string? SummaryArtifactVersion { get; init; }
        [JsonRequired, JsonPropertyName("repeat_id")]
        public string RepeatId { get; init; } = string.Empty;
        [JsonRequired, JsonPropertyName("pair_manifest_hash")]
        public string PairManifestHash { get; init; } = string.Empty;
        [JsonRequired, JsonPropertyName("condition_order")]
        public string[] ConditionOrder { get; init; } = [];
        [JsonRequired, JsonPropertyName("artifacts")]
        public SuiteArtifactDocument[] Artifacts { get; init; } = [];
    }

    private sealed record SuiteArtifactDocument
    {
        [JsonRequired, JsonPropertyName("artifact_id")]
        public string ArtifactId { get; init; } = string.Empty;
        [JsonRequired, JsonPropertyName("content_hash")]
        public string ContentHash { get; init; } = string.Empty;
    }
}

public sealed record FormalExperimentSuiteCoverageObservation(
    string PairId,
    string PairManifestHash,
    string EvidenceArtifactHash);

public sealed class FormalExperimentSuiteCoverageReport
{
    private readonly ReadOnlyCollection<string> _blockers;
    private readonly ReadOnlyCollection<FormalExperimentSuiteCoverageObservation> _observations;
    private readonly byte[] _canonicalBytes;

    internal FormalExperimentSuiteCoverageReport(
        string suiteManifestHash,
        IEnumerable<string> blockers,
        IEnumerable<FormalExperimentSuiteCoverageObservation> observations)
    {
        SuiteManifestHash = FormalExperimentCanonical.ValidateSha256(
            suiteManifestHash,
            nameof(suiteManifestHash));
        _blockers = Array.AsReadOnly(blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        _observations = Array.AsReadOnly(observations
            .OrderBy(GetObservationPairId, StringComparer.Ordinal)
            .ThenBy(GetObservationEvidenceHash, StringComparer.Ordinal)
            .ToArray());
        _canonicalBytes = Serialize();
        CoverageHash = FormalExperimentCanonical.Hash(_canonicalBytes);
    }

    public string SuiteManifestHash { get; }
    public bool IsComplete => _blockers.Count == 0;
    public IReadOnlyList<string> Blockers => _blockers;
    public IReadOnlyList<FormalExperimentSuiteCoverageObservation> Observations => _observations;
    public string CoverageHash { get; }
    public byte[] GetCanonicalBytes() => _canonicalBytes.ToArray();

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-experiment-suite-coverage.v1");
            writer.WriteString("suite_manifest_hash", SuiteManifestHash);
            writer.WriteBoolean("complete", IsComplete);
            writer.WritePropertyName("blockers");
            writer.WriteStartArray();
            foreach (string blocker in Blockers) writer.WriteStringValue(blocker);
            writer.WriteEndArray();
            writer.WritePropertyName("observations");
            writer.WriteStartArray();
            foreach (FormalExperimentSuiteCoverageObservation observation in Observations)
            {
                writer.WriteStartObject();
                writer.WriteString("pair_id", observation.PairId);
                writer.WriteString("pair_manifest_hash", observation.PairManifestHash);
                writer.WriteString("evidence_artifact_hash", observation.EvidenceArtifactHash);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static string GetObservationPairId(FormalExperimentSuiteCoverageObservation value) => value.PairId;
    private static string GetObservationEvidenceHash(FormalExperimentSuiteCoverageObservation value) =>
        value.EvidenceArtifactHash;
}

/// <summary>Final fail-closed gate before any suite-level scoring or treatment comparison.</summary>
public static class FormalExperimentSuiteCoverageGate
{
    public static FormalExperimentSuiteCoverageReport Verify(
        FormalExperimentSuiteManifest suite,
        IEnumerable<FormalExperimentEvidenceSeal> evidenceSeals)
    {
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentNullException.ThrowIfNull(evidenceSeals);
        var blockers = new List<string>();
        var observations = new List<FormalExperimentSuiteCoverageObservation>();
        var evidenceHashes = new HashSet<string>(StringComparer.Ordinal);
        var observedPairIds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (FormalExperimentEvidenceSeal? seal in evidenceSeals)
        {
            if (seal is null) throw new ArgumentException("Suite evidence cannot contain null.", nameof(evidenceSeals));
            if (!evidenceHashes.Add(seal.ArtifactHash))
            {
                blockers.Add($"duplicate_evidence:{seal.ArtifactHash}");
                continue;
            }

            FormalExperimentEvidenceReplayReport replay;
            try
            {
                replay = FormalExperimentEvidenceReplayVerifier.Verify(seal);
            }
            catch (InvalidDataException)
            {
                blockers.Add($"invalid_evidence:{seal.ArtifactHash}");
                continue;
            }
            if (!replay.Records.Any(IsMatchedScore))
            {
                blockers.Add($"incomplete_evidence:{seal.ArtifactHash}");
                continue;
            }

            try
            {
                using JsonDocument permitDocument = JsonDocument.Parse(replay.Require("collection_permit").Payload);
                JsonElement permit = permitDocument.RootElement;
                string suiteHash = RequiredString(permit, "suite_manifest_hash");
                string pairId = RequiredString(permit, "suite_pair_id");
                string pairHash = RequiredString(permit, "pair_manifest_hash");
                if (!StringComparer.Ordinal.Equals(suiteHash, suite.ManifestHash))
                {
                    blockers.Add($"unexpected_suite:{seal.ArtifactHash}");
                    continue;
                }
                FormalExperimentSuitePairEntry? expected = suite.Pairs.SingleOrDefault(
                    value => StringComparer.Ordinal.Equals(value.PairId, pairId));
                if (expected is null || !StringComparer.Ordinal.Equals(expected.PairManifestHash, pairHash))
                {
                    blockers.Add($"unexpected_pair:{pairId}");
                    continue;
                }
                observedPairIds[pairId] = observedPairIds.GetValueOrDefault(pairId) + 1;
                observations.Add(new FormalExperimentSuiteCoverageObservation(pairId, pairHash, seal.ArtifactHash));
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException)
            {
                blockers.Add($"invalid_suite_binding:{seal.ArtifactHash}");
            }
        }

        foreach (FormalExperimentSuitePairEntry expected in suite.Pairs)
        {
            int count = observedPairIds.GetValueOrDefault(expected.PairId);
            if (count == 0) blockers.Add($"missing_pair:{expected.PairId}");
            else if (count > 1) blockers.Add($"duplicate_pair:{expected.PairId}");
        }
        return new FormalExperimentSuiteCoverageReport(suite.ManifestHash, blockers, observations);
    }

    private static bool IsMatchedScore(FormalExperimentEvidenceReplayRecord value) =>
        StringComparer.Ordinal.Equals(value.RecordKind, "matched_score");

    private static string RequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Formal suite evidence identity is missing: {propertyName}.");
        return value.GetString()!;
    }
}
