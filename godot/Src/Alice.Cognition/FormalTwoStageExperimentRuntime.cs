using Alice.Memory;

namespace Alice.Cognition;

public enum FormalConditionPairExecutionKind
{
    PreflightBlocked,
    PairEvidenceInvalid,
    AwaitingOfflineScoring
}

public sealed class FormalRq1ConditionPairExecutionArtifact
{
    private CanonicalFormalExperimentRecorder? _recorder;

    internal FormalRq1ConditionPairExecutionArtifact(
        FormalRq1MatchedPairManifest manifest,
        FormalExperimentCollectionPermit permit,
        FormalExperimentPreflightReport preflight,
        FormalRq1ConditionExecutionResult agentCentric,
        FormalRq1ConditionExecutionResult eventCentric,
        CanonicalFormalExperimentRecorder recorder)
    {
        Manifest = manifest;
        Permit = permit;
        Preflight = preflight;
        AgentCentric = agentCentric;
        EventCentric = eventCentric;
        _recorder = recorder;
    }

    public string PairManifestHash => Manifest.PairManifestHash;
    public string CollectionPermitHash => Permit.PermitHash;
    internal FormalRq1MatchedPairManifest Manifest { get; }
    internal FormalExperimentCollectionPermit Permit { get; }
    internal FormalExperimentPreflightReport Preflight { get; }
    internal FormalRq1ConditionExecutionResult AgentCentric { get; }
    internal FormalRq1ConditionExecutionResult EventCentric { get; }

    internal CanonicalFormalExperimentRecorder TakeRecorder()
    {
        CanonicalFormalExperimentRecorder recorder = _recorder
            ?? throw new InvalidOperationException("Formal RQ1 execution artifact was already scored.");
        _recorder = null;
        return recorder;
    }
}

public sealed record FormalRq1ConditionPairExecutionResult(
    FormalConditionPairExecutionKind Kind,
    FormalExperimentPreflightReport Preflight,
    FormalRq1ConditionPairExecutionArtifact? Artifact,
    FormalExperimentEvidenceSeal? TerminalEvidenceSeal);

