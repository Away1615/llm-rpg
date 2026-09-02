using System.Collections.ObjectModel;
using System.Text.Json;
using Alice.Memory;
using Alice.ModelRuntime;

namespace Alice.Cognition;

public readonly record struct FormalRq2TestCaseId
{
    public FormalRq2TestCaseId(string value)
    {
        FormalExperimentCanonical.RequireIdentity(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

/// <summary>Fixture-owned required sources. It is a gate/scorer input and never enters model context.</summary>
public sealed class FormalRq2RequiredSourceSet
{
    private readonly ReadOnlyCollection<DecisionMemorySourceId> _sourceIds;
    private readonly byte[] _canonicalBytes;

    public FormalRq2RequiredSourceSet(IEnumerable<DecisionMemorySourceId> sourceIds)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        DecisionMemorySourceId[] snapshot = sourceIds.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(IsNullSource))
        {
            throw new ArgumentException("RQ2 required-source set cannot be empty.", nameof(sourceIds));
        }

        Array.Sort(snapshot, SourceComparer.Instance);
        for (int index = 1; index < snapshot.Length; index++)
        {
            if (snapshot[index - 1] == snapshot[index])
            {
                throw new ArgumentException("RQ2 required-source IDs must be unique.", nameof(sourceIds));
            }
        }

        _sourceIds = Array.AsReadOnly(snapshot);
        _canonicalBytes = Serialize();
        RequiredSourceSetHash = FormalExperimentCanonical.Hash(_canonicalBytes);
    }

    public IReadOnlyList<DecisionMemorySourceId> SourceIds => _sourceIds;
    public string RequiredSourceSetHash { get; }

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
            writer.WriteString("schema_version", "alice.formal-rq2-required-source-set.v1");
            writer.WritePropertyName("source_ids");
            writer.WriteStartArray();
            foreach (DecisionMemorySourceId sourceId in SourceIds)
            {
                writer.WriteStringValue(sourceId.Value);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static bool IsNullSource(DecisionMemorySourceId sourceId)
    {
        return sourceId is null;
    }

    private sealed class SourceComparer : IComparer<DecisionMemorySourceId>
    {
        public static SourceComparer Instance { get; } = new();

        public int Compare(DecisionMemorySourceId? left, DecisionMemorySourceId? right)
        {
            return StringComparer.Ordinal.Compare(left!.Value, right!.Value);
        }
    }
}

public sealed record FormalRq2RequiredSourceObservation(
    DecisionMemorySourceId SourceId,
    int? FirstCandidateRank);

public sealed class FormalRq2RequiredSourceGateResult
{
    private readonly ReadOnlyCollection<FormalRq2RequiredSourceObservation> _observations;

    internal FormalRq2RequiredSourceGateResult(
        IEnumerable<FormalRq2RequiredSourceObservation> observations)
    {
        _observations = Array.AsReadOnly(observations.ToArray());
    }

    public IReadOnlyList<FormalRq2RequiredSourceObservation> Observations => _observations;
    public bool IsComplete => Observations.All(HasCandidateRank);
    public IReadOnlyList<DecisionMemorySourceId> MissingSourceIds =>
        Observations.Where(IsMissing).Select(GetObservationSourceId).ToArray();

    private static bool HasCandidateRank(FormalRq2RequiredSourceObservation observation)
    {
        return observation.FirstCandidateRank is not null;
    }

    private static bool IsMissing(FormalRq2RequiredSourceObservation observation)
    {
        return observation.FirstCandidateRank is null;
    }

    private static DecisionMemorySourceId GetObservationSourceId(FormalRq2RequiredSourceObservation observation)
    {
        return observation.SourceId;
    }
}

public static class FormalRq2RequiredSourceGate
{
    public static FormalRq2RequiredSourceGateResult Evaluate(
        FormalRq2RequiredSourceSet requiredSources,
        DecisionMemoryCandidateSet candidateSet)
    {
        ArgumentNullException.ThrowIfNull(requiredSources);
        ArgumentNullException.ThrowIfNull(candidateSet);
        var firstRankBySource = new Dictionary<DecisionMemorySourceId, int>();
        for (int rank = 0; rank < candidateSet.RankedSlices.Count; rank++)
        {
            DecisionMemorySlice slice = candidateSet.RankedSlices[rank];
            foreach (DecisionMemorySourceId sourceId in slice.SourceIds)
            {
                firstRankBySource.TryAdd(sourceId, rank);
            }
        }

        var observations = new List<FormalRq2RequiredSourceObservation>(requiredSources.SourceIds.Count);
        foreach (DecisionMemorySourceId sourceId in requiredSources.SourceIds)
        {
            observations.Add(new FormalRq2RequiredSourceObservation(
                sourceId,
                firstRankBySource.TryGetValue(sourceId, out int rank) ? rank : null));
        }

        return new FormalRq2RequiredSourceGateResult(observations);
    }
}

public sealed record FormalRq2HiddenOutcomePredicate
{
    public FormalRq2HiddenOutcomePredicate(
        FormalRq2TestCaseId testCaseId,
        FormalRq2TerminalOutcomeKind expectedTerminalKind,
        string? expectedGameActionId = null)
    {
        if (expectedTerminalKind is not FormalRq2TerminalOutcomeKind.AuthorityCommitted
            and not FormalRq2TerminalOutcomeKind.JustifiedDefer)
            throw new ArgumentOutOfRangeException(nameof(expectedTerminalKind));
        if (expectedTerminalKind == FormalRq2TerminalOutcomeKind.AuthorityCommitted)
            FormalExperimentCanonical.RequireIdentity(
                expectedGameActionId ?? throw new ArgumentNullException(nameof(expectedGameActionId)),
                nameof(expectedGameActionId));
        else if (expectedGameActionId is not null)
            throw new ArgumentException("A justified defer has no GameActionId.", nameof(expectedGameActionId));

        TestCaseId = testCaseId;
        ExpectedTerminalKind = expectedTerminalKind;
        ExpectedGameActionId = expectedGameActionId;
    }

    public FormalRq2TestCaseId TestCaseId { get; }
    public FormalRq2TerminalOutcomeKind ExpectedTerminalKind { get; }
    public string? ExpectedGameActionId { get; }
}

public sealed record FormalRq2SummaryFidelityEvidence
{
    private FormalRq2SummaryFidelityEvidence(
        FrozenSummaryProfileVersion profileVersion,
        string registryId,
        FrozenSummaryArtifactId artifactId,
        string requiredSourceSetHash,
        string validatorVersion,
        bool isValid)
    {
        ArgumentNullException.ThrowIfNull(profileVersion);
        ArgumentNullException.ThrowIfNull(artifactId);
        ProfileVersion = profileVersion;
        FormalExperimentCanonical.RequireIdentity(registryId, nameof(registryId));
        RegistryId = registryId;
        ArtifactId = artifactId;
        RequiredSourceSetHash = FormalExperimentCanonical.ValidateSha256(
            requiredSourceSetHash,
            nameof(requiredSourceSetHash));
        FormalExperimentCanonical.RequireIdentity(validatorVersion, nameof(validatorVersion));
        ValidatorVersion = validatorVersion;
        IsValid = isValid;
    }

    public FrozenSummaryProfileVersion ProfileVersion { get; }
    public string RegistryId { get; }
    public FrozenSummaryArtifactId ArtifactId { get; }
    public string RequiredSourceSetHash { get; }
    public string ValidatorVersion { get; }
    public bool IsValid { get; }

    public bool Matches(
        FormalRq2SummaryBinding binding,
        FormalRq2RequiredSourceSet requiredSources)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(requiredSources);
        return ProfileVersion == binding.ProfileVersion
            && StringComparer.Ordinal.Equals(RegistryId, binding.RegistryId)
            && ArtifactId == binding.ArtifactId
            && StringComparer.Ordinal.Equals(
                RequiredSourceSetHash,
                requiredSources.RequiredSourceSetHash);
    }

    internal static FormalRq2SummaryFidelityEvidence Issued(
        FrozenSummaryArtifactRegistry registry,
        FrozenSummaryArtifact artifact,
        FormalRq2RequiredSourceSet requiredSources,
        string validatorVersion,
        bool isValid) => new(
            registry.ProfileVersion,
            registry.RegistryId,
            artifact.ArtifactId,
            requiredSources.RequiredSourceSetHash,
            validatorVersion,
            isValid);
}

/// <summary>
/// Deterministic structural Summary validator. It issues the receipt; callers cannot provide a success flag.
/// Validator implementation is identified by a declared version; no content fingerprint is recorded.
/// </summary>
public static class FormalRq2SummaryFidelityValidator
{
    public static FormalRq2SummaryFidelityEvidence Validate(
        FrozenSummaryArtifactRegistry registry,
        FormalRq2SummaryBinding binding,
        FormalRq2RequiredSourceSet requiredSources,
        string validatorVersion)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(requiredSources);
        FormalExperimentCanonical.RequireIdentity(validatorVersion, nameof(validatorVersion));

        FrozenSummaryArtifact? artifact = registry.Artifacts.SingleOrDefault(
            value => value.ArtifactId == binding.ArtifactId);
        if (artifact is null)
            throw new ArgumentException("Summary binding does not identify an artifact in the supplied registry.", nameof(binding));

        var citedSources = new HashSet<DecisionMemorySourceId>(
            artifact.Claims.SelectMany(GetClaimSources));
        bool valid = registry.ProfileVersion == binding.ProfileVersion
            && StringComparer.Ordinal.Equals(registry.RegistryId, binding.RegistryId)
            && artifact.ProfileVersion == binding.ProfileVersion
            && requiredSources.SourceIds.All(artifact.InputSourceIds.Contains)
            && requiredSources.SourceIds.All(citedSources.Contains);
        return FormalRq2SummaryFidelityEvidence.Issued(
            registry,
            artifact,
            requiredSources,
            validatorVersion,
            valid);
    }

    private static IEnumerable<DecisionMemorySourceId> GetClaimSources(FrozenSummaryClaim claim) =>
        claim.SourceIds;
}

