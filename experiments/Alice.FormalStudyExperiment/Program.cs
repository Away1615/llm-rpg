using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Alice.Activities;
using Alice.Actors;
using Alice.Authority;
using Alice.Capabilities;
using Alice.Cognition;
using Alice.Commitments;
using Alice.Damage;
using Alice.Execution;
using Alice.Interaction;
using Alice.Items;
using Alice.LivingTown;
using Alice.Memory;
using Alice.ModelRuntime;
using Alice.Navigation;
using Alice.Npc;
using Alice.Perception;
using Alice.ProductRuntime;
using Alice.Social;
using Alice.Validation;
using Alice.World;

bool validateInputs = args.Contains("--validate-inputs", StringComparer.Ordinal);
int writeFreezeIndex = Array.IndexOf(args, "--write-freeze-bundle");
int runRq1Index = Array.IndexOf(args, "--run-rq1");
int runRq2Index = Array.IndexOf(args, "--run-rq2");

try
{
    if (validateInputs)
    {
        FormalStudyAssetPreparation.ValidateRq1AssetBuilder();
        FormalStudyAssetPreparation.ValidateRq2FixtureDesign();
        Console.WriteLine("FORMAL_INPUT_VALIDATION=PASS");
        return 0;
    }

    if (writeFreezeIndex >= 0)
    {
        if (writeFreezeIndex + 1 >= args.Length)
            throw new ArgumentException("--write-freeze-bundle requires an output path.");
        FormalStudyAssetPreparation.WriteFreezeBundle(args[writeFreezeIndex + 1]);
        Console.WriteLine("FORMAL_FREEZE_BUNDLE=COMPLETE");
        return 0;
    }

    if (runRq1Index >= 0)
    {
        if (runRq1Index + 1 >= args.Length)
            throw new ArgumentException("--run-rq1 requires a freeze-bundle path.");
        int workerCount = ParseWorkerCount(args, "--worker-count") ?? 30;
        string? replayAttemptRoot = ParseOptionalPath(args, "--replay-attempt-root");
        IReadOnlyList<string>? credentialEnvironmentNames =
            ParseCredentialEnvironmentNames(args, "--credential-environment-names");
        if (credentialEnvironmentNames is not null && credentialEnvironmentNames.Count != workerCount)
            throw new ArgumentException(
                "--credential-environment-names must supply exactly --worker-count entries.");
        var concurrency = new Rq1ConcurrencyOptions(
            workerCount,
            credentialEnvironmentNames is null
                ? null
                : workerIndex => credentialEnvironmentNames[workerIndex]);
        await FormalStudyAssetPreparation.RunFormalRq1StudyAsync(
            args[runRq1Index + 1],
            concurrency,
            replayAttemptRoot,
            CancellationToken.None);
        Console.WriteLine("FORMAL_RQ1_STUDY=COMPLETE");
        return 0;
    }

    if (runRq2Index >= 0)
    {
        if (runRq2Index + 1 >= args.Length)
            throw new ArgumentException("--run-rq2 requires a freeze-bundle path.");
        await FormalStudyAssetPreparation.RunFormalRq2StudyAsync(
            args[runRq2Index + 1],
            CancellationToken.None);
        Console.WriteLine("FORMAL_RQ2_STUDY=COMPLETE");
        return 0;
    }

    Console.Error.WriteLine(
        "Usage: --validate-inputs | --write-freeze-bundle <path> | --run-rq1 <freeze> | --run-rq2 <freeze>");
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

static int? ParseWorkerCount(string[] args, string flag)
{
    int index = Array.IndexOf(args, flag);
    if (index < 0)
        return null;
    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out int value) || value <= 0)
        throw new ArgumentException(flag + " requires a positive integer.");
    return value;
}