/// <summary>Formal RQ1 execution stage. It has no hidden ledgers, mapping, cases, or expected outcomes.</summary>
public sealed class FormalRq1ConditionPairRunner
{
    public async ValueTask<FormalRq1ConditionPairExecutionResult> RunAsync(
        FormalRq1MatchedPairManifest manifest,
        byte[] canonicalPublicFixtureBytes,
        IReadOnlyList<FormalRq1Treatment> conditionOrder,
        FormalCollectionAuthorization authorization,
        FormalExperimentCollectionPermit collectionPermit,
        IFormalRq1ConditionExecutorFactory executorFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(canonicalPublicFixtureBytes);
        ArgumentNullException.ThrowIfNull(conditionOrder);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(collectionPermit);
        ArgumentNullException.ThrowIfNull(executorFactory);
        FormalRq1MatchedRunner.ValidateOrder(conditionOrder);
        string[] conditionTokens = conditionOrder.Select(Rq1ConditionToken).ToArray();

        FormalEvidenceArtifactBinding[] artifacts = PermitBindings(collectionPermit);
        FormalExperimentPreflightReport preflight = FormalExperimentPreflight.Evaluate(
            FormalExperimentRq.Rq1,
            true,
            manifest.AgentCentric.PreregistrationArtifactVersion,
            manifest.PairManifestHash,
            manifest.AgentCentric.RuntimeVersion,
            manifest.AgentCentric.ModelProfileId,
            authorization,
            [],
            artifacts,
            collectionPermit);
        if (!collectionPermit.MatchesConditionOrder(conditionTokens)
            || !PermitMatches(collectionPermit, "rq1_pair_manifest", manifest.GetCanonicalBytes())
            || !PermitMatches(collectionPermit, "rq1_public_fixture", canonicalPublicFixtureBytes))
            preflight = FormalRq1MatchedRunner.AddBlocker(
                preflight,
                "rq1_verified_public_execution_input_mismatch");

        var recorder = new CanonicalFormalExperimentRecorder();
        AppendPreflight(
            recorder,
            FormalExperimentRq.Rq1,
            manifest.AgentCentric.PreregistrationArtifactVersion,
            manifest.PairManifestHash,
            manifest.AgentCentric.RuntimeVersion,
            manifest.AgentCentric.ModelProfileId,
            authorization,
            collectionPermit,
            artifacts,
            preflight);
        if (!preflight.IsReady)
            return new FormalRq1ConditionPairExecutionResult(
                FormalConditionPairExecutionKind.PreflightBlocked,
                preflight,
                null,
                recorder.Seal());

        recorder.Append("rq1_pair_manifest", manifest.GetCanonicalBytes());
        recorder.Append("rq1_agent_centric_manifest", manifest.AgentCentric.GetCanonicalBytes());
        recorder.Append("rq1_event_centric_manifest", manifest.EventCentric.GetCanonicalBytes());
        recorder.Append("rq1_public_fixture", FormalExperimentEvidencePayloads.SerializeUnhashedBlob(
            "alice.formal-rq1-public-fixture-blob.v1",
            canonicalPublicFixtureBytes));
        var results = new Dictionary<FormalRq1Treatment, FormalRq1ConditionExecutionResult>();
        var runtimeIds = new HashSet<string>(StringComparer.Ordinal);
        var providerSessionIds = new HashSet<string>(StringComparer.Ordinal);
        bool publicExecutionEvidenceValid = true;
        foreach (FormalRq1Treatment treatment in conditionOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IFormalRq1ConditionExecutor executor = executorFactory.Create(treatment)
                ?? throw new InvalidOperationException("Formal RQ1 executor factory returned null.");
            FormalExperimentCanonical.RequireIdentity(executor.RuntimeInstanceId, nameof(executor.RuntimeInstanceId));
            FormalExperimentCanonical.RequireIdentity(executor.ProviderSessionId, nameof(executor.ProviderSessionId));
            if (!runtimeIds.Add(executor.RuntimeInstanceId)
                || !providerSessionIds.Add(executor.ProviderSessionId))
                throw new InvalidOperationException("Formal RQ1 conditions require fresh runtime and Provider sessions.");
            FormalRq1ConditionManifest conditionManifest = treatment == FormalRq1Treatment.AgentCentric
                ? manifest.AgentCentric
                : manifest.EventCentric;
            FormalRq1ConditionExecutionResult result = await executor.ExecuteAsync(
                new FormalRq1ConditionExecutionInput(conditionManifest, canonicalPublicFixtureBytes),
                cancellationToken).ConfigureAwait(false);
            if (result.Treatment != treatment)
                throw new InvalidOperationException("Formal RQ1 executor returned the wrong treatment.");
            publicExecutionEvidenceValid &= ValidatePublicExecutionEvidence(result);
            results.Add(treatment, result);
            recorder.Append(
                FormalRq1MatchedRunner.TreatmentRecordKind(treatment),
                FormalRq1MatchedRunner.SerializeConditionResult(result));
        }

        FormalRq1ConditionExecutionResult agent = results[FormalRq1Treatment.AgentCentric];
        FormalRq1ConditionExecutionResult eventCentric = results[FormalRq1Treatment.EventCentric];
        if (!publicExecutionEvidenceValid
            || !FormalRq1MatchedRunner.HasConsistentFormalModelEvidence(
                manifest,
                agent,
                eventCentric,
                true))
        {
            recorder.Append("pair_evidence_invalid", FormalRq1MatchedRunner.SerializePairEvidenceInvalid());
            return new FormalRq1ConditionPairExecutionResult(
                FormalConditionPairExecutionKind.PairEvidenceInvalid,
                preflight,
                null,
                recorder.Seal());
        }

        return new FormalRq1ConditionPairExecutionResult(
            FormalConditionPairExecutionKind.AwaitingOfflineScoring,
            preflight,
            new FormalRq1ConditionPairExecutionArtifact(
                manifest,
                collectionPermit,
                preflight,
                agent,
                eventCentric,
                recorder),
            null);
    }