public enum FormalRq2TerminalOutcomeKind
{
    InvalidDecision,
    TransportFailure,
    ValidatorRejected,
    JustifiedDefer,
    AuthorityCommitted
}

public sealed record FormalRq2ConditionTerminalEvidence
{
    public FormalRq2ConditionTerminalEvidence(
        FormalRq2Treatment treatment,
        FormalRq2TerminalOutcomeKind terminalKind,
        string? terminalEvidenceHash,
        FormalModelCallEvidence? modelCall,
        FormalTerminalOutcomeReceipt? terminalReceipt = null,
        string? gameActionId = null,
        int transportAttemptCount = 1)
    {
        if (!Enum.IsDefined(treatment)) throw new ArgumentOutOfRangeException(nameof(treatment));
        if (!Enum.IsDefined(terminalKind)) throw new ArgumentOutOfRangeException(nameof(terminalKind));
        if (terminalKind == FormalRq2TerminalOutcomeKind.TransportFailure)
        {
            if (modelCall is not null)
                throw new ArgumentException("A no-envelope transport failure cannot carry completed model-call evidence.", nameof(modelCall));
        }
        else ArgumentNullException.ThrowIfNull(modelCall);
        if (terminalKind is FormalRq2TerminalOutcomeKind.AuthorityCommitted
            or FormalRq2TerminalOutcomeKind.JustifiedDefer)
        {
            FormalExperimentCanonical.ValidateSha256(
                terminalEvidenceHash
                    ?? throw new ArgumentNullException(nameof(terminalEvidenceHash)),
                nameof(terminalEvidenceHash));
        }
        else if (terminalEvidenceHash is not null)
        {
            throw new ArgumentException(
                "Only committed or justified-defer terminals carry outcome evidence.",
                nameof(terminalEvidenceHash));
        }
        if (terminalKind == FormalRq2TerminalOutcomeKind.TransportFailure
            && terminalReceipt is null)
            throw new ArgumentNullException(
                nameof(terminalReceipt),
                "A transport failure requires its exact sanitized transport receipt.");
        if (terminalReceipt is not null
            && ((modelCall is not null
                    && !StringComparer.Ordinal.Equals(terminalReceipt.ModelCallId, modelCall.CallId))
                || !StringComparer.Ordinal.Equals(
                    terminalReceipt.TerminalEvidenceHash,
                    terminalEvidenceHash)))
            throw new ArgumentException("RQ2 terminal receipt does not bind its model call/outcome.", nameof(terminalReceipt));
        string? observedGameActionId = terminalReceipt?.GameActionId ?? gameActionId;
        if (terminalReceipt?.GameActionId is not null
            && gameActionId is not null
            && !StringComparer.Ordinal.Equals(terminalReceipt.GameActionId, gameActionId))
            throw new ArgumentException("Observed GameActionId does not match its terminal receipt.", nameof(gameActionId));
        if (terminalKind == FormalRq2TerminalOutcomeKind.AuthorityCommitted)
            FormalExperimentCanonical.RequireIdentity(
                observedGameActionId ?? throw new ArgumentNullException(nameof(gameActionId)),
                nameof(gameActionId));
        else if (observedGameActionId is not null)
            throw new ArgumentException("Only an Authority commit can carry a GameActionId.", nameof(gameActionId));
        if (transportAttemptCount is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(transportAttemptCount));

        Treatment = treatment;
        TerminalKind = terminalKind;
        TerminalEvidenceHash = terminalEvidenceHash;
        GameActionId = observedGameActionId;
        ModelCall = modelCall;
        TerminalReceipt = terminalReceipt;
        TransportAttemptCount = transportAttemptCount;
    }

