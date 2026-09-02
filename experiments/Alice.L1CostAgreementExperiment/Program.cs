using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alice.Cognition;
using Alice.LivingTown;
using Alice.ModelRuntime;
using Alice.ProductRuntime;

namespace Alice.L1CostAgreementExperiment;

internal static class Program
{
    private const string CasesProtocol = "alice.l1_cost_agreement.cases.v1";
    private const string ExpectedProtocol = "alice.l1_cost_agreement.expected.v1";
    private const string ResultProtocol = "alice.l1_cost_agreement.result.v1";
    private const string RemoteToolName = "submit_l1_decision";
    private const int DefaultRepeats = 5;
    private const int MaximumAttempts = 3;
    private static readonly TimeSpan[] RetryBackoffs = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static int Main(string[] args)
    {
        try
        {
            return MainAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<int> MainAsync(string[] args)
    {
        Arguments parsed = Arguments.Parse(args);
        if (parsed.Mode == RunMode.Analyze)
        {
            AnalysisWriter.Write(parsed.InputPath!, parsed.OutputPath);
            return 0;
        }
        StudyInputs inputs = StudyInputs.Load(parsed.CasesPath, parsed.ExpectedPath);
        inputs.Validate();
        if (parsed.Mode == RunMode.Preflight)
        {
            Console.WriteLine(
                $"L1 COST AGREEMENT PREFLIGHT PASS cases={inputs.Cases.Cases.Length} " +
                $"choose={inputs.CountGroup("choose")} defer={inputs.CountGroup("defer")} " +
                $"escalate={inputs.CountGroup("escalate")} cases_sha256={inputs.CasesSha256} " +
                $"expected_sha256={inputs.ExpectedSha256}");
            return 0;
        }

        TownWorldConfiguration world = TownWorldConfiguration.Load(parsed.WorldPath);
        await RunLiveAsync(parsed, inputs, world).ConfigureAwait(false);
        return 0;
    }

    private static async Task RunLiveAsync(Arguments args, StudyInputs inputs, TownWorldConfiguration world)
    {
        ProviderProfilesConfiguration profiles = world.Runtime.ProviderProfiles;
        ProviderQueueConfiguration queue = world.Runtime.ProviderQueue;
        ProviderProfileConfiguration localProfile = profiles.LocalReasoner;
        ProviderProfileConfiguration remoteProfile = profiles.RemotePlanner;
        if (localProfile.TransportProtocol != "openai_chat_completions")
            throw new InvalidDataException("The L1 supplement requires the configured OpenAI-compatible local reasoner.");
        if (remoteProfile.TransportProtocol != "deepseek_anthropic_messages")
            throw new InvalidDataException("The L1 supplement requires the configured DeepSeek Anthropic Messages profile.");
        string credentialName = remoteProfile.CredentialEnvironmentVariable
            ?? throw new InvalidDataException("The remote profile has no credential environment variable.");
        string remoteApiKey = Environment.GetEnvironmentVariable(credentialName) ?? string.Empty;
        if (!args.LocalOnly && string.IsNullOrWhiteSpace(remoteApiKey))
            throw new InvalidOperationException($"Environment variable {credentialName} is missing or blank.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args.OutputPath))!);
        using var http = new HttpClient();
        var pairs = new List<PairResult>();
        int pairNumber = 0;
        foreach (StudyCase studyCase in inputs.Cases.Cases)
        {
            ExpectedCase expected = inputs.ExpectedById[studyCase.CaseId];
            string canonicalUserJson = inputs.CreateCanonicalUserJson(studyCase);
            for (int repeat = 1; repeat <= args.Repeats; repeat++)
            {
                pairNumber++;
                bool localFirst = pairNumber % 2 == 1;
                BranchResult? local = null;
                BranchResult? remote = null;
                if (localFirst)
                {
                    local = await RunBranchAsync(
                        ModelBranch.Local, studyCase, expected, canonicalUserJson,
                        localProfile, queue, null, http, CancellationToken.None).ConfigureAwait(false);
                    if (!args.LocalOnly)
                        remote = await RunBranchAsync(
                            ModelBranch.Remote, studyCase, expected, canonicalUserJson,
                            remoteProfile, queue, remoteApiKey, http, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    if (!args.LocalOnly)
                        remote = await RunBranchAsync(
                            ModelBranch.Remote, studyCase, expected, canonicalUserJson,
                            remoteProfile, queue, remoteApiKey, http, CancellationToken.None).ConfigureAwait(false);
                    local = await RunBranchAsync(
                        ModelBranch.Local, studyCase, expected, canonicalUserJson,
                        localProfile, queue, null, http, CancellationToken.None).ConfigureAwait(false);
                }

                var pair = new PairResult(
                    pairNumber,
                    studyCase.CaseId,
                    studyCase.Group,
                    repeat,
                    localFirst ? "local" : "remote",
                    local,
                    remote,
                    remote is null ? null : Agreement.Route(local.Decision, remote.Decision),
                    remote is null ? null : Agreement.Exact(local.Decision, remote.Decision));
                pairs.Add(pair);
                ResultDocument checkpoint = CreateResultDocument(args, inputs, profiles, pairs);
                File.WriteAllBytes(args.OutputPath, JsonSerializer.SerializeToUtf8Bytes(checkpoint, WriteOptions));
                Console.WriteLine(
                    $"L1 COST AGREEMENT pair={pairNumber}/{inputs.Cases.Cases.Length * args.Repeats} " +
                    $"case={studyCase.CaseId} repeat={repeat} local={local.Outcome}/{local.Acceptable} " +
                    $"remote={(remote is null ? "skipped" : $"{remote.Outcome}/{remote.Acceptable}")}");
            }
        }

        ResultDocument completed = CreateResultDocument(args, inputs, profiles, pairs);
        File.WriteAllBytes(args.OutputPath, JsonSerializer.SerializeToUtf8Bytes(completed, WriteOptions));
        Console.WriteLine(
            $"L1 COST AGREEMENT COMPLETE pairs={pairs.Count} output={Path.GetFullPath(args.OutputPath)}");
    }

    private static async Task<BranchResult> RunBranchAsync(
        ModelBranch branch,
        StudyCase studyCase,
        ExpectedCase expected,
        string canonicalUserJson,
        ProviderProfileConfiguration profile,
        ProviderQueueConfiguration queue,
        string? remoteApiKey,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        var attempts = new List<AttemptResult>();
        for (int attemptNumber = 1; attemptNumber <= MaximumAttempts; attemptNumber++)
        {
            AttemptResult attempt = await SendAttemptAsync(
                branch, studyCase, canonicalUserJson, profile, queue, remoteApiKey,
                attemptNumber, http, cancellationToken).ConfigureAwait(false);
            attempts.Add(attempt);
            if (!attempt.Retryable || attemptNumber == MaximumAttempts)
                return ScoreBranch(branch, studyCase, expected, attempts, attempt);
            await Task.Delay(RetryBackoffs[attemptNumber - 1], cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("The bounded attempt loop did not produce a terminal result.");
    }

    private static async Task<AttemptResult> SendAttemptAsync(
        ModelBranch branch,
        StudyCase studyCase,
        string canonicalUserJson,
        ProviderProfileConfiguration profile,
        ProviderQueueConfiguration queue,
        string? remoteApiKey,
        int attemptNumber,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        byte[] requestBody = branch == ModelBranch.Local
            ? RequestBodies.CreateLocal(profile.ModelId, queue.MaxOutputTokens, canonicalUserJson)
            : RequestBodies.CreateRemote(
                profile.ModelId,
                queue.MaxOutputTokens,
                canonicalUserJson,
                !profile.DisableThinking,
                profile.ThinkingEffort);
        using var request = new HttpRequestMessage(HttpMethod.Post, profile.Endpoint);
        request.Content = new ByteArrayContent(requestBody);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (branch == ModelBranch.Remote)
        {
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Add("x-api-key", remoteApiKey!);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(profile.TimeoutMilliseconds));
        var stopwatch = Stopwatch.StartNew();
        int? status = null;
        try
        {
            using HttpResponseMessage response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            status = (int)response.StatusCode;
            byte[] responseBytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            if (responseBytes.Length > profile.MaxResponseBodyBytes)
                return AttemptResult.Failure(
                    attemptNumber, "response_body_too_large", true, status,
                    stopwatch.ElapsedMilliseconds, requestBody, null, null);
            string responseBody = Encoding.UTF8.GetString(responseBytes);
            TokenUsage? usage = UsageReader.Read(branch, responseBody);
            if (!response.IsSuccessStatusCode)
                return AttemptResult.Failure(
                    attemptNumber, "http_failure", true, status,
                    stopwatch.ElapsedMilliseconds, requestBody, responseBody, usage);
            EnvelopeRead envelope = branch == ModelBranch.Local
                ? EnvelopeReader.ReadLocal(responseBody)
                : EnvelopeReader.ReadRemote(responseBody);
            if (!envelope.Success)
                return AttemptResult.Failure(
                    attemptNumber, envelope.FailureKind!, envelope.Retryable, status,
                    stopwatch.ElapsedMilliseconds, requestBody, responseBody, usage);
            LocalReasonerCallAttempt decoded = LocalReasonerResponseDecoder.Decode(envelope.DecisionJson);
            DecisionValue decision = DecisionValue.From(decoded);
            string outcome = decoded is LocalReasonerCallFailed ? "protocol_invalid" : "decoded";
            return new AttemptResult(
                attemptNumber, outcome, false, status, stopwatch.ElapsedMilliseconds,
                Encoding.UTF8.GetString(requestBody), responseBody, usage, decision);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return AttemptResult.Failure(
                attemptNumber, "timeout", true, status,
                stopwatch.ElapsedMilliseconds, requestBody, null, null);
        }
        catch (HttpRequestException)
        {
            return AttemptResult.Failure(
                attemptNumber, "network_failure", true, status,
                stopwatch.ElapsedMilliseconds, requestBody, null, null);
        }
        catch (IOException)
        {
            return AttemptResult.Failure(
                attemptNumber, "network_failure", true, status,
                stopwatch.ElapsedMilliseconds, requestBody, null, null);
        }
    }

    private static BranchResult ScoreBranch(
        ModelBranch branch,
        StudyCase studyCase,
        ExpectedCase expected,
        IReadOnlyList<AttemptResult> attempts,
        AttemptResult terminal)
    {
        DecisionValue? decision = terminal.Decision;
        bool hostAccepted = decision is not null && HostChecks.Accepts(studyCase, decision);
        bool acceptable = hostAccepted && HiddenScorer.Accepts(expected, decision!);
        long duration = 0;
        long? input = 0;
        long? output = 0;
        long? reasoning = 0;
        long? cacheRead = 0;
        long? cacheCreation = 0;
        long? total = 0;
        foreach (AttemptResult attempt in attempts)
        {
            duration = checked(duration + attempt.DurationMilliseconds);
            input = AddKnown(input, attempt.Usage?.InputTokens);
            output = AddKnown(output, attempt.Usage?.OutputTokens);
            reasoning = AddKnown(reasoning, attempt.Usage?.ReasoningTokens);
            cacheRead = AddKnown(cacheRead, attempt.Usage?.CacheReadInputTokens);
            cacheCreation = AddKnown(cacheCreation, attempt.Usage?.CacheCreationInputTokens);
            total = AddKnown(total, attempt.Usage?.TotalTokens);
        }

        return new BranchResult(
            branch == ModelBranch.Local ? "local" : "remote",
            terminal.Outcome,
            hostAccepted,
            acceptable,
            decision,
            duration,
            new TokenUsage(input, output, reasoning, cacheRead, cacheCreation, total),
            attempts);
    }

    private static long? AddKnown(long? current, long? value) =>
        current is null || value is null ? null : checked(current.Value + value.Value);

    private static ResultDocument CreateResultDocument(
        Arguments args,
        StudyInputs inputs,
        ProviderProfilesConfiguration profiles,
        IReadOnlyList<PairResult> pairs)
    {
        Summary summary = Summary.Create(pairs);
        return new ResultDocument(
            ResultProtocol,
            DateTimeOffset.UtcNow,
            inputs.CasesSha256,
            inputs.ExpectedSha256,
            args.Repeats,
            profiles.LocalReasoner.ProfileId,
            profiles.LocalReasoner.ModelId,
            profiles.RemotePlanner.ProfileId,
            profiles.RemotePlanner.ModelId,
            args.LocalOnly,
            pairs,
            summary);
    }
}

internal enum RunMode { Preflight, Live, Analyze }
internal enum ModelBranch { Local, Remote }

internal sealed record Arguments(
    RunMode Mode,
    string WorldPath,
    string CasesPath,
    string ExpectedPath,
    string OutputPath,
    int Repeats,
    bool LocalOnly,
    string? InputPath)
{
    public static Arguments Parse(string[] args)
    {
        string root = Environment.CurrentDirectory;
        string world = Path.GetFullPath("godot/Data/FormalResearch/Frozen/rq1/world_configuration.json", root);
        string cases = Path.GetFullPath("godot/Data/L1CostAgreement/l1_cost_agreement_cases.json", root);
        string expected = Path.GetFullPath("godot/Data/L1CostAgreement/l1_cost_agreement_expected.json", root);
        string output = Path.GetFullPath("tmp/l1-cost-agreement/result.json", root);
        string? input = null;
        RunMode? mode = null;
        int repeats = 5;
        bool localOnly = false;
        for (int index = 0; index < args.Length; index++)
        {
            string value = args[index];
            switch (value)
            {
                case "--preflight": mode = RunMode.Preflight; break;
                case "--live": mode = RunMode.Live; break;
                case "--analyze": mode = RunMode.Analyze; break;
                case "--local-only": localOnly = true; break;
                case "--world": world = RequireValue(args, ref index, value); break;
                case "--cases": cases = RequireValue(args, ref index, value); break;
                case "--expected": expected = RequireValue(args, ref index, value); break;
                case "--output": output = RequireValue(args, ref index, value); break;
                case "--input": input = RequireValue(args, ref index, value); break;
                case "--repeats":
                    if (!int.TryParse(RequireValue(args, ref index, value), out repeats) || repeats <= 0)
                        throw new ArgumentException("--repeats must be a positive integer.");
                    break;
                default: throw new ArgumentException($"Unknown argument: {value}");
            }
        }
        if (mode is null)
            throw new ArgumentException("Use --preflight, --live, or --analyze.");
        if (mode == RunMode.Analyze && string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("--analyze requires --input <result.json>.");
        return new Arguments(
            mode.Value,
            Path.GetFullPath(world),
            Path.GetFullPath(cases),
            Path.GetFullPath(expected),
            Path.GetFullPath(output),
            repeats,
            localOnly,
            input is null ? null : Path.GetFullPath(input));
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        index++;
        if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"{option} requires a value.");
        return args[index];
    }
}

internal sealed class StudyInputs
{
    private StudyInputs(
        CaseManifest cases,
        ExpectedManifest expected,
        string casesSha256,
        string expectedSha256,
        IReadOnlyDictionary<string, ExpectedCase> expectedById)
    {
        Cases = cases;
        Expected = expected;
        CasesSha256 = casesSha256;
        ExpectedSha256 = expectedSha256;
        ExpectedById = expectedById;
    }

    public CaseManifest Cases { get; }
    public ExpectedManifest Expected { get; }
    public string CasesSha256 { get; }
    public string ExpectedSha256 { get; }
    public IReadOnlyDictionary<string, ExpectedCase> ExpectedById { get; }

    public static StudyInputs Load(string casesPath, string expectedPath)
    {
        byte[] casesBytes = File.ReadAllBytes(casesPath);
        byte[] expectedBytes = File.ReadAllBytes(expectedPath);
        CaseManifest cases = JsonSerializer.Deserialize<CaseManifest>(casesBytes, ProgramJson.ReadOptions)
            ?? throw new InvalidDataException("The L1 case manifest is empty.");
        ExpectedManifest expected = JsonSerializer.Deserialize<ExpectedManifest>(expectedBytes, ProgramJson.ReadOptions)
            ?? throw new InvalidDataException("The L1 expected ledger is empty.");
        var expectedById = new Dictionary<string, ExpectedCase>(StringComparer.Ordinal);
        foreach (ExpectedCase item in expected.Cases)
            if (!expectedById.TryAdd(item.CaseId, item))
                throw new InvalidDataException($"Duplicate expected case: {item.CaseId}");
        return new StudyInputs(
            cases,
            expected,
            Hash(casesBytes),
            Hash(expectedBytes),
            expectedById);
    }

    public void Validate()
    {
        ValidatePromptContract();
        if (Cases.Protocol != "alice.l1_cost_agreement.cases.v1"
            || Expected.Protocol != "alice.l1_cost_agreement.expected.v1")
            throw new InvalidDataException("The L1 supplement protocol identity is wrong.");
        if (Cases.Cases.Length != 24 || CountGroup("choose") != 8
            || CountGroup("defer") != 4 || CountGroup("escalate") != 12)
            throw new InvalidDataException("The L1 supplement must contain 8 choose, 4 defer, and 12 escalate cases.");
        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (StudyCase studyCase in Cases.Cases)
        {
            if (!caseIds.Add(studyCase.CaseId))
                throw new InvalidDataException($"Duplicate study case: {studyCase.CaseId}");
            if (!ExpectedById.TryGetValue(studyCase.CaseId, out ExpectedCase? expected))
                throw new InvalidDataException($"Missing expected outcome for {studyCase.CaseId}.");
            ValidateCase(studyCase, expected);
        }
        if (ExpectedById.Count != caseIds.Count)
            throw new InvalidDataException("The expected ledger contains cases absent from the case manifest.");
    }

    private static void ValidatePromptContract()
    {
        string prompt = TownAutonomyL1Request.SystemPrompt;
        string[] required =
        [
            "Defer when every blocker is explicitly temporary",
            "no_feasible_local_action",
            "goal_or_plan_change",
            "commitment_or_debt",
            "major_relationship",
            "medical_or_body_deadline",
            "repeated_visible_failure"
        ];
        foreach (string value in required)
            if (!prompt.Contains(value, StringComparison.Ordinal))
                throw new InvalidDataException($"The shared L1 prompt does not expose required contract text: {value}");
    }

    public int CountGroup(string group)
    {
        int count = 0;
        foreach (StudyCase studyCase in Cases.Cases)
            if (studyCase.Group == group) count++;
        return count;
    }

    public string CreateCanonicalUserJson(StudyCase studyCase)
    {
        BodyDocument body = studyCase.Body ?? Cases.Defaults.Body;
        InventoryDocument[] inventory = studyCase.Inventory ?? Cases.Defaults.Inventory;
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("actor_id", Cases.Defaults.ActorId);
            writer.WriteString("name", Cases.Defaults.Name);
            WriteStrings(writer, "personality_traits", Cases.Defaults.PersonalityTraits);
            WriteStrings(writer, "aspirations", Cases.Defaults.Aspirations);
            writer.WriteString("current_emotion", Cases.Defaults.CurrentEmotion);
            WriteStrings(writer, "current_goal_refs", Cases.Defaults.CurrentGoalRefs);
            writer.WriteString("domain", studyCase.Domain);
            writer.WriteString("subject_ref", studyCase.SubjectRef);
            writer.WriteNumber("visible_failure_count", studyCase.VisibleFailureCount);
            writer.WritePropertyName("body");
            writer.WriteStartObject();
            writer.WriteNumber("health", body.Health);
            writer.WriteNumber("satiety", body.Satiety);
            writer.WriteNumber("spirit", body.Spirit);
            writer.WriteString("disease", body.Disease);
            writer.WriteEndObject();
            writer.WritePropertyName("inventory");
            writer.WriteStartArray();
            foreach (InventoryDocument item in inventory)
            {
                writer.WriteStartObject();
                writer.WriteString("asset_id", item.AssetId);
                writer.WriteNumber("quantity", item.Quantity);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("candidates");
            writer.WriteStartArray();
            foreach (CandidateDocument candidate in studyCase.Candidates)
            {
                writer.WriteStartObject();
                writer.WriteString("candidate_id", candidate.CandidateId);
                writer.WriteString("kind", candidate.Kind);
                writer.WriteString("target_id", candidate.TargetId);
                writer.WriteString("label", candidate.Label);
                writer.WriteBoolean("available", candidate.Available);
                if (candidate.UnavailableReason is null) writer.WriteNull("unavailable_reason");
                else writer.WriteString("unavailable_reason", candidate.UnavailableReason);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void ValidateCase(StudyCase studyCase, ExpectedCase expected)
    {
        if (string.IsNullOrWhiteSpace(studyCase.CaseId)
            || studyCase.Group is not ("choose" or "defer" or "escalate")
            || string.IsNullOrWhiteSpace(studyCase.Domain)
            || string.IsNullOrWhiteSpace(studyCase.SubjectRef)
            || studyCase.VisibleFailureCount < 0
            || studyCase.Candidates.Length is < 2 or > 4)
            throw new InvalidDataException($"Invalid study case header: {studyCase.CaseId}");
        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (CandidateDocument candidate in studyCase.Candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.CandidateId)
                || !candidateIds.Add(candidate.CandidateId)
                || candidate.Kind is not ("Action" or "Evidence")
                || string.IsNullOrWhiteSpace(candidate.TargetId)
                || string.IsNullOrWhiteSpace(candidate.Label)
                || !candidate.Available && string.IsNullOrWhiteSpace(candidate.UnavailableReason))
                throw new InvalidDataException($"Invalid candidate in {studyCase.CaseId}.");
        }
        if (expected.CaseId != studyCase.CaseId || expected.Decision is not ("choose" or "defer" or "request_escalation"))
            throw new InvalidDataException($"Invalid expected outcome for {studyCase.CaseId}.");
        if (studyCase.Group == "choose")
        {
            if (expected.Decision != "choose" || expected.AcceptableCandidateIds.Length == 0)
                throw new InvalidDataException($"Choose case {studyCase.CaseId} has no acceptable candidate.");
            foreach (string candidateId in expected.AcceptableCandidateIds)
            {
                CandidateDocument candidate = RequireCandidate(studyCase, candidateId);
                if (!candidate.Available || candidate.Kind != "Action")
                    throw new InvalidDataException($"Choose answer {candidateId} is not an available action.");
            }
        }
        else if (studyCase.Group == "defer")
        {
            if (expected.Decision != "defer" || HasAvailableAction(studyCase))
                throw new InvalidDataException($"Defer case {studyCase.CaseId} has an available action or wrong answer.");
        }
        else
        {
            if (expected.Decision != "request_escalation" || expected.AcceptableReasonCodes.Length == 0
                || expected.RequiredEvidenceRefs.Length == 0)
                throw new InvalidDataException($"Escalation case {studyCase.CaseId} lacks its reason or evidence.");
            foreach (string evidenceRef in expected.RequiredEvidenceRefs) RequireCandidate(studyCase, evidenceRef);
            var decision = new DecisionValue(
                "request_escalation", null, expected.AcceptableReasonCodes[0], [expected.RequiredEvidenceRefs[0]], null);
            if (!HostChecks.Accepts(studyCase, decision))
                throw new InvalidDataException($"Escalation case {studyCase.CaseId} does not satisfy its Host threshold.");
        }
    }

    private static CandidateDocument RequireCandidate(StudyCase studyCase, string candidateId)
    {
        foreach (CandidateDocument candidate in studyCase.Candidates)
            if (candidate.CandidateId == candidateId) return candidate;
        throw new InvalidDataException($"Case {studyCase.CaseId} does not contain candidate {candidateId}.");
    }

    private static bool HasAvailableAction(StudyCase studyCase)
    {
        foreach (CandidateDocument candidate in studyCase.Candidates)
            if (candidate.Available && candidate.Kind == "Action") return true;
        return false;
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (string value in values) writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal static class RequestBodies
{
    public static byte[] CreateLocal(string modelId, int maxTokens, string userJson)
    {
        using JsonDocument schema = JsonDocument.Parse(LocalReasonerProtocol.OutputSchemaJson);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", modelId);
            WriteMessages(writer, userJson);
            writer.WritePropertyName("response_format");
            writer.WriteStartObject();
            writer.WriteString("type", "json_schema");
            writer.WritePropertyName("json_schema");
            writer.WriteStartObject();
            writer.WriteString("name", "l1_cost_agreement_decision");
            writer.WriteBoolean("strict", true);
            writer.WritePropertyName("schema");
            schema.RootElement.WriteTo(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteNumber("max_tokens", maxTokens);
            writer.WriteBoolean("stream", false);
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    public static byte[] CreateRemote(
        string modelId,
        int maxTokens,
        string userJson,
        bool thinkingEnabled,
        string? thinkingEffort)
    {
        using JsonDocument schema = JsonDocument.Parse(LocalReasonerProtocol.OutputSchemaJson);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", modelId);
            writer.WriteNumber("max_tokens", maxTokens);
            writer.WriteString("system", TownAutonomyL1Request.SystemPrompt);
            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", userJson);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WritePropertyName("tools");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("name", "submit_l1_decision");
            writer.WriteString("description", "Submit the bounded L1 decision.");
            writer.WritePropertyName("input_schema");
            schema.RootElement.WriteTo(writer);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WritePropertyName("tool_choice");
            writer.WriteStartObject();
            writer.WriteString("type", "any");
            writer.WriteBoolean("disable_parallel_tool_use", true);
            writer.WriteEndObject();
            writer.WritePropertyName("thinking");
            writer.WriteStartObject();
            writer.WriteString("type", thinkingEnabled ? "enabled" : "disabled");
            if (thinkingEnabled) writer.WriteNumber("budget_tokens", maxTokens);
            writer.WriteEndObject();
            if (thinkingEnabled)
            {
                writer.WritePropertyName("output_config");
                writer.WriteStartObject();
                writer.WriteString("effort", thinkingEffort ?? "high");
                writer.WriteEndObject();
            }
            writer.WriteBoolean("stream", false);
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static void WriteMessages(Utf8JsonWriter writer, string userJson)
    {
        writer.WritePropertyName("messages");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("role", "system");
        writer.WriteString("content", TownAutonomyL1Request.SystemPrompt);
        writer.WriteEndObject();
        writer.WriteStartObject();
        writer.WriteString("role", "user");
        writer.WriteString("content", userJson);
        writer.WriteEndObject();
        writer.WriteEndArray();
    }
}

internal static class EnvelopeReader
{
    public static EnvelopeRead ReadLocal(string responseBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("choices", out JsonElement choices)
                || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1)
                return EnvelopeRead.Failed("invalid_response_envelope", true);
            JsonElement choice = choices[0];
            string? finish = OptionalString(choice, "finish_reason");
            if (finish == "length") return EnvelopeRead.Failed("output_token_limit", true);
            if (finish != "stop"
                || !choice.TryGetProperty("message", out JsonElement message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("content", out JsonElement content)
                || content.ValueKind != JsonValueKind.String)
                return EnvelopeRead.Failed("invalid_response_envelope", true);
            return EnvelopeRead.Received(content.GetString()!);
        }
        catch (JsonException)
        {
            return EnvelopeRead.Failed("invalid_response_envelope", true);
        }
    }

    public static EnvelopeRead ReadRemote(string responseBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            string? stop = OptionalString(root, "stop_reason");
            if (stop == "max_tokens") return EnvelopeRead.Failed("output_token_limit", true);
            if (stop != "tool_use"
                || !root.TryGetProperty("content", out JsonElement content)
                || content.ValueKind != JsonValueKind.Array)
                return EnvelopeRead.Failed("invalid_response_envelope", true);
            JsonElement? selected = null;
            foreach (JsonElement block in content.EnumerateArray())
            {
                if (OptionalString(block, "type") != "tool_use") continue;
                if (selected is not null) return EnvelopeRead.Failed("invalid_response_envelope", true);
                if (OptionalString(block, "name") != "submit_l1_decision"
                    || !block.TryGetProperty("input", out JsonElement input)
                    || input.ValueKind != JsonValueKind.Object)
                    return EnvelopeRead.Failed("invalid_response_envelope", true);
                selected = input.Clone();
            }
            return selected is null
                ? EnvelopeRead.Failed("invalid_response_envelope", true)
                : EnvelopeRead.Received(selected.Value.GetRawText());
        }
        catch (JsonException)
        {
            return EnvelopeRead.Failed("invalid_response_envelope", true);
        }
    }

    private static string? OptionalString(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

internal static class UsageReader
{
    public static TokenUsage? Read(ModelBranch branch, string responseBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
                return null;
            if (branch == ModelBranch.Local)
                return new TokenUsage(
                    OptionalLong(usage, "prompt_tokens"),
                    OptionalLong(usage, "completion_tokens"),
                    OptionalNestedLong(usage, "completion_tokens_details", "reasoning_tokens"),
                    OptionalNestedLong(usage, "prompt_tokens_details", "cached_tokens"),
                    null,
                    OptionalLong(usage, "total_tokens"));
            long? input = OptionalLong(usage, "input_tokens");
            long? output = OptionalLong(usage, "output_tokens");
            long? cacheRead = OptionalLong(usage, "cache_read_input_tokens");
            long? cacheCreation = OptionalLong(usage, "cache_creation_input_tokens");
            return new TokenUsage(
                input,
                output,
                OptionalNestedLong(usage, "output_tokens_details", "reasoning_tokens")
                    ?? OptionalNestedLong(usage, "output_tokens_details", "thinking_tokens"),
                cacheRead,
                cacheCreation,
                SumRemoteTotal(input, output, cacheRead, cacheCreation));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long? SumRemoteTotal(long? input, long? output, long? cacheRead, long? cacheCreation)
    {
        if (input is null || output is null) return null;
        return checked(input.Value + output.Value + (cacheRead ?? 0) + (cacheCreation ?? 0));
    }

    private static long? OptionalLong(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out long parsed)
        && parsed >= 0 ? parsed : null;

    private static long? OptionalNestedLong(JsonElement parent, string objectName, string name) =>
        parent.TryGetProperty(objectName, out JsonElement nested)
        && nested.ValueKind == JsonValueKind.Object ? OptionalLong(nested, name) : null;
}

internal static class HostChecks
{
    private static readonly HashSet<string> EscalationReasons = new(StringComparer.Ordinal)
    {
        "no_feasible_local_action", "goal_or_plan_change", "commitment_or_debt",
        "major_relationship", "medical_or_body_deadline", "repeated_visible_failure"
    };

    public static bool Accepts(StudyCase studyCase, DecisionValue decision)
    {
        if (decision.Decision == "choose")
        {
            CandidateDocument? selected = FindCandidate(studyCase, decision.CandidateId);
            return selected is { Kind: "Action", Available: true };
        }
        if (decision.Decision == "defer") return !string.IsNullOrWhiteSpace(decision.ReasonCode);
        if (decision.Decision != "request_escalation"
            || string.IsNullOrWhiteSpace(decision.ReasonCode)
            || !EscalationReasons.Contains(decision.ReasonCode)
            || decision.EvidenceRefs.Length == 0)
            return false;
        foreach (string evidenceRef in decision.EvidenceRefs)
            if (FindCandidate(studyCase, evidenceRef) is null) return false;
        return MeetsThreshold(studyCase, decision.ReasonCode);
    }

    private static bool MeetsThreshold(StudyCase studyCase, string reasonCode)
    {
        return reasonCode switch
        {
            "no_feasible_local_action" => AllUnavailable(studyCase) && !AllTemporary(studyCase),
            "goal_or_plan_change" => studyCase.Domain is "aspiration" or "work-blocker" or "business-stock",
            "commitment_or_debt" => studyCase.Domain == "commitment",
            "major_relationship" => studyCase.Domain is "relationship" or "conversation",
            "medical_or_body_deadline" => studyCase.Domain is "treatment" or "hunger" or "rest",
            "repeated_visible_failure" => studyCase.VisibleFailureCount >= 2,
            _ => false
        };
    }

    private static CandidateDocument? FindCandidate(StudyCase studyCase, string? candidateId)
    {
        if (candidateId is null) return null;
        foreach (CandidateDocument candidate in studyCase.Candidates)
            if (candidate.CandidateId == candidateId) return candidate;
        return null;
    }

    private static bool AllUnavailable(StudyCase studyCase)
    {
        foreach (CandidateDocument candidate in studyCase.Candidates)
            if (candidate.Available) return false;
        return true;
    }

    private static bool AllTemporary(StudyCase studyCase)
    {
        foreach (CandidateDocument candidate in studyCase.Candidates)
            if (!IsTemporary(candidate.UnavailableReason)) return false;
        return true;
    }

    private static bool IsTemporary(string? reason)
    {
        string value = reason ?? string.Empty;
        return value.Contains("closed", StringComparison.OrdinalIgnoreCase)
            || value.Contains("cooldown", StringComparison.OrdinalIgnoreCase)
            || value.Contains("until tick", StringComparison.OrdinalIgnoreCase)
            || value.Contains("growing", StringComparison.OrdinalIgnoreCase)
            || value.Contains("not currently available", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class HiddenScorer
{
    public static bool Accepts(ExpectedCase expected, DecisionValue decision)
    {
        if (expected.Decision != decision.Decision) return false;
        if (decision.Decision == "choose")
        {
            foreach (string accepted in expected.AcceptableCandidateIds)
                if (accepted == decision.CandidateId) return true;
            return false;
        }
        if (decision.Decision == "defer") return true;
        bool reasonAccepted = false;
        foreach (string acceptedReason in expected.AcceptableReasonCodes)
            if (acceptedReason == decision.ReasonCode) reasonAccepted = true;
        if (!reasonAccepted) return false;
        foreach (string evidenceRef in decision.EvidenceRefs)
            foreach (string accepted in expected.RequiredEvidenceRefs)
                if (evidenceRef == accepted) return true;
        return false;
    }
}

internal static class Agreement
{
    public static bool Route(DecisionValue? local, DecisionValue? remote) =>
        local is not null && remote is not null && local.Decision == remote.Decision;

    public static bool Exact(DecisionValue? local, DecisionValue? remote)
    {
        if (!Route(local, remote)) return false;
        if (local!.Decision == "choose") return local.CandidateId == remote!.CandidateId;
        if (local.Decision == "request_escalation") return local.ReasonCode == remote!.ReasonCode;
        return true;
    }
}

internal sealed record EnvelopeRead(bool Success, string? DecisionJson, string? FailureKind, bool Retryable)
{
    public static EnvelopeRead Received(string decisionJson) => new(true, decisionJson, null, false);
    public static EnvelopeRead Failed(string kind, bool retryable) => new(false, null, kind, retryable);
}

internal sealed record DecisionValue(
    string Decision,
    string? CandidateId,
    string? ReasonCode,
    string[] EvidenceRefs,
    string? FailureKind)
{
    public static DecisionValue From(LocalReasonerCallAttempt attempt)
    {
        return attempt switch
        {
            LocalReasonerChoiceProduced choice => new(
                "choose", choice.Choice.NextAction.Value, string.Empty, [], null),
            LocalReasonerDeferProduced defer => new(
                "defer", string.Empty, defer.Decision.ReasonCode, [], null),
            LocalReasonerEscalationRequested escalation => new(
                "request_escalation", string.Empty, escalation.Decision.ReasonCode,
                escalation.Decision.EvidenceRefs.ToArray(), null),
            LocalReasonerCallFailed failed => new(
                "failure", null, null, [], failed.FailureKind.ToString()),
            _ => throw new InvalidOperationException("Unknown L1 decision type.")
        };
    }
}

internal sealed record TokenUsage(
    long? InputTokens,
    long? OutputTokens,
    long? ReasoningTokens,
    long? CacheReadInputTokens,
    long? CacheCreationInputTokens,
    long? TotalTokens);

internal sealed record AttemptResult(
    int Attempt,
    string Outcome,
    bool Retryable,
    int? HttpStatus,
    long DurationMilliseconds,
    string RequestBody,
    string? ResponseBody,
    TokenUsage? Usage,
    DecisionValue? Decision)
{
    public static AttemptResult Failure(
        int attempt,
        string outcome,
        bool retryable,
        int? httpStatus,
        long durationMilliseconds,
        byte[] requestBody,
        string? responseBody,
        TokenUsage? usage) => new(
            attempt,
            outcome,
            retryable,
            httpStatus,
            durationMilliseconds,
            Encoding.UTF8.GetString(requestBody),
            responseBody,
            usage,
            null);
}

internal sealed record BranchResult(
    string Branch,
    string Outcome,
    bool HostAccepted,
    bool Acceptable,
    DecisionValue? Decision,
    long DurationMilliseconds,
    TokenUsage Usage,
    IReadOnlyList<AttemptResult> Attempts);

internal sealed record PairResult(
    int Pair,
    string CaseId,
    string Group,
    int Repeat,
    string FirstBranch,
    BranchResult Local,
    BranchResult? Remote,
    bool? RouteAgreement,
    bool? ExactAgreement);

internal sealed record Summary(
    int Pairs,
    int LocalAcceptable,
    int? RemoteAcceptable,
    int? BothAcceptable,
    int? LocalOnlyAcceptable,
    int? RemoteOnlyAcceptable,
    int? NeitherAcceptable,
    int? RouteAgreements,
    int? ExactAgreements,
    TokenUsage LocalUsage,
    TokenUsage? RemoteUsage,
    long LocalDurationMilliseconds,
    long? RemoteDurationMilliseconds)
{
    public static Summary Create(IReadOnlyList<PairResult> pairs)
    {
        int localAccepted = 0;
        int remoteAccepted = 0;
        int both = 0;
        int localOnly = 0;
        int remoteOnly = 0;
        int neither = 0;
        int route = 0;
        int exact = 0;
        bool hasRemote = false;
        long localDuration = 0;
        long remoteDuration = 0;
        TokenAccumulator localUsage = new();
        TokenAccumulator remoteUsage = new();
        foreach (PairResult pair in pairs)
        {
            if (pair.Local.Acceptable) localAccepted++;
            localDuration = checked(localDuration + pair.Local.DurationMilliseconds);
            localUsage.Add(pair.Local.Usage);
            if (pair.Remote is null) continue;
            hasRemote = true;
            if (pair.Remote.Acceptable) remoteAccepted++;
            if (pair.Local.Acceptable && pair.Remote.Acceptable) both++;
            else if (pair.Local.Acceptable) localOnly++;
            else if (pair.Remote.Acceptable) remoteOnly++;
            else neither++;
            if (pair.RouteAgreement == true) route++;
            if (pair.ExactAgreement == true) exact++;
            remoteDuration = checked(remoteDuration + pair.Remote.DurationMilliseconds);
            remoteUsage.Add(pair.Remote.Usage);
        }
        return new Summary(
            pairs.Count,
            localAccepted,
            hasRemote ? remoteAccepted : null,
            hasRemote ? both : null,
            hasRemote ? localOnly : null,
            hasRemote ? remoteOnly : null,
            hasRemote ? neither : null,
            hasRemote ? route : null,
            hasRemote ? exact : null,
            localUsage.Value,
            hasRemote ? remoteUsage.Value : null,
            localDuration,
            hasRemote ? remoteDuration : null);
    }
}

internal sealed class TokenAccumulator
{
    private long? _input = 0;
    private long? _output = 0;
    private long? _reasoning = 0;
    private long? _cacheRead = 0;
    private long? _cacheCreation = 0;
    private long? _total = 0;

    public TokenUsage Value => new(_input, _output, _reasoning, _cacheRead, _cacheCreation, _total);

    public void Add(TokenUsage usage)
    {
        _input = AddKnown(_input, usage.InputTokens);
        _output = AddKnown(_output, usage.OutputTokens);
        _reasoning = AddKnown(_reasoning, usage.ReasoningTokens);
        _cacheRead = AddKnown(_cacheRead, usage.CacheReadInputTokens);
        _cacheCreation = AddKnown(_cacheCreation, usage.CacheCreationInputTokens);
        _total = AddKnown(_total, usage.TotalTokens);
    }

    private static long? AddKnown(long? current, long? value) =>
        current is null || value is null ? null : checked(current.Value + value.Value);
}

internal sealed record ResultDocument(
    string Protocol,
    DateTimeOffset UpdatedAtUtc,
    string CasesSha256,
    string ExpectedSha256,
    int Repeats,
    string LocalProfileId,
    string LocalModelId,
    string RemoteProfileId,
    string RemoteModelId,
    bool LocalOnly,
    IReadOnlyList<PairResult> Pairs,
    Summary Summary);

internal static class AnalysisWriter
{
    private const int BootstrapSamples = 100_000;
    private const int BootstrapSeed = 20260901;
    private const double NonInferiorityMargin = -0.05;
    private const double OffPeakCacheHitPerMillion = 0.022;
    private const double OffPeakCacheMissPerMillion = 0.66;
    private const double OffPeakOutputPerMillion = 1.98;
    private const double PeakCacheHitPerMillion = 0.044;
    private const double PeakCacheMissPerMillion = 1.32;
    private const double PeakOutputPerMillion = 3.96;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Write(string inputPath, string outputPath)
    {
        byte[] sourceBytes = File.ReadAllBytes(inputPath);
        ResultDocument source = JsonSerializer.Deserialize<ResultDocument>(sourceBytes)
            ?? throw new InvalidDataException("The L1 result document is empty.");
        ValidateCoverage(source);
        AnalysisDocument analysis = Create(source, Hash(sourceBytes));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, JsonSerializer.SerializeToUtf8Bytes(analysis, Options));
        Console.WriteLine(
            $"L1 COST AGREEMENT ANALYSIS PASS pairs={analysis.Coverage.Pairs} " +
            $"local={analysis.Quality.LocalAcceptable}/{analysis.Coverage.Pairs} " +
            $"remote={analysis.Quality.RemoteAcceptable}/{analysis.Coverage.Pairs} " +
            $"difference_pp={analysis.Quality.LocalMinusRemotePercentagePoints:F2} " +
            $"lower_pp={analysis.Quality.OneSided95LowerPercentagePoints:F2} " +
            $"output={Path.GetFullPath(outputPath)}");
    }

    private static AnalysisDocument Create(ResultDocument source, string sourceSha256)
    {
        Dictionary<string, List<double>> caseDifferences = BuildCaseDifferences(source.Pairs);
        double point = MeanCaseDifference(caseDifferences);
        double[] bootstrap = Bootstrap(caseDifferences);
        double oneSidedLower = Quantile(bootstrap, 0.05);
        double twoSidedLower = Quantile(bootstrap, 0.025);
        double twoSidedUpper = Quantile(bootstrap, 0.975);
        int localOnly = source.Summary.LocalOnlyAcceptable ?? 0;
        int remoteOnly = source.Summary.RemoteOnlyAcceptable ?? 0;
        int decodedAttempts = 0;
        int retries = 0;
        foreach (PairResult pair in source.Pairs)
        {
            CountAttempts(pair.Local.Attempts, ref decodedAttempts, ref retries);
            CountAttempts(pair.Remote!.Attempts, ref decodedAttempts, ref retries);
        }

        long remoteInput = Require(source.Summary.RemoteUsage?.InputTokens, "remote input tokens");
        long remoteCacheRead = Require(source.Summary.RemoteUsage?.CacheReadInputTokens, "remote cache-read tokens");
        long remoteCacheCreation = source.Summary.RemoteUsage?.CacheCreationInputTokens ?? 0;
        long remoteOutput = Require(source.Summary.RemoteUsage?.OutputTokens, "remote output tokens");
        double offPeakBatch = Price(
            remoteInput, remoteCacheRead, remoteCacheCreation, remoteOutput,
            OffPeakCacheMissPerMillion, OffPeakCacheHitPerMillion, OffPeakOutputPerMillion);
        double peakBatch = Price(
            remoteInput, remoteCacheRead, remoteCacheCreation, remoteOutput,
            PeakCacheMissPerMillion, PeakCacheHitPerMillion, PeakOutputPerMillion);
        int pairs = source.Pairs.Count;
        return new AnalysisDocument(
            "alice.l1_cost_agreement.analysis.v1",
            DateTimeOffset.UtcNow,
            sourceSha256,
            source.CasesSha256,
            source.ExpectedSha256,
            new CoverageAnalysis(
                24,
                source.Repeats,
                pairs,
                decodedAttempts,
                retries,
                source.Pairs.Count == 120 && decodedAttempts == 240),
            new QualityAnalysis(
                source.Summary.LocalAcceptable,
                source.Summary.RemoteAcceptable!.Value,
                source.Summary.LocalAcceptable * 100.0 / pairs,
                source.Summary.RemoteAcceptable.Value * 100.0 / pairs,
                point * 100.0,
                oneSidedLower * 100.0,
                twoSidedLower * 100.0,
                twoSidedUpper * 100.0,
                NonInferiorityMargin * 100.0,
                oneSidedLower > NonInferiorityMargin,
                ExactMcNemarTwoSided(localOnly, remoteOnly)),
            new AgreementAnalysis(
                source.Summary.BothAcceptable!.Value,
                localOnly,
                remoteOnly,
                source.Summary.NeitherAcceptable!.Value,
                source.Summary.RouteAgreements!.Value,
                source.Summary.RouteAgreements.Value * 100.0 / pairs,
                source.Summary.ExactAgreements!.Value,
                source.Summary.ExactAgreements.Value * 100.0 / pairs),
            new TokenAnalysis(
                source.Summary.LocalUsage,
                source.Summary.RemoteUsage!,
                Scale(source.Summary.LocalUsage, pairs),
                Scale(source.Summary.RemoteUsage!, pairs)),
            new LatencyAnalysis(
                source.Summary.LocalDurationMilliseconds / (double)pairs,
                Median(source.Pairs, true),
                source.Summary.RemoteDurationMilliseconds!.Value / (double)pairs,
                Median(source.Pairs, false)),
            new CostAnalysis(
                "2026-09-01",
                "https://api-docs.deepseek.com/quick_start/pricing/",
                "deepseek-v4-pro",
                offPeakBatch,
                peakBatch,
                offPeakBatch * 1000.0 / pairs,
                peakBatch * 1000.0 / pairs,
                "Remote Provider charge avoided; local hardware and electricity are not priced."),
            GroupAnalyses(source.Pairs),
            CaseAnalyses(source.Pairs));
    }

    private static void ValidateCoverage(ResultDocument source)
    {
        if (source.Protocol != "alice.l1_cost_agreement.result.v1"
            || source.LocalOnly
            || source.Repeats != 5
            || source.Pairs.Count != 120)
            throw new InvalidDataException("The L1 result does not contain the complete 120-pair batch.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (PairResult pair in source.Pairs)
        {
            if (pair.Remote is null || !identities.Add($"{pair.CaseId}/{pair.Repeat}"))
                throw new InvalidDataException("The L1 result has a missing or duplicate paired branch.");
        }
        if (identities.Count != 120)
            throw new InvalidDataException("The L1 result pair identities are incomplete.");
    }

    private static Dictionary<string, List<double>> BuildCaseDifferences(IReadOnlyList<PairResult> pairs)
    {
        var values = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (PairResult pair in pairs)
        {
            if (!values.TryGetValue(pair.CaseId, out List<double>? caseValues))
            {
                caseValues = [];
                values.Add(pair.CaseId, caseValues);
            }
            caseValues.Add((pair.Local.Acceptable ? 1.0 : 0.0) - (pair.Remote!.Acceptable ? 1.0 : 0.0));
        }
        return values;
    }

    private static double MeanCaseDifference(IReadOnlyDictionary<string, List<double>> values)
    {
        double total = 0;
        foreach (List<double> caseValues in values.Values) total += Mean(caseValues);
        return total / values.Count;
    }

    private static double[] Bootstrap(IReadOnlyDictionary<string, List<double>> values)
    {
        var caseMeans = new List<double>();
        foreach (List<double> caseValues in values.Values) caseMeans.Add(Mean(caseValues));
        var random = new Random(BootstrapSeed);
        var samples = new double[BootstrapSamples];
        for (int sample = 0; sample < samples.Length; sample++)
        {
            double total = 0;
            for (int draw = 0; draw < caseMeans.Count; draw++)
                total += caseMeans[random.Next(caseMeans.Count)];
            samples[sample] = total / caseMeans.Count;
        }
        Array.Sort(samples);
        return samples;
    }

    private static double Quantile(IReadOnlyList<double> sorted, double probability)
    {
        double position = (sorted.Count - 1) * probability;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    private static double ExactMcNemarTwoSided(int localOnly, int remoteOnly)
    {
        int discordant = localOnly + remoteOnly;
        int smaller = Math.Min(localOnly, remoteOnly);
        if (discordant == 0) return 1.0;
        double cumulative = 0;
        for (int value = 0; value <= smaller; value++)
            cumulative += Combination(discordant, value) / Math.Pow(2, discordant);
        return Math.Min(1.0, 2.0 * cumulative);
    }

    private static double Combination(int n, int k)
    {
        double result = 1;
        for (int index = 1; index <= k; index++) result = result * (n - k + index) / index;
        return result;
    }

    private static void CountAttempts(
        IReadOnlyList<AttemptResult> attempts,
        ref int decodedAttempts,
        ref int retries)
    {
        if (attempts.Count > 1) retries += attempts.Count - 1;
        foreach (AttemptResult attempt in attempts)
            if (attempt.Outcome == "decoded") decodedAttempts++;
    }

    private static IReadOnlyList<GroupAnalysis> GroupAnalyses(IReadOnlyList<PairResult> pairs)
    {
        string[] groups = ["choose", "defer", "escalate"];
        var results = new List<GroupAnalysis>();
        foreach (string group in groups)
        {
            int count = 0;
            int local = 0;
            int remote = 0;
            int route = 0;
            int exact = 0;
            foreach (PairResult pair in pairs)
            {
                if (pair.Group != group) continue;
                count++;
                if (pair.Local.Acceptable) local++;
                if (pair.Remote!.Acceptable) remote++;
                if (pair.RouteAgreement == true) route++;
                if (pair.ExactAgreement == true) exact++;
            }
            results.Add(new GroupAnalysis(group, count, local, remote, route, exact));
        }
        return results;
    }

    private static IReadOnlyList<CaseAnalysis> CaseAnalyses(IReadOnlyList<PairResult> pairs)
    {
        var order = new List<string>();
        var results = new Dictionary<string, MutableCaseAnalysis>(StringComparer.Ordinal);
        foreach (PairResult pair in pairs)
        {
            if (!results.TryGetValue(pair.CaseId, out MutableCaseAnalysis? value))
            {
                value = new MutableCaseAnalysis(pair.CaseId, pair.Group);
                results.Add(pair.CaseId, value);
                order.Add(pair.CaseId);
            }
            value.Add(pair);
        }
        var output = new List<CaseAnalysis>();
        foreach (string caseId in order) output.Add(results[caseId].Value);
        return output;
    }

    private static TokenUsage Scale(TokenUsage value, int pairs)
    {
        return new TokenUsage(
            Scale(value.InputTokens, pairs),
            Scale(value.OutputTokens, pairs),
            Scale(value.ReasoningTokens, pairs),
            Scale(value.CacheReadInputTokens, pairs),
            Scale(value.CacheCreationInputTokens, pairs),
            Scale(value.TotalTokens, pairs));
    }

    private static long? Scale(long? value, int pairs) =>
        value is null ? null : checked((long)Math.Round(value.Value * 1000.0 / pairs));

    private static double Median(IReadOnlyList<PairResult> pairs, bool local)
    {
        var values = new List<long>();
        foreach (PairResult pair in pairs)
            values.Add(local ? pair.Local.DurationMilliseconds : pair.Remote!.DurationMilliseconds);
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2.0;
    }

    private static double Price(
        long input,
        long cacheRead,
        long cacheCreation,
        long output,
        double missPrice,
        double hitPrice,
        double outputPrice) =>
        ((input + cacheCreation) * missPrice + cacheRead * hitPrice + output * outputPrice) / 1_000_000.0;

    private static long Require(long? value, string name) =>
        value ?? throw new InvalidDataException($"The complete result lacks {name}.");

    private static double Mean(IReadOnlyList<double> values)
    {
        double total = 0;
        foreach (double value in values) total += value;
        return total / values.Count;
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class MutableCaseAnalysis
    {
        private int _pairs;
        private int _local;
        private int _remote;
        private int _route;
        private int _exact;

        public MutableCaseAnalysis(string caseId, string group)
        {
            CaseId = caseId;
            Group = group;
        }

        public string CaseId { get; }
        public string Group { get; }
        public CaseAnalysis Value => new(CaseId, Group, _pairs, _local, _remote, _route, _exact);

        public void Add(PairResult pair)
        {
            _pairs++;
            if (pair.Local.Acceptable) _local++;
            if (pair.Remote!.Acceptable) _remote++;
            if (pair.RouteAgreement == true) _route++;
            if (pair.ExactAgreement == true) _exact++;
        }
    }
}

internal sealed record AnalysisDocument(
    string Protocol,
    DateTimeOffset AnalyzedAtUtc,
    string SourceResultSha256,
    string CasesSha256,
    string ExpectedSha256,
    CoverageAnalysis Coverage,
    QualityAnalysis Quality,
    AgreementAnalysis Agreement,
    TokenAnalysis Tokens,
    LatencyAnalysis Latency,
    CostAnalysis Cost,
    IReadOnlyList<GroupAnalysis> Groups,
    IReadOnlyList<CaseAnalysis> Cases);

internal sealed record CoverageAnalysis(
    int DistinctCases,
    int Repeats,
    int Pairs,
    int DecodedAttempts,
    int Retries,
    bool Complete);

internal sealed record QualityAnalysis(
    int LocalAcceptable,
    int RemoteAcceptable,
    double LocalAcceptablePercent,
    double RemoteAcceptablePercent,
    double LocalMinusRemotePercentagePoints,
    double OneSided95LowerPercentagePoints,
    double TwoSided95LowerPercentagePoints,
    double TwoSided95UpperPercentagePoints,
    double NonInferiorityMarginPercentagePoints,
    bool NonInferiorityPassed,
    double ExactMcNemarTwoSidedP);

internal sealed record AgreementAnalysis(
    int BothAcceptable,
    int LocalOnlyAcceptable,
    int RemoteOnlyAcceptable,
    int NeitherAcceptable,
    int RouteAgreements,
    double RouteAgreementPercent,
    int ExactAgreements,
    double ExactAgreementPercent);

internal sealed record TokenAnalysis(
    TokenUsage LocalObserved,
    TokenUsage RemoteObserved,
    TokenUsage LocalProjectedPer1000,
    TokenUsage RemoteProjectedPer1000);

internal sealed record LatencyAnalysis(
    double LocalMeanMilliseconds,
    double LocalMedianMilliseconds,
    double RemoteMeanMilliseconds,
    double RemoteMedianMilliseconds);

internal sealed record CostAnalysis(
    string PriceSnapshotDate,
    string PriceSource,
    string Model,
    double OffPeakObservedBatchUsd,
    double PeakObservedBatchUsd,
    double OffPeakProjectedPer1000Usd,
    double PeakProjectedPer1000Usd,
    string Boundary);

internal sealed record GroupAnalysis(
    string Group,
    int Pairs,
    int LocalAcceptable,
    int RemoteAcceptable,
    int RouteAgreements,
    int ExactAgreements);

internal sealed record CaseAnalysis(
    string CaseId,
    string Group,
    int Pairs,
    int LocalAcceptable,
    int RemoteAcceptable,
    int RouteAgreements,
    int ExactAgreements);

internal sealed record CaseManifest(
    [property: JsonRequired, JsonPropertyName("protocol")] string Protocol,
    [property: JsonRequired, JsonPropertyName("defaults")] CaseDefaults Defaults,
    [property: JsonRequired, JsonPropertyName("cases")] StudyCase[] Cases);

internal sealed record CaseDefaults(
    [property: JsonRequired, JsonPropertyName("actor_id")] string ActorId,
    [property: JsonRequired, JsonPropertyName("name")] string Name,
    [property: JsonRequired, JsonPropertyName("personality_traits")] string[] PersonalityTraits,
    [property: JsonRequired, JsonPropertyName("aspirations")] string[] Aspirations,
    [property: JsonRequired, JsonPropertyName("current_emotion")] string CurrentEmotion,
    [property: JsonRequired, JsonPropertyName("current_goal_refs")] string[] CurrentGoalRefs,
    [property: JsonRequired, JsonPropertyName("body")] BodyDocument Body,
    [property: JsonRequired, JsonPropertyName("inventory")] InventoryDocument[] Inventory);

internal sealed record StudyCase(
    [property: JsonRequired, JsonPropertyName("case_id")] string CaseId,
    [property: JsonRequired, JsonPropertyName("group")] string Group,
    [property: JsonRequired, JsonPropertyName("domain")] string Domain,
    [property: JsonRequired, JsonPropertyName("subject_ref")] string SubjectRef,
    [property: JsonRequired, JsonPropertyName("visible_failure_count")] int VisibleFailureCount,
    [property: JsonPropertyName("body")] BodyDocument? Body,
    [property: JsonPropertyName("inventory")] InventoryDocument[]? Inventory,
    [property: JsonRequired, JsonPropertyName("candidates")] CandidateDocument[] Candidates);

internal sealed record BodyDocument(
    [property: JsonRequired, JsonPropertyName("health")] int Health,
    [property: JsonRequired, JsonPropertyName("satiety")] int Satiety,
    [property: JsonRequired, JsonPropertyName("spirit")] int Spirit,
    [property: JsonRequired, JsonPropertyName("disease")] string Disease);

internal sealed record InventoryDocument(
    [property: JsonRequired, JsonPropertyName("asset_id")] string AssetId,
    [property: JsonRequired, JsonPropertyName("quantity")] int Quantity);

internal sealed record CandidateDocument(
    [property: JsonRequired, JsonPropertyName("candidate_id")] string CandidateId,
    [property: JsonRequired, JsonPropertyName("kind")] string Kind,
    [property: JsonRequired, JsonPropertyName("target_id")] string TargetId,
    [property: JsonRequired, JsonPropertyName("label")] string Label,
    [property: JsonRequired, JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("unavailable_reason")] string? UnavailableReason);

internal sealed record ExpectedManifest(
    [property: JsonRequired, JsonPropertyName("protocol")] string Protocol,
    [property: JsonRequired, JsonPropertyName("cases")] ExpectedCase[] Cases);

internal sealed record ExpectedCase(
    [property: JsonRequired, JsonPropertyName("case_id")] string CaseId,
    [property: JsonRequired, JsonPropertyName("decision")] string Decision,
    [property: JsonRequired, JsonPropertyName("acceptable_candidate_ids")] string[] AcceptableCandidateIds,
    [property: JsonRequired, JsonPropertyName("acceptable_reason_codes")] string[] AcceptableReasonCodes,
    [property: JsonRequired, JsonPropertyName("required_evidence_refs")] string[] RequiredEvidenceRefs);

internal static class ProgramJson
{
    public static JsonSerializerOptions ReadOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}