    private static bool ValidatePublicExecutionEvidence(FormalRq1ConditionExecutionResult result)
    {
        var callIds = new HashSet<string>(result.ModelCalls.Select(value => value.CallId), StringComparer.Ordinal);
        if (callIds.Count == 0 || callIds.Count != result.ModelCalls.Count)
            return false;
        foreach (FormalRq1OpportunityRunEvidence evidence in result.OpportunityEvidence)
        {
            if (evidence.TerminalKind is null) continue;
            if (evidence.ModelCallId is null
                || !callIds.Contains(evidence.ModelCallId)
                || evidence.TerminalReceipt is null
                || !StringComparer.Ordinal.Equals(
                    evidence.TerminalReceipt.ModelCallId,
                    evidence.ModelCallId)
                || !StringComparer.Ordinal.Equals(
                    evidence.TerminalReceipt.NeedId,
                    evidence.NeedId?.Value))
                return false;
        }
        return true;
    }

    internal static FormalEvidenceArtifactBinding[] PermitBindings(FormalExperimentCollectionPermit permit) =>
        permit.ArtifactIds.Select(value => new FormalEvidenceArtifactBinding(value)).ToArray();

    internal static bool PermitMatches(
        FormalExperimentCollectionPermit permit,
        string artifactId,
        ReadOnlySpan<byte> bytes) =>
        permit.MatchesArtifactBytes(artifactId, bytes);

    private static string Rq1ConditionToken(FormalRq1Treatment treatment) => treatment switch
    {
        FormalRq1Treatment.AgentCentric => "agent_centric",
        FormalRq1Treatment.EventCentric => "event_centric",
        _ => throw new ArgumentOutOfRangeException(nameof(treatment))
    };

    internal static void AppendPreflight(
        CanonicalFormalExperimentRecorder recorder,
        FormalExperimentRq rq,
        string preregistrationArtifactVersion,
        string pairManifestHash,
        string runtimeVersion,
        string modelProfileId,
        FormalCollectionAuthorization authorization,
        FormalExperimentCollectionPermit permit,
        FormalEvidenceArtifactBinding[] artifacts,
        FormalExperimentPreflightReport preflight)
    {
        recorder.Append("collection_authorization", authorization.GetCanonicalBytes());
        recorder.Append("collection_permit", permit.GetCanonicalBytes());
        recorder.Append("frozen_artifact_bundle", permit.GetFrozenArtifactBundleCanonicalBytes());
        recorder.Append("preflight_inputs", FormalExperimentEvidencePayloads.SerializePreflightInputs(
            rq,
            true,
            preregistrationArtifactVersion,
            pairManifestHash,
            runtimeVersion,
            modelProfileId,
            [],
            artifacts));
        recorder.Append("preflight", preflight.GetCanonicalBytes());
    }
}

/// <summary>Formal RQ1 hidden stage. It receives no executor or Provider factory.</summary>
public static class FormalRq1OfflineScoringRuntime
{
    public static FormalRq1MatchedRunResult Score(
        FormalRq1ConditionPairExecutionArtifact execution,
        ActorOpportunityLedger opportunityLedger,
        Rq1TestCaseLedger testCaseLedger,
        FormalRq1OpportunityTestCaseMap mapping,
        IEnumerable<FormalRq1HiddenTestCase> hiddenTestCases)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(opportunityLedger);
        ArgumentNullException.ThrowIfNull(testCaseLedger);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(hiddenTestCases);
        FormalRq1HiddenTestCase[] hidden = hiddenTestCases.ToArray();
        byte[] hiddenBytes = FormalRq1MatchedRunner.SerializeHiddenTestCases(hidden);
        if (!FormalRq1ConditionPairRunner.PermitMatches(
                execution.Permit,
                "rq1_opportunity_ledger",
                opportunityLedger.GetCanonicalBytes())
            || !FormalRq1ConditionPairRunner.PermitMatches(
                execution.Permit,
                "rq1_test_case_ledger",
                testCaseLedger.GetCanonicalBytes())
            || !FormalRq1ConditionPairRunner.PermitMatches(
                execution.Permit,
                "rq1_opportunity_test_case_map",
                mapping.GetCanonicalBytes())
            || !FormalRq1ConditionPairRunner.PermitMatches(
                execution.Permit,
                "rq1_hidden_test_cases",
                hiddenBytes))
            throw new InvalidDataException("Formal RQ1 hidden scoring inputs do not match the verified freeze permit.");