    public FormalRq2Treatment Treatment { get; }
    public FormalRq2TerminalOutcomeKind TerminalKind { get; }
    public string? TerminalEvidenceHash { get; }
    public string? GameActionId { get; }
    public FormalModelCallEvidence? ModelCall { get; }
    public FormalTerminalOutcomeReceipt? TerminalReceipt { get; }
    public int TransportAttemptCount { get; }
}

public sealed record FormalRq2ConditionScore(
    FormalRq2Treatment Treatment,
    bool CandidateSetValid,
    bool RequiredSourceCoverage,
    bool FidelityValid,
    bool ReasoningDecisionValid,
    bool TerminalOutcomeValid,
    bool GroundedWorldActionSuccess);

public sealed record FormalRq2MatchedPairScore(
    FormalRq2TestCaseId TestCaseId,
    string RequiredSourceSetHash,
    FormalRq2ConditionScore Verbatim,
    FormalRq2ConditionScore Summary);

/// <summary>Offline hidden scorer over packet bytes and terminal Authority evidence.</summary>
public static class FormalRq2HiddenOutcomeScorer
{
    public static FormalRq2MatchedPairScore ScoreMatchedPair(
        FormalRq2PairCompositionResult composition,
        FormalRq2RequiredSourceSet requiredSources,
        FormalRq2RequiredSourceGateResult sourceGate,
        FormalRq2HiddenOutcomePredicate hiddenPredicate,
        FormalRq2ConditionTerminalEvidence verbatimEvidence,
        FormalRq2ConditionTerminalEvidence summaryEvidence,
        FormalRq2SummaryFidelityEvidence summaryFidelityEvidence)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(requiredSources);
        ArgumentNullException.ThrowIfNull(sourceGate);
        ArgumentNullException.ThrowIfNull(hiddenPredicate);
        ArgumentNullException.ThrowIfNull(verbatimEvidence);
        ArgumentNullException.ThrowIfNull(summaryEvidence);
        ArgumentNullException.ThrowIfNull(summaryFidelityEvidence);
        if (composition.Kind != FormalRq2PairCompositionKind.Succeeded
            || composition.Verbatim is null
            || composition.Summary is null)
        {
            throw new ArgumentException("RQ2 scoring requires one successful paired composition.", nameof(composition));
        }