static IReadOnlyList<string>? ParseCredentialEnvironmentNames(string[] args, string flag)
{
    int index = Array.IndexOf(args, flag);
    if (index < 0)
        return null;
    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        throw new ArgumentException(flag + " requires a comma-separated list.");
    return args[index + 1].Split(
        ',',
        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

static string? ParseOptionalPath(string[] args, string flag)
{
    int index = Array.IndexOf(args, flag);
    if (index < 0)
        return null;
    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        throw new ArgumentException(flag + " requires a path.");
    return Path.GetFullPath(args[index + 1]);
}

internal static class FormalStudyAssetPreparation
{
    private const string PreregistrationVersion = "alice-formal-preregistration-current";
    private const string RuntimeVersion = "alice-formal-runtime-current";
    private const string ModelProfileId = "formal-deepseek-v4-pro-current-timeout300";
    private const string Rq1ContextBuilderVersion = "l2-planless-strategic-context-v1";
    private const string SummaryProfile = "deepseek_v4_pro_summary_current";
    private const string SummaryArtifactVersion = "current";
    private const string Root = "godot/Data/FormalResearch/Frozen";
    private static readonly string[] Rq2Strata =
    [
        "simple_current_state",
        "stale_state",
        "conflicting_reports",
        "commitment_lifecycle",
        "failed_plan_revision",
        "salient_distraction"
    ];
    private const int Rq1BlockCount = 30;
    private const string Rq1CandidateRoot =
        "godot/Data/FormalResearch/Candidates/rq1-redesign/blocks";
    private const int Rq2RepeatsPerCell = 8;
    private static readonly string[] Rq2Tiers = ["T1", "T2", "T3", "T4", "T5", "T6"];

    private static string ComputeSha256(string value) =>
        ComputeSha256(Encoding.UTF8.GetBytes(value));

    private static string ComputeSha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static AnthropicMessagesRemotePlannerClient CreateProviderClient(
        string responseId,
        string profileId = ModelProfileId,
        IAnthropicMessagesProviderAttemptSink? attemptSink = null,
        string? credentialEnvironmentName = null)
    {
        _ = responseId;
        string credentialName = credentialEnvironmentName ?? "DEEPSEEK_API_KEY";
        var credential = new ProviderCredentialReference(credentialName);
        ProviderApiKey key = ProviderApiKey.LoadFromEnvironment(credential).ApiKey
            ?? throw new InvalidOperationException(
                $"Provider credential was not found in environment variable {credentialName}.");
        var profile = new AnthropicMessagesProviderProfile(
            new AnthropicMessagesProfileId(profileId),
            new Uri("https://api.deepseek.com/anthropic/v1/messages"),
            new AnthropicMessagesModelId("deepseek-v4-pro"),
            TimeSpan.FromSeconds(300),
            16_384,
            1_048_576,
            credential,
            true,
            AnthropicThinkingEffort.High);
        return new AnthropicMessagesRemotePlannerClient(
            new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan },
            profile,
            key,
            attemptSink);
    }

    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] preregistration = File.ReadAllBytes(
            "godot/Data/FormalResearch/Frozen/common/preregistration.md");
        byte[] sourceManifest = LoadFrozenSourceManifest();
        byte[] modelProfile = LoadFrozenModelProfile();
        PreparedRq1 rq1 = BuildRq1(preregistration, sourceManifest, modelProfile);
        Rq1ExperimentShape.ValidateThirtyDistinctBlockSuite(rq1.Suite.Pairs);
        PreparedRq2 rq2 = await BuildRq2Async(
            preregistration,
            sourceManifest,
            modelProfile,
            cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(Root);
        foreach (PreparedFile file in rq1.Files.Concat(rq2.Files))
            Write(file.RelativePath, file.Bytes);
        Write("rq1/rq1_suite_manifest.json", rq1.Suite.GetCanonicalBytes());
        Write("rq2/rq2_suite_manifest.json", rq2.Suite.GetCanonicalBytes());
        Write("formal_asset_index.json", BuildIndex(rq1, rq2));
        Console.WriteLine($"FORMAL_RQ1_SUITE_HASH={rq1.Suite.ManifestHash}");
        Console.WriteLine($"FORMAL_RQ2_SUITE_HASH={rq2.Suite.ManifestHash}");
        Console.WriteLine($"FORMAL_SUMMARY_CALLS={rq2.Cells.Count}");
    }

    public static void ValidateRq1AssetBuilder()
    {
        byte[] preregistration = File.ReadAllBytes(
            "godot/Data/FormalResearch/Frozen/common/preregistration.md");
        PreparedRq1 rq1 = BuildRq1(
            preregistration,
            LoadFrozenSourceManifest(),
            LoadFrozenModelProfile());
        Rq1ExperimentShape.ValidateThirtyDistinctBlockSuite(rq1.Suite.Pairs);
        if (rq1.Scenarios.Count != Rq1BlockCount * 10
            || rq1.Pairs.Any(pair => pair.Rq1Scenarios?.Count != 10
                || pair.Rq1PressureStates?.Count != 10
                || pair.Rq1ScoringInputs?.OpportunityLedger.Entries.Count != 10
                || pair.Rq1ScoringInputs.TestCaseLedger.Entries.Count != 10
                || pair.Rq1ScoringInputs.Mapping.Entries.Count != 10
                || pair.Rq1ScoringInputs.HiddenTestCases.Count != 10))
            throw new InvalidDataException("RQ1 asset builder did not produce 30 pair-scoped 10-case blocks.");
        Console.WriteLine("FORMAL_RQ1_TERMINAL_FIXTURE_PREFLIGHT=PASS committed=150 deferred=150");
        Console.WriteLine("FORMAL_RQ1_AUTHORITY_ACTION_PREFLIGHT=PASS committed=150");
        Console.WriteLine("FORMAL_RQ1_ADMISSION_PREFLIGHT=PASS blocks=30 conditions=60");
    }

    public static async Task RunPromptDevelopmentCheckAsync(CancellationToken cancellationToken)
    {
        var cells = new List<PreparedRq2Cell>();
        int index = 0;
        foreach (string stratum in Rq2Strata)
        {
            cells.Add(CreateRq2Cell(index++, stratum, "T5", "prompt-development"));
            cells.Add(CreateRq2Cell(index++, stratum, "T6", "prompt-development"));
        }

        var summaryArtifacts = new List<FrozenSummaryArtifact>();
        foreach (PreparedRq2Cell cell in cells)
        {
            DecisionMemorySlice source = cell.CandidateSet.RankedSlices.First(value =>
                value.EvidenceStatus == DecisionMemoryEvidenceStatus.Current);
            summaryArtifacts.Add(FrozenSummaryArtifact.Create(
                cell.CandidateSet,
                new FrozenSummaryProfileVersion("prompt_development_summary_current"),
                new FrozenSummaryArtifactVersion("prompt_development_current"),
                [new FrozenSummaryClaim(
                    0,
                    source.GetCanonicalSourceBytes(),
                    source.EvidenceStatus,
                    source.SourceIds,
                    source.SupersedesSourceIds,
                    source.ConflictsWithSourceIds)]));
        }
        var registry = new FrozenSummaryArtifactRegistry(
            new FrozenSummaryProfileVersion("prompt_development_summary_current"),
            summaryArtifacts,
            "prompt-development-summary-registry-current");
        var observations = new List<PromptDevelopmentObservation>();
        foreach (PreparedRq2Cell cell in cells)
        {
            var tasks = new List<Task<PromptDevelopmentObservation>>(4);
            for (int repeat = 1; repeat <= 2; repeat++)
            {
                FormalRq2MatchedPairManifest manifest = CreateRq2PairManifest(registry, cell, repeat);
                FormalRq2PlanningFixture planning = CreateRq2PlanningFixture(cell);
                DecisionNeed need = CreateInFlightNeed(planning.View, planning.Plan);
                FormalRq2CandidateSelectionProvenance provenance = CreateRq2Provenance();
                FormalRq2CandidateScoringResult scoring = FormalRq2CandidateScorer.Score(
                    [],
                    new SimTime(10_000),
                    40,
                    provenance.ScorerConfiguration,
                    cell.CandidateSet.RankedSlices.Select(value =>
                        new FormalRq2CandidateScoreInput(value, [], 5)));
                FormalRq2PairCompositionResult composition = new FormalRq2PairCompositionRuntime(
                    manifest,
                    FormalRq2RunPurpose.EngineeringEvidence,
                    registry,
                    new FormalRq2PairCompositionDependencies(
                        new FormalApproximateTokenCounter(),
                        FormalRq2PlanningContextRenderer.Instance)).ComposePlanningPair(
                            FormalRq2CandidateEvidence.Available(
                                scoring,
                                provenance,
                                FormalRq2PreTreatmentEmotionEvidence.CreateNoEmotion(cell.CandidateSet)),
                            need,
                            planning.View,
                            planning.Plan);
                if (composition.Kind != FormalRq2PairCompositionKind.Succeeded)
                    throw new InvalidDataException($"Prompt-development composition failed: {cell.FixtureId}/{composition.Kind}.");
                tasks.Add(InvokePromptDevelopmentConditionAsync(
                    cell,
                    repeat,
                    FormalRq2Treatment.Verbatim,
                    composition.Verbatim!,
                    cancellationToken));
                tasks.Add(InvokePromptDevelopmentConditionAsync(
                    cell,
                    repeat,
                    FormalRq2Treatment.Summary,
                    composition.Summary!,
                    cancellationToken));
            }
            observations.AddRange(await Task.WhenAll(tasks).ConfigureAwait(false));
            Console.WriteLine($"FORMAL_PROMPT_PROGRESS={observations.Count}/48 fixture={cell.FixtureId}");
        }

        string outputRoot = Path.Combine("godot", "Artifacts", "PromptDevelopment");
        Directory.CreateDirectory(outputRoot);
        string outputPath = Path.Combine(
            outputRoot,
            $"prompt-development-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}.json");
        File.WriteAllBytes(
            outputPath,
            JsonSerializer.SerializeToUtf8Bytes(
                new PromptDevelopmentReport(
                    observations.Count,
                    observations.Count(value => value.Valid),
                    observations),
                new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("FORMAL_PROMPT_ARTIFACT=" + Path.GetFullPath(outputPath));
        int invalid = observations.Count(value => !value.Valid);
        if (invalid > 0)
            throw new InvalidDataException($"Prompt-development check produced {invalid} invalid structured decisions.");
    }

    private static async Task<PromptDevelopmentObservation> InvokePromptDevelopmentConditionAsync(
        PreparedRq2Cell cell,
        int repeat,
        FormalRq2Treatment treatment,
        FormalRq2ConditionComposition composition,
        CancellationToken cancellationToken)
    {
        AnthropicMessagesRemotePlannerClient client = CreateProviderClient(
            $"prompt-development-{cell.FixtureId}-{repeat:D2}-{treatment}",
            profileId: ModelProfileId);
        var request = RemotePlannerRequest.Create(
            new RemotePlannerRequestId($"prompt-development-{cell.FixtureId}-{repeat:D2}-{treatment}"),
            composition.Context);
        ModelClientResult<RemotePlannerResponse> result = await client.InvokeAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        RemotePlannerDecision? decision = result.Output?.Decision;
        bool valid = result.Status == ModelClientResultStatus.Produced
            && decision is not null
            && decision is not RemotePlannerFailure;
        long duration = result.ExecutionEvidence is AnthropicMessagesRemotePlannerExecutionEvidence evidence
            ? evidence.DurationMilliseconds
            : 0;
        return new PromptDevelopmentObservation(
            cell.FixtureId,
            repeat,
            treatment.ToString(),
            valid,
            decision?.GetType().Name ?? result.Status.ToString(),
            duration);
    }

    public static void WriteFreezeBundle(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Formal freeze output path is required.", nameof(outputPath));
        string revision = RunGit("rev-parse", "HEAD").Trim();
        if (RunGit("status", "--porcelain=v1", "--untracked-files=all").Length != 0)
            throw new InvalidOperationException("Formal freeze requires a clean Git checkout.");
        FormalExperimentSuiteManifest rq1 = FormalExperimentSuiteManifest.Load(
            File.ReadAllBytes(Path.Combine(Root, "rq1", "rq1_suite_manifest.json")));
        FormalExperimentSuiteManifest rq2 = FormalExperimentSuiteManifest.Load(
            File.ReadAllBytes(Path.Combine(Root, "rq2", "rq2_suite_manifest.json")));
        byte[] bytes = SerializeFreezeBundle(revision, rq1, rq2);
        string fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
        Console.WriteLine("FORMAL_FREEZE_PATH=" + fullPath);
        Console.WriteLine("FORMAL_FREEZE_HASH=" + ComputeSha256(bytes));
    }

    public static async Task RunFormalRq1StudyAsync(
        string freezeBundlePath,
        Rq1ConcurrencyOptions rq1Concurrency,
        string? replayAttemptRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rq1Concurrency);
        byte[] preregistration = File.ReadAllBytes(
            "godot/Data/FormalResearch/Frozen/common/preregistration.md");
        PreparedRq1 rq1 = BuildRq1(
            preregistration,
            LoadFrozenSourceManifest(),
            LoadFrozenModelProfile());
        Rq1ExperimentShape.ValidateThirtyDistinctBlockSuite(rq1.Suite.Pairs);
        byte[] freeze = File.ReadAllBytes(Path.GetFullPath(freezeBundlePath));
        byte[] readiness = File.ReadAllBytes("godot/Config/formal_readiness_v1.json");
        string runRoot = Path.Combine(
            "godot",
            "Artifacts",
            "FormalStudy",
            ComputeSha256(freeze)[..16]);
        Directory.CreateDirectory(runRoot);

        IReadOnlyList<FormalExperimentEvidenceSeal> rq1Seals = await RunRq1SuiteAsync(
            rq1,
            freeze,
            readiness,
            runRoot,
            rq1Concurrency,
            replayAttemptRoot,
            cancellationToken).ConfigureAwait(false);
        WriteCoverage(rq1.Suite, rq1Seals, Path.Combine(runRoot, "rq1-coverage.json"));
        FormalProviderTokenReport.WriteRq1(
            Path.Combine(runRoot, "rq1"),
            rq1.Pairs.Select(pair => pair.PairId).ToArray(),
            rq1.Scenarios.Select(scenario => scenario.PressureId).ToArray());
        Console.WriteLine("FORMAL_RQ1_STUDY_ARTIFACT_ROOT=" + Path.GetFullPath(runRoot));
    }

    public static async Task RunFormalRq2StudyAsync(
        string freezeBundlePath,
        CancellationToken cancellationToken)
    {
        byte[] preregistration = File.ReadAllBytes(
            "godot/Data/FormalResearch/Frozen/common/preregistration.md");
        byte[] sourceManifest = LoadFrozenSourceManifest();
        byte[] modelProfile = LoadFrozenModelProfile();
        PreparedRq2 rq2 = await BuildRq2Async(
            preregistration,
            sourceManifest,
            modelProfile,
            cancellationToken).ConfigureAwait(false);
        byte[] freeze = File.ReadAllBytes(Path.GetFullPath(freezeBundlePath));
        byte[] readiness = File.ReadAllBytes("godot/Config/formal_readiness_v1.json");
        string runRoot = Path.Combine(
            "godot",
            "Artifacts",
            "FormalStudy",
            ComputeSha256(freeze)[..16]);
        Directory.CreateDirectory(runRoot);

        IReadOnlyList<FormalExperimentEvidenceSeal> rq2Seals = await RunRq2SuiteAsync(
            rq2,
            freeze,
            readiness,
            runRoot,
            cancellationToken).ConfigureAwait(false);
        WriteCoverage(rq2.Suite, rq2Seals, Path.Combine(runRoot, "rq2-coverage.json"));
        FormalProviderTokenReport.WriteRq2(Path.Combine(runRoot, "rq2"));
        Console.WriteLine("FORMAL_RQ2_STUDY_ARTIFACT_ROOT=" + Path.GetFullPath(runRoot));
    }

    private static async Task<IReadOnlyList<FormalExperimentEvidenceSeal>> RunRq1SuiteAsync(
        PreparedRq1 prepared,
        byte[] freeze,
        byte[] readiness,
        string runRoot,
        Rq1ConcurrencyOptions concurrency,
        string? replayAttemptRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(concurrency);
        Rq1ExperimentShape.ValidateThirtyDistinctBlockSuite(prepared.Suite.Pairs);
        if (prepared.Pairs.Count != prepared.Suite.Pairs.Count)
            throw new InvalidDataException("Prepared RQ1 pair assets do not match the 30-block suite manifest.");
        for (int pairIndex = 0; pairIndex < prepared.Pairs.Count; pairIndex++)
        {
            if (!StringComparer.Ordinal.Equals(
                    prepared.Pairs[pairIndex].PairId,
                    prepared.Suite.Pairs[pairIndex].PairId))
            {
                throw new InvalidDataException(
                    $"Prepared RQ1 pair order differs from the suite manifest at index {pairIndex}.");
            }
        }

        int pairCount = prepared.Pairs.Count;
        string outputRoot = Path.Combine(runRoot, "rq1");
        Directory.CreateDirectory(outputRoot);
        var seals = new List<FormalExperimentEvidenceSeal>();
        int completed = 0;

        async Task RunPairAsync(int pairIndex, int workerIndex, CancellationToken pairToken)
        {
            string? credentialEnvironmentName = concurrency.CredentialEnvironmentNameFor(workerIndex);
            PreparedPairAssets pairAssets = prepared.Pairs[pairIndex];
            string pairId = pairAssets.PairId;
            string evidencePath = Path.Combine(outputRoot, pairId + ".jsonl");
            if (File.Exists(evidencePath))
            {
                FormalExperimentEvidenceSeal existing = FormalExperimentEvidenceSeal.Load(
                    File.ReadAllBytes(evidencePath));
                lock (seals) seals.Add(existing);
                int progress = Interlocked.Increment(ref completed);
                Console.WriteLine($"FORMAL_RQ1_PROGRESS={progress}/{pairCount} pair={pairId} status=resumed");
                return;
            }
            FormalRq1MatchedPairManifest manifest = pairAssets.Rq1Manifest
                ?? throw new InvalidDataException("Prepared RQ1 pair lacks its exact matched-pair manifest: " + pairId);
            IReadOnlyDictionary<string, byte[]> artifacts = WithSuite(
                pairAssets.Artifacts,
                "rq1_suite_manifest",
                prepared.Suite.GetCanonicalBytes());
            FormalCollectionFreezeGateResult gate = FormalCollectionFreezeGate.Verify(
                freeze,
                readiness,
                Directory.GetCurrentDirectory(),
                FormalExperimentRq.Rq1,
                manifest.PairManifestHash,
                RuntimeVersion,
                ModelProfileId,
                artifacts);
            RequireReadyGate(gate, pairId);
            FormalExperimentSuitePairEntry entry = prepared.Suite.Pairs.Single(value =>
                StringComparer.Ordinal.Equals(value.PairId, pairId));
            var agentAttempts = new FormalProviderAttemptCollector();
            var eventAttempts = new FormalProviderAttemptCollector();
            FormalRq1ReplayPair? replay = replayAttemptRoot is null
                ? null
                : FormalRq1ReplayPair.Load(
                    Path.Combine(replayAttemptRoot, pairId + "-provider-attempts.jsonl"),
                    pairId);
            IFormalRq1ConditionExecutor agent = CreateFormalRq1Executor(
                prepared,
                pairAssets,
                manifest.AgentCentric,
                gate,
                pairId + "-agent",
                ExtractRq1BlockContext(pairAssets.Artifacts["rq1_public_fixture"]),
                agentAttempts,
                credentialEnvironmentName,
                replay?.CreateClient("AgentCentric", ModelProfileId, agentAttempts));
            IFormalRq1ConditionExecutor eventCentric = CreateFormalRq1Executor(
                prepared,
                pairAssets,
                manifest.EventCentric,
                gate,
                pairId + "-event",
                ExtractRq1BlockContext(pairAssets.Artifacts["rq1_public_fixture"]),
                eventAttempts,
                credentialEnvironmentName,
                replay?.CreateClient("EventCentric", ModelProfileId, eventAttempts));
            FormalRq1ConditionPairExecutionResult execution = await new FormalRq1ConditionPairRunner().RunAsync(
                manifest,
                pairAssets.Artifacts["rq1_public_fixture"],
                entry.ConditionOrder.Select(ParseRq1Treatment).ToArray(),
                gate.Authorization!,
                gate.Permit!,
                new FormalRq1ConditionExecutorPairFactory(agent, eventCentric),
                pairToken).ConfigureAwait(false);
            WriteRq1ProviderAttempts(
                outputRoot,
                pairId,
                pairAssets.Rq1Scenarios
                    ?? throw new InvalidDataException($"RQ1 pair lacks scenarios: {pairId}."),
                entry.ConditionOrder.Select(ParseRq1Treatment).ToArray(),
                agentAttempts,
                eventAttempts);
            replay?.RequireFullyConsumed();
            if (execution.Kind != FormalConditionPairExecutionKind.AwaitingOfflineScoring)
            {
                FormalExperimentEvidenceSeal terminal = execution.TerminalEvidenceSeal
                    ?? throw new InvalidDataException($"Formal RQ1 pair lacks terminal evidence: {pairId}/{execution.Kind}.");
                File.WriteAllBytes(evidencePath, terminal.GetCanonicalJsonLines());
                lock (seals) seals.Add(terminal);
                int progress = Interlocked.Increment(ref completed);
                Console.WriteLine($"FORMAL_RQ1_PROGRESS={progress}/{pairCount} pair={pairId} status={execution.Kind}");
                return;
            }
            PreparedRq1ScoringInputs scoringInputs = pairAssets.Rq1ScoringInputs
                ?? throw new InvalidDataException($"RQ1 pair lacks scoped scoring inputs: {pairId}.");
            FormalRq1MatchedRunResult scored = FormalRq1OfflineScoringRuntime.Score(
                execution.Artifact!,
                scoringInputs.OpportunityLedger,
                scoringInputs.TestCaseLedger,
                scoringInputs.Mapping,
                scoringInputs.HiddenTestCases);
            File.WriteAllBytes(evidencePath, scored.EvidenceSeal.GetCanonicalJsonLines());
            File.WriteAllBytes(
                Path.Combine(outputRoot, pairId + "-score.json"),
                JsonSerializer.SerializeToUtf8Bytes(scored.Score, new JsonSerializerOptions { WriteIndented = true }));
            lock (seals) seals.Add(scored.EvidenceSeal);
            int completeProgress = Interlocked.Increment(ref completed);
            Console.WriteLine($"FORMAL_RQ1_PROGRESS={completeProgress}/{pairCount} pair={pairId} status=complete");
        }

        Rq1ConcurrencySchedulerResult result = await Rq1ConcurrencyScheduler.RunAsync(
            pairCount,
            concurrency.WorkerCount,
            RunPairAsync,
            cancellationToken).ConfigureAwait(false);
        if (result.Failures.Count > 0)
        {
            string detail = string.Join(
                ",",
                result.Failures.Select(failure =>
                    $"pair={failure.PairIndex + 1}/worker={failure.WorkerIndex}:{failure.Exception.GetType().Name}:{failure.Exception.Message}"));
            throw new InvalidOperationException(
                $"Formal RQ1 concurrent run had {result.Failures.Count} failed pair(s): {detail}");
        }
        return seals;
    }

    private static async Task<IReadOnlyList<FormalExperimentEvidenceSeal>> RunRq2SuiteAsync(
        PreparedRq2 prepared,
        byte[] freeze,
        byte[] readiness,
        string runRoot,
        CancellationToken cancellationToken)
    {
        string outputRoot = Path.Combine(runRoot, "rq2");
        Directory.CreateDirectory(outputRoot);
        var seals = new List<FormalExperimentEvidenceSeal>();
        int completed = 0;
        await Parallel.ForEachAsync(
            Enumerable.Range(0, prepared.Pairs.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 1, CancellationToken = cancellationToken },
            async (pairIndex, pairToken) =>
        {
                PreparedRq2Cell cell = prepared.Cells[pairIndex / Rq2RepeatsPerCell];
                int repeat = pairIndex % Rq2RepeatsPerCell + 1;
                string pairId = $"rq2-{cell.FixtureId}-repeat-{repeat:D2}";
                string evidencePath = Path.Combine(outputRoot, pairId + ".jsonl");
                if (File.Exists(evidencePath))
                {
                    FormalExperimentEvidenceSeal existing = FormalExperimentEvidenceSeal.Load(File.ReadAllBytes(evidencePath));
                    lock (seals) seals.Add(existing);
                    int progress = Interlocked.Increment(ref completed);
                    Console.WriteLine($"FORMAL_RQ2_PROGRESS={progress}/{prepared.Pairs.Count} pair={pairId} status=resumed");
                    return;
                }
                FormalRq2MatchedPairManifest manifest = CreateRq2PairManifest(prepared.Registry, cell, repeat);
                PreparedPairAssets pairAssets = prepared.Pairs.Single(value =>
                    StringComparer.Ordinal.Equals(value.PairId, pairId));
                IReadOnlyDictionary<string, byte[]> artifacts = WithSuite(
                    pairAssets.Artifacts,
                    "rq2_suite_manifest",
                    prepared.Suite.GetCanonicalBytes());
                FormalCollectionFreezeGateResult gate = FormalCollectionFreezeGate.Verify(
                    freeze,
                    readiness,
                    Directory.GetCurrentDirectory(),
                    FormalExperimentRq.Rq2,
                    manifest.PairManifestHash,
                    RuntimeVersion,
                    ModelProfileId,
                    artifacts);
                RequireReadyGate(gate, pairId);
                FormalRq2PlanningFixture planning = CreateRq2PlanningFixture(cell);
                DecisionNeed compositionNeed = CreateInFlightNeed(planning.View, planning.Plan);
                FormalRq2CandidateSelectionProvenance provenance = CreateRq2Provenance();
                FormalRq2CandidateScoringResult scoring = FormalRq2CandidateScorer.Score(
                    [],
                    new SimTime(10_000),
                    40,
                    provenance.ScorerConfiguration,
                    cell.CandidateSet.RankedSlices.Select(value =>
                        new FormalRq2CandidateScoreInput(value, [], 5)));
                FormalRq2PairCompositionResult composition = new FormalRq2PairCompositionRuntime(
                    manifest,
                    FormalRq2RunPurpose.FormalCollection,
                    prepared.Registry,
                    new FormalRq2PairCompositionDependencies(
                        new FormalApproximateTokenCounter(),
                        FormalRq2PlanningContextRenderer.Instance),
                    gate.Authorization,
                    gate.Permit).ComposePlanningPair(
                        FormalRq2CandidateEvidence.Available(
                            scoring,
                            provenance,
                            FormalRq2PreTreatmentEmotionEvidence.CreateNoEmotion(cell.CandidateSet)),
                        compositionNeed,
                        planning.View,
                        planning.Plan);
                if (composition.Kind != FormalRq2PairCompositionKind.Succeeded)
                    throw new InvalidDataException($"Formal RQ2 composition failed: {pairId}/{composition.Kind}.");
                FormalRq2SummaryFidelityEvidence fidelity = FormalRq2SummaryFidelityValidator.Validate(
                    prepared.Registry,
                    composition.SummaryBinding!,
                    cell.RequiredSources,
                    "formal-rq2-summary-fidelity-validator-current");
                FormalExperimentSuitePairEntry entry = prepared.Suite.Pairs.Single(value =>
                    StringComparer.Ordinal.Equals(value.PairId, pairId));
                var verbatimAttempts = new FormalProviderAttemptCollector();
                var summaryAttempts = new FormalProviderAttemptCollector();
                IFormalRq2ConditionExecutor verbatim = CreateFormalRq2Executor(
                    FormalRq2Treatment.Verbatim,
                    composition.Verbatim!,
                    cell,
                    pairId + "-verbatim",
                    verbatimAttempts);
                IFormalRq2ConditionExecutor summary = CreateFormalRq2Executor(
                    FormalRq2Treatment.Summary,
                    composition.Summary!,
                    cell,
                    pairId + "-summary",
                    summaryAttempts);
                FormalRq2ConditionPairExecutionResult execution = await new FormalRq2ConditionPairRunner().RunAsync(
                    composition,
                    fidelity,
                    entry.ConditionOrder.Select(ParseRq2Treatment).ToArray(),
                    gate.Authorization!,
                    gate.Permit!,
                    new FormalRq2ConditionExecutorPairFactory(verbatim, summary),
                    pairToken).ConfigureAwait(false);
                WriteRq2ProviderAttempts(
                    outputRoot,
                    pairId,
                    cell,
                    entry.ConditionOrder.Select(ParseRq2Treatment).ToArray(),
                    verbatimAttempts,
                    summaryAttempts);
                if (execution.Kind != FormalConditionPairExecutionKind.AwaitingOfflineScoring)
                {
                    FormalExperimentEvidenceSeal terminal = execution.TerminalEvidenceSeal
                        ?? throw new InvalidDataException($"Formal RQ2 pair lacks terminal evidence: {pairId}/{execution.Kind}.");
                    File.WriteAllBytes(evidencePath, terminal.GetCanonicalJsonLines());
                    lock (seals) seals.Add(terminal);
                    int progress = Interlocked.Increment(ref completed);
                    Console.WriteLine($"FORMAL_RQ2_PROGRESS={progress}/{prepared.Pairs.Count} pair={pairId} status={execution.Kind}");
                    return;
                }
                FormalRq2MatchedRunResult scored = FormalRq2OfflineScoringRuntime.Score(
                    execution.Artifact!,
                    cell.RequiredSources,
                    cell.HiddenPredicate);
                File.WriteAllBytes(evidencePath, scored.EvidenceSeal.GetCanonicalJsonLines());
                File.WriteAllBytes(
                    Path.Combine(outputRoot, pairId + "-score.json"),
                    JsonSerializer.SerializeToUtf8Bytes(scored.Score, new JsonSerializerOptions { WriteIndented = true }));
                lock (seals) seals.Add(scored.EvidenceSeal);
                int completeProgress = Interlocked.Increment(ref completed);
                Console.WriteLine($"FORMAL_RQ2_PROGRESS={completeProgress}/{prepared.Pairs.Count} pair={pairId} status=complete");
        }).ConfigureAwait(false);
        return seals;
    }

    private static void WriteCoverage(
        FormalExperimentSuiteManifest suite,
        IReadOnlyList<FormalExperimentEvidenceSeal> seals,
        string path)
    {
        FormalExperimentSuiteCoverageReport report = FormalExperimentSuiteCoverageGate.Verify(suite, seals);
        File.WriteAllBytes(path, report.GetCanonicalBytes());
        if (!report.IsComplete)
            throw new InvalidDataException("Formal suite coverage is incomplete: " + string.Join(",", report.Blockers));
    }

    private static void RequireReadyGate(FormalCollectionFreezeGateResult gate, string pairId)
    {
        if (!gate.Report.IsReady || gate.Permit is null || gate.Authorization is null)
            throw new InvalidOperationException(
                "Formal freeze gate blocked " + pairId + ": " + string.Join(",", gate.Report.Blockers));
    }

    private static IFormalRq1ConditionExecutor CreateFormalRq1Executor(
        PreparedRq1 prepared,
        PreparedPairAssets pairAssets,
        FormalRq1ConditionManifest manifest,
        FormalCollectionFreezeGateResult gate,
        string suffix,
        byte[] blockContext,
        IAnthropicMessagesProviderAttemptSink attemptSink,
        string? credentialEnvironmentName = null,
        IModelClient<RemotePlannerResponse>? replayClient = null)
    {
        IReadOnlyList<PreparedRq1Scenario> scenarios = pairAssets.Rq1Scenarios
            ?? throw new InvalidDataException($"RQ1 pair lacks scenarios: {pairAssets.PairId}.");
        IReadOnlyList<PressureState> pressureStates = pairAssets.Rq1PressureStates
            ?? throw new InvalidDataException($"RQ1 pair lacks pressure state: {pairAssets.PairId}.");
        FormalRq1Treatment treatment = manifest.Treatment;
        var store = new DecisionNeedStore();
        var candidates = new List<Rq1DecisionNeedAdmissionCandidate>();
        var viewByNeed = new Dictionary<DecisionNeedId, ActorDecisionView>();
        var scenarioByNeed = new Dictionary<DecisionNeedId, PreparedRq1Scenario>();
        foreach (PreparedRq1Scenario scenario in scenarios)
        {
            ActorDecisionView view = CreateRq1ScenarioView(scenario);
            var registrar = new DecisionNeedDiscoveryRegistrar(store);
            DecisionNeed need;
            if (treatment == FormalRq1Treatment.AgentCentric)
            {
                var binding = new AgentCentricPlanlessStrategicDecisionBinding(
                    view.ActorId,
                    view,
                    new DecisionNeedKind("planless_strategic"),
                    new DecisionProblemCode("formal_pressure_resolution"),
                    new DecisionNeedWorldRevision(1),
                    new SimTime(scenario.DecisionTick),
                    new SimTime(scenario.DeadlineTick));
                AgentCentricPlanOptionalCompleted result = (AgentCentricPlanOptionalCompleted)
                    new AgentCentricPlanOptionalDecisionNeedRuntime(registrar).Run(
                    [
                        new AgentCentricTriggerNomination(
                            binding,
                            Enum.Parse<AgentCentricRankBand>(scenario.AgentRankBand, false),
                            new AgentCentricTriggerId(scenario.PressureId + "-trigger"))
                    ]);
                AgentCentricRegistrationReceipt receipt = result.QueuedSchedule.Single();
                need = receipt.SelectedNeed;
                candidates.Add(new Rq1DecisionNeedAdmissionCandidate(need, receipt.TreatmentRank));
            }
            else
            {
                Rq1EventDependencyFixture dependency = CreateRq1EventDependencyFixture(
                    scenario,
                    view.ActorId);
                DependencyIndex index = DependencyIndex.Create([dependency.Edge]);
                var binding = new EventCentricPlanlessStrategicBinding(
                    view.ActorId,
                    view,
                    new DecisionNeedKind("planless_strategic"),
                    new DecisionProblemCode("formal_pressure_resolution"),
                    new DecisionNeedWorldRevision(1),
                    new SimTime(scenario.DecisionTick),
                    new SimTime(scenario.DeadlineTick));
                EventCentricPlanOptionalCompleted result = (EventCentricPlanOptionalCompleted)
                    new EventCentricPlanOptionalDecisionNeedPolicy(index, registrar).Run(
                        [dependency.Fact],
                        [binding]);
                EventCentricPlanOptionalRegistrationReceipt receipt = result.QueuedSchedule.Single();
                need = receipt.SelectedNeed;
                candidates.Add(new Rq1DecisionNeedAdmissionCandidate(need, receipt.TreatmentRank));
            }
            viewByNeed.Add(need.NeedId, view);
            scenarioByNeed.Add(need.NeedId, scenario);
        }

        var dispatch = new FormalRq1DispatchRuntime(store, prepared.DispatchConfiguration);
        var condition = new FormalRq1ConditionRuntime(
            manifest,
            FormalRq1RunPurpose.FormalCollection,
            dispatch,
            new FormalRq1ConditionDependencies(
                ModelProfileId,
                replayClient ?? CreateProviderClient(
                        "formal-rq1-" + suffix,
                        profileId: ModelProfileId,
                        attemptSink: attemptSink,
                        credentialEnvironmentName: credentialEnvironmentName),
                CreateAuthorityProjector(),
                new AuthorityPressureEventCompositionRuntime(CreatePressureRuntime(pressureStates))),
            gate.Authorization,
            gate.Permit);
        FormalRq1DispatchAdmissionResult admission = dispatch.Admit(
            candidates,
            treatment,
            new SimTime(scenarios.Max(value => value.DecisionTick)));
        var nonAttempted = admission.Projection.MissedDueToBudget.Select(entry =>
        {
            PreparedRq1Scenario scenario = scenarioByNeed[entry.Need.NeedId];
            return new FormalRq1OpportunityRunEvidence(
                new Rq1OpportunityId(scenario.PressureId),
                new SimTime(scenario.DecisionTick),
                entry.Need.CreatedAt,
                null,
                null,
                entry.IsStarvationPromoted,
                entry.Need.NeedId,
                null,
                null,
                null,
                null);
        }).ToArray();
        var trials = new List<FormalRq1ScheduledOpportunityTrial>();
        foreach (Rq1DecisionNeedAdmissionEntry entry in admission.Projection.SelectedForAdmission)
        {
            PreparedRq1Scenario scenario = scenarioByNeed[entry.Need.NeedId];
            ActorDecisionView view = viewByNeed[entry.Need.NeedId];
            Rq1LogicalSessionDispatch session = admission.ReservedSessions.Single(value =>
                ReferenceEquals(value.Need, entry.Need));
            IFormalRq1ScheduledInvocationOwner starter;
            if (scenario.ExpectedDefer)
            {
                starter = new FormalPlanlessRq1InvocationStarter(
                    store,
                    entry.Need,
                    view,
                    new NpcPlanningState(view.ActiveGoals, null),
                    CreateRq1MemoryPacket(view.ActorId, scenario, blockContext),
                    new RemotePlannerRequestId(suffix + "-" + scenario.PressureId),
                    Rq1ContextBuilderVersion,
                    new SimTime(scenario.DecisionTick + 2));
            }
            else
            {
                FormalProductActionFixture action = CreateRq1ProductActionFixture(
                    view,
                    scenario,
                    scenario.GameActionId
                        ?? throw new InvalidDataException($"RQ1 committed case lacks an action ID: {scenario.PressureId}."));
                starter = new FormalPlanlessRq1InvocationStarter(
                    store,
                    entry.Need,
                    view,
                    new NpcPlanningState(view.ActiveGoals, null),
                    CreateRq1MemoryPacket(view.ActorId, scenario, blockContext),
                    new RemotePlannerRequestId(suffix + "-" + scenario.PressureId),
                    Rq1ContextBuilderVersion,
                    new SimTime(scenario.DecisionTick + 2),
                    action.Catalogue,
                    action.Executor);
            }
            trials.Add(new FormalRq1ScheduledOpportunityTrial(
                new Rq1OpportunityId(scenario.PressureId),
                new SimTime(scenario.DecisionTick),
                entry.Need.CreatedAt,
                new SimTime(scenario.DecisionTick + 1),
                new SimTime(scenario.DecisionTick + 2),
                entry.IsStarvationPromoted,
                new Rq1SessionId(suffix + "-" + scenario.PressureId + "-session"),
                new FormalTerminalReceiptId(suffix + "-" + scenario.PressureId + "-receipt"),
                session,
                starter));
        }
        return new FormalRq1ConditionRuntimeExecutor(
            suffix + "-runtime",
            suffix + "-provider",
            condition,
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            nonAttempted,
            trials);
    }

    private static IFormalRq2ConditionExecutor CreateFormalRq2Executor(
        FormalRq2Treatment treatment,
        FormalRq2ConditionComposition composition,
        PreparedRq2Cell cell,
        string suffix,
        IAnthropicMessagesProviderAttemptSink attemptSink)
    {
        NpcPlan sourcePlan = composition.Context.SourcePlanBinding.Plan;
        FormalRq2PlanningFixture planning = CreateRq2PlanningFixture(cell, sourcePlan);
        (DecisionNeedStore store, DecisionNeed need) = CloneInFlightNeed(planning.View, planning.Plan);
        if (need.NeedId != composition.Context.NeedId)
            throw new InvalidDataException("Formal RQ2 isolated branch Need identity changed.");
        PlanStep sourceStep = sourcePlan.Steps.Single(step =>
            step.PlanStepId == composition.Context.SourcePlanBinding.CurrentPlanStepId);
        if (!ReferenceEquals(planning.Plan, sourcePlan)
            || !ReferenceEquals(planning.View.CurrentStep, sourceStep))
            throw new InvalidDataException("Formal RQ2 isolated branch is not bound to the composed current-plan snapshot.");
        RemotePlannerRequest request = RemotePlannerRequest.Create(
            new RemotePlannerRequestId(suffix + "-request"),
            composition.Context);
        return new FormalRq2LiveConditionExecutor(
            treatment,
            suffix + "-runtime",
            suffix + "-provider",
            CreateProviderClient(
                "formal-rq2-" + suffix,
                profileId: ModelProfileId,
                attemptSink: attemptSink),
            request,
            new FormalPlanningTerminalizer(
                store,
                need,
                planning.View,
                composition.Context,
                request,
                planning.Plan,
                new SimTime(20),
                planning.Catalogue,
                planning.Executor));
    }

    private static void WriteRq1ProviderAttempts(
        string outputRoot,
        string pairId,
        IReadOnlyList<PreparedRq1Scenario> scenarios,
        IReadOnlyList<FormalRq1Treatment> conditionOrder,
        FormalProviderAttemptCollector agentAttempts,
        FormalProviderAttemptCollector eventAttempts)
    {
        var conditions = new List<FormalProviderAttemptCondition>();
        foreach (FormalRq1Treatment treatment in conditionOrder)
        {
            IReadOnlyList<AnthropicMessagesProviderAttemptTrace> attempts = treatment == FormalRq1Treatment.AgentCentric
                ? agentAttempts.Snapshot()
                : eventAttempts.Snapshot();
            foreach (PreparedRq1Scenario scenario in scenarios)
            {
                AnthropicMessagesProviderAttemptTrace[] scenarioAttempts = attempts.Where(trace =>
                    trace.RequestId.EndsWith("-" + scenario.PressureId, StringComparison.Ordinal)).ToArray();
                conditions.Add(new FormalProviderAttemptCondition(
                    treatment.ToString(),
                    scenario.PressureId,
                    scenario.Stratum,
                    null,
                    scenarioAttempts));
            }
        }
        FormalProviderAttemptSidecar.Write(
            Path.Combine(outputRoot, pairId + "-provider-attempts.jsonl"),
            "RQ1",
            pairId,
            conditions);
    }

    private static void WriteRq2ProviderAttempts(
        string outputRoot,
        string pairId,
        PreparedRq2Cell cell,
        IReadOnlyList<FormalRq2Treatment> conditionOrder,
        FormalProviderAttemptCollector verbatimAttempts,
        FormalProviderAttemptCollector summaryAttempts)
    {
        FormalProviderAttemptCondition[] conditions = conditionOrder.Select(treatment =>
            new FormalProviderAttemptCondition(
                treatment.ToString(),
                cell.FixtureId,
                cell.Stratum,
                cell.Tier,
                treatment == FormalRq2Treatment.Verbatim
                    ? verbatimAttempts.Snapshot()
                    : summaryAttempts.Snapshot())).ToArray();
        FormalProviderAttemptSidecar.Write(
            Path.Combine(outputRoot, pairId + "-provider-attempts.jsonl"),
            "RQ2",
            pairId,
            conditions);
    }

    private static ActorDecisionView CreateRq1ScenarioView(PreparedRq1Scenario scenario)
    {
        ActorId actor = new(scenario.ActorId);
        var inventory = new InventoryState(actor, [], 1);
        var shared = new SharedActorState(
            new ActorIdentity(actor, new ActorName(scenario.ActorId), new ActorAge(30)),
            new ActorBodyState(actor, new Health(100, 100), new Satiety(50), new Spirit(50), Disease.Healthy),
            new ActorTraversalState(actor, MovementMode.Land),
            inventory,
            new EquipmentState(actor, null, 1, inventory));
        var objective = new ExperienceObjective(new ExperienceId("resolve-" + scenario.PressureId));
        var goal = new NpcGoal(new GoalId("goal-" + scenario.PressureId), objective);
        var npc = new NpcState(
            actor,
            new NpcPersonalityState(
                new CognitiveFunctionProfile(0, 1, 0, 1, 0, 1, 0, 1),
                [new PersonalityTagId("formal"), new PersonalityTagId("pressure-aware")],
                []),
            new NpcKnowledgeState(
                new NpcKnownTargetSpatialState([]),
                new NpcKnownOpportunityState([], [], [])),
            new NpcPlanningState([goal], null));
        return ActorDecisionView.Create(shared, npc, null);
    }

    private static MemoryPacket CreateRq1MemoryPacket(
        ActorId actor,
        PreparedRq1Scenario scenario,
        byte[] blockContext)
    {
        byte[] modelVisibleInput = BuildRq1ModelVisibleInput(scenario.PublicInput, blockContext);
        DecisionMemorySlice slice = DecisionMemorySlice.Create(
            actor,
            new DecisionMemoryKind("decision_fact"),
            new SimTime(scenario.DecisionTick),
            new DecisionMemoryProjectorVersion("formal_rq1_v1"),
            0,
            DecisionMemoryEvidenceStatus.Current,
            [new DecisionMemorySourceId(scenario.PressureId + "-public")],
            [],
            [],
            modelVisibleInput);
        MemoryPacketBuildOutcome outcome = MemoryPacketBuilders.BuildVerbatim(
            DecisionMemoryCandidateSet.Create([slice]),
            new FormalApproximateTokenCounter(),
            new MemoryPacketTokenCeiling(8192),
            new MemoryPacketTokenizerVersion("utf8_bytes_div4_v1"));
        return ((MemoryPacketBuildSuccess)outcome).Packet;
    }

    private static byte[] BuildRq1ModelVisibleInput(byte[] pressureInput, byte[] blockContext)
    {
        using JsonDocument pressure = JsonDocument.Parse(pressureInput);
        using JsonDocument context = JsonDocument.Parse(blockContext);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("pressure");
            pressure.RootElement.WriteTo(writer);
            writer.WritePropertyName("town_block_context");
            context.RootElement.WriteTo(writer);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static byte[] ExtractRq1BlockContext(byte[] fixtureBytes)
    {
        using JsonDocument fixture = JsonDocument.Parse(fixtureBytes);
        return Encoding.UTF8.GetBytes(fixture.RootElement.GetProperty("block_context").GetRawText());
    }

    private static byte[] BuildRq1PublicCaseInput(JsonElement source)
    {
        string[] publicProperties =
        [
            "slot",
            "pressure_id",
            "actor_id",
            "domain",
            "decision_tick",
            "deadline_tick",
            "pressure_packet",
            "public_facts",
            "selection_inputs",
            "actor_decision_view",
            "action_catalogue"
        ];
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (string propertyName in publicProperties)
            {
                writer.WritePropertyName(propertyName);
                source.GetProperty(propertyName).WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void ValidateRq1BlockRoots(
        JsonElement publicRoot,
        JsonElement privateRoot,
        string blockToken)
    {
        if (!StringComparer.Ordinal.Equals(
                RequiredString(publicRoot, "schema_version"),
                "alice.rq1-30-block-public-fixture.v1")
            || !StringComparer.Ordinal.Equals(
                RequiredString(privateRoot, "schema_version"),
                "alice.rq1-30-block-private-expectations.v1")
            || !StringComparer.Ordinal.Equals(RequiredString(publicRoot, "block_id"), blockToken)
            || !StringComparer.Ordinal.Equals(RequiredString(privateRoot, "block_id"), blockToken)
            || !StringComparer.Ordinal.Equals(
                RequiredString(publicRoot, "fixture_id"),
                RequiredString(privateRoot, "fixture_id"))
            || publicRoot.GetProperty("logical_l2_budget").GetInt32() != 4
            || publicRoot.GetProperty("cases").GetArrayLength() != 10
            || privateRoot.GetProperty("cases").GetArrayLength() != 10
            || !publicRoot.TryGetProperty("authority_setup", out JsonElement authoritySetup)
            || authoritySetup.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"RQ1 {blockToken} root contract is invalid.");
        _ = ParseRq1ConditionOrder(RequiredString(privateRoot, "condition_order"));
    }

    private static void ValidateRq1CaseJoin(
        JsonElement publicCase,
        JsonElement privateCase,
        string blockToken)
    {
        JsonElement matrix = privateCase.GetProperty("matrix");
        string pressureId = RequiredString(publicCase, "pressure_id");
        if (!StringComparer.Ordinal.Equals(RequiredString(privateCase, "pressure_id"), pressureId)
            || !StringComparer.Ordinal.Equals(RequiredString(matrix, "pressure_id"), pressureId)
            || !StringComparer.Ordinal.Equals(
                RequiredString(publicCase, "actor_id"),
                RequiredString(privateCase, "actor_id"))
            || !StringComparer.Ordinal.Equals(
                RequiredString(publicCase, "actor_id"),
                RequiredString(matrix, "actor_id"))
            || !StringComparer.Ordinal.Equals(
                RequiredString(publicCase, "domain"),
                RequiredString(privateCase, "domain"))
            || !StringComparer.Ordinal.Equals(
                RequiredString(publicCase, "domain"),
                RequiredString(matrix, "domain"))
            || !StringComparer.Ordinal.Equals(RequiredString(matrix, "block_id"), blockToken)
            || publicCase.GetProperty("slot").GetInt32() != privateCase.GetProperty("slot").GetInt32()
            || publicCase.GetProperty("slot").GetInt32() != matrix.GetProperty("slot").GetInt32()
            || !StringComparer.Ordinal.Equals(
                RequiredString(privateCase, "expected_terminal"),
                RequiredString(matrix, "expected_terminal")))
            throw new InvalidDataException($"RQ1 {pressureId} public/private join is inconsistent.");
        if (publicCase.TryGetProperty("expected_terminal", out _)
            || publicCase.TryGetProperty("expected_action_family", out _)
            || publicCase.TryGetProperty("action_template", out _)
            || publicCase.TryGetProperty("matrix", out _))
            throw new InvalidDataException($"RQ1 {pressureId} leaks a private label into the public fixture.");
    }

    private static void ValidateRq1RankMapping(
        string agentRankBand,
        string eventRankBand,
        string eventEdgeKind,
        string pressureId)
    {
        _ = Enum.Parse<AgentCentricRankBand>(agentRankBand, false);
        DependencyEdgeKind edge = Enum.Parse<DependencyEdgeKind>(eventEdgeKind, false);
        EventCentricRankBand expected = Enum.Parse<EventCentricRankBand>(eventRankBand, false);
        if (DependencyEdgeRankBandMapping.GetRankBand(edge) != expected)
            throw new InvalidDataException($"RQ1 {pressureId} event edge and rank band disagree.");
    }

    private static string[] ParseRq1ConditionOrder(string value) => value switch
    {
        "agent_first" => ["agent_centric", "event_centric"],
        "event_first" => ["event_centric", "agent_centric"],
        _ => throw new InvalidDataException("Unknown RQ1 block condition order: " + value)
    };

    private static void ValidateRq1DatasetBalance(
        IReadOnlyList<PreparedRq1Block> blocks,
        IReadOnlyList<PreparedRq1Scenario> scenarios)
    {
        if (blocks.Count != 30
            || scenarios.Count != 300
            || blocks.Count(value => StringComparer.Ordinal.Equals(
                value.ConditionOrder[0], "agent_centric")) != 15
            || blocks.Count(value => StringComparer.Ordinal.Equals(
                value.ConditionOrder[0], "event_centric")) != 15
            || scenarios.Count(value => value.ExpectedDefer) != 150)
            throw new InvalidDataException("RQ1 block, order, or terminal-outcome balance is invalid.");

        IReadOnlyDictionary<string, int> expectedAgentBands = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["A0"] = 60,
            ["A1"] = 60,
            ["A2"] = 60,
            ["A3"] = 60,
            ["A4"] = 60
        };
        IReadOnlyDictionary<string, int> expectedEventBands = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["E0"] = 120,
            ["E1"] = 90,
            ["E2"] = 90
        };
        IReadOnlyDictionary<string, int> expectedActionFamilies = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [ProductActionFamily.AssetTransfer.ToString()] = 30,
            [ProductActionFamily.Craft.ToString()] = 30,
            [ProductActionFamily.ListedExchange.ToString()] = 30,
            [ProductActionFamily.RegionOperation.ToString()] = 30,
            [ProductActionFamily.ServiceExchange.ToString()] = 30
        };
        IReadOnlyDictionary<string, int> expectedAdmissionRoles = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["agent_only"] = 90,
            ["both"] = 30,
            ["event_only"] = 90,
            ["neither"] = 90
        };
        ValidateRq1Counts(scenarios.Select(value => value.AgentRankBand), expectedAgentBands, "agent rank bands");
        ValidateRq1Counts(
            scenarios.Select(value => DependencyEdgeRankBandMapping.GetRankBand(
                Enum.Parse<DependencyEdgeKind>(value.EventEdgeKind, false)).ToString()),
            expectedEventBands,
            "event rank bands");
        ValidateRq1Counts(
            scenarios.Where(value => !value.ExpectedDefer).Select(value => value.ExpectedActionFamily!),
            expectedActionFamilies,
            "committed action families");
        ValidateRq1Counts(
            scenarios.Select(value => value.AdmissionRole),
            expectedAdmissionRoles,
            "admission roles");
    }

    private static void ValidateRq1Counts(
        IEnumerable<string> values,
        IReadOnlyDictionary<string, int> expected,
        string label)
    {
        Dictionary<string, int> actual = values
            .GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        if (actual.Count != expected.Count
            || expected.Any(value => !actual.TryGetValue(value.Key, out int count) || count != value.Value))
            throw new InvalidDataException("RQ1 " + label + " are not balanced as declared.");
    }

    private static Rq1EventDependencyFixture CreateRq1EventDependencyFixture(
        PreparedRq1Scenario scenario,
        ActorId actorId)
    {
        using JsonDocument document = JsonDocument.Parse(scenario.PublicInput);
        JsonElement dependency = document.RootElement
            .GetProperty("selection_inputs")
            .GetProperty("event_dependency");
        DependencySourceKind sourceKind = Enum.Parse<DependencySourceKind>(
            RequiredString(dependency, "source_kind"),
            false);
        string sourceId = RequiredString(dependency, "source_id");
        DependencyEdgeKind edgeKind = Enum.Parse<DependencyEdgeKind>(
            RequiredString(dependency, "edge_kind"),
            false);
        if (!StringComparer.Ordinal.Equals(sourceId, scenario.PressureId)
            || !StringComparer.Ordinal.Equals(edgeKind.ToString(), scenario.EventEdgeKind))
            throw new InvalidDataException(
                $"RQ1 {scenario.PressureId} public event dependency identity is inconsistent.");

        JsonElement affectedNode = dependency.GetProperty("affected_node");
        string nodeKind = RequiredString(affectedNode, "kind");
        string nodeId = RequiredString(affectedNode, "id");
        string expectedNodeKind = edgeKind switch
        {
            DependencyEdgeKind.AffectsPlace => "Place",
            DependencyEdgeKind.AssignedToActor => "Actor",
            DependencyEdgeKind.BoundByCommitment => "Commitment",
            _ => "Resource"
        };
        if (!StringComparer.Ordinal.Equals(nodeKind, expectedNodeKind))
            throw new InvalidDataException(
                $"RQ1 {scenario.PressureId} {edgeKind} requires a {expectedNodeKind} affected node.");
        AffectedNode node = nodeKind switch
        {
            "Place" => AffectedNode.FromPlace(new PlaceRef(nodeId)),
            "Resource" => AffectedNode.FromResource(new ResourceRef(nodeId)),
            "Commitment" => AffectedNode.FromCommitment(new CommitmentId(nodeId)),
            "Actor" => AffectedNode.FromActor(new ActorId(nodeId)),
            "Duty" => AffectedNode.FromDuty(new DutyRef(nodeId)),
            _ => throw new InvalidDataException(
                $"RQ1 {scenario.PressureId} has unknown affected-node kind {nodeKind}.")
        };
        if (edgeKind == DependencyEdgeKind.AssignedToActor
            && !StringComparer.Ordinal.Equals(nodeId, actorId.Value))
            throw new InvalidDataException(
                $"RQ1 {scenario.PressureId} actor-assignment node does not match its actor.");

        JsonElement role = dependency.GetProperty("role_assignment");
        DependencyEdge edge;
        if (edgeKind == DependencyEdgeKind.MemberOfOrganization)
        {
            if (role.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(
                    $"RQ1 {scenario.PressureId} organization edge lacks its exact role assignment.");
            string responsibilityKind = RequiredString(role, "responsibility_kind");
            string responsibilityId = RequiredString(role, "responsibility_id");
            ResponsibilityRef responsibility = responsibilityKind switch
            {
                "Place" => ResponsibilityRef.FromPlace(new PlaceRef(responsibilityId)),
                "Resource" => ResponsibilityRef.FromResource(new ResourceRef(responsibilityId)),
                "Commitment" => ResponsibilityRef.FromCommitment(new CommitmentId(responsibilityId)),
                "Duty" => ResponsibilityRef.FromDuty(new DutyRef(responsibilityId)),
                _ => throw new InvalidDataException(
                    $"RQ1 {scenario.PressureId} has unknown role-responsibility kind {responsibilityKind}.")
            };
            edge = DependencyEdge.FromRoleAssignment(
                node,
                new RoleAssignment(
                    actorId,
                    new OrganizationId(RequiredString(role, "organization_id")),
                    responsibility));
        }
        else
        {
            if (role.ValueKind != JsonValueKind.Null)
                throw new InvalidDataException(
                    $"RQ1 {scenario.PressureId} has a role assignment without an organization edge.");
            edge = DependencyEdge.Create(edgeKind, node, actorId);
        }
        return new Rq1EventDependencyFixture(
            edge,
            new AffectedNodeFact(sourceKind, sourceId, node));
    }

    private static void ValidateRq1AdmissionSelections(
        IReadOnlyList<PreparedRq1Block> blocks,
        FormalRq1DispatchConfiguration dispatchConfiguration)
    {
        foreach (PreparedRq1Block block in blocks)
        {
            ValidateRq1AdmissionSelection(block, dispatchConfiguration, FormalRq1Treatment.AgentCentric);
            ValidateRq1AdmissionSelection(block, dispatchConfiguration, FormalRq1Treatment.EventCentric);
        }
    }

    private static void ValidateRq1AdmissionSelection(
        PreparedRq1Block block,
        FormalRq1DispatchConfiguration dispatchConfiguration,
        FormalRq1Treatment treatment)
    {
        var store = new DecisionNeedStore();
        var registrar = new DecisionNeedDiscoveryRegistrar(store);
        var candidates = new List<Rq1DecisionNeedAdmissionCandidate>(block.Scenarios.Count);
        var scenarioByNeed = new Dictionary<DecisionNeedId, PreparedRq1Scenario>();
        foreach (PreparedRq1Scenario scenario in block.Scenarios)
        {
            ActorDecisionView view = CreateRq1ScenarioView(scenario);
            DecisionNeed need;
            if (treatment == FormalRq1Treatment.AgentCentric)
            {
                var binding = new AgentCentricPlanlessStrategicDecisionBinding(
                    view.ActorId,
                    view,
                    new DecisionNeedKind("planless_strategic"),
                    new DecisionProblemCode("formal_pressure_resolution"),
                    new DecisionNeedWorldRevision(1),
                    new SimTime(scenario.DecisionTick),
                    new SimTime(scenario.DeadlineTick));
                AgentCentricPlanOptionalCompleted result = (AgentCentricPlanOptionalCompleted)
                    new AgentCentricPlanOptionalDecisionNeedRuntime(registrar).Run(
                    [
                        new AgentCentricTriggerNomination(
                            binding,
                            Enum.Parse<AgentCentricRankBand>(scenario.AgentRankBand, false),
                            new AgentCentricTriggerId(scenario.PressureId + "-preflight-trigger"))
                    ]);
                AgentCentricRegistrationReceipt receipt = result.QueuedSchedule.Single();
                need = receipt.SelectedNeed;
                candidates.Add(new Rq1DecisionNeedAdmissionCandidate(need, receipt.TreatmentRank));
            }
            else
            {
                Rq1EventDependencyFixture dependency = CreateRq1EventDependencyFixture(
                    scenario,
                    view.ActorId);
                DependencyIndex index = DependencyIndex.Create([dependency.Edge]);
                var binding = new EventCentricPlanlessStrategicBinding(
                    view.ActorId,
                    view,
                    new DecisionNeedKind("planless_strategic"),
                    new DecisionProblemCode("formal_pressure_resolution"),
                    new DecisionNeedWorldRevision(1),
                    new SimTime(scenario.DecisionTick),
                    new SimTime(scenario.DeadlineTick));
                EventCentricPlanOptionalCompleted result = (EventCentricPlanOptionalCompleted)
                    new EventCentricPlanOptionalDecisionNeedPolicy(index, registrar).Run(
                        [dependency.Fact],
                        [binding]);
                EventCentricPlanOptionalRegistrationReceipt receipt = result.QueuedSchedule.Single();
                need = receipt.SelectedNeed;
                candidates.Add(new Rq1DecisionNeedAdmissionCandidate(need, receipt.TreatmentRank));
            }
            scenarioByNeed.Add(need.NeedId, scenario);
        }

        FormalRq1DispatchAdmissionResult admission = new FormalRq1DispatchRuntime(
            store,
            dispatchConfiguration).Admit(
                candidates,
                treatment,
                new SimTime(block.Scenarios.Max(value => value.DecisionTick)));
        HashSet<string> actual = admission.Projection.SelectedForAdmission
            .Select(value => scenarioByNeed[value.Need.NeedId].PressureId)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> expected = block.Scenarios.Where(value => treatment == FormalRq1Treatment.AgentCentric
                ? value.AdmissionRole is "agent_only" or "both"
                : value.AdmissionRole is "event_only" or "both")
            .Select(value => value.PressureId)
            .ToHashSet(StringComparer.Ordinal);
        if (actual.Count != 4 || !actual.SetEquals(expected))
            throw new InvalidDataException(
                $"RQ1 {block.BlockToken} {treatment} admission differs from the fixed matrix. " +
                $"Expected [{string.Join(",", expected.Order(StringComparer.Ordinal))}], " +
                $"actual [{string.Join(",", actual.Order(StringComparer.Ordinal))}].");
    }

    private static FormalProductActionFixture CreateRq1ProductActionFixture(
        ActorDecisionView view,
        PreparedRq1Scenario scenario,
        string gameActionId)
    {
        TownWorldConfiguration world = TownWorldConfiguration.Load(
            Path.GetFullPath("godot/Config/town_world.json"));
        TownGameplayConfigurationDocument gameplay = CreateRq1ScenarioGameplay(
            world.Gameplay,
            world.Runtime.TicksPerDay,
            scenario);
        TownPopulationManifest population;
        try
        {
            population = CreateRq1Population(scenario);
        }
        catch (InvalidDataException error)
        {
            throw new InvalidDataException(
                $"RQ1 population projection failed for {scenario.PressureId}: {error.Message}",
                error);
        }
        RegionSocialGameplayRuntime runtime = RegionSocialGameplayRuntime.Create(
            gameplay,
            world.Runtime.Player,
            population,
            world.Runtime.TicksPerDay);
        ApplyRq1AuthorityState(runtime, scenario);
        ActorId actor = view.ActorId;
        using JsonDocument argumentsDocument = JsonDocument.Parse(scenario.ActionArguments);
        JsonElement arguments = argumentsDocument.RootElement;
        GameActionSpec action = scenario.ExpectedActionFamily switch
        {
            nameof(ProductActionFamily.AssetTransfer) => runtime.CreateAssetTransfer(
                actor,
                Enum.Parse<AssetContainerOwnerKind>(RequiredString(arguments, "source_kind"), false),
                RequiredString(arguments, "source_id"),
                Enum.Parse<AssetContainerOwnerKind>(RequiredString(arguments, "destination_kind"), false),
                RequiredString(arguments, "destination_id"),
                RequiredString(arguments, "asset_id"),
                arguments.GetProperty("quantity").GetInt64()),
            nameof(ProductActionFamily.Craft) =>
                runtime.CreateCraft(actor, RequiredString(arguments, "recipe_id")),
            nameof(ProductActionFamily.ListedExchange) =>
                runtime.CreateListedExchange(actor, RequiredString(arguments, "listing_id")),
            nameof(ProductActionFamily.RegionOperation) => runtime.CreateRegionOperation(
                actor,
                RequiredString(arguments, "region_id"),
                new SimTime(scenario.DecisionTick + 2)),
            nameof(ProductActionFamily.ServiceExchange) => runtime.CreateServiceExchange(
                actor,
                RequiredString(arguments, "service_id"),
                RequiredString(arguments, "provider_actor_id"),
                OptionalString(arguments, "target_item_instance_id")),
            _ => throw new InvalidDataException(
                "Unknown actionable RQ1 family: " + scenario.ExpectedActionFamily)
        };
        SimTime executionTime = new(scenario.DecisionTick + 2);
        GameplayValidationResult validation = runtime.Validate(action, executionTime);
        if (!validation.Available)
            throw new InvalidDataException(
                $"Frozen RQ1 product action is not executable: {scenario.PressureId}/{validation.Reason}.");
        GoalObjective objective = view.ActiveGoals.Single().Objective;
        var result = new ExperienceCompleted(
            view.ActorId,
            ((ExperienceObjective)objective).ExperienceId);
        var catalogue = new FormalPlanningActionCatalogue(
            actor,
            [new FormalPlanningActionCandidate(gameActionId, objective, null, result, action)]);
        return new FormalProductActionFixture(
            catalogue,
            runtime.CreateExecutor(actor));
    }

    private static void ValidateRq1ProductActions(IEnumerable<PreparedRq1Scenario> scenarios)
    {
        foreach (PreparedRq1Scenario scenario in scenarios)
        {
            if (scenario.ExpectedDefer) continue;
            ActorDecisionView view = CreateRq1ScenarioView(scenario);
            FormalProductActionFixture fixture = CreateRq1ProductActionFixture(
                view,
                scenario,
                scenario.GameActionId
                    ?? throw new InvalidDataException($"RQ1 committed case lacks an action ID: {scenario.PressureId}."));
            FormalPlanningActionCandidate candidate = fixture.Catalogue.Candidates.Single();
            var request = new ActorExecutionRequest(
                new ActorExecutionId("formal-rq1-fixture-validation/" + scenario.PressureId),
                view.ActorId,
                ActorExecutionMode.Interact,
                new InteractExecutionPayload(view.ActorId, candidate.Action),
                new SimTime(scenario.DecisionTick + 2),
                AutonomousNpcCognitionRoute.L2);
            ActorExecutionReceipt receipt = ActorExecutionPipeline.Dispatch(request, fixture.Executor);
            if (receipt is not
                {
                    Outcome: ActorExecutionOutcome.Completed,
                    Result: AuthorityCommitExecutionResult authority
                }
                || authority.ActionFamily != scenario.ExpectedActionFamily)
                throw new InvalidDataException(
                    $"Frozen RQ1 product action did not Authority-commit its domain action: {scenario.PressureId}.");
        }
    }

    private static TownGameplayConfigurationDocument CreateRq1ScenarioGameplay(
        TownGameplayConfigurationDocument source,
        long ticksPerDay,
        PreparedRq1Scenario scenario)
    {
        var assets = source.AssetDefinitions.ToList();
        var containers = source.Containers.ToList();
        var shops = source.Shops.ToList();
        var listings = source.Listings.ToList();
        var recipes = source.Recipes.ToList();
        var services = source.Services.ToList();
        var regions = source.Regions.ToList();
        var farmPlots = source.FarmPlots.ToList();
        var restocks = source.Restocks.ToList();
        using JsonDocument setupDocument = JsonDocument.Parse(scenario.AuthoritySetup);
        using JsonDocument stateDocument = JsonDocument.Parse(scenario.AuthorityExecutionState);
        JsonElement setup = setupDocument.RootElement.GetProperty("source_state");
        JsonElement state = stateDocument.RootElement;

        MergeRq1GameplayConfiguration(
            state,
            assets,
            shops,
            listings,
            recipes,
            services,
            regions,
            farmPlots);
        ApplyRq1ShopHours(setup, shops, ticksPerDay);

        List<Rq1ContainerFixtureState> containerStates = CollectRq1ContainerStates(
            setup,
            state,
            scenario.ActorDecisionView,
            scenario.ActorId);
        foreach (Rq1ContainerFixtureState containerState in containerStates)
        {
            EnsureRq1Assets(assets, containerState.Balances.Select(GetBalanceAssetId));
            if (containerState.OwnerKind == AssetContainerOwnerKind.Actor) continue;
            UpsertRq1Container(containers, new TownGameplayContainerConfiguration
            {
                OwnerKind = containerState.OwnerKind.ToString(),
                OwnerId = containerState.OwnerId,
                Balances = containerState.Balances.ToArray()
            });
        }

        EnsureRq1ConfigurationAssets(assets, listings, recipes, services, regions, farmPlots);
        using JsonDocument argumentsDocument = JsonDocument.Parse(scenario.ActionArguments);
        JsonElement arguments = argumentsDocument.RootElement;
        if (StringComparer.Ordinal.Equals(scenario.ExpectedActionFamily, nameof(ProductActionFamily.AssetTransfer)))
        {
            string assetId = RequiredString(arguments, "asset_id");
            EnsureRq1Assets(assets, [assetId]);
            AssetContainerOwnerKind sourceKind = Enum.Parse<AssetContainerOwnerKind>(
                RequiredString(arguments, "source_kind"), false);
            AssetContainerOwnerKind destinationKind = Enum.Parse<AssetContainerOwnerKind>(
                RequiredString(arguments, "destination_kind"), false);
            string sourceId = RequiredString(arguments, "source_id");
            string destinationId = RequiredString(arguments, "destination_id");
            EnsureRq1Container(containers, sourceKind, sourceId);
            EnsureRq1Container(containers, destinationKind, destinationId);
            if (sourceKind != AssetContainerOwnerKind.Actor
                || !StringComparer.Ordinal.Equals(sourceId, scenario.ActorId))
                UpsertRq1Restock(restocks, new TownGameplayRestockConfiguration
                {
                    RestockId = "formal-" + scenario.PressureId,
                    MerchantActorId = scenario.ActorId,
                    SourceContainerId = sourceId,
                    ShopContainerId = destinationId,
                    AssetId = assetId,
                    Quantity = arguments.GetProperty("quantity").GetInt64()
                });
        }
        return source with
        {
            AssetDefinitions = assets.ToArray(),
            Containers = containers.ToArray(),
            Shops = shops.ToArray(),
            Listings = listings.ToArray(),
            Recipes = recipes.ToArray(),
            Services = services.ToArray(),
            Regions = regions.ToArray(),
            FarmPlots = farmPlots.ToArray(),
            Restocks = restocks.ToArray()
        };
    }

    private static TownPopulationManifest CreateRq1Population(PreparedRq1Scenario scenario)
    {
        byte[] bytes = File.ReadAllBytes(Path.GetFullPath("godot/Config/town_world.json"));
        TownWorldConfigurationDocument document = JsonSerializer.Deserialize<TownWorldConfigurationDocument>(bytes)
            ?? throw new InvalidDataException("Town world configuration is absent.");
        TownCapabilityConfiguration[]? capabilities = ReadRq1ActorCapabilities(scenario);
        Rq1ActorInventoryFixture? inventory = ReadRq1ActorInventory(scenario, document.Gameplay);
        if (capabilities is null && inventory is null) return TownWorldConfiguration.Create(document).Population;
        TownNpcConfiguration[] actors = document.Population.Actors.ToArray();
        for (int index = 0; index < actors.Length; index++)
        {
            if (!StringComparer.Ordinal.Equals(actors[index].Identity.ActorId, scenario.ActorId)) continue;
            TownNpcConfiguration actor = actors[index];
            actors[index] = actor with
            {
                Capabilities = capabilities ?? actor.Capabilities,
                Inventory = inventory is null
                    ? actor.Inventory
                    : actor.Inventory with
                    {
                        Version = inventory.InventoryVersion,
                        EquipmentVersion = inventory.EquipmentVersion,
                        Stacks = [],
                        Instances = inventory.Instances,
                        EquippedHandInstanceId = inventory.EquippedInstanceId
                    },
                Currency = inventory is null ? actor.Currency : []
            };
            return TownWorldConfiguration.Create(document with
            {
                Population = document.Population with { Actors = actors }
            }).Population;
        }
        throw new InvalidDataException("RQ1 actor is absent from the town population: " + scenario.ActorId);
    }

    private static TownCapabilityConfiguration[]? ReadRq1ActorCapabilities(PreparedRq1Scenario scenario)
    {
        using JsonDocument stateDocument = JsonDocument.Parse(scenario.AuthorityExecutionState);
        TownCapabilityConfiguration[]? fromState = ReadRq1ActorCapabilities(stateDocument.RootElement);
        if (fromState is not null) return fromState;
        using JsonDocument viewDocument = JsonDocument.Parse(scenario.ActorDecisionView);
        return ReadRq1ActorCapabilities(viewDocument.RootElement);
    }

    private static TownCapabilityConfiguration[]? ReadRq1ActorCapabilities(JsonElement root)
    {
        if (!TryGetRq1Actor(root, out JsonElement actor)
            || !actor.TryGetProperty("capabilities", out JsonElement capabilities)) return null;
        if (capabilities.ValueKind == JsonValueKind.Array)
            return capabilities.EnumerateArray().Select(value => new TownCapabilityConfiguration
            {
                CapabilityId = RequiredString(value, "capability_id"),
                Value = value.GetProperty("value").GetInt32()
            }).ToArray();
        if (capabilities.ValueKind == JsonValueKind.Object)
            return capabilities.EnumerateObject().Select(value => new TownCapabilityConfiguration
            {
                CapabilityId = value.Name,
                Value = value.Value.GetInt32()
            }).ToArray();
        return null;
    }

    private static Rq1ActorInventoryFixture? ReadRq1ActorInventory(
        PreparedRq1Scenario scenario,
        TownGameplayConfigurationDocument baseGameplay)
    {
        using JsonDocument stateDocument = JsonDocument.Parse(scenario.AuthorityExecutionState);
        if (TryReadRq1ActorInventory(stateDocument.RootElement, baseGameplay, out Rq1ActorInventoryFixture? state))
            return state;
        using JsonDocument viewDocument = JsonDocument.Parse(scenario.ActorDecisionView);
        return TryReadRq1ActorInventory(viewDocument.RootElement, baseGameplay, out Rq1ActorInventoryFixture? view)
            ? view
            : null;
    }

    private static bool TryReadRq1ActorInventory(
        JsonElement root,
        TownGameplayConfigurationDocument baseGameplay,
        out Rq1ActorInventoryFixture? fixture)
    {
        fixture = null;
        if (!TryGetRq1Actor(root, out JsonElement actor)
            || !actor.TryGetProperty("inventory", out JsonElement inventory)
            || inventory.ValueKind != JsonValueKind.Object) return false;
        if (!inventory.TryGetProperty("instances", out JsonElement instances)
            || instances.ValueKind != JsonValueKind.Array) return false;
        HashSet<string> baseAssets = baseGameplay.AssetDefinitions
            .Select(value => value.AssetId).ToHashSet(StringComparer.Ordinal);
        TownItemInstanceConfiguration[] items = instances.EnumerateArray()
            .Where(value => baseAssets.Contains(RequiredString(value, "item_type_id")))
            .Select(value => new TownItemInstanceConfiguration
            {
                ItemInstanceId = RequiredString(value, "item_instance_id"),
                ItemTypeId = RequiredString(value, "item_type_id")
            }).ToArray();
        int inventoryVersion = inventory.TryGetProperty("version", out JsonElement version)
            ? version.GetInt32()
            : inventory.TryGetProperty("revision", out JsonElement revision) ? revision.GetInt32() : 1;
        int equipmentVersion = inventory.TryGetProperty("equipment_version", out JsonElement equipment)
            ? equipment.GetInt32()
            : 1;
        string? equipped = OptionalString(inventory, "equipped_hand_instance_id");
        if (equipped is null && inventory.TryGetProperty("hand_equipment", out JsonElement hand)
            && hand.ValueKind == JsonValueKind.Object)
            equipped = OptionalString(hand, "item_instance_id");
        if (actor.TryGetProperty("equipment", out JsonElement actorEquipment)
            && actorEquipment.ValueKind == JsonValueKind.Object)
        {
            if (actorEquipment.TryGetProperty("version", out JsonElement actorEquipmentVersion))
                equipmentVersion = actorEquipmentVersion.GetInt32();
            equipped ??= OptionalString(actorEquipment, "equipped_instance_id");
        }
        fixture = new Rq1ActorInventoryFixture(
            inventoryVersion,
            equipmentVersion,
            items,
            items.Any(value => StringComparer.Ordinal.Equals(value.ItemInstanceId, equipped)) ? equipped : null);
        return true;
    }

    private static bool TryGetRq1Actor(JsonElement root, out JsonElement actor)
    {
        actor = root;
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (root.TryGetProperty("actor_state", out JsonElement actorState)) actor = actorState;
        else if (root.TryGetProperty("actor", out JsonElement actorValue)) actor = actorValue;
        else if (root.TryGetProperty("self", out JsonElement self)) actor = self;
        else if (root.TryGetProperty("self_state", out JsonElement selfState)) actor = selfState;
        return actor.ValueKind == JsonValueKind.Object;
    }

    private static void ApplyRq1AuthorityState(
        RegionSocialGameplayRuntime runtime,
        PreparedRq1Scenario scenario)
    {
        using JsonDocument setupDocument = JsonDocument.Parse(scenario.AuthoritySetup);
        using JsonDocument stateDocument = JsonDocument.Parse(scenario.AuthorityExecutionState);
        JsonElement setup = setupDocument.RootElement.GetProperty("source_state");
        List<Rq1ContainerFixtureState> fixtures = CollectRq1ContainerStates(
            setup,
            stateDocument.RootElement,
            scenario.ActorDecisionView,
            scenario.ActorId);
        TownGameplayDurableState state = runtime.CaptureDurableState();
        TownGameplayContainerDurableState[] containers = state.Containers.ToArray();
        foreach (Rq1ContainerFixtureState fixture in fixtures)
        {
            for (int index = 0; index < containers.Length; index++)
            {
                TownGameplayContainerDurableState current = containers[index];
                if (current.OwnerKind != fixture.OwnerKind
                    || !StringComparer.Ordinal.Equals(current.OwnerId, fixture.OwnerId)) continue;
                containers[index] = current with
                {
                    Balances = fixture.Balances.Select(value =>
                        new TownGameplayAssetBalanceDurableState(value.AssetId, value.Quantity)).ToArray(),
                    Revision = fixture.Revision
                };
                break;
            }
        }
        TownGameplayItemInstanceDurableState[] items = state.ItemInstances.ToArray();
        foreach (Rq1ItemFixtureState fixture in CollectRq1ItemStates(scenario))
        {
            for (int index = 0; index < items.Length; index++)
            {
                if (!StringComparer.Ordinal.Equals(items[index].ItemInstanceId, fixture.ItemInstanceId)) continue;
                if (items[index].MaximumDurability != fixture.MaximumDurability) break;
                items[index] = items[index] with
                {
                    Durability = fixture.Durability,
                    MaximumDurability = fixture.MaximumDurability,
                    Version = fixture.Version
                };
                break;
            }
        }
        try
        {
            runtime.RestoreDurableState(state with { Containers = containers, ItemInstances = items });
        }
        catch (InvalidDataException error)
        {
            throw new InvalidDataException(
                $"RQ1 Authority state restore failed for {scenario.PressureId}: {error.Message}",
                error);
        }
    }

    private static IReadOnlyList<Rq1ItemFixtureState> CollectRq1ItemStates(PreparedRq1Scenario scenario)
    {
        var result = new Dictionary<string, Rq1ItemFixtureState>(StringComparer.Ordinal);
        using JsonDocument stateDocument = JsonDocument.Parse(scenario.AuthorityExecutionState);
        AddRq1ItemStates(stateDocument.RootElement, scenario.ActorId, result);
        using JsonDocument viewDocument = JsonDocument.Parse(scenario.ActorDecisionView);
        AddRq1ItemStates(viewDocument.RootElement, scenario.ActorId, result);
        return result.Values.ToArray();
    }

    private static void AddRq1ItemStates(
        JsonElement root,
        string actorId,
        Dictionary<string, Rq1ItemFixtureState> result)
    {
        if (TryGetRq1Actor(root, out JsonElement actor)
            && actor.TryGetProperty("inventory", out JsonElement inventory)
            && inventory.ValueKind == JsonValueKind.Object
            && inventory.TryGetProperty("instances", out JsonElement instances)
            && instances.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement value in instances.EnumerateArray())
                AddRq1ItemState(value, AssetContainerOwnerKind.Actor, actorId, result);
        }
        if (!root.TryGetProperty("entities", out JsonElement entities)
            || entities.ValueKind != JsonValueKind.Array) return;
        foreach (JsonElement value in entities.EnumerateArray())
        {
            if (!value.TryGetProperty("item_instance_id", out _)) continue;
            AssetContainerOwnerKind ownerKind = value.TryGetProperty("owner_kind", out JsonElement kind)
                ? Enum.Parse<AssetContainerOwnerKind>(kind.GetString()!, false)
                : AssetContainerOwnerKind.Actor;
            string ownerId = OptionalString(value, "owner_id") ?? actorId;
            AddRq1ItemState(value, ownerKind, ownerId, result);
        }
    }

    private static void AddRq1ItemState(
        JsonElement value,
        AssetContainerOwnerKind ownerKind,
        string ownerId,
        Dictionary<string, Rq1ItemFixtureState> result)
    {
        if (!value.TryGetProperty("durability", out _)
            && !value.TryGetProperty("maximum_durability", out _)
            && !value.TryGetProperty("version", out _)) return;
        string itemInstanceId = RequiredString(value, "item_instance_id");
        int? durability = value.TryGetProperty("durability", out JsonElement durabilityElement)
            && durabilityElement.ValueKind != JsonValueKind.Null
            ? durabilityElement.GetInt32()
            : null;
        int? maximum = value.TryGetProperty("maximum_durability", out JsonElement maximumElement)
            && maximumElement.ValueKind != JsonValueKind.Null
            ? maximumElement.GetInt32()
            : null;
        int version = value.TryGetProperty("version", out JsonElement versionElement)
            ? versionElement.GetInt32()
            : 1;
        result[itemInstanceId] = new Rq1ItemFixtureState(
            itemInstanceId,
            RequiredString(value, "item_type_id"),
            durability,
            maximum,
            version,
            ownerKind,
            ownerId);
    }

    private static void MergeRq1GameplayConfiguration(
        JsonElement state,
        List<TownGameplayAssetDefinitionConfiguration> assets,
        List<TownGameplayShopConfiguration> shops,
        List<TownGameplayListingConfiguration> listings,
        List<TownGameplayRecipeConfiguration> recipes,
        List<TownGameplayServiceConfiguration> services,
        List<TownGameplayRegionConfiguration> regions,
        List<TownGameplayFarmPlotConfiguration> farmPlots)
    {
        if (state.TryGetProperty("asset_definitions", out JsonElement definitions)
            && definitions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement definition in definitions.EnumerateArray())
                UpsertRq1Asset(assets, DeserializeRq1<TownGameplayAssetDefinitionConfiguration>(definition));
        }
        if (TryDeserializeRq1(state, "shop_override", out TownGameplayShopConfiguration? shop))
            UpsertRq1Shop(shops, shop!);
        if (TryDeserializeRq1(state, "listing", out TownGameplayListingConfiguration? listing))
            UpsertRq1Listing(listings, listing!);
        if (TryDeserializeRq1(state, "recipe", out TownGameplayRecipeConfiguration? recipe))
            UpsertRq1Recipe(recipes, recipe!);
        if (TryDeserializeRq1(state, "service", out TownGameplayServiceConfiguration? service))
            UpsertRq1Service(services, service!);
        if (TryDeserializeRq1(state, "region", out TownGameplayRegionConfiguration? region))
            UpsertRq1Region(regions, region!);
        if (TryDeserializeRq1(state, "farm_plot", out TownGameplayFarmPlotConfiguration? farmPlot))
            UpsertRq1FarmPlot(farmPlots, farmPlot!);
    }

    private static void ApplyRq1ShopHours(
        JsonElement setup,
        List<TownGameplayShopConfiguration> shops,
        long ticksPerDay)
    {
        if (setup.ValueKind != JsonValueKind.Object) return;
        string? propertyName = setup.TryGetProperty("shop_hours", out _)
            ? "shop_hours"
            : setup.TryGetProperty("shops", out _) ? "shops" : null;
        if (propertyName is null) return;
        foreach (JsonElement value in setup.GetProperty(propertyName).EnumerateArray())
        {
            string shopId = RequiredString(value, "shop_id");
            for (int index = 0; index < shops.Count; index++)
            {
                if (!StringComparer.Ordinal.Equals(shops[index].ShopId, shopId)) continue;
                long opens = value.TryGetProperty("opens_at_tick_of_day", out JsonElement opensAtDay)
                    ? opensAtDay.GetInt64()
                    : value.GetProperty("opens_at_tick").GetInt64();
                long closes = value.TryGetProperty("closes_at_tick_of_day", out JsonElement closesAtDay)
                    ? closesAtDay.GetInt64()
                    : value.GetProperty("closes_at_tick").GetInt64();
                shops[index] = shops[index] with
                {
                    OpensAtTickOfDay = Math.Clamp(opens, 0, ticksPerDay - 1),
                    ClosesAtTickOfDay = Math.Clamp(closes, 1, ticksPerDay)
                };
                break;
            }
        }
    }

    private static List<Rq1ContainerFixtureState> CollectRq1ContainerStates(
        JsonElement setup,
        JsonElement state,
        byte[] actorDecisionView,
        string actorId)
    {
        var result = new List<Rq1ContainerFixtureState>();
        AddRq1ContainerArray(setup, "containers", result);
        AddRq1IndividualContainerBalances(setup, result);
        AddRq1ContainerArray(state, "container_overrides", result);
        AddRq1ContainerArray(state, "related_containers", result);
        AddRq1ContainerArray(state, "target_snapshots", result);
        AddRq1ContainerArray(state, "entities", result);
        AddRq1ContainerArray(state, "objects", result);
        AddRq1ActorContainer(state, actorId, result);
        using JsonDocument viewDocument = JsonDocument.Parse(actorDecisionView);
        AddRq1ActorContainer(viewDocument.RootElement, actorId, result);
        return MergeRq1ContainerStates(result);
    }

    private static void AddRq1ContainerArray(
        JsonElement root,
        string propertyName,
        List<Rq1ContainerFixtureState> result)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(propertyName, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array) return;
        foreach (JsonElement value in array.EnumerateArray())
        {
            if (!TryParseRq1ContainerState(value, out Rq1ContainerFixtureState? state)) continue;
            result.Add(state!);
        }
    }

    private static void AddRq1IndividualContainerBalances(
        JsonElement root,
        List<Rq1ContainerFixtureState> result)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("container_balances", out JsonElement balances)
            || balances.ValueKind != JsonValueKind.Array) return;
        foreach (IGrouping<string, JsonElement> group in balances.EnumerateArray().GroupBy(
                     GetRq1ContainerBalanceOwnerKey,
                     StringComparer.Ordinal))
        {
            JsonElement first = group.First();
            result.Add(new Rq1ContainerFixtureState(
                Enum.Parse<AssetContainerOwnerKind>(RequiredString(first, "owner_kind"), false),
                RequiredString(first, "owner_id"),
                1,
                group.Select(value => new TownGameplayAssetBalanceConfiguration
                {
                    AssetId = RequiredString(value, "asset_id"),
                    Quantity = value.GetProperty("quantity").GetInt64()
                }).ToArray(),
                []));
        }
    }

    private static string GetRq1ContainerBalanceOwnerKey(JsonElement value) =>
        RequiredString(value, "owner_kind") + "\u001f" + RequiredString(value, "owner_id");

    private static bool TryParseRq1ContainerState(
        JsonElement value,
        out Rq1ContainerFixtureState? state)
    {
        state = null;
        if (value.ValueKind != JsonValueKind.Object) return false;
        string? ownerKind = OptionalString(value, "owner_kind");
        string? ownerId = OptionalString(value, "owner_id");
        if (ownerKind is null && value.TryGetProperty("kind", out JsonElement kindElement))
        {
            string? kind = kindElement.GetString();
            if (kind is "Actor" or "Shop" or "Warehouse") ownerKind = kind;
        }
        ownerId ??= OptionalString(value, "object_id");
        if (ownerKind is null || ownerId is null
            || !Enum.TryParse(ownerKind, false, out AssetContainerOwnerKind parsedKind)
            || !value.TryGetProperty("balances", out JsonElement balances)) return false;
        long revision = value.TryGetProperty("revision", out JsonElement revisionElement)
            ? revisionElement.GetInt64()
            : 1;
        string[] itemIds = value.TryGetProperty("item_instances", out JsonElement items)
            && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(GetJsonString).ToArray()
            : [];
        state = new Rq1ContainerFixtureState(
            parsedKind,
            ownerId,
            revision,
            ParseRq1Balances(balances),
            itemIds);
        return true;
    }

    private static TownGameplayAssetBalanceConfiguration[] ParseRq1Balances(JsonElement balances)
    {
        if (balances.ValueKind == JsonValueKind.Array)
            return balances.EnumerateArray().Select(value => new TownGameplayAssetBalanceConfiguration
            {
                AssetId = value.TryGetProperty("asset_id", out JsonElement asset)
                    ? asset.GetString()!
                    : RequiredString(value, "item_type_id"),
                Quantity = value.GetProperty("quantity").GetInt64()
            }).ToArray();
        if (balances.ValueKind == JsonValueKind.Object)
            return balances.EnumerateObject().Select(value => new TownGameplayAssetBalanceConfiguration
            {
                AssetId = value.Name,
                Quantity = value.Value.GetInt64()
            }).ToArray();
        throw new InvalidDataException("RQ1 container balances must be an object or array.");
    }

    private static void AddRq1ActorContainer(
        JsonElement root,
        string actorId,
        List<Rq1ContainerFixtureState> result)
    {
        JsonElement actor = root;
        if (root.ValueKind != JsonValueKind.Object) return;
        if (root.TryGetProperty("actor_state", out JsonElement actorState)) actor = actorState;
        else if (root.TryGetProperty("actor", out JsonElement actorValue)) actor = actorValue;
        else if (root.TryGetProperty("self", out JsonElement self)) actor = self;
        else if (root.TryGetProperty("self_state", out JsonElement selfState)) actor = selfState;
        if (actor.ValueKind != JsonValueKind.Object) return;

        long revision = 1;
        var balances = new Dictionary<string, long>(StringComparer.Ordinal);
        if (actor.TryGetProperty("balances", out JsonElement directBalances))
            AddRq1Balances(balances, ParseRq1Balances(directBalances));
        if (actor.TryGetProperty("container", out JsonElement container)
            && container.ValueKind == JsonValueKind.Object)
        {
            if (container.TryGetProperty("revision", out JsonElement containerRevision))
                revision = containerRevision.GetInt64();
            if (container.TryGetProperty("balances", out JsonElement containerBalances))
                AddRq1Balances(balances, ParseRq1Balances(containerBalances));
        }
        if (actor.TryGetProperty("inventory", out JsonElement inventory))
        {
            if (inventory.ValueKind == JsonValueKind.Object)
            {
                if (inventory.TryGetProperty("version", out JsonElement version)) revision = version.GetInt64();
                else if (inventory.TryGetProperty("revision", out JsonElement inventoryRevision))
                    revision = inventoryRevision.GetInt64();
                if (inventory.TryGetProperty("fungible_balances", out JsonElement fungible))
                    AddRq1Balances(balances, ParseRq1Balances(fungible));
                if (inventory.TryGetProperty("stacks", out JsonElement stacks))
                    AddRq1Balances(balances, ParseRq1Balances(stacks));
            }
            else if (inventory.ValueKind == JsonValueKind.Array)
                AddRq1Balances(balances, ParseRq1Balances(inventory));
        }
        if (actor.TryGetProperty("currency", out JsonElement currency))
        {
            if (currency.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in currency.EnumerateArray())
                {
                    string assetId = entry.TryGetProperty("asset_id", out JsonElement asset)
                        ? asset.GetString()!
                        : RequiredString(entry, "currency_id");
                    balances[assetId] = entry.GetProperty("quantity").GetInt64();
                }
            }
            else if (currency.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty entry in currency.EnumerateObject())
                    balances[entry.Name] = entry.Value.GetInt64();
            }
        }
        if (balances.Count == 0) return;
        result.Add(new Rq1ContainerFixtureState(
            AssetContainerOwnerKind.Actor,
            actorId,
            revision,
            balances.Select(value => new TownGameplayAssetBalanceConfiguration
            {
                AssetId = value.Key,
                Quantity = value.Value
            }).ToArray(),
            []));
    }

    private static void AddRq1Balances(
        Dictionary<string, long> target,
        IEnumerable<TownGameplayAssetBalanceConfiguration> source)
    {
        foreach (TownGameplayAssetBalanceConfiguration balance in source)
            target[balance.AssetId] = balance.Quantity;
    }

    private static List<Rq1ContainerFixtureState> MergeRq1ContainerStates(
        IEnumerable<Rq1ContainerFixtureState> source)
    {
        var result = new Dictionary<string, Rq1ContainerFixtureState>(StringComparer.Ordinal);
        foreach (Rq1ContainerFixtureState value in source)
        {
            string key = value.OwnerKind + "\u001f" + value.OwnerId;
            result[key] = value;
        }
        return result.Values.ToList();
    }

    private static void EnsureRq1ConfigurationAssets(
        List<TownGameplayAssetDefinitionConfiguration> assets,
        IEnumerable<TownGameplayListingConfiguration> listings,
        IEnumerable<TownGameplayRecipeConfiguration> recipes,
        IEnumerable<TownGameplayServiceConfiguration> services,
        IEnumerable<TownGameplayRegionConfiguration> regions,
        IEnumerable<TownGameplayFarmPlotConfiguration> farmPlots)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { "coin" };
        foreach (TownGameplayListingConfiguration value in listings) ids.Add(value.AssetId);
        foreach (TownGameplayRecipeConfiguration value in recipes)
        {
            if (value.RequiredAssetId is not null) ids.Add(value.RequiredAssetId);
            foreach (TownGameplayAssetAmountConfiguration amount in value.Inputs.Concat(value.Outputs))
                ids.Add(amount.AssetId);
        }
        foreach (TownGameplayServiceConfiguration value in services)
        {
            foreach (TownGameplayAssetAmountConfiguration amount in value.CustomerInputs
                         .Concat(value.ProviderInputs).Concat(value.CustomerOutputs))
                ids.Add(amount.AssetId);
        }
        foreach (TownGameplayRegionConfiguration value in regions) ids.Add(value.OutputAssetId);
        foreach (TownGameplayFarmPlotConfiguration value in farmPlots)
        {
            ids.Add(value.SeedAssetId);
            ids.Add(value.OutputAssetId);
        }
        EnsureRq1Assets(assets, ids);
    }

    private static string GetBalanceAssetId(TownGameplayAssetBalanceConfiguration value) => value.AssetId;
    private static string GetJsonString(JsonElement value) =>
        value.GetString() ?? throw new InvalidDataException("RQ1 string array contains null.");

    private static void EnsureRq1Assets(
        List<TownGameplayAssetDefinitionConfiguration> assets,
        IEnumerable<string> assetIds)
    {
        foreach (string assetId in assetIds)
        {
            if (string.IsNullOrWhiteSpace(assetId) || assets.Any(value =>
                    StringComparer.Ordinal.Equals(value.AssetId, assetId))) continue;
            assets.Add(new TownGameplayAssetDefinitionConfiguration
            {
                AssetId = assetId,
                StorageKind = "Fungible",
                MaximumDurability = null
            });
        }
    }

    private static void EnsureRq1Container(
        List<TownGameplayContainerConfiguration> containers,
        AssetContainerOwnerKind kind,
        string ownerId)
    {
        if (kind == AssetContainerOwnerKind.Actor || containers.Any(value =>
                StringComparer.Ordinal.Equals(value.OwnerKind, kind.ToString())
                && StringComparer.Ordinal.Equals(value.OwnerId, ownerId))) return;
        containers.Add(new TownGameplayContainerConfiguration
        {
            OwnerKind = kind.ToString(),
            OwnerId = ownerId,
            Balances = []
        });
    }

    private static T DeserializeRq1<T>(JsonElement value) where T : class =>
        JsonSerializer.Deserialize<T>(value.GetRawText())
        ?? throw new InvalidDataException("RQ1 configuration object could not be parsed as " + typeof(T).Name + ".");

    private static bool TryDeserializeRq1<T>(JsonElement root, string propertyName, out T? value)
        where T : class
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out JsonElement element)
            || element.ValueKind == JsonValueKind.Null) return false;
        value = DeserializeRq1<T>(element);
        return true;
    }

    private static void UpsertRq1Asset(
        List<TownGameplayAssetDefinitionConfiguration> values,
        TownGameplayAssetDefinitionConfiguration replacement)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(values[index].AssetId, replacement.AssetId)) continue;
            values[index] = replacement;
            return;
        }
        values.Add(replacement);
    }

    private static void UpsertRq1Container(
        List<TownGameplayContainerConfiguration> values,
        TownGameplayContainerConfiguration replacement)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(values[index].OwnerKind, replacement.OwnerKind)
                || !StringComparer.Ordinal.Equals(values[index].OwnerId, replacement.OwnerId)) continue;
            values[index] = replacement;
            return;
        }
        values.Add(replacement);
    }

    private static void UpsertRq1Shop(
        List<TownGameplayShopConfiguration> values,
        TownGameplayShopConfiguration replacement)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(values[index].ShopId, replacement.ShopId)) continue;
            values[index] = replacement;
            return;
        }
        values.Add(replacement);
    }

    private static void UpsertRq1Listing(
        List<TownGameplayListingConfiguration> values,
        TownGameplayListingConfiguration replacement)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(values[index].ListingId, replacement.ListingId)) continue;
            values[index] = replacement;
            return;
        }
        values.Add(replacement);
    }

    private static void UpsertRq1Recipe(
        List<TownGameplayRecipeConfiguration> values,
        TownGameplayRecipeConfiguration replacement)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(values[index].RecipeId, replacement.RecipeId)) continue;
            values[index] = replacement;
            return;
        }
        values.Add(replacement);
    }

    private static void UpsertRq1Service(
        List<TownGameplayServiceConfiguration> values,
        TownGameplayServiceConfiguration replacement)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(values[index].ServiceId, replacement.ServiceId)) continue;
            values[index] = replacement;
            return;
        }
        values.Add(replacement);
    }

    private static void UpsertRq1Region(
        List<TownGameplayRegionConfiguration> values,
        TownGameplayRegionConfiguration replacement)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(values[index].RegionId, replacement.RegionId)) continue;
            values[index] = replacement;
            return;
        }
        values.Add(replacement);
    }

    private static void UpsertRq1FarmPlot(
        List<TownGameplayFarmPlotConfiguration> values,
        TownGameplayFarmPlotConfiguration replacement)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(values[index].PlotId, replacement.PlotId)) continue;
            values[index] = replacement;
            return;
        }
        values.Add(replacement);
    }

    private static void UpsertRq1Restock(
        List<TownGameplayRestockConfiguration> values,
        TownGameplayRestockConfiguration replacement)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(values[index].RestockId, replacement.RestockId)) continue;
            values[index] = replacement;
            return;
        }
        values.Add(replacement);
    }

    private static FormalRq2PlanningFixture CreateRq2PlanningFixture(
        PreparedRq2Cell cell,
        NpcPlan? sourcePlan = null)
    {
        ActorId actor = cell.CandidateSet.ActorId;
        var objective = new AcquireItemObjective(new ItemTypeId("timber"), 1);
        TargetRef east = new("tree-east");
        TargetRef west = new("tree-west");
        var eastResult = new TargetTerminal(actor, east);
        var westResult = new TargetTerminal(actor, west);
        FormalDamageFixture actions = CreateDamageFixture(
            actor,
            [
                (east.Value, cell.FixtureId + "-tree-east-action", (GoalObjective)objective, (TargetRef?)east, (ResultPredicate)eastResult),
                (west.Value, cell.FixtureId + "-tree-west-action", (GoalObjective)objective, (TargetRef?)west, (ResultPredicate)westResult)
            ]);
        GameActionSpec initialAction = actions.Actions[west.Value];
        var goal = new NpcGoal(new GoalId("goal-acquire-timber"), objective);
        var step = new PlanStep(
            new PlanStepId("step-select-timber-source"),
            objective,
            initialAction,
            west,
            westResult);
        var template = new NpcPlan(new PlanId("plan-1"), actor, goal, 1, [step]);
        NpcPlan plan = sourcePlan ?? template;
        if (!plan.Equals(template))
            throw new InvalidDataException("Formal RQ2 source plan does not match the frozen planning fixture.");
        NpcState npc = new(
            actor,
            actions.NpcState.Personality,
            actions.NpcState.Knowledge,
            new NpcPlanningState([plan.Goal], plan),
            actions.NpcState.Social);
        ActorCognitionView view = ActorCognitionView.Create(
            actions.ActorState,
            npc,
            new PlanRuntime(plan));
        return new FormalRq2PlanningFixture(
            view,
            plan,
            actions.Catalogue,
            actions.Executor);
    }

    private static FormalDamageFixture CreateDamageFixture(
        ActorId actor,
        IReadOnlyList<(string TargetId, string GameActionId, GoalObjective Objective, TargetRef? PlanTarget, ResultPredicate Result)> candidates)
    {
        var axeType = new ItemTypeId("formal-axe");
        var axeInstanceId = new ItemInstanceId("formal-axe-" + actor.Value + "-" + candidates[0].GameActionId);
        var inventory = new InventoryState(actor, [new InstanceEntry(axeInstanceId)], 1);
        var equipment = new EquipmentState(actor, new InstanceHandItemRef(axeInstanceId), 1, inventory);
        var actorState = new SharedActorState(
            new ActorIdentity(actor, new ActorName(actor.Value), new ActorAge(30)),
            new ActorBodyState(actor, new Health(100, 100), new Satiety(60), new Spirit(60), Disease.Healthy),
            new ActorTraversalState(actor, MovementMode.Land),
            inventory,
            equipment);
        var capability = new CapabilityIdentity("cutting");
        var axe = new ItemInstance(axeInstanceId, axeType, 100, 1);
        var axeDefinition = new ItemDefinition(
            axeType,
            ItemStackability.Instanced,
            [new CapabilityContribution(capability, 1)],
            new DamageContribution(DamageType.Slashing, 10));
        var runtimes = new Dictionary<string, DamageAuthorityRuntime>(StringComparer.Ordinal);
        var actionIds = new Dictionary<string, GameActionId>(StringComparer.Ordinal);
        var actions = new Dictionary<string, GameActionSpec>(StringComparer.Ordinal);
        var catalogue = new List<FormalPlanningActionCandidate>();
        var targetSnapshots = new List<ActorVisibleTargetSpatialSnapshot>();
        var opportunities = new List<KnownDamageOpportunity>();
        foreach ((string targetId, string actionId, GoalObjective objective, TargetRef? planTarget, ResultPredicate result) in candidates)
        {
            var target = new TargetRef(targetId);
            var contract = new DamageContract(
                new ContractRef(target, "formal-damage"),
                1,
                new InteractionRange(2),
                new InteractionCapabilityRequirement(capability, 1),
                [DamageType.Slashing],
                [],
                []);
            var action = new GameActionSpec(
                actor,
                new InteractionBinding(
                    contract.ContractRef,
                    new ExpectedContractVersion(1),
                    capability,
                    new InstrumentRef(axeInstanceId.Value)),
                new DamageActionArguments(DamageType.Slashing));
            actions.Add(targetId, action);
            actionIds.Add(targetId, new GameActionId(actionId));
            runtimes.Add(targetId, new DamageAuthorityRuntime(
                target,
                contract,
                new WorldPosition(0, 0),
                new DestructibleState(10, 10),
                new DestructionProfile(
                    new TerminalRepresentationId(targetId + "-stump"),
                    [new DestructionYield(new ItemTypeId("timber"), 1)],
                    DestructionDropPolicy.ClaimantWorldDrop),
                [axe],
                [axeDefinition],
                new WorldDropId(targetId + "-drop")));
            catalogue.Add(new FormalPlanningActionCandidate(
                actionId,
                objective,
                planTarget,
                result,
                action));
            targetSnapshots.Add(new ActorVisibleTargetSpatialSnapshot(
                target,
                TargetKind.Tree,
                new WorldPosition(0, 0)));
            opportunities.Add(new KnownDamageOpportunity(
                contract.ContractRef,
                contract.Version,
                contract.InteractionRange,
                new KnownCapabilityRequirement(capability, 1),
                [new KnownDestructionYield(new ItemTypeId("timber"), 1)]));
        }
        var npc = new NpcState(
            actor,
            new NpcPersonalityState(
                new CognitiveFunctionProfile(0, 1, 0, 1, 0, 1, 0, 1),
                [new PersonalityTagId("formal"), new PersonalityTagId("practical")],
                []),
            new NpcKnowledgeState(
                new NpcKnownTargetSpatialState(targetSnapshots),
                new NpcKnownOpportunityState(opportunities, [], [])),
            new NpcPlanningState([], null));
        var context = new DamageValidationContext(actorState, new WorldPosition(0, 0), [], [], null);
        return new FormalDamageFixture(
            actorState,
            npc,
            new FormalPlanningActionCatalogue(actor, catalogue),
            new FormalDamageActorExecutor(actor, runtimes, actionIds, context),
            actions);
    }

    private static DecisionNeed CreateInFlightNeed(ActorCognitionView view, NpcPlan plan)
    {
        CurrentStepDecisionProblemDescriptor descriptor = DecisionProblemDescriptorBuilder.CreateCurrentStep(
            view,
            new DecisionProblemCode("formal_source_selection"));
        DecisionNeed need = DecisionNeed.Create(
            view.ActorId,
            plan.PlanId,
            view.CurrentStep.PlanStepId,
            new DecisionNeedKind("current_step_blocked"),
            descriptor,
            new DecisionNeedDiscoveryTrace(
                DecisionNeedDiscoveryRoute.HostRuntime,
                new DecisionNeedDiscoverySourceId("formal-rq2"),
                [new DecisionNeedDiscoveryNodeId("source-selection")]),
            new DecisionNeedWorldRevision(1),
            new SimTime(10));
        need.Queue();
        need.BeginInFlightAttempt();
        return need;
    }

    private static (DecisionNeedStore Store, DecisionNeed Need) CloneInFlightNeed(
        ActorCognitionView view,
        NpcPlan plan)
    {
        var store = new DecisionNeedStore();
        CurrentStepDecisionProblemDescriptor descriptor = DecisionProblemDescriptorBuilder.CreateCurrentStep(
            view,
            new DecisionProblemCode("formal_source_selection"));
        DecisionNeed need = ((RegisteredNew)store.Register(
            view.ActorId,
            plan.PlanId,
            view.CurrentStep.PlanStepId,
            new DecisionNeedKind("current_step_blocked"),
            descriptor,
            new DecisionNeedDiscoveryTrace(
                DecisionNeedDiscoveryRoute.HostRuntime,
                new DecisionNeedDiscoverySourceId("formal-rq2"),
                [new DecisionNeedDiscoveryNodeId("source-selection")]),
            new DecisionNeedWorldRevision(1),
            new SimTime(10))).Need;
        need.BeginInFlightAttempt();
        return (store, need);
    }

    private static FormalRq2CandidateSelectionProvenance CreateRq2Provenance() => new(
        FormalRq2IdentitySetting.Resolved("normalized-relevance-recency-importance-v1"),
        FormalRq2ConfigurationSetting.Resolved("weights=1:1:1;recency_base=0.995;ticks_per_hour=40"));

    private static PressureWorldRuntime CreatePressureRuntime(IEnumerable<PressureState> states)
    {
        PressureState[] snapshot = states.ToArray();
        return new PressureWorldRuntime(
            "formal-pressure-host-20260830",
            PressureDependencyIndex.Create("formal-dependency-index-20260830", []),
            snapshot,
            snapshot.Select(value => (IPressureEvaluator)new StableFormalPressureEvaluator(value)));
    }

    private static AuthorityCommitAffectedNodeProjector CreateAuthorityProjector() => new(
        AuthorityTargetAffectedNodeIndex.Create("formal-rq1-projection-20260830", []));

    private static FormalRq1Treatment ParseRq1Treatment(string value) => value switch
    {
        "agent_centric" => FormalRq1Treatment.AgentCentric,
        "event_centric" => FormalRq1Treatment.EventCentric,
        _ => throw new InvalidDataException("Unknown RQ1 condition token: " + value)
    };

    private static FormalRq2Treatment ParseRq2Treatment(string value) => value switch
    {
        "verbatim" => FormalRq2Treatment.Verbatim,
        "summary" => FormalRq2Treatment.Summary,
        _ => throw new InvalidDataException("Unknown RQ2 condition token: " + value)
    };

    private static IReadOnlyDictionary<string, byte[]> WithSuite(
        IReadOnlyDictionary<string, byte[]> source,
        string suiteArtifactId,
        byte[] suiteBytes)
    {
        var result = source.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
        result.Add(suiteArtifactId, suiteBytes);
        return result;
    }

    private static byte[] SerializeFreezeBundle(
        string revision,
        FormalExperimentSuiteManifest rq1,
        FormalExperimentSuiteManifest rq2)
    {
        string[] rq1Artifacts =
        [
            "preregistration", "repository_source_manifest", "model_profile", "request_protocol_catalogue",
            "rq1_pair_manifest", "rq1_public_fixture", "rq1_world_configuration", "rq1_opportunity_ledger", "rq1_test_case_ledger",
            "rq1_opportunity_test_case_map", "rq1_hidden_test_cases", "rq1_outcome_evaluator",
            "rq1_suite_manifest"
        ];
        string[] rq2Artifacts =
        [
            "preregistration", "repository_source_manifest", "model_profile", "request_protocol_catalogue",
            "rq2_pair_manifest", "rq2_pre_treatment_emotion", "rq2_public_fixture_bundle",
            "rq2_required_source_sets", "rq2_summary_registry", "rq2_summary_fidelity_validator",
            "rq2_hidden_predicates", "rq2_outcome_evaluator", "rq2_suite_manifest"
        ];
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", FormalCollectionFreezeGate.ProtocolVersion);
            writer.WriteString("state", "frozen_authorized");
            writer.WriteNull("tbd_reason");
            writer.WriteString("preregistration_artifact_version", PreregistrationVersion);
            writer.WriteString("repository_revision", revision);
            writer.WritePropertyName("authorized_rqs");
            writer.WriteStartArray();
            writer.WriteStringValue("rq1");
            writer.WriteStringValue("rq2");
            writer.WriteEndArray();
            writer.WritePropertyName("unresolved_input_ids");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WritePropertyName("rq_bindings");
            writer.WriteStartArray();
            WriteFreezeRq(writer, "rq1", rq1.ManifestHash, rq1Artifacts);
            WriteFreezeRq(writer, "rq2", rq2.ManifestHash, rq2Artifacts);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteFreezeRq(
        Utf8JsonWriter writer,
        string rq,
        string suiteHash,
        IEnumerable<string> artifactIds)
    {
        writer.WriteStartObject();
        writer.WriteString("rq", rq);
        writer.WriteString("suite_manifest_hash", suiteHash);
        writer.WriteString("runtime_version", RuntimeVersion);
        writer.WriteString("model_profile_id", ModelProfileId);
        writer.WritePropertyName("artifacts");
        writer.WriteStartArray();
        foreach (string artifactId in artifactIds.Order(StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("artifact_id", artifactId);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string RunGit(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Unable to inspect the formal Git checkout.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Git formal freeze check failed: " + error.Trim());
        return output;
    }

    private static PreparedRq1 BuildRq1(
        byte[] preregistration,
        byte[] sourceManifest,
        byte[] modelProfile)
    {
        byte[] worldConfiguration = File.ReadAllBytes("godot/Config/town_world.json");
        var opportunityEntries = new List<ActorOpportunityLedgerEntry>();
        var testCaseEntries = new List<Rq1TestCaseLedgerEntry>();
        var mappings = new List<FormalRq1OpportunityTestCaseMapEntry>();
        var hidden = new List<FormalRq1HiddenTestCase>();
        var pressureStates = new List<PressureState>();
        var scenarios = new List<PreparedRq1Scenario>();
        var blocks = new List<PreparedRq1Block>();
        for (int blockNumber = 1; blockNumber <= Rq1BlockCount; blockNumber++)
        {
            string blockToken = $"block_{blockNumber:D2}";
            string blockRoot = Path.Combine(Rq1CandidateRoot, blockToken);
            var blockOpportunityEntries = new List<ActorOpportunityLedgerEntry>(10);
            var blockTestCaseEntries = new List<Rq1TestCaseLedgerEntry>(10);
            var blockMappings = new List<FormalRq1OpportunityTestCaseMapEntry>(10);
            var blockHidden = new List<FormalRq1HiddenTestCase>(10);
            byte[] publicFixture = File.ReadAllBytes(Path.Combine(blockRoot, "public_fixture.json"));
            byte[] privateExpectations = File.ReadAllBytes(Path.Combine(blockRoot, "private_expectations.json"));
            using JsonDocument publicDocument = JsonDocument.Parse(publicFixture);
            using JsonDocument privateDocument = JsonDocument.Parse(privateExpectations);
            JsonElement publicRoot = publicDocument.RootElement;
            JsonElement privateRoot = privateDocument.RootElement;
            ValidateRq1BlockRoots(publicRoot, privateRoot, blockToken);

            JsonElement[] publicCases = publicRoot.GetProperty("cases").EnumerateArray().ToArray();
            Dictionary<string, JsonElement> privateByPressure = privateRoot.GetProperty("cases")
                .EnumerateArray()
                .ToDictionary(value => RequiredString(value, "pressure_id"), value => value, StringComparer.Ordinal);
            string[] duplicateActors = publicCases
                .GroupBy(value => RequiredString(value, "actor_id"), StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (duplicateActors.Length > 0)
                throw new InvalidDataException(
                    $"RQ1 {blockToken} requires ten actor-distinct simultaneous cases: {string.Join(", ", duplicateActors)}");

            byte[] authoritySetup = Encoding.UTF8.GetBytes(
                publicRoot.GetProperty("authority_setup").GetRawText());
            var blockScenarios = new List<PreparedRq1Scenario>(10);
            var blockPressureStates = new List<PressureState>(10);
            foreach (JsonElement publicCase in publicCases.OrderBy(value => value.GetProperty("slot").GetInt32()))
            {
                string pressureId = RequiredString(publicCase, "pressure_id");
                JsonElement privateCase = privateByPressure.GetValueOrDefault(pressureId);
                if (privateCase.ValueKind == JsonValueKind.Undefined)
                    throw new InvalidDataException($"RQ1 {blockToken} lacks private expectations for {pressureId}.");
                ValidateRq1CaseJoin(publicCase, privateCase, blockToken);

                string actorId = RequiredString(publicCase, "actor_id");
                string domain = RequiredString(publicCase, "domain");
                long decisionTick = publicCase.GetProperty("decision_tick").GetInt64();
                long deadlineTick = publicCase.GetProperty("deadline_tick").GetInt64();
                JsonElement matrix = privateCase.GetProperty("matrix");
                string taskFamily = RequiredString(matrix, "task_family");
                string admissionRole = RequiredString(matrix, "admission_role");
                JsonElement selectionInputs = publicCase.GetProperty("selection_inputs");
                string agentRankBand = RequiredString(
                    selectionInputs.GetProperty("actor_local_evidence"),
                    "rank_band");
                string eventEdgeKind = RequiredString(
                    selectionInputs.GetProperty("event_dependency"),
                    "edge_kind");
                string expectedAgentRankBand = RequiredString(matrix, "agent_rank_band");
                string eventRankBand = RequiredString(matrix, "event_rank_band");
                string expectedEventEdgeKind = RequiredString(matrix, "event_edge_kind");
                if (!StringComparer.Ordinal.Equals(agentRankBand, expectedAgentRankBand)
                    || !StringComparer.Ordinal.Equals(eventEdgeKind, expectedEventEdgeKind))
                    throw new InvalidDataException(
                        $"RQ1 {pressureId} public selection input differs from the balanced matrix.");
                ValidateRq1RankMapping(agentRankBand, eventRankBand, eventEdgeKind, pressureId);

                string expectedTerminal = RequiredString(privateCase, "expected_terminal");
                bool expectedDefer = expectedTerminal switch
                {
                    "JustifiedDefer" => true,
                    "AuthorityCommitted" => false,
                    _ => throw new InvalidDataException(
                        $"RQ1 {pressureId} has unknown expected terminal {expectedTerminal}.")
                };
                string? expectedActionFamily = expectedDefer
                    ? null
                    : RequiredString(privateCase, "expected_action_family");
                string? gameActionId = null;
                byte[] actionArguments = Encoding.UTF8.GetBytes("null");
                if (!expectedDefer)
                {
                    JsonElement actionTemplate = privateCase.GetProperty("action_template");
                    gameActionId = RequiredString(actionTemplate, "game_action_id");
                    if (!StringComparer.Ordinal.Equals(
                            RequiredString(actionTemplate, "action_family"),
                            expectedActionFamily))
                        throw new InvalidDataException($"RQ1 {pressureId} action-family labels disagree.");
                    actionArguments = Encoding.UTF8.GetBytes(
                        actionTemplate.GetProperty("arguments").GetRawText());
                }

                byte[] publicInput = BuildRq1PublicCaseInput(publicCase);
                byte[] actorDecisionView = Encoding.UTF8.GetBytes(
                    publicCase.GetProperty("actor_decision_view").GetRawText());
                byte[] authorityExecutionState = Encoding.UTF8.GetBytes(
                    publicCase.GetProperty("authority_execution_state").GetRawText());
                var opportunityId = new Rq1OpportunityId(pressureId);
                var testCaseId = new Rq1TestCaseId(pressureId + "-case");
                var opportunityEntry = new ActorOpportunityLedgerEntry(
                    opportunityId,
                    new ActorId(actorId),
                    new SimTime(decisionTick),
                    new SimTime(deadlineTick),
                    ComputeSha256(publicInput),
                    DependencyDegreeFromRank(eventRankBand));
                var testCaseEntry = new Rq1TestCaseLedgerEntry(testCaseId);
                var mappingEntry = new FormalRq1OpportunityTestCaseMapEntry(opportunityId, testCaseId);
                FormalRq1HiddenTestCase hiddenTestCase = expectedDefer
                    ? new FormalRq1HiddenTestCase(testCaseId, FormalRq1TerminalOutcomeKind.JustifiedDefer)
                    : new FormalRq1HiddenTestCase(
                        testCaseId,
                        FormalRq1TerminalOutcomeKind.AuthorityCommitted,
                        gameActionId!,
                        expectedActionFamily!);
                opportunityEntries.Add(opportunityEntry);
                testCaseEntries.Add(testCaseEntry);
                mappings.Add(mappingEntry);
                hidden.Add(hiddenTestCase);
                blockOpportunityEntries.Add(opportunityEntry);
                blockTestCaseEntries.Add(testCaseEntry);
                blockMappings.Add(mappingEntry);
                blockHidden.Add(hiddenTestCase);

                string profileToken = (domain + "-" + taskFamily).Replace('_', '-');
                var profileId = new PressureProfileId("formal-" + profileToken);
                var pressureState = new PressureState(
                    new PressureId(pressureId),
                    profileId,
                    1,
                    ComputeSha256("formal-pressure-evaluator:" + profileToken),
                    authorityExecutionState);
                var scenario = new PreparedRq1Scenario(
                    pressureId,
                    actorId,
                    domain,
                    taskFamily,
                    admissionRole,
                    agentRankBand,
                    eventEdgeKind,
                    decisionTick,
                    deadlineTick,
                    publicInput,
                    actorDecisionView,
                    authoritySetup,
                    authorityExecutionState,
                    expectedDefer,
                    expectedActionFamily,
                    gameActionId,
                    actionArguments);
                pressureStates.Add(pressureState);
                blockPressureStates.Add(pressureState);
                scenarios.Add(scenario);
                blockScenarios.Add(scenario);
            }
            if (privateByPressure.Count != publicCases.Length)
                throw new InvalidDataException($"RQ1 {blockToken} public/private case sets differ.");
            var blockOpportunityLedger = new ActorOpportunityLedger(
                $"brackenford-rq1-opportunities-{blockToken}-current",
                blockOpportunityEntries);
            var blockTestCaseLedger = new Rq1TestCaseLedger(
                $"brackenford-rq1-test-cases-{blockToken}-current",
                blockTestCaseEntries);
            var blockMapping = new FormalRq1OpportunityTestCaseMap(
                blockOpportunityLedger,
                blockTestCaseLedger,
                blockMappings);
            blocks.Add(new PreparedRq1Block(
                blockToken,
                RequiredString(publicRoot, "fixture_id"),
                ParseRq1ConditionOrder(RequiredString(privateRoot, "condition_order")),
                publicFixture,
                blockScenarios,
                blockPressureStates,
                blockOpportunityLedger,
                blockTestCaseLedger,
                blockMapping,
                blockHidden));
        }
        ValidateRq1DatasetBalance(blocks, scenarios);
        ValidateRq1ProductActions(scenarios);

        var opportunityLedger = new ActorOpportunityLedger(
            "brackenford-rq1-opportunities-30-block-current",
            opportunityEntries);
        var testCaseLedger = new Rq1TestCaseLedger(
            "brackenford-rq1-test-cases-30-block-current",
            testCaseEntries);
        var mapping = new FormalRq1OpportunityTestCaseMap(
            opportunityLedger,
            testCaseLedger,
            mappings);
        FormalRq1PressureManifest pressureManifest = CreatePressureRuntime(pressureStates).CreateManifest();
        var dispatch = new FormalRq1DispatchConfiguration(
            "formal-rq1-dispatch-b4-30-block-current",
            240,
            4,
            1,
            [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)],
            new FormalRq1RetryClassificationPolicy(
                "formal-rq1-transient-transport-retry-current",
                [
                    new FormalRq1TransportFailureCode(LiveRemoteFailureKind.Timeout.ToString()),
                    new FormalRq1TransportFailureCode(LiveRemoteFailureKind.NetworkFailure.ToString()),
                    new FormalRq1TransportFailureCode(LiveRemoteFailureKind.HttpFailure.ToString()),
                    new FormalRq1TransportFailureCode(LiveRemoteFailureKind.ResponseBodyTooLarge.ToString()),
                    new FormalRq1TransportFailureCode(LiveRemoteFailureKind.OutputTokenLimitReached.ToString()),
                    new FormalRq1TransportFailureCode(LiveRemoteFailureKind.InvalidResponseEnvelope.ToString())
                ]));
        ValidateRq1AdmissionSelections(blocks, dispatch);
        var projection = new AuthorityCommitAffectedNodeProjector(
            AuthorityTargetAffectedNodeIndex.Create("formal-rq1-projection-20260830", []));
        var protocol = new FormalRq1RequestProtocolManifestEntry(
            RemotePlannerRequestKind.PlanlessStrategic,
            RemotePlanlessStrategicProtocol.ProtocolVersion,
            Rq1ContextBuilderVersion);

        byte[] opportunityBytes = opportunityLedger.GetCanonicalBytes();
        byte[] testCaseBytes = testCaseLedger.GetCanonicalBytes();
        byte[] mappingBytes = mapping.GetCanonicalBytes();
        byte[] hiddenBytes = SerializeRq1Hidden(hidden);
        byte[] outcomeEvaluator = File.ReadAllBytes(
            "godot/Src/Alice.Cognition/FormalRq1ExperimentClosure.cs");
        var files = new List<PreparedFile>
        {
            new("common/preregistration.md", preregistration),
            new("common/repository_source_manifest.json", sourceManifest),
            new("common/model_profile.json", modelProfile),
            new("rq1/world_configuration.json", worldConfiguration),
            new("rq1/opportunity_ledger.json", opportunityBytes),
            new("rq1/test_case_ledger.json", testCaseBytes),
            new("rq1/opportunity_test_case_map.json", mappingBytes),
            new("rq1/hidden_test_cases.json", hiddenBytes),
            new("rq1/outcome_evaluator.source.txt", outcomeEvaluator)
        };
        var entries = new List<FormalExperimentSuitePairEntry>();
        var pairAssets = new List<PreparedPairAssets>();
        foreach (PreparedRq1Block block in blocks)
        {
            byte[] blockOpportunityBytes = block.OpportunityLedger.GetCanonicalBytes();
            byte[] blockTestCaseBytes = block.TestCaseLedger.GetCanonicalBytes();
            byte[] blockMappingBytes = block.Mapping.GetCanonicalBytes();
            byte[] blockHiddenBytes = SerializeRq1Hidden(block.HiddenTestCases);
            files.Add(new PreparedFile(
                $"rq1/blocks/{block.BlockToken}/public_fixture.json",
                block.PublicFixture));
            files.Add(new PreparedFile(
                $"rq1/blocks/{block.BlockToken}/opportunity_ledger.json",
                blockOpportunityBytes));
            files.Add(new PreparedFile(
                $"rq1/blocks/{block.BlockToken}/test_case_ledger.json",
                blockTestCaseBytes));
            files.Add(new PreparedFile(
                $"rq1/blocks/{block.BlockToken}/opportunity_test_case_map.json",
                blockMappingBytes));
            files.Add(new PreparedFile(
                $"rq1/blocks/{block.BlockToken}/hidden_test_cases.json",
                blockHiddenBytes));
            FormalRq1PressureManifest blockPressureManifest =
                CreatePressureRuntime(block.PressureStates).CreateManifest();
            FormalRq1ConditionManifest agent = CreateRq1Manifest(
                $"rq1-agent-{block.BlockToken}-repeat-01",
                FormalRq1Treatment.AgentCentric,
                block.OpportunityLedger.LedgerId,
                dispatch,
                blockPressureManifest,
                projection.BindingContentHash,
                protocol);
            FormalRq1ConditionManifest eventCentric = CreateRq1Manifest(
                $"rq1-event-{block.BlockToken}-repeat-01",
                FormalRq1Treatment.EventCentric,
                block.OpportunityLedger.LedgerId,
                dispatch,
                blockPressureManifest,
                projection.BindingContentHash,
                protocol);
            var pair = new FormalRq1MatchedPairManifest(agent, eventCentric);
            byte[] pairBytes = pair.GetCanonicalBytes();
            string pairPath = $"rq1/pairs/{block.BlockToken}_repeat_01.json";
            files.Add(new PreparedFile(pairPath, pairBytes));
            var artifacts = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["preregistration"] = preregistration,
                ["repository_source_manifest"] = sourceManifest,
                ["model_profile"] = modelProfile,
                ["request_protocol_catalogue"] = RemotePlanlessStrategicProtocol.GetToolCatalogueUtf8(),
                ["rq1_pair_manifest"] = pairBytes,
                ["rq1_public_fixture"] = block.PublicFixture,
                ["rq1_world_configuration"] = worldConfiguration,
                ["rq1_opportunity_ledger"] = blockOpportunityBytes,
                ["rq1_test_case_ledger"] = blockTestCaseBytes,
                ["rq1_opportunity_test_case_map"] = blockMappingBytes,
                ["rq1_hidden_test_cases"] = blockHiddenBytes,
                ["rq1_outcome_evaluator"] = outcomeEvaluator
            };
            string pairId = $"rq1-{block.BlockToken}-repeat-01";
            entries.Add(new FormalExperimentSuitePairEntry(
                pairId,
                block.FixtureId,
                "balanced_multi_domain",
                null,
                null,
                null,
                null,
                "repeat-01",
                pair.PairManifestHash,
                block.ConditionOrder,
                Bind(artifacts)));
            pairAssets.Add(new PreparedPairAssets(
                pairId,
                pairPath,
                artifacts,
                pair,
                block.Scenarios,
                block.PressureStates,
                new PreparedRq1ScoringInputs(
                    block.OpportunityLedger,
                    block.TestCaseLedger,
                    block.Mapping,
                    block.HiddenTestCases)));
        }
        var suite = new FormalExperimentSuiteManifest(
            "brackenford-formal-rq1-suite-current",
            FormalExperimentRq.Rq1,
            PreregistrationVersion,
            entries);
        return new PreparedRq1(
            suite,
            files,
            pairAssets,
            opportunityLedger,
            testCaseLedger,
            mapping,
            hidden,
            pressureManifest,
            dispatch,
            scenarios,
            pressureStates);
    }

    private static async Task<PreparedRq2> BuildRq2Async(
        byte[] preregistration,
        byte[] sourceManifest,
        byte[] modelProfile,
        CancellationToken cancellationToken)
    {
        var cells = new List<PreparedRq2Cell>();
        int index = 0;
        foreach (string stratum in Rq2Strata)
        {
            foreach (string tier in Rq2Tiers)
            {
                cells.Add(CreateRq2Cell(index, stratum, tier));
                index++;
            }
        }

        List<FrozenSummaryArtifact> summaryArtifacts;
        List<SummaryGenerationEvidence> generationEvidence;
        if (!TryLoadCompleteSummaryBatch(cells, out summaryArtifacts, out generationEvidence))
        {
            string? apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("DEEPSEEK_API_KEY is required for formal Summary generation.");
            summaryArtifacts = [];
            generationEvidence = [];
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
            const int summaryConcurrency = 4;
            for (int batchStart = 0; batchStart < cells.Count; batchStart += summaryConcurrency)
            {
                int batchCount = Math.Min(summaryConcurrency, cells.Count - batchStart);
                var tasks = new List<Task<(FrozenSummaryArtifact Artifact, SummaryGenerationEvidence Evidence)>>(batchCount);
                for (int offset = 0; offset < batchCount; offset++)
                    tasks.Add(GenerateSummaryAsync(http, apiKey, cells[batchStart + offset], cancellationToken));
                (FrozenSummaryArtifact Artifact, SummaryGenerationEvidence Evidence)[] results =
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                for (int offset = 0; offset < results.Length; offset++)
                {
                    summaryArtifacts.Add(results[offset].Artifact);
                    generationEvidence.Add(results[offset].Evidence);
                    PreparedRq2Cell cell = cells[batchStart + offset];
                    Console.WriteLine($"FORMAL_SUMMARY_PROGRESS={batchStart + offset + 1}/{cells.Count} cell={cell.FixtureId}");
                }
            }
        }

        var registry = new FrozenSummaryArtifactRegistry(
            new FrozenSummaryProfileVersion(SummaryProfile),
            summaryArtifacts,
            "brackenford-formal-summary-registry-current");
        byte[] registryBytes = registry.GetCanonicalBytes();
        byte[] fixtureBundle = BuildRq2FixtureBundle(cells);
        byte[] summaryValidator = File.ReadAllBytes(
            "godot/Src/Alice.Cognition/FormalRq2ExperimentClosure.cs");
        byte[] outcomeEvaluator = summaryValidator;
        var files = new List<PreparedFile>
        {
            new("rq2/fixture_bundle.json", fixtureBundle),
            new("rq2/summary_registry.json", registryBytes),
            new("rq2/summary_generation_evidence.json", SerializeSummaryGenerationEvidence(generationEvidence)),
            new("rq2/summary_fidelity_validator.source.txt", summaryValidator),
            new("rq2/outcome_evaluator.source.txt", outcomeEvaluator)
        };
        for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
        {
            files.Add(new PreparedFile(
                $"rq2/candidate_sets/{cells[cellIndex].FixtureId}.json",
                cells[cellIndex].CandidateSet.GetCanonicalBytes()));
            files.Add(new PreparedFile(
                $"rq2/summaries/{cells[cellIndex].FixtureId}.json",
                summaryArtifacts[cellIndex].GetCanonicalBytes()));
        }

        var entries = new List<FormalExperimentSuitePairEntry>();
        var pairAssets = new List<PreparedPairAssets>();
        foreach (PreparedRq2Cell cell in cells)
        {
            FrozenSummaryArtifact summaryArtifact = summaryArtifacts.Single(value =>
                value.CandidateSet.CandidateSetId.Value == cell.CandidateSet.CandidateSetId.Value);
            for (int repeat = 1; repeat <= Rq2RepeatsPerCell; repeat++)
            {
                FormalRq2MatchedPairManifest pair = CreateRq2PairManifest(registry, cell, repeat);
                byte[] pairBytes = pair.GetCanonicalBytes();
                byte[] emotionBytes = FormalRq2PreTreatmentEmotionEvidence
                    .CreateNoEmotion(cell.CandidateSet).GetCanonicalBytes();
                byte[] requiredBytes = cell.RequiredSources.GetCanonicalBytes();
                byte[] hiddenBytes = SerializeRq2Hidden(cell.HiddenPredicate);
                var artifacts = new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    ["preregistration"] = preregistration,
                    ["repository_source_manifest"] = sourceManifest,
                    ["model_profile"] = modelProfile,
                    ["request_protocol_catalogue"] = RemotePlannerProtocol.GetToolCatalogueUtf8(),
                    ["rq2_pair_manifest"] = pairBytes,
                    ["rq2_pre_treatment_emotion"] = emotionBytes,
                    ["rq2_public_fixture_bundle"] = fixtureBundle,
                    ["rq2_required_source_sets"] = requiredBytes,
                    ["rq2_summary_registry"] = registryBytes,
                    ["rq2_summary_fidelity_validator"] = summaryValidator,
                    ["rq2_hidden_predicates"] = hiddenBytes,
                    ["rq2_outcome_evaluator"] = outcomeEvaluator
                };
                string pairId = $"rq2-{cell.FixtureId}-repeat-{repeat:D2}";
                string pairPath = $"rq2/pairs/{pairId}.json";
                files.Add(new PreparedFile(pairPath, pairBytes));
                string[] order = (cell.Index + repeat) % 2 == 0
                    ? ["verbatim", "summary"]
                    : ["summary", "verbatim"];
                entries.Add(new FormalExperimentSuitePairEntry(
                    pairId,
                    cell.FixtureId,
                    cell.Stratum,
                    cell.Tier,
                    cell.CandidateSet.CandidateSetId.Value,
                    summaryArtifact.ArtifactId.Value,
                    summaryArtifact.ArtifactVersion.Value,
                    $"repeat-{repeat:D2}",
                    pair.PairManifestHash,
                    order,
                    Bind(artifacts)));
                pairAssets.Add(new PreparedPairAssets(pairId, pairPath, artifacts));
            }
        }
        var suite = new FormalExperimentSuiteManifest(
            "brackenford-formal-rq2-suite-current",
            FormalExperimentRq.Rq2,
            PreregistrationVersion,
            entries);
        return new PreparedRq2(suite, files, pairAssets, cells, registry);
    }

    private static bool TryLoadCompleteSummaryBatch(
        IReadOnlyList<PreparedRq2Cell> cells,
        out List<FrozenSummaryArtifact> artifacts,
        out List<SummaryGenerationEvidence> evidence)
    {
        artifacts = [];
        evidence = [];
        try
        {
            string evidencePath = Path.Combine(Root, "rq2", "summary_generation_evidence.json");
            using JsonDocument evidenceDocument = JsonDocument.Parse(File.ReadAllBytes(evidencePath));
            var evidenceByFixture = new Dictionary<string, SummaryGenerationEvidence>(StringComparer.Ordinal);
            foreach (JsonElement record in evidenceDocument.RootElement.GetProperty("records").EnumerateArray())
            {
                string fixtureId = RequiredString(record, "fixture_id");
                if (!evidenceByFixture.TryAdd(fixtureId, new SummaryGenerationEvidence(
                        fixtureId,
                        OptionalString(record, "provider_response_id"),
                        RequiredString(record, "request_hash"),
                        RequiredString(record, "response_hash"),
                        RequiredString(record, "decoded_tool_input_hash"),
                        OptionalDirectInt64(record, "input_tokens"),
                        OptionalDirectInt64(record, "output_tokens"),
                        record.GetProperty("duration_milliseconds").GetInt64(),
                        RequiredString(record, "summary_artifact_id"))))
                    throw new InvalidDataException("Formal Summary evidence contains duplicate fixtures.");
            }
            foreach (PreparedRq2Cell cell in cells)
            {
                string path = Path.Combine(Root, "rq2", "summaries", cell.FixtureId + ".json");
                FrozenSummaryArtifact artifact = LoadSummaryArtifact(path, cell.CandidateSet);
                SummaryGenerationEvidence record = evidenceByFixture[cell.FixtureId];
                if (!StringComparer.Ordinal.Equals(record.SummaryArtifactId, artifact.ArtifactId.Value)
                    || !artifact.Claims.SelectMany(value => value.SourceIds)
                        .Contains(cell.RequiredSources.SourceIds.Single()))
                    throw new InvalidDataException("Formal Summary evidence does not bind its exact artifact.");
                artifacts.Add(artifact);
                evidence.Add(record);
            }
            if (evidenceByFixture.Count != cells.Count)
                throw new InvalidDataException($"Formal Summary evidence is not one complete {cells.Count}-cell batch.");
            Console.WriteLine("FORMAL_SUMMARY_BATCH=REUSED_AND_VERIFIED");
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                           or JsonException
                                           or InvalidDataException
                                           or KeyNotFoundException
                                           or ArgumentException)
        {
            artifacts = [];
            evidence = [];
            Console.WriteLine("FORMAL_SUMMARY_BATCH=REGENERATE reason=" + exception.GetType().Name);
            return false;
        }
    }

    private static FrozenSummaryArtifact LoadSummaryArtifact(
        string path,
        DecisionMemoryCandidateSet candidateSet)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        if (!StringComparer.Ordinal.Equals(
                RequiredString(root, "candidate_set_id"),
                candidateSet.CandidateSetId.Value))
            throw new InvalidDataException("Frozen Summary candidate set changed.");
        var claims = new List<FrozenSummaryClaim>();
        foreach (JsonElement claim in root.GetProperty("claims").EnumerateArray())
        {
            claims.Add(new FrozenSummaryClaim(
                claim.GetProperty("ordinal").GetInt64(),
                Convert.FromBase64String(RequiredString(claim, "content_base64")),
                ParseEvidenceStatus(RequiredString(claim, "evidence_status")),
                ParseSourceIds(claim, "source_ids"),
                ParseSourceIds(claim, "supersedes_source_ids"),
                ParseSourceIds(claim, "conflicts_with_source_ids")));
        }
        FrozenSummaryArtifact artifact = FrozenSummaryArtifact.Create(
            candidateSet,
            new FrozenSummaryProfileVersion(RequiredString(root, "profile_version")),
            new FrozenSummaryArtifactVersion(RequiredString(root, "artifact_version")),
            claims);
        if (!artifact.GetCanonicalBytes().AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("Frozen Summary bytes are not canonical for the current candidate set.");
        return artifact;
    }

    private static DecisionMemorySourceId[] ParseSourceIds(JsonElement root, string propertyName) =>
        root.GetProperty(propertyName).EnumerateArray()
            .Select(value => new DecisionMemorySourceId(value.GetString()!))
            .ToArray();

    private static DecisionMemoryEvidenceStatus ParseEvidenceStatus(string value) => value switch
    {
        "current" => DecisionMemoryEvidenceStatus.Current,
        "stale" => DecisionMemoryEvidenceStatus.Stale,
        "superseded" => DecisionMemoryEvidenceStatus.Superseded,
        "uncertain" => DecisionMemoryEvidenceStatus.Uncertain,
        _ => throw new InvalidDataException("Frozen Summary evidence status is invalid.")
    };

    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? OptionalDirectInt64(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long parsed)
            ? parsed
            : null;

    internal static void ValidateRq2FixtureDesign()
    {
        var previousTokenCount = new Dictionary<string, int>(StringComparer.Ordinal);
        int index = 0;
        foreach (string stratum in Rq2Strata)
        {
            foreach (string tier in Rq2Tiers)
            {
                PreparedRq2Cell cell = CreateRq2Cell(index, stratum, tier);
                FormalRq2PlanningFixture composed = CreateRq2PlanningFixture(cell);
                FormalRq2PlanningFixture isolated = CreateRq2PlanningFixture(cell, composed.Plan);
                if (!ReferenceEquals(isolated.Plan, composed.Plan)
                    || !ReferenceEquals(isolated.View.CurrentStep, composed.Plan.Steps[0])
                    || ReferenceEquals(isolated.Executor, composed.Executor))
                    throw new InvalidDataException($"{cell.FixtureId} does not isolate execution while preserving its current-plan binding.");
                Console.WriteLine(
                    $"RQ2_FIXTURE_SIZE={cell.FixtureId} records={cell.CandidateSet.RankedSlices.Count} bytes={cell.FullVerbatimBytes} tokens={cell.FullVerbatimTokens} sha256={ComputeSha256(cell.CandidateSet.GetCanonicalBytes())}");
                if (!cell.CandidateSet.SourceIds.Contains(cell.RequiredSources.SourceIds[0]))
                    throw new InvalidDataException($"{cell.FixtureId} is missing its required source.");
                if (previousTokenCount.TryGetValue(stratum, out int previous)
                    && cell.FullVerbatimTokens <= previous)
                    throw new InvalidDataException($"{stratum} load tiers do not increase monotonically.");
                previousTokenCount[stratum] = cell.FullVerbatimTokens;
                MemoryPacketBuildOutcome bounded = MemoryPacketBuilders.BuildVerbatim(
                    cell.CandidateSet,
                    new FormalApproximateTokenCounter(),
                    new MemoryPacketTokenCeiling(8192),
                    new MemoryPacketTokenizerVersion("utf8_bytes_div4_v1"));
                if (bounded is not MemoryPacketBuildSuccess boundedSuccess)
                    throw new InvalidDataException($"{cell.FixtureId} cannot build its bounded Verbatim packet.");
                DecisionMemoryId requiredMemory = cell.CandidateSet.RankedSlices.Single(value =>
                    value.SourceIds.Contains(cell.RequiredSources.SourceIds[0])).MemoryId;
                if (!boundedSuccess.Packet.IncludedMemoryIds.Contains(requiredMemory))
                    throw new InvalidDataException($"{cell.FixtureId} drops its required decision evidence at the frozen ceiling.");
                index++;
            }
        }
    }

    private static PreparedRq2Cell CreateRq2Cell(
        int index,
        string stratum,
        string tier,
        string fixturePrefix = "rq2")
    {
        string token = stratum.Replace('_', '-');
        string fixtureId = $"{fixturePrefix}-{token}-{tier.ToLowerInvariant()}";
        string familyId = $"{fixturePrefix}-{token}-{tier.ToLowerInvariant()}";
        ActorId actor = new("formal-rq2-actor");
        int sliceCount = tier switch
        {
            "T1" => 25,
            "T2" => 37,
            "T3" => 46,
            "T4" => 70,
            "T5" => 92,
            "T6" => 118,
            _ => throw new ArgumentOutOfRangeException(nameof(tier))
        };
        int baseRelevantRank = stratum switch
        {
            "simple_current_state" => 0,
            "stale_state" => 4,
            "conflicting_reports" => 8,
            "commitment_lifecycle" => 12,
            "failed_plan_revision" => 18,
            _ => 23
        };
        int tierOffset = tier switch
        {
            "T1" => 0,
            "T2" => 2,
            "T3" => 4,
            "T4" => 6,
            "T5" => 8,
            "T6" => 10,
            _ => throw new ArgumentOutOfRangeException(nameof(tier))
        };
        int relevantRank = baseRelevantRank + tierOffset;
        string requiredSource = OpaqueSourceId(familyId, relevantRank);
        bool east = Array.IndexOf(Rq2Strata, stratum) % 2 == 0;
        string correctTarget = east ? "tree-east" : "tree-west";
        DecisionMemoryCandidateSet set = CreateNestedCandidateSet(
            actor,
            familyId,
            stratum,
            sliceCount,
            relevantRank,
            correctTarget);
        var required = new FormalRq2RequiredSourceSet([new DecisionMemorySourceId(requiredSource)]);
        string actionId = fixtureId + (east ? "-tree-east-action" : "-tree-west-action");
        var hidden = new FormalRq2HiddenOutcomePredicate(
            new FormalRq2TestCaseId(fixtureId + "-case"),
            FormalRq2TerminalOutcomeKind.AuthorityCommitted,
            actionId);
        (int packetBytes, int packetTokens) = MeasureFullVerbatim(set);
        ValidateTierSize(tier, packetTokens);
        return new PreparedRq2Cell(
            index,
            fixtureId,
            stratum,
            tier,
            set,
            required,
            hidden,
            correctTarget,
            packetBytes,
            packetTokens);
    }

    private static DecisionMemoryCandidateSet CreateNestedCandidateSet(
        ActorId actor,
        string familyId,
        string stratum,
        int sliceCount,
        int relevantRank,
        string correctTarget)
    {
        var slices = new List<DecisionMemorySlice>(sliceCount);
        for (int rank = 0; rank < sliceCount; rank++)
        {
            bool relevant = rank == relevantRank;
            string sourceId = OpaqueSourceId(familyId, rank);
            string content = relevant
                ? RelevantFact(stratum, correctTarget)
                : BackgroundTownMemory(stratum, rank);
            DecisionMemorySourceId[] supersedes = stratum is "stale_state" or "failed_plan_revision"
                && relevant
                ? [new DecisionMemorySourceId(OpaqueSourceId(familyId, rank - 1))]
                : [];
            DecisionMemorySourceId[] conflicts = stratum == "conflicting_reports" && relevant
                ? [new DecisionMemorySourceId(OpaqueSourceId(familyId, rank - 1))]
                : [];
            slices.Add(DecisionMemorySlice.Create(
                actor,
                new DecisionMemoryKind(relevant ? "decision_fact" : "episodic"),
                new SimTime(10_000 - rank * 40),
                new DecisionMemoryProjectorVersion("formal_rq2_v1"),
                rank,
                relevant ? DecisionMemoryEvidenceStatus.Current : DecisionMemoryEvidenceStatus.Uncertain,
                [new DecisionMemorySourceId(sourceId)],
                supersedes,
                conflicts,
                Encoding.UTF8.GetBytes(content)));
        }
        return DecisionMemoryCandidateSet.Create(slices);
    }

    private static string OpaqueSourceId(string familyId, int rank) =>
        "src-" + ComputeSha256(Encoding.UTF8.GetBytes($"{familyId}\u001f{rank:D3}"))[..24];

    private static string RelevantFact(string stratum, string correctTarget) => stratum switch
    {
        "simple_current_state" => $"Current direct inspection confirms that {correctTarget} is the usable timber source for the blocked acquisition plan.",
        "stale_state" => $"The earlier source report is stale. This newer direct inspection supersedes it and confirms {correctTarget} as reachable.",
        "conflicting_reports" => $"Two reports conflict. The latest direct inspection is current and confirms that only {correctTarget} is usable now.",
        "commitment_lifecycle" => $"The supply commitment remains active, and its currently valid fulfillment source is {correctTarget}.",
        "failed_plan_revision" => $"The previous source attempt failed after the world changed. This current observation supersedes it and supports revising toward {correctTarget}.",
        _ => $"The emotionally salient incident is unrelated to the blocked acquisition. Current direct evidence identifies {correctTarget} as the relevant source."
    };

    private static string BackgroundTownMemory(string stratum, int rank)
    {
        string[] people = ["Mara", "Jonah", "Elin", "Bram", "Quinn", "Silas", "Rosa", "Iris"];
        string[] places = ["market lane", "mill yard", "clinic porch", "south bridge", "garden path", "fishing bank", "smithy court", "town square"];
        string[] work = ["delivery timing", "tool maintenance", "meal planning", "weather preparation", "shop inventory", "household repairs", "crop inspection", "road traffic"];
        string person = people[rank % people.Length];
        string place = places[(rank * 3 + 1) % places.Length];
        string topic = work[(rank * 5 + 2) % work.Length];
        string status = (rank % 4) switch
        {
            0 => "The observation was direct but has no bearing on the current timber choice.",
            1 => "The report came from ordinary conversation and remains uncertain.",
            2 => "The event was witnessed nearby and concerns a different household.",
            _ => "The note records routine town context rather than a current resource fact."
        };
        return $"Town memory {rank:D2} for the {stratum.Replace('_', ' ')} fixture. {person} was seen near the {place} discussing {topic}. "
            + $"The account mentions the morning schedule, a delayed errand, changing foot traffic, and a minor disagreement over shared work. {status} "
            + "It preserves who was present, where the event occurred, what was publicly visible, and why the actor considered it memorable. "
            + "No participant inspected either candidate timber source during this episode, and no valid resource availability conclusion follows from it.";
    }

    private static (int Bytes, int Tokens) MeasureFullVerbatim(DecisionMemoryCandidateSet set)
    {
        var counter = new FormalApproximateTokenCounter();
        MemoryPacketBuildOutcome outcome = MemoryPacketBuilders.BuildVerbatim(
            set,
            counter,
            new MemoryPacketTokenCeiling(100_000),
            counter.TokenizerVersion);
        if (outcome is not MemoryPacketBuildSuccess success
            || success.Packet.IncludedMemoryIds.Count != set.RankedSlices.Count)
            throw new InvalidDataException("Unable to measure the complete RQ2 candidate packet.");
        return (success.Packet.GetModelVisibleBytes().Length, success.Packet.ConsumedTokens);
    }

    private static void ValidateTierSize(string tier, int tokens)
    {
        (int minimum, int maximum) = tier switch
        {
            "T1" => (4915, 5734),
            "T2" => (7373, 8192),
            "T3" => (9011, 10650),
            "T4" => (13926, 16384),
            "T5" => (18000, 23000),
            "T6" => (23000, 30000),
            _ => throw new ArgumentOutOfRangeException(nameof(tier))
        };
        if (tokens < minimum || tokens > maximum)
            throw new InvalidDataException($"{tier} complete Verbatim size {tokens} is outside [{minimum}, {maximum}].");
    }

    private static async Task<(FrozenSummaryArtifact Artifact, SummaryGenerationEvidence Evidence)> GenerateSummaryAsync(
        HttpClient http,
        string apiKey,
        PreparedRq2Cell cell,
        CancellationToken cancellationToken)
    {
        byte[] requestBody = BuildSummaryRequest(cell);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.deepseek.com/anthropic/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new ByteArrayContent(requestBody);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        var stopwatch = Stopwatch.StartNew();
        using HttpResponseMessage response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);
        byte[] responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Formal Summary batch failed at {cell.FixtureId}: HTTP {(int)response.StatusCode}: "
                + Encoding.UTF8.GetString(responseBytes));
        using JsonDocument document = JsonDocument.Parse(responseBytes);
        JsonElement root = document.RootElement;
        JsonElement[] toolCalls = root.GetProperty("content").EnumerateArray().Where(value =>
            value.TryGetProperty("type", out JsonElement type)
            && type.GetString() == "tool_use"
            && value.GetProperty("name").GetString() == "submit_memory_summary").ToArray();
        if (toolCalls.Length != 1)
        {
            string diagnosticPath = SaveSummaryDiagnosticResponse(cell, responseBytes);
            throw new InvalidDataException(
                $"Summary {cell.FixtureId} returned {toolCalls.Length} submit_memory_summary calls; response saved to {diagnosticPath}.");
        }
        JsonElement tool = toolCalls[0];
        JsonElement input = tool.GetProperty("input");
        if (!input.TryGetProperty("claims", out JsonElement claimsElement)
            || claimsElement.ValueKind != JsonValueKind.Array)
        {
            string diagnosticPath = SaveSummaryDiagnosticResponse(cell, responseBytes);
            throw new InvalidDataException(
                $"Summary {cell.FixtureId} omitted the required claims array; response saved to {diagnosticPath}.");
        }
        if (claimsElement.GetArrayLength() is < 1 or > 8)
        {
            string diagnosticPath = SaveSummaryDiagnosticResponse(cell, responseBytes);
            throw new InvalidDataException(
                $"Summary {cell.FixtureId} returned {claimsElement.GetArrayLength()} claims outside [1, 8]; response saved to {diagnosticPath}.");
        }
        var claims = new List<FrozenSummaryClaim>();
        int ordinal = 0;
        var knownSources = new HashSet<string>(
            cell.CandidateSet.SourceIds.Select(value => value.Value),
            StringComparer.Ordinal);
        foreach (JsonElement claim in claimsElement.EnumerateArray())
        {
            DecisionMemorySourceId[] sourceIds = ReadSummarySourceIds(claim, "source_ids", 4);
            DecisionMemorySourceId[] supersedes = ReadSummarySourceIds(claim, "supersedes_source_ids", 4);
            DecisionMemorySourceId[] conflicts = ReadSummarySourceIds(claim, "conflicts_with_source_ids", 4);
            if (sourceIds.Length == 0
                || sourceIds.Concat(supersedes).Concat(conflicts)
                    .Any(value => !knownSources.Contains(value.Value)))
            {
                string diagnosticPath = SaveSummaryDiagnosticResponse(cell, responseBytes);
                throw new InvalidDataException(
                    $"Summary {cell.FixtureId} cited an unknown or empty source set; response saved to {diagnosticPath}.");
            }
            DecisionMemoryEvidenceStatus status = claim.GetProperty("evidence_status").GetString() switch
            {
                "current" => DecisionMemoryEvidenceStatus.Current,
                "stale" => DecisionMemoryEvidenceStatus.Stale,
                "superseded" => DecisionMemoryEvidenceStatus.Superseded,
                "uncertain" => DecisionMemoryEvidenceStatus.Uncertain,
                _ => throw new InvalidDataException($"Summary {cell.FixtureId} returned an invalid evidence status.")
            };
            claims.Add(new FrozenSummaryClaim(
                ordinal++,
                Encoding.UTF8.GetBytes(RequiredString(claim, "content")),
                status,
                sourceIds,
                supersedes,
                conflicts));
        }
        DecisionMemorySourceId requiredSource = cell.RequiredSources.SourceIds.Single();
        FrozenSummaryClaim[] requiredClaims = claims
            .Where(value => value.SourceIds.Contains(requiredSource))
            .ToArray();
        if (requiredClaims.Length == 0)
            throw new InvalidDataException($"Summary {cell.FixtureId} omitted its required decision source.");
        DecisionMemorySlice requiredSlice = cell.CandidateSet.RankedSlices.Single(value =>
            value.SourceIds.Contains(requiredSource));
        var preservedSupersedes = new HashSet<DecisionMemorySourceId>(
            requiredClaims.SelectMany(value => value.SupersedesSourceIds));
        var preservedConflicts = new HashSet<DecisionMemorySourceId>(
            requiredClaims.SelectMany(value => value.ConflictsWithSourceIds));
        if (requiredSlice.SupersedesSourceIds.Any(value => !preservedSupersedes.Contains(value))
            || requiredSlice.ConflictsWithSourceIds.Any(value => !preservedConflicts.Contains(value)))
            throw new InvalidDataException($"Summary {cell.FixtureId} dropped required source relations.");
        FrozenSummaryArtifact artifact = FrozenSummaryArtifact.Create(
            cell.CandidateSet,
            new FrozenSummaryProfileVersion(SummaryProfile),
            new FrozenSummaryArtifactVersion(SummaryArtifactVersion),
            claims);
        long? inputTokens = OptionalInt64(root, "input_tokens");
        long? outputTokens = OptionalInt64(root, "output_tokens");
        return (artifact, new SummaryGenerationEvidence(
            cell.FixtureId,
            root.TryGetProperty("id", out JsonElement id) ? id.GetString() : null,
            ComputeSha256(requestBody),
            ComputeSha256(responseBytes),
            ComputeSha256(input.GetRawText()),
            inputTokens,
            outputTokens,
            stopwatch.ElapsedMilliseconds,
            artifact.ArtifactId.Value));
    }

    private static byte[] BuildSummaryRequest(PreparedRq2Cell cell)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", "deepseek-v4-pro");
            writer.WriteNumber("max_tokens", 4096);
            writer.WriteString("system",
                "Compress the supplied actor-visible memory set without adding facts. Return only 1 to 8 concise claims that matter to the stated decision; omit unrelated memories instead of aggregating long source lists. Every claim must cite 1 to 4 supplied source_ids and preserve directly associated supersedes_source_ids and conflicts_with_source_ids, with at most 4 IDs in each array. Submit exactly one submit_memory_summary tool call and no chain-of-thought. Its input is a closed schema: include content, evidence_status, source_ids, supersedes_source_ids, and conflicts_with_source_ids in every claim; omit every undeclared field. Copy source identifiers exactly from the matching input arrays and use empty relation arrays when none were supplied. Do not copy fixture, rank, decision, or memory metadata into the tool input unless the schema declares it.");
            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", BuildSummaryUserContent(cell));
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WritePropertyName("tools");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("name", "submit_memory_summary");
            writer.WritePropertyName("input_schema");
            WriteSummaryInputSchema(writer, cell);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WritePropertyName("tool_choice");
            writer.WriteStartObject();
            writer.WriteString("type", "any");
            writer.WriteBoolean("disable_parallel_tool_use", true);
            writer.WriteEndObject();
            writer.WritePropertyName("thinking");
            writer.WriteStartObject();
            writer.WriteString("type", "disabled");
            writer.WriteEndObject();
            writer.WriteBoolean("stream", false);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteSummaryInputSchema(Utf8JsonWriter writer, PreparedRq2Cell cell)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        writer.WritePropertyName("claims");
        writer.WriteStartObject();
        writer.WriteString("type", "array");
        writer.WriteNumber("minItems", 1);
        writer.WriteNumber("maxItems", 8);
        writer.WritePropertyName("items");
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        writer.WritePropertyName("content");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WriteNumber("minLength", 1);
        writer.WriteNumber("maxLength", 600);
        writer.WriteEndObject();
        writer.WritePropertyName("evidence_status");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WritePropertyName("enum");
        writer.WriteStartArray();
        writer.WriteStringValue("current");
        writer.WriteStringValue("stale");
        writer.WriteStringValue("superseded");
        writer.WriteStringValue("uncertain");
        writer.WriteEndArray();
        writer.WriteEndObject();
        WriteSummarySourceIdArraySchema(writer, cell, "source_ids", true);
        WriteSummarySourceIdArraySchema(writer, cell, "supersedes_source_ids", false);
        WriteSummarySourceIdArraySchema(writer, cell, "conflicts_with_source_ids", false);
        writer.WriteEndObject();
        writer.WritePropertyName("required");
        writer.WriteStartArray();
        writer.WriteStringValue("content");
        writer.WriteStringValue("evidence_status");
        writer.WriteStringValue("source_ids");
        writer.WriteStringValue("supersedes_source_ids");
        writer.WriteStringValue("conflicts_with_source_ids");
        writer.WriteEndArray();
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WritePropertyName("required");
        writer.WriteStartArray();
        writer.WriteStringValue("claims");
        writer.WriteEndArray();
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteEndObject();
    }

    private static void WriteSummarySourceIdArraySchema(
        Utf8JsonWriter writer,
        PreparedRq2Cell cell,
        string propertyName,
        bool requireItem)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        writer.WriteString("type", "array");
        if (requireItem) writer.WriteNumber("minItems", 1);
        writer.WriteNumber("maxItems", 4);
        writer.WriteBoolean("uniqueItems", true);
        writer.WritePropertyName("items");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WritePropertyName("enum");
        writer.WriteStartArray();
        foreach (DecisionMemorySourceId sourceId in cell.CandidateSet.SourceIds)
            writer.WriteStringValue(sourceId.Value);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static DecisionMemorySourceId[] ReadSummarySourceIds(
        JsonElement claim,
        string propertyName,
        int maximumCount)
    {
        if (!claim.TryGetProperty(propertyName, out JsonElement values)
            || values.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Summary claim omitted {propertyName}.");
        if (values.GetArrayLength() > maximumCount)
            throw new InvalidDataException($"Summary claim exceeded {propertyName} limit {maximumCount}.");
        return values.EnumerateArray()
            .Select(value => new DecisionMemorySourceId(value.GetString()!))
            .ToArray();
    }

    private static string SaveSummaryDiagnosticResponse(PreparedRq2Cell cell, byte[] responseBytes)
    {
        string diagnosticDirectory = Path.GetFullPath("godot/Artifacts/SummaryDevelopment");
        Directory.CreateDirectory(diagnosticDirectory);
        string diagnosticPath = Path.Combine(
            diagnosticDirectory,
            $"{cell.FixtureId}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}.json");
        File.WriteAllBytes(diagnosticPath, responseBytes);
        return diagnosticPath;
    }

    private static string BuildSummaryUserContent(PreparedRq2Cell cell)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("fixture_id", cell.FixtureId);
            writer.WriteString("decision", "Choose the grounded source for the blocked acquisition plan.");
            writer.WritePropertyName("ranked_memories");
            writer.WriteStartArray();
            for (int rank = 0; rank < cell.CandidateSet.RankedSlices.Count; rank++)
            {
                DecisionMemorySlice slice = cell.CandidateSet.RankedSlices[rank];
                writer.WriteStartObject();
                writer.WriteNumber("rank", rank);
                writer.WriteString("evidence_status", EvidenceToken(slice.EvidenceStatus));
                writer.WritePropertyName("source_ids");
                writer.WriteStartArray();
                foreach (DecisionMemorySourceId source in slice.SourceIds) writer.WriteStringValue(source.Value);
                writer.WriteEndArray();
                writer.WritePropertyName("supersedes_source_ids");
                writer.WriteStartArray();
                foreach (DecisionMemorySourceId source in slice.SupersedesSourceIds) writer.WriteStringValue(source.Value);
                writer.WriteEndArray();
                writer.WritePropertyName("conflicts_with_source_ids");
                writer.WriteStartArray();
                foreach (DecisionMemorySourceId source in slice.ConflictsWithSourceIds) writer.WriteStringValue(source.Value);
                writer.WriteEndArray();
                writer.WriteString("content", Encoding.UTF8.GetString(slice.GetCanonicalSourceBytes()));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static FormalRq1ConditionManifest CreateRq1Manifest(
        string manifestId,
        FormalRq1Treatment treatment,
        string opportunityLedgerId,
        FormalRq1DispatchConfiguration dispatch,
        FormalRq1PressureManifest pressure,
        string projectionHash,
        FormalRq1RequestProtocolManifestEntry protocol) => new(
            manifestId,
            PreregistrationVersion,
            treatment,
            RuntimeVersion,
            ModelProfileId,
            [protocol],
            projectionHash,
            opportunityLedgerId,
            dispatch,
            pressure);

    private static FormalRq2MatchedPairManifest CreateRq2PairManifest(
        FrozenSummaryArtifactRegistry registry,
        PreparedRq2Cell cell,
        int repeat)
    {
        var shared = new FormalRq2SharedConfigurationManifest(
            PreregistrationVersion,
            RuntimeVersion,
            ModelProfileId,
            RemotePlannerProtocol.ProtocolVersion,
            FormalRq2IdentitySetting.Resolved("normalized-relevance-recency-importance-v1"),
            FormalRq2ConfigurationSetting.Resolved("weights=1:1:1;recency_base=0.995;ticks_per_hour=40"),
            FormalRq2PlanningContextRenderer.Instance.Identity,
            FormalRq2IdentitySetting.Resolved("utf8_bytes_div4_v1"),
            FormalRq2PositiveIntSetting.Resolved(8192, "preregistered-context-ceiling"),
            FormalRq2PositiveIntSetting.Resolved(16384, "preregistered-output-ceiling"),
            FormalRq2IdentitySetting.Resolved("typed-action-authority-receipt-v1"),
            FormalRq2ConfigurationSetting.Resolved("exact-game-action-id-and-authority-terminal"),
            FormalRq2EmptyHistoryPolicy.RejectPair);
        return new FormalRq2MatchedPairManifest(
            new FormalRq2ConditionManifest(
                $"rq2-{cell.FixtureId}-verbatim-repeat-{repeat:D2}",
                FormalRq2Treatment.Verbatim,
                shared,
                null,
                null),
            new FormalRq2ConditionManifest(
                $"rq2-{cell.FixtureId}-summary-repeat-{repeat:D2}",
                FormalRq2Treatment.Summary,
                shared,
                registry.RegistryId,
                registry.ProfileVersion));
    }

    private static FormalExperimentSuiteArtifactBinding[] Bind(
        IReadOnlyDictionary<string, byte[]> artifacts) => artifacts
        .OrderBy(value => value.Key, StringComparer.Ordinal)
        .Select(value => new FormalExperimentSuiteArtifactBinding(
            value.Key,
            ComputeSha256(value.Value)))
        .ToArray();

    private static byte[] BuildRq2FixtureBundle(IEnumerable<PreparedRq2Cell> cells)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-frozen-fixture-bundle.v1");
            writer.WriteString("bundle_id", "brackenford-formal-rq2-fixtures-current");
            writer.WriteString("rq", "rq2");
            writer.WriteString("preregistration_artifact_version", PreregistrationVersion);
            writer.WritePropertyName("fixture_records");
            writer.WriteStartArray();
            foreach (PreparedRq2Cell cell in cells)
            {
                writer.WriteStartObject();
                writer.WriteString("fixture_id", cell.FixtureId);
                writer.WriteString("stratum", cell.Stratum);
                writer.WriteString("candidate_set_id", cell.CandidateSet.CandidateSetId.Value);
                writer.WriteString("candidate_set_sha256", ComputeSha256(cell.CandidateSet.GetCanonicalBytes()));
                writer.WriteNumber("full_verbatim_bytes", cell.FullVerbatimBytes);
                writer.WriteNumber("full_verbatim_tokens", cell.FullVerbatimTokens);
                writer.WritePropertyName("required_source_ids");
                writer.WriteStartArray();
                foreach (DecisionMemorySourceId source in cell.RequiredSources.SourceIds)
                    writer.WriteStringValue(source.Value);
                writer.WriteEndArray();
                writer.WriteString("tier_id", cell.Tier);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("unresolved_input_ids");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static byte[] LoadFrozenSourceManifest() => File.ReadAllBytes(
        Path.Combine(Root, "common", "repository_source_manifest.json"));

    private static byte[] LoadFrozenModelProfile() => File.ReadAllBytes(
        Path.Combine(Root, "common", "model_profile.json"));

    private static byte[] BuildIndex(PreparedRq1 rq1, PreparedRq2 rq2)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-asset-index.v1");
            writer.WriteString("preregistration_artifact_version", PreregistrationVersion);
            writer.WriteString("runtime_version", RuntimeVersion);
            writer.WriteString("model_profile_id", ModelProfileId);
            WritePairIndex(writer, "rq1", rq1.Suite, rq1.Pairs);
            WritePairIndex(writer, "rq2", rq2.Suite, rq2.Pairs);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WritePairIndex(
        Utf8JsonWriter writer,
        string propertyName,
        FormalExperimentSuiteManifest suite,
        IReadOnlyList<PreparedPairAssets> pairs)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        writer.WriteString("suite_manifest_hash", suite.ManifestHash);
        writer.WritePropertyName("pairs");
        writer.WriteStartArray();
        foreach (PreparedPairAssets pair in pairs)
        {
            writer.WriteStartObject();
            writer.WriteString("pair_id", pair.PairId);
            writer.WriteString("pair_manifest_path", pair.PairManifestPath);
            writer.WritePropertyName("artifact_hashes");
            writer.WriteStartObject();
            foreach (KeyValuePair<string, byte[]> artifact in pair.Artifacts.OrderBy(
                         value => value.Key,
                         StringComparer.Ordinal))
                writer.WriteString(artifact.Key, ComputeSha256(artifact.Value));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static byte[] SerializeRq1Hidden(IEnumerable<FormalRq1HiddenTestCase> hidden)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-rq1-hidden-test-cases.v1");
            writer.WritePropertyName("test_cases");
            writer.WriteStartArray();
            foreach (FormalRq1HiddenTestCase value in hidden.OrderBy(
                         item => item.TestCaseId.Value,
                         StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("test_case_id", value.TestCaseId.Value);
                writer.WriteString("expected_terminal_kind", value.ExpectedTerminalKind.ToString());
                writer.WriteString("expected_game_action_id", value.ExpectedGameActionId);
                writer.WriteString("expected_authority_action_family", value.ExpectedAuthorityActionFamily);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static byte[] SerializeRq2Hidden(FormalRq2HiddenOutcomePredicate hidden)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-rq2-hidden-predicate.v1");
            writer.WriteString("test_case_id", hidden.TestCaseId.Value);
            writer.WriteString("expected_terminal_kind", hidden.ExpectedTerminalKind.ToString());
            writer.WriteString("expected_game_action_id", hidden.ExpectedGameActionId);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static byte[] SerializeSummaryGenerationEvidence(
        IEnumerable<SummaryGenerationEvidence> evidence) => JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                schema_version = "alice.formal-summary-generation-evidence.v1",
                generation_policy = "one_complete_current_fixture_batch_no_selective_reruns",
                records = evidence
            },
            new JsonSerializerOptions { WriteIndented = true });

    private static long? OptionalInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage)
            || !usage.TryGetProperty(name, out JsonElement value)
            || !value.TryGetInt64(out long parsed)) return null;
        return parsed;
    }

    private static string EvidenceToken(DecisionMemoryEvidenceStatus status) => status switch
    {
        DecisionMemoryEvidenceStatus.Current => "current",
        DecisionMemoryEvidenceStatus.Stale => "stale",
        DecisionMemoryEvidenceStatus.Superseded => "superseded",
        DecisionMemoryEvidenceStatus.Uncertain => "uncertain",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString()
        ?? throw new InvalidDataException($"Required string '{name}' is absent.");

    private static int DependencyDegreeFromRank(string rankBand) => rankBand switch
    {
        "E0" => 2,
        "E1" => 1,
        "E2" => 0,
        _ => throw new InvalidDataException("Unknown RQ1 event rank band: " + rankBand)
    };

    private static void Write(string relativePath, byte[] bytes)
    {
        string path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private sealed record FormalDamageFixture(
        SharedActorState ActorState,
        NpcState NpcState,
        FormalPlanningActionCatalogue Catalogue,
        IActorExecutionExecutor Executor,
        IReadOnlyDictionary<string, GameActionSpec> Actions);

    private sealed record FormalProductActionFixture(
        FormalPlanningActionCatalogue Catalogue,
        IActorExecutionExecutor Executor);

    private sealed record FormalRq2PlanningFixture(
        ActorCognitionView View,
        NpcPlan Plan,
        FormalPlanningActionCatalogue Catalogue,
        IActorExecutionExecutor Executor);

    private sealed class StableFormalPressureEvaluator : IPressureEvaluator
    {
        private readonly PressureState _state;
        public StableFormalPressureEvaluator(PressureState state) { _state = state; }
        public PressureId PressureId => _state.PressureId;
        public PressureProfileId ProfileId => _state.ProfileId;
        public long ProfileVersion => _state.ProfileVersion;
        public string EvaluatorContentHash => _state.EvaluatorContentHash;
        public PressureEvaluation Evaluate(PressureState currentState, PressureSourceCommit sourceCommit) =>
            new(currentState, []);
    }

    private sealed class FormalDamageActorExecutor : IActorExecutionExecutor
    {
        private readonly IReadOnlyDictionary<string, DamageAuthorityRuntime> _runtimes;
        private readonly IReadOnlyDictionary<string, GameActionId> _actionIds;
        private readonly DamageValidationContext _context;

        public FormalDamageActorExecutor(
            ActorId actorId,
            IReadOnlyDictionary<string, DamageAuthorityRuntime> runtimes,
            IReadOnlyDictionary<string, GameActionId> actionIds,
            DamageValidationContext context)
        {
            ActorId = actorId;
            _runtimes = runtimes;
            _actionIds = actionIds;
            _context = context;
        }

        public ActorId ActorId { get; }

        public ActorExecutionReceipt Execute(ActorExecutionRequest request)
        {
            if (request.ActorId != ActorId
                || request.Payload is not InteractExecutionPayload interaction
                || interaction.Action.Arguments is not DamageActionArguments)
                return ActorExecutionReceipt.Rejected(
                    request,
                    ActorExecutionFailure.Unsupported,
                    "formal-damage/unsupported");
            string targetId = interaction.Action.Binding.ContractRef.TargetRef.Value;
            if (!_runtimes.TryGetValue(targetId, out DamageAuthorityRuntime? runtime)
                || !_actionIds.TryGetValue(targetId, out GameActionId? actionId))
                return ActorExecutionReceipt.Rejected(
                    request,
                    ActorExecutionFailure.Unavailable,
                    "formal-damage/target-unavailable");
            DamageCommitResult result = runtime.TryCommitDamage(interaction.Action, actionId, _context);
            return result.IsCommitted
                ? ActorExecutionReceipt.Completed(
                    request,
                    "formal-damage/authority-commit",
                    new AuthorityCommitExecutionResult("Damage"))
                : ActorExecutionReceipt.Rejected(
                    request,
                    ActorExecutionFailure.Unavailable,
                    "formal-damage/validator-rejected");
        }
    }

    private sealed record PreparedFile(string RelativePath, byte[] Bytes);
    private sealed record PreparedPairAssets(
        string PairId,
        string PairManifestPath,
        IReadOnlyDictionary<string, byte[]> Artifacts,
        FormalRq1MatchedPairManifest? Rq1Manifest = null,
        IReadOnlyList<PreparedRq1Scenario>? Rq1Scenarios = null,
        IReadOnlyList<PressureState>? Rq1PressureStates = null,
        PreparedRq1ScoringInputs? Rq1ScoringInputs = null);
    private sealed record PreparedRq1ScoringInputs(
        ActorOpportunityLedger OpportunityLedger,
        Rq1TestCaseLedger TestCaseLedger,
        FormalRq1OpportunityTestCaseMap Mapping,
        IReadOnlyList<FormalRq1HiddenTestCase> HiddenTestCases);
    private sealed record PreparedRq1(
        FormalExperimentSuiteManifest Suite,
        IReadOnlyList<PreparedFile> Files,
        IReadOnlyList<PreparedPairAssets> Pairs,
        ActorOpportunityLedger OpportunityLedger,
        Rq1TestCaseLedger TestCaseLedger,
        FormalRq1OpportunityTestCaseMap Mapping,
        IReadOnlyList<FormalRq1HiddenTestCase> HiddenTestCases,
        FormalRq1PressureManifest PressureManifest,
        FormalRq1DispatchConfiguration DispatchConfiguration,
        IReadOnlyList<PreparedRq1Scenario> Scenarios,
        IReadOnlyList<PressureState> PressureStates);
    private sealed record PreparedRq2(
        FormalExperimentSuiteManifest Suite,
        IReadOnlyList<PreparedFile> Files,
        IReadOnlyList<PreparedPairAssets> Pairs,
        IReadOnlyList<PreparedRq2Cell> Cells,
        FrozenSummaryArtifactRegistry Registry);
    private sealed record PreparedRq1Scenario(
        string PressureId,
        string ActorId,
        string Stratum,
        string TaskFamily,
        string AdmissionRole,
        string AgentRankBand,
        string EventEdgeKind,
        long DecisionTick,
        long DeadlineTick,
        byte[] PublicInput,
        byte[] ActorDecisionView,
        byte[] AuthoritySetup,
        byte[] AuthorityExecutionState,
        bool ExpectedDefer,
        string? ExpectedActionFamily,
        string? GameActionId,
        byte[] ActionArguments);
    private sealed record Rq1EventDependencyFixture(
        DependencyEdge Edge,
        AffectedNodeFact Fact);
    private sealed record PreparedRq1Block(
        string BlockToken,
        string FixtureId,
        IReadOnlyList<string> ConditionOrder,
        byte[] PublicFixture,
        IReadOnlyList<PreparedRq1Scenario> Scenarios,
        IReadOnlyList<PressureState> PressureStates,
        ActorOpportunityLedger OpportunityLedger,
        Rq1TestCaseLedger TestCaseLedger,
        FormalRq1OpportunityTestCaseMap Mapping,
        IReadOnlyList<FormalRq1HiddenTestCase> HiddenTestCases);
    private sealed record Rq1ContainerFixtureState(
        AssetContainerOwnerKind OwnerKind,
        string OwnerId,
        long Revision,
        IReadOnlyList<TownGameplayAssetBalanceConfiguration> Balances,
        IReadOnlyList<string> ItemInstanceIds);
    private sealed record Rq1ActorInventoryFixture(
        int InventoryVersion,
        int EquipmentVersion,
        TownItemInstanceConfiguration[] Instances,
        string? EquippedInstanceId);
    private sealed record Rq1ItemFixtureState(
        string ItemInstanceId,
        string ItemTypeId,
        int? Durability,
        int? MaximumDurability,
        int Version,
        AssetContainerOwnerKind OwnerKind,
        string OwnerId);
    private sealed record PreparedRq2Cell(
        int Index,
        string FixtureId,
        string Stratum,
        string Tier,
        DecisionMemoryCandidateSet CandidateSet,
        FormalRq2RequiredSourceSet RequiredSources,
        FormalRq2HiddenOutcomePredicate HiddenPredicate,
        string CorrectTarget,
        int FullVerbatimBytes,
        int FullVerbatimTokens);
    private sealed record PromptDevelopmentObservation(
        [property: System.Text.Json.Serialization.JsonPropertyName("fixture_id")] string FixtureId,
        [property: System.Text.Json.Serialization.JsonPropertyName("repeat")] int Repeat,
        [property: System.Text.Json.Serialization.JsonPropertyName("treatment")] string Treatment,
        [property: System.Text.Json.Serialization.JsonPropertyName("valid")] bool Valid,
        [property: System.Text.Json.Serialization.JsonPropertyName("decision_kind")] string DecisionKind,
        [property: System.Text.Json.Serialization.JsonPropertyName("duration_milliseconds")] long DurationMilliseconds);
    private sealed record PromptDevelopmentReport(
        [property: System.Text.Json.Serialization.JsonPropertyName("total_calls")] int TotalCalls,
        [property: System.Text.Json.Serialization.JsonPropertyName("valid_calls")] int ValidCalls,
        [property: System.Text.Json.Serialization.JsonPropertyName("observations")]
        IReadOnlyList<PromptDevelopmentObservation> Observations);

    private sealed class FormalApproximateTokenCounter : IFormalRq2TokenCounter
    {
        public MemoryPacketTokenizerVersion TokenizerVersion { get; } = new("utf8_bytes_div4_v1");

        public int CountTokens(ReadOnlySpan<byte> modelVisibleBytes) =>
            checked((modelVisibleBytes.Length + 3) / 4);
    }
    private sealed record SummaryGenerationEvidence(
        [property: System.Text.Json.Serialization.JsonPropertyName("fixture_id")] string FixtureId,
        [property: System.Text.Json.Serialization.JsonPropertyName("provider_response_id")] string? ProviderResponseId,
        [property: System.Text.Json.Serialization.JsonPropertyName("request_hash")] string RequestHash,
        [property: System.Text.Json.Serialization.JsonPropertyName("response_hash")] string ResponseHash,
        [property: System.Text.Json.Serialization.JsonPropertyName("decoded_tool_input_hash")] string DecodedToolInputHash,
        [property: System.Text.Json.Serialization.JsonPropertyName("input_tokens")] long? InputTokens,
        [property: System.Text.Json.Serialization.JsonPropertyName("output_tokens")] long? OutputTokens,
        [property: System.Text.Json.Serialization.JsonPropertyName("duration_milliseconds")] long DurationMilliseconds,
        [property: System.Text.Json.Serialization.JsonPropertyName("summary_artifact_id")] string SummaryArtifactId);
}