        FormalRq1MatchedRunner.ValidateConditionResult(execution.AgentCentric, true, opportunityLedger);
        FormalRq1MatchedRunner.ValidateConditionResult(execution.EventCentric, true, opportunityLedger);
        FormalRq1EvaluatedConditionEvidence agent = FormalRq1OutcomeEvaluator.Evaluate(
            opportunityLedger,
            testCaseLedger,
            mapping,
            hidden,
            execution.AgentCentric.OpportunityEvidence);
        FormalRq1EvaluatedConditionEvidence eventCentric = FormalRq1OutcomeEvaluator.Evaluate(
            opportunityLedger,
            testCaseLedger,
            mapping,
            hidden,
            execution.EventCentric.OpportunityEvidence);
        FormalRq1MatchedPairScore score = FormalRq1OfflineScorer.ScoreMatchedPair(
            execution.Manifest,
            opportunityLedger,
            testCaseLedger,
            agent.ActivationEvidence,
            agent.TestCaseOutcomes,
            execution.AgentCentric.SessionOutcomes,
            eventCentric.ActivationEvidence,
            eventCentric.TestCaseOutcomes,
            execution.EventCentric.SessionOutcomes);
        CanonicalFormalExperimentRecorder recorder = execution.TakeRecorder();
        recorder.Append("rq1_opportunity_ledger", opportunityLedger.GetCanonicalBytes());
        recorder.Append("rq1_test_case_ledger", testCaseLedger.GetCanonicalBytes());
        recorder.Append("rq1_opportunity_test_case_map", mapping.GetCanonicalBytes());
        recorder.Append("rq1_hidden_test_cases", hiddenBytes);
        recorder.Append("matched_score", FormalRq1MatchedRunner.SerializeScore(score));
        FormalExperimentEvidenceSeal seal = recorder.Seal();
        _ = FormalExperimentEvidenceReplayVerifier.Verify(seal);
        return new FormalRq1MatchedRunResult(
            FormalRq1MatchedRunKind.Completed,
            execution.Preflight,
            score,
            seal);
    }
}

public sealed class FormalRq2ConditionPairExecutionArtifact
{
    private CanonicalFormalExperimentRecorder? _recorder;

    internal FormalRq2ConditionPairExecutionArtifact(
        FormalRq2PairCompositionResult composition,
        FormalRq2SummaryFidelityEvidence fidelity,
        FormalExperimentCollectionPermit permit,
        FormalExperimentPreflightReport preflight,
        FormalRq2ConditionTerminalEvidence verbatim,
        FormalRq2ConditionTerminalEvidence summary,
        CanonicalFormalExperimentRecorder recorder)
    {
        Composition = composition;
        Fidelity = fidelity;
        Permit = permit;
        Preflight = preflight;
        Verbatim = verbatim;
        Summary = summary;
        _recorder = recorder;
    }

    public string PairManifestHash => Composition.Manifest.PairManifestHash;
    public string CollectionPermitHash => Permit.PermitHash;
    internal FormalRq2PairCompositionResult Composition { get; }
    internal FormalRq2SummaryFidelityEvidence Fidelity { get; }
    internal FormalExperimentCollectionPermit Permit { get; }
    internal FormalExperimentPreflightReport Preflight { get; }
    internal FormalRq2ConditionTerminalEvidence Verbatim { get; }
    internal FormalRq2ConditionTerminalEvidence Summary { get; }

    internal CanonicalFormalExperimentRecorder TakeRecorder()
    {
        CanonicalFormalExperimentRecorder recorder = _recorder
            ?? throw new InvalidOperationException("Formal RQ2 execution artifact was already scored.");
        _recorder = null;
        return recorder;
    }
}

public sealed record FormalRq2ConditionPairExecutionResult(
    FormalConditionPairExecutionKind Kind,
    FormalExperimentPreflightReport Preflight,
    FormalRq2ConditionPairExecutionArtifact? Artifact,
    FormalExperimentEvidenceSeal? TerminalEvidenceSeal);