        if (verbatimEvidence.Treatment != FormalRq2Treatment.Verbatim
            || summaryEvidence.Treatment != FormalRq2Treatment.Summary)
        {
            throw new ArgumentException("RQ2 evidence treatments do not form a matched pair.");
        }

        return new FormalRq2MatchedPairScore(
            hiddenPredicate.TestCaseId,
            requiredSources.RequiredSourceSetHash,
            ScoreCondition(
                composition.Verbatim,
                requiredSources,
                sourceGate,
                hiddenPredicate,
                verbatimEvidence,
                true),
            ScoreCondition(
                composition.Summary,
                requiredSources,
                sourceGate,
                hiddenPredicate,
                summaryEvidence,
                summaryFidelityEvidence.IsValid));
    }

    private static FormalRq2ConditionScore ScoreCondition(
        FormalRq2ConditionComposition composition,
        FormalRq2RequiredSourceSet requiredSources,
        FormalRq2RequiredSourceGateResult sourceGate,
        FormalRq2HiddenOutcomePredicate hiddenPredicate,
        FormalRq2ConditionTerminalEvidence evidence,
        bool fidelityValid)
    {
        HashSet<DecisionMemorySourceId> packetSources = ExtractPacketSourceIds(
            composition.Packet.GetModelVisibleBytes());
        bool coverage = requiredSources.SourceIds.All(packetSources.Contains);
        bool decisionValid = evidence.TerminalKind is FormalRq2TerminalOutcomeKind.AuthorityCommitted
            or FormalRq2TerminalOutcomeKind.JustifiedDefer;
        bool terminalValid = decisionValid
            && evidence.TerminalKind == hiddenPredicate.ExpectedTerminalKind
            && StringComparer.Ordinal.Equals(
                evidence.GameActionId,
                hiddenPredicate.ExpectedGameActionId);
        return new FormalRq2ConditionScore(
            evidence.Treatment,
            sourceGate.IsComplete,
            coverage,
            fidelityValid,
            decisionValid,
            terminalValid,
            terminalValid);
    }

    private static HashSet<DecisionMemorySourceId> ExtractPacketSourceIds(byte[] packetBytes)
    {
        using JsonDocument document = JsonDocument.Parse(packetBytes);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("memory", out JsonElement memory)
            || memory.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("RQ2 packet lacks a canonical memory array.", nameof(packetBytes));
        }

        var result = new HashSet<DecisionMemorySourceId>();
        foreach (JsonElement item in memory.EnumerateArray())
        {
            if (!item.TryGetProperty("source_ids", out JsonElement sourceIds)
                || sourceIds.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("RQ2 packet memory lacks source provenance.", nameof(packetBytes));
            }

            foreach (JsonElement sourceId in sourceIds.EnumerateArray())
            {
                if (sourceId.ValueKind != JsonValueKind.String)
                {
                    throw new ArgumentException("RQ2 packet source identity is malformed.", nameof(packetBytes));
                }

                result.Add(new DecisionMemorySourceId(sourceId.GetString()!));
            }
        }

        return result;
    }
}

public sealed class FormalRq2ConditionExecutionInput
{
    public FormalRq2ConditionExecutionInput(FormalRq2ConditionComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        Composition = composition;
    }

    public FormalRq2ConditionComposition Composition { get; }
    public FormalRq2Treatment Treatment => Composition.Manifest.Treatment;
}

public interface IFormalRq2ConditionExecutor
{
    string RuntimeInstanceId { get; }
    string ProviderSessionId { get; }
    ValueTask<FormalRq2ConditionTerminalEvidence> ExecuteAsync(
        FormalRq2ConditionExecutionInput input,
        CancellationToken cancellationToken);
}

public interface IFormalRq2ConditionExecutorFactory
{
    IFormalRq2ConditionExecutor Create(FormalRq2Treatment treatment);
}

public enum FormalRq2MatchedRunKind
{
    PreflightBlocked,
    PairEvidenceInvalid,
    Completed
}

public sealed record FormalRq2MatchedRunResult(
    FormalRq2MatchedRunKind Kind,
    FormalExperimentPreflightReport Preflight,
    FormalRq2MatchedPairScore? Score,
    FormalExperimentEvidenceSeal EvidenceSeal);

/// <summary>Sequential one-decision runner. Branch executors never receive required sources or hidden truth.</summary>
public sealed class FormalRq2MatchedRunner
{
    public async ValueTask<FormalRq2MatchedRunResult> RunAsync(
        FormalRq2PairCompositionResult composition,
        FormalRq2RequiredSourceSet requiredSources,
        FormalRq2HiddenOutcomePredicate hiddenPredicate,
        FormalRq2SummaryFidelityEvidence summaryFidelityEvidence,
        IReadOnlyList<FormalRq2Treatment> conditionOrder,
        FormalRq2RunPurpose runPurpose,
        FormalCollectionAuthorization authorization,
        IEnumerable<string> unresolvedInputIds,
        IEnumerable<FormalEvidenceArtifactBinding> requiredArtifacts,
        IFormalRq2ConditionExecutorFactory executorFactory,
        IFormalExperimentRecorder recorder,
        CancellationToken cancellationToken,
        FormalExperimentCollectionPermit? collectionPermit = null)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(requiredSources);
        ArgumentNullException.ThrowIfNull(hiddenPredicate);
        ArgumentNullException.ThrowIfNull(summaryFidelityEvidence);
        ArgumentNullException.ThrowIfNull(conditionOrder);
        ArgumentNullException.ThrowIfNull(executorFactory);
        ArgumentNullException.ThrowIfNull(recorder);
        if (!Enum.IsDefined(runPurpose)) throw new ArgumentOutOfRangeException(nameof(runPurpose));
        ValidateOrder(conditionOrder);
        bool formalCollection = runPurpose == FormalRq2RunPurpose.FormalCollection;
        FormalRq2MatchedPairManifest manifest = composition.Manifest;
        string[] unresolved = unresolvedInputIds.ToArray();
        FormalEvidenceArtifactBinding[] artifacts = requiredArtifacts.ToArray();
        FormalExperimentPreflightReport preflight = FormalExperimentPreflight.Evaluate(
            FormalExperimentRq.Rq2,
            formalCollection,
            manifest.SharedConfiguration.PreregistrationArtifactVersion,
            manifest.PairManifestHash,
            manifest.SharedConfiguration.RuntimeVersion,
            manifest.SharedConfiguration.ModelProfileId,
            authorization,
            unresolved,
            artifacts,
            collectionPermit);
        if (formalCollection)
            preflight = AddBlocker(preflight, "formal_two_stage_runner_required");
        if (composition.RunPurpose != runPurpose)
        {
            preflight = AddBlocker(preflight, "rq2_composition_run_purpose_mismatch");
        }
        if (composition.Kind != FormalRq2PairCompositionKind.Succeeded
            || composition.Verbatim is null
            || composition.Summary is null)
        {
            preflight = AddBlocker(preflight, "rq2_pair_composition_not_ready");
        }

        else if (composition.SummaryBinding is null
            || !summaryFidelityEvidence.Matches(composition.SummaryBinding, requiredSources)
            || !summaryFidelityEvidence.IsValid)
        {
            preflight = AddBlocker(preflight, "rq2_summary_fidelity_not_validated");
        }

        if (formalCollection
            && collectionPermit is not null
            && !collectionPermit.ArtifactIds.Contains(
                "rq2_summary_fidelity_validator",
                StringComparer.Ordinal))
        {
            preflight = AddBlocker(preflight, "rq2_summary_fidelity_validator_freeze_mismatch");
        }

        if (formalCollection)
        {
            foreach (string tbdField in manifest.SharedConfiguration.GetFormalRequiredTbdFields())
            {
                preflight = AddBlocker(preflight, $"unresolved:{tbdField}");
            }
        }

        FormalRq2RequiredSourceGateResult? sourceGate = null;
        if (composition.Verbatim is not null)
        {
            sourceGate = FormalRq2RequiredSourceGate.Evaluate(
                requiredSources,
                composition.Verbatim.Packet.CandidateSet);
            foreach (DecisionMemorySourceId missing in sourceGate.MissingSourceIds)
            {
                preflight = AddBlocker(preflight, $"rq2_required_source_missing:{missing.Value}");
            }
        }

        recorder.Append("collection_authorization", authorization.GetCanonicalBytes());
        if (collectionPermit is not null)
            recorder.Append("collection_permit", collectionPermit.GetCanonicalBytes());
        recorder.Append("preflight_inputs", FormalExperimentEvidencePayloads.SerializePreflightInputs(
            FormalExperimentRq.Rq2,
            formalCollection,
            manifest.SharedConfiguration.PreregistrationArtifactVersion,
            manifest.PairManifestHash,
            manifest.SharedConfiguration.RuntimeVersion,
            manifest.SharedConfiguration.ModelProfileId,
            unresolved,
            artifacts));
        recorder.Append("preflight", preflight.GetCanonicalBytes());
        if (composition.EmotionEvidence is not null)
            recorder.Append("rq2_pre_treatment_emotion", composition.EmotionEvidence.GetCanonicalBytes());
        if (!preflight.IsReady)
        {
            return new FormalRq2MatchedRunResult(
                FormalRq2MatchedRunKind.PreflightBlocked,
                preflight,
                null,
                recorder.Seal());
        }