/// <summary>Formal RQ2 execution stage. Required-source sets and hidden predicates are absent.</summary>
public sealed class FormalRq2ConditionPairRunner
{
    public async ValueTask<FormalRq2ConditionPairExecutionResult> RunAsync(
        FormalRq2PairCompositionResult composition,
        FormalRq2SummaryFidelityEvidence summaryFidelityEvidence,
        IReadOnlyList<FormalRq2Treatment> conditionOrder,
        FormalCollectionAuthorization authorization,
        FormalExperimentCollectionPermit collectionPermit,
        IFormalRq2ConditionExecutorFactory executorFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(summaryFidelityEvidence);
        ArgumentNullException.ThrowIfNull(conditionOrder);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(collectionPermit);
        ArgumentNullException.ThrowIfNull(executorFactory);
        FormalRq2MatchedRunner.ValidateOrder(conditionOrder);
        string[] conditionTokens = conditionOrder.Select(Rq2ConditionToken).ToArray();
        FormalRq2MatchedPairManifest manifest = composition.Manifest;
        FormalEvidenceArtifactBinding[] artifacts = FormalRq1ConditionPairRunner.PermitBindings(collectionPermit);
        FormalExperimentPreflightReport preflight = FormalExperimentPreflight.Evaluate(
            FormalExperimentRq.Rq2,
            true,
            manifest.SharedConfiguration.PreregistrationArtifactVersion,
            manifest.PairManifestHash,
            manifest.SharedConfiguration.RuntimeVersion,
            manifest.SharedConfiguration.ModelProfileId,
            authorization,
            manifest.SharedConfiguration.GetFormalRequiredTbdFields(),
            artifacts,
            collectionPermit);
        bool validatorBound = collectionPermit.ArtifactIds.Contains(
            "rq2_summary_fidelity_validator",
            StringComparer.Ordinal);
        bool fixtureBundleBound = collectionPermit.ArtifactIds.Contains(
            "rq2_public_fixture_bundle",
            StringComparer.Ordinal);
        bool summaryRegistryBound = collectionPermit.ArtifactIds.Contains(
            "rq2_summary_registry",
            StringComparer.Ordinal);
        if (composition.RunPurpose != FormalRq2RunPurpose.FormalCollection
            || composition.Kind != FormalRq2PairCompositionKind.Succeeded
            || composition.Verbatim is null
            || composition.Summary is null
            || composition.SummaryBinding is null
            || composition.EmotionEvidence is null
            || composition.ScoringEvidence is null
            || !StringComparer.Ordinal.Equals(
                collectionPermit.CandidateSetId,
                composition.CandidateSetId?.Value)
            || !StringComparer.Ordinal.Equals(
                collectionPermit.SummaryArtifactId,
                composition.SummaryBinding?.ArtifactId.Value)
            || !collectionPermit.MatchesConditionOrder(conditionTokens)
            || !FormalRq1ConditionPairRunner.PermitMatches(
                collectionPermit,
                "rq2_pair_manifest",
                manifest.GetCanonicalBytes())
            || !FormalRq1ConditionPairRunner.PermitMatches(
                collectionPermit,
                "rq2_pre_treatment_emotion",
                composition.EmotionEvidence.GetCanonicalBytes())
            || !validatorBound
            || !fixtureBundleBound
            || !summaryRegistryBound)
            preflight = FormalRq2MatchedRunner.AddBlocker(
                preflight,
                "rq2_verified_public_execution_input_mismatch");

        var recorder = new CanonicalFormalExperimentRecorder();
        FormalRq1ConditionPairRunner.AppendPreflight(
            recorder,
            FormalExperimentRq.Rq2,
            manifest.SharedConfiguration.PreregistrationArtifactVersion,
            manifest.PairManifestHash,
            manifest.SharedConfiguration.RuntimeVersion,
            manifest.SharedConfiguration.ModelProfileId,
            authorization,
            collectionPermit,
            artifacts,
            preflight);
        if (!preflight.IsReady)
            return new FormalRq2ConditionPairExecutionResult(
                FormalConditionPairExecutionKind.PreflightBlocked,
                preflight,
                null,
                recorder.Seal());

        AppendPublicComposition(recorder, composition, summaryFidelityEvidence);
        var results = new Dictionary<FormalRq2Treatment, FormalRq2ConditionTerminalEvidence>();
        var runtimeIds = new HashSet<string>(StringComparer.Ordinal);
        var providerSessionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (FormalRq2Treatment treatment in conditionOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IFormalRq2ConditionExecutor executor = executorFactory.Create(treatment)
                ?? throw new InvalidOperationException("Formal RQ2 executor factory returned null.");
            FormalExperimentCanonical.RequireIdentity(executor.RuntimeInstanceId, nameof(executor.RuntimeInstanceId));
            FormalExperimentCanonical.RequireIdentity(executor.ProviderSessionId, nameof(executor.ProviderSessionId));
            if (!runtimeIds.Add(executor.RuntimeInstanceId)
                || !providerSessionIds.Add(executor.ProviderSessionId))
                throw new InvalidOperationException("Formal RQ2 conditions require fresh runtime and Provider sessions.");
            FormalRq2ConditionComposition branch = treatment == FormalRq2Treatment.Verbatim
                ? composition.Verbatim!
                : composition.Summary!;
            FormalRq2ConditionTerminalEvidence evidence = await executor.ExecuteAsync(
                new FormalRq2ConditionExecutionInput(branch),
                cancellationToken).ConfigureAwait(false);
            if (evidence.Treatment != treatment)
                throw new InvalidOperationException("Formal RQ2 executor returned the wrong treatment.");
            results.Add(treatment, evidence);
            recorder.Append(
                FormalRq2MatchedRunner.TreatmentRecordKind(treatment),
                FormalRq2MatchedRunner.SerializeTerminalEvidence(evidence));
        }

        FormalRq2ConditionTerminalEvidence verbatim = results[FormalRq2Treatment.Verbatim];
        FormalRq2ConditionTerminalEvidence summary = results[FormalRq2Treatment.Summary];
        bool complete = verbatim.ModelCall?.IsFormalPairingComplete == true
            && summary.ModelCall?.IsFormalPairingComplete == true;
        bool bindingsMatch = FormalRq2MatchedRunner.FormalRequestBindingsMatch(
            composition,
            manifest,
            verbatim,
            summary);
        if (!complete || !bindingsMatch)
        {
            recorder.Append("pair_evidence_invalid", FormalRq2MatchedRunner.SerializePairEvidenceInvalid(
                complete,
                bindingsMatch));
            return new FormalRq2ConditionPairExecutionResult(
                FormalConditionPairExecutionKind.PairEvidenceInvalid,
                preflight,
                null,
                recorder.Seal());
        }

        return new FormalRq2ConditionPairExecutionResult(
            FormalConditionPairExecutionKind.AwaitingOfflineScoring,
            preflight,
            new FormalRq2ConditionPairExecutionArtifact(
                composition,
                summaryFidelityEvidence,
                collectionPermit,
                preflight,
                verbatim,
                summary,
                recorder),
            null);
    }