        if (sourceGate is null)
        {
            throw new InvalidOperationException("A ready RQ2 run requires source-gate evidence.");
        }

        recorder.Append("rq2_pair_manifest", manifest.GetCanonicalBytes());
        recorder.Append("rq2_shared_configuration", manifest.SharedConfiguration.GetCanonicalBytes());
        recorder.Append("rq2_verbatim_manifest", manifest.Verbatim.GetCanonicalBytes());
        recorder.Append("rq2_summary_manifest", manifest.Summary.GetCanonicalBytes());
        DecisionMemoryCandidateSet candidateSet = composition.Verbatim!.Packet.CandidateSet;
        recorder.Append("rq2_candidate_set", candidateSet.GetCanonicalBytes());
        if (composition.ScoringEvidence is not null)
            recorder.Append("rq2_candidate_scoring", composition.ScoringEvidence.GetCanonicalBytes());
        recorder.Append("rq2_verbatim_packet", FormalExperimentEvidencePayloads.SerializeCanonicalBlob(
            "alice.formal-rq2-verbatim-packet-blob.v1",
            FormalExperimentCanonical.Hash(composition.Verbatim.Packet.GetModelVisibleBytes()),
            composition.Verbatim.Packet.GetModelVisibleBytes()));
        recorder.Append(
            "rq2_verbatim_packing_trace",
            composition.Verbatim.Packet.PackingTrace.GetCanonicalBytes());
        recorder.Append("rq2_summary_packet", FormalExperimentEvidencePayloads.SerializeCanonicalBlob(
            "alice.formal-rq2-summary-packet-blob.v1",
            FormalExperimentCanonical.Hash(composition.Summary!.Packet.GetModelVisibleBytes()),
            composition.Summary.Packet.GetModelVisibleBytes()));
        recorder.Append("rq2_verbatim_context", FormalExperimentEvidencePayloads.SerializeBoundContextBlob(
            "alice.formal-rq2-verbatim-context-blob.v1",
            FormalExperimentCanonical.Hash(composition.Verbatim.Context.GetModelVisibleBytes()),
            composition.Verbatim.Context.ActorId.Value,
            composition.Verbatim.Context.NeedId.Value,
            composition.Verbatim.Context.GetModelVisibleBytes()));
        recorder.Append("rq2_summary_context", FormalExperimentEvidencePayloads.SerializeBoundContextBlob(
            "alice.formal-rq2-summary-context-blob.v1",
            FormalExperimentCanonical.Hash(composition.Summary.Context.GetModelVisibleBytes()),
            composition.Summary.Context.ActorId.Value,
            composition.Summary.Context.NeedId.Value,
            composition.Summary.Context.GetModelVisibleBytes()));
        recorder.Append("rq2_required_sources", requiredSources.GetCanonicalBytes());
        recorder.Append("rq2_required_source_gate", SerializeRequiredSourceGate(sourceGate));
        recorder.Append("rq2_hidden_predicate", SerializeHiddenPredicate(hiddenPredicate));
        recorder.Append("rq2_summary_fidelity", SerializeSummaryFidelity(summaryFidelityEvidence));
        var results = new Dictionary<FormalRq2Treatment, FormalRq2ConditionTerminalEvidence>();
        var runtimeIds = new HashSet<string>(StringComparer.Ordinal);
        var providerSessionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (FormalRq2Treatment treatment in conditionOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IFormalRq2ConditionExecutor executor = executorFactory.Create(treatment)
                ?? throw new InvalidOperationException("RQ2 executor factory returned null.");
            FormalExperimentCanonical.RequireIdentity(executor.RuntimeInstanceId, nameof(executor.RuntimeInstanceId));
            FormalExperimentCanonical.RequireIdentity(executor.ProviderSessionId, nameof(executor.ProviderSessionId));
            if (!runtimeIds.Add(executor.RuntimeInstanceId)
                || !providerSessionIds.Add(executor.ProviderSessionId))
            {
                throw new InvalidOperationException("Matched RQ2 conditions must use fresh runtime and Provider sessions.");
            }

            FormalRq2ConditionComposition branch = treatment == FormalRq2Treatment.Verbatim
                ? composition.Verbatim!
                : composition.Summary!;
            FormalRq2ConditionTerminalEvidence evidence = await executor.ExecuteAsync(
                new FormalRq2ConditionExecutionInput(branch),
                cancellationToken).ConfigureAwait(false);
            if (evidence.Treatment != treatment)
            {
                throw new InvalidOperationException("RQ2 executor returned the wrong treatment identity.");
            }

            results.Add(treatment, evidence);
            recorder.Append(TreatmentRecordKind(treatment), SerializeTerminalEvidence(evidence));
        }

        FormalRq2ConditionTerminalEvidence verbatim = results[FormalRq2Treatment.Verbatim];
        FormalRq2ConditionTerminalEvidence summary = results[FormalRq2Treatment.Summary];
        bool completeFormalEvidence = !formalCollection
            || (verbatim.ModelCall?.IsFormalPairingComplete == true
                && summary.ModelCall?.IsFormalPairingComplete == true);
        bool requestBindingsMatch = !formalCollection || FormalRequestBindingsMatch(
            composition,
            manifest,
            verbatim,
            summary);
        if (!completeFormalEvidence || !requestBindingsMatch)
        {
            recorder.Append("pair_evidence_invalid", SerializePairEvidenceInvalid(
                completeFormalEvidence,
                requestBindingsMatch));
            return new FormalRq2MatchedRunResult(
                FormalRq2MatchedRunKind.PairEvidenceInvalid,
                preflight,
                null,
                recorder.Seal());
        }

        FormalRq2MatchedPairScore score = FormalRq2HiddenOutcomeScorer.ScoreMatchedPair(
            composition,
            requiredSources,
            sourceGate,
            hiddenPredicate,
            verbatim,
            summary,
            summaryFidelityEvidence);
        recorder.Append("matched_score", SerializeScore(score));
        return new FormalRq2MatchedRunResult(
            FormalRq2MatchedRunKind.Completed,
            preflight,
            score,
            recorder.Seal());
    }

    internal static FormalExperimentPreflightReport AddBlocker(
        FormalExperimentPreflightReport report,
        string blocker)
    {
        return new FormalExperimentPreflightReport(report.Blockers.Append(blocker));
    }

    internal static void ValidateOrder(IReadOnlyList<FormalRq2Treatment> order)
    {
        if (order.Count != 2
            || !order.Contains(FormalRq2Treatment.Verbatim)
            || !order.Contains(FormalRq2Treatment.Summary)
            || order[0] == order[1])
        {
            throw new ArgumentException("RQ2 order must contain both conditions exactly once.", nameof(order));
        }
    }

    internal static string TreatmentRecordKind(FormalRq2Treatment treatment)
    {
        return treatment == FormalRq2Treatment.Verbatim
            ? "rq2_verbatim_result"
            : "rq2_summary_result";
    }

    internal static byte[] SerializeSummaryFidelity(FormalRq2SummaryFidelityEvidence evidence)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-rq2-summary-fidelity.v1");
            writer.WriteString("profile_version", evidence.ProfileVersion.Value);
            writer.WriteString("registry_id", evidence.RegistryId);
            writer.WriteString("artifact_id", evidence.ArtifactId.Value);
            writer.WriteString("required_source_set_hash", evidence.RequiredSourceSetHash);
            writer.WriteString("validator_version", evidence.ValidatorVersion);
            writer.WriteBoolean("valid", evidence.IsValid);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    internal static byte[] SerializeRequiredSourceGate(FormalRq2RequiredSourceGateResult gate)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-rq2-required-source-gate.v1");
            writer.WriteBoolean("complete", gate.IsComplete);
            writer.WritePropertyName("observations");
            writer.WriteStartArray();
            foreach (FormalRq2RequiredSourceObservation observation in gate.Observations)
            {
                writer.WriteStartObject();
                writer.WriteString("source_id", observation.SourceId.Value);
                if (observation.FirstCandidateRank is int rank)
                    writer.WriteNumber("first_candidate_rank", rank);
                else writer.WriteNull("first_candidate_rank");
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    internal static byte[] SerializeHiddenPredicate(FormalRq2HiddenOutcomePredicate predicate)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-rq2-hidden-predicate.v1");
            writer.WriteString("test_case_id", predicate.TestCaseId.Value);
            writer.WriteString("expected_terminal_kind", predicate.ExpectedTerminalKind.ToString());
            writer.WriteString("expected_game_action_id", predicate.ExpectedGameActionId);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    internal static byte[] SerializeTerminalEvidence(FormalRq2ConditionTerminalEvidence evidence)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-rq2-condition-terminal-evidence.v1");
            writer.WriteString("treatment", TreatmentRecordKind(evidence.Treatment));
            writer.WriteString("terminal_kind", evidence.TerminalKind.ToString());
            writer.WriteString("terminal_evidence_hash", evidence.TerminalEvidenceHash);
            writer.WriteString("game_action_id", evidence.GameActionId);
            writer.WriteNumber("transport_attempt_count", evidence.TransportAttemptCount);
            writer.WriteString("model_call_evidence_hash", evidence.ModelCall?.EvidenceHash);
            if (evidence.ModelCall is null) writer.WriteNull("model_call_evidence");
            else writer.WriteBase64String("model_call_evidence", evidence.ModelCall.GetCanonicalBytes());
            writer.WriteString("terminal_receipt_hash", evidence.TerminalReceipt?.ReceiptHash);
            if (evidence.TerminalReceipt is null)
                writer.WriteNull("terminal_receipt");
            else writer.WriteBase64String(
                "terminal_receipt",
                evidence.TerminalReceipt.GetCanonicalBytes());
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    internal static byte[] SerializePairEvidenceInvalid(
        bool complete,
        bool requestBindingsMatch)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-rq2-pair-evidence-invalid.v1");
            writer.WriteBoolean("formal_metadata_complete", complete);
            writer.WriteBoolean("request_bindings_match", requestBindingsMatch);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    internal static byte[] SerializeScore(FormalRq2MatchedPairScore score)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-rq2-matched-score.v1");
            writer.WriteString("test_case_id", score.TestCaseId.Value);
            writer.WriteString("required_source_set_hash", score.RequiredSourceSetHash);
            WriteConditionScore(writer, "verbatim", score.Verbatim);
            WriteConditionScore(writer, "summary", score.Summary);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteConditionScore(
        Utf8JsonWriter writer,
        string propertyName,
        FormalRq2ConditionScore score)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        writer.WriteString("treatment", score.Treatment.ToString());
        writer.WriteBoolean("candidate_set_valid", score.CandidateSetValid);
        writer.WriteBoolean("required_source_coverage", score.RequiredSourceCoverage);
        writer.WriteBoolean("fidelity_valid", score.FidelityValid);
        writer.WriteBoolean("reasoning_decision_valid", score.ReasoningDecisionValid);
        writer.WriteBoolean("terminal_outcome_valid", score.TerminalOutcomeValid);
        writer.WriteBoolean("grounded_world_action_success", score.GroundedWorldActionSuccess);
        writer.WriteEndObject();
    }

    internal static bool FormalRequestBindingsMatch(
        FormalRq2PairCompositionResult composition,
        FormalRq2MatchedPairManifest manifest,
        FormalRq2ConditionTerminalEvidence verbatim,
        FormalRq2ConditionTerminalEvidence summary)
    {
        if (verbatim.ModelCall is null || summary.ModelCall is null) return false;
        FormalModelCallEvidence[] calls = [verbatim.ModelCall, summary.ModelCall];
        FormalRq2ConditionComposition[] branches = [composition.Verbatim!, composition.Summary!];
        for (int index = 0; index < calls.Length; index++)
        {
            FormalModelCallEvidence call = calls[index];
            FormalRq2ConditionComposition branch = branches[index];
            if (!StringComparer.Ordinal.Equals(call.ProviderProfileId, manifest.SharedConfiguration.ModelProfileId)
                || !StringComparer.Ordinal.Equals(call.RequestProtocolVersion, manifest.SharedConfiguration.RequestProtocolVersion)
                || !StringComparer.Ordinal.Equals(call.ActorId, branch.Context.ActorId.Value)
                || !StringComparer.Ordinal.Equals(call.NeedId, branch.Context.NeedId.Value)
                || !StringComparer.Ordinal.Equals(call.CandidateSetId, branch.Context.CandidateSetId.Value))
                return false;
            if (!FormalTerminalReceiptMatches(
                    index == 0 ? verbatim : summary,
                    branch,
                    call))
                return false;
        }
        return !StringComparer.Ordinal.Equals(verbatim.ModelCall.RequestBindingId, summary.ModelCall.RequestBindingId)
            && !StringComparer.Ordinal.Equals(verbatim.ModelCall.ProviderResponseId, summary.ModelCall.ProviderResponseId);
    }

    private static bool FormalTerminalReceiptMatches(
        FormalRq2ConditionTerminalEvidence evidence,
        FormalRq2ConditionComposition branch,
        FormalModelCallEvidence call)
    {
        if (evidence.TerminalReceipt is not FormalTerminalOutcomeReceipt receipt)
            return false;
        FormalTerminalOutcomeReceiptKind expected = evidence.TerminalKind switch
        {
            FormalRq2TerminalOutcomeKind.AuthorityCommitted => FormalTerminalOutcomeReceiptKind.AuthorityCommit,
            FormalRq2TerminalOutcomeKind.JustifiedDefer => FormalTerminalOutcomeReceiptKind.ValidatedDefer,
            FormalRq2TerminalOutcomeKind.TransportFailure => FormalTerminalOutcomeReceiptKind.TransportFailure,
            FormalRq2TerminalOutcomeKind.InvalidDecision or FormalRq2TerminalOutcomeKind.ValidatorRejected =>
                FormalTerminalOutcomeReceiptKind.ValidatorRejection,
            _ => throw new InvalidOperationException("A formal RQ2 terminal kind is required.")
        };
        return receipt.Kind == expected
            && StringComparer.Ordinal.Equals(receipt.ActorId, branch.Context.ActorId.Value)
            && StringComparer.Ordinal.Equals(receipt.NeedId, branch.Context.NeedId.Value)
            && StringComparer.Ordinal.Equals(receipt.ModelCallId, call.CallId)
            && StringComparer.Ordinal.Equals(receipt.TerminalEvidenceHash, evidence.TerminalEvidenceHash);
    }
}