    private static void AppendPublicComposition(
        CanonicalFormalExperimentRecorder recorder,
        FormalRq2PairCompositionResult composition,
        FormalRq2SummaryFidelityEvidence fidelity)
    {
        FormalRq2ConditionComposition verbatim = composition.Verbatim!;
        FormalRq2ConditionComposition summary = composition.Summary!;
        recorder.Append("rq2_pair_manifest", composition.Manifest.GetCanonicalBytes());
        recorder.Append("rq2_shared_configuration", composition.Manifest.SharedConfiguration.GetCanonicalBytes());
        recorder.Append("rq2_verbatim_manifest", composition.Manifest.Verbatim.GetCanonicalBytes());
        recorder.Append("rq2_summary_manifest", composition.Manifest.Summary.GetCanonicalBytes());
        recorder.Append("rq2_candidate_set", verbatim.Packet.CandidateSet.GetCanonicalBytes());
        recorder.Append("rq2_candidate_scoring", composition.ScoringEvidence!.GetCanonicalBytes());
        recorder.Append("rq2_pre_treatment_emotion", composition.EmotionEvidence!.GetCanonicalBytes());
        AppendBlob(recorder, "rq2_verbatim_packet", "alice.formal-rq2-verbatim-packet-blob.v1", verbatim.Packet.GetModelVisibleBytes());
        recorder.Append("rq2_verbatim_packing_trace", verbatim.Packet.PackingTrace.GetCanonicalBytes());
        AppendBlob(recorder, "rq2_summary_packet", "alice.formal-rq2-summary-packet-blob.v1", summary.Packet.GetModelVisibleBytes());
        AppendContextBlob(recorder, "rq2_verbatim_context", "alice.formal-rq2-verbatim-context-blob.v1", verbatim);
        AppendContextBlob(recorder, "rq2_summary_context", "alice.formal-rq2-summary-context-blob.v1", summary);
        recorder.Append("rq2_summary_fidelity", FormalRq2MatchedRunner.SerializeSummaryFidelity(fidelity));
    }

    private static string Rq2ConditionToken(FormalRq2Treatment treatment) => treatment switch
    {
        FormalRq2Treatment.Verbatim => "verbatim",
        FormalRq2Treatment.Summary => "summary",
        _ => throw new ArgumentOutOfRangeException(nameof(treatment))
    };

    private static void AppendContextBlob(
        CanonicalFormalExperimentRecorder recorder,
        string kind,
        string schema,
        FormalRq2ConditionComposition composition)
    {
        byte[] bytes = composition.Context.GetModelVisibleBytes();
        recorder.Append(kind, FormalExperimentEvidencePayloads.SerializeBoundContextBlob(
            schema,
            FormalExperimentCanonical.Hash(bytes),
            composition.Context.ActorId.Value,
            composition.Context.NeedId.Value,
            bytes));
    }

    private static void AppendBlob(
        CanonicalFormalExperimentRecorder recorder,
        string kind,
        string schema,
        byte[] bytes) =>
        recorder.Append(kind, FormalExperimentEvidencePayloads.SerializeCanonicalBlob(
            schema,
            FormalExperimentCanonical.Hash(bytes),
            bytes));
}

/// <summary>Formal RQ2 hidden stage. It receives no executor or Provider factory.</summary>
public static class FormalRq2OfflineScoringRuntime
{
    public static FormalRq2MatchedRunResult Score(
        FormalRq2ConditionPairExecutionArtifact execution,
        FormalRq2RequiredSourceSet requiredSources,
        FormalRq2HiddenOutcomePredicate hiddenPredicate)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(requiredSources);
        ArgumentNullException.ThrowIfNull(hiddenPredicate);
        byte[] requiredBytes = requiredSources.GetCanonicalBytes();
        byte[] hiddenBytes = FormalRq2MatchedRunner.SerializeHiddenPredicate(hiddenPredicate);
        if (!FormalRq1ConditionPairRunner.PermitMatches(
                execution.Permit,
                "rq2_required_source_sets",
                requiredBytes)
            || !FormalRq1ConditionPairRunner.PermitMatches(
                execution.Permit,
                "rq2_hidden_predicates",
                hiddenBytes)
            || execution.Composition.SummaryBinding is null
            || !execution.Fidelity.Matches(execution.Composition.SummaryBinding, requiredSources)
            || !execution.Fidelity.IsValid)
            throw new InvalidDataException("Formal RQ2 hidden scoring inputs do not match the verified freeze permit.");

        FormalRq2RequiredSourceGateResult sourceGate = FormalRq2RequiredSourceGate.Evaluate(
            requiredSources,
            execution.Composition.Verbatim!.Packet.CandidateSet);
        if (!sourceGate.IsComplete)
            throw new InvalidDataException("Formal RQ2 required-source gate is incomplete.");
        FormalRq2MatchedPairScore score = FormalRq2HiddenOutcomeScorer.ScoreMatchedPair(
            execution.Composition,
            requiredSources,
            sourceGate,
            hiddenPredicate,
            execution.Verbatim,
            execution.Summary,
            execution.Fidelity);
        CanonicalFormalExperimentRecorder recorder = execution.TakeRecorder();
        recorder.Append("rq2_required_sources", requiredBytes);
        recorder.Append("rq2_required_source_gate", FormalRq2MatchedRunner.SerializeRequiredSourceGate(sourceGate));
        recorder.Append("rq2_hidden_predicate", hiddenBytes);
        recorder.Append("matched_score", FormalRq2MatchedRunner.SerializeScore(score));
        FormalExperimentEvidenceSeal seal = recorder.Seal();
        _ = FormalExperimentEvidenceReplayVerifier.Verify(seal);
        return new FormalRq2MatchedRunResult(
            FormalRq2MatchedRunKind.Completed,
            execution.Preflight,
            score,
            seal);
    }
}
