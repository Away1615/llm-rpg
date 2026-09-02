using System.Text;
using System.Text.Json;

namespace Alice.CognitiveLodDialogueExperiment;

internal sealed class StudyArtifactWriter : IDisposable
{
    private readonly StreamWriter _lifecycleWriter;
    private readonly StreamWriter _attemptWriter;
    private readonly List<DialogueLifecycleRecord> _lifecycles = [];
    private readonly List<ProviderAttemptRecord> _attempts = [];
    private readonly string _payloadDirectory;
    private int _attemptSequence;
    private bool _disposed;

    public StudyArtifactWriter(string outputDirectory, string runId, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        string fullPath = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any())
            throw new IOException($"Output directory is not empty: {fullPath}");
        Directory.CreateDirectory(fullPath);
        _payloadDirectory = Path.Combine(fullPath, "provider_payloads");
        Directory.CreateDirectory(_payloadDirectory);
        OutputDirectory = fullPath;
        RunId = runId;
        Source = source;
        _lifecycleWriter = CreateWriter(Path.Combine(fullPath, "dialogue_lod_lifecycle.jsonl"));
        _attemptWriter = CreateWriter(Path.Combine(fullPath, "dialogue_lod_provider_attempts.jsonl"));
    }

    public string OutputDirectory { get; }
    public string RunId { get; }
    public string Source { get; }
    public IReadOnlyList<DialogueLifecycleRecord> Lifecycles => _lifecycles;
    public IReadOnlyList<ProviderAttemptRecord> Attempts => _attempts;

    public void WriteManifest(
        StudyInputs inputs,
        string mode,
        bool liveProvider,
        int npcCount,
        int? controlledRepeats,
        int? workloadDays,
        string worldPath,
        string surfacePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var document = new RunManifestDocument(
            "alice.dialogue_lod.run_manifest.v1",
            RunId,
            Source,
            mode,
            DateTimeOffset.UtcNow,
            liveProvider,
            npcCount,
            controlledRepeats,
            workloadDays,
            inputs.CasesSha256,
            inputs.ExpectedSha256,
            StudyJson.Sha256(File.ReadAllBytes(worldPath)),
            StudyJson.Sha256(File.ReadAllBytes(surfacePath)));
        File.WriteAllBytes(
            Path.Combine(OutputDirectory, "dialogue_lod_run_manifest.json"),
            JsonSerializer.SerializeToUtf8Bytes(document, StudyJson.WriteOptions));
    }

    public void AddLifecycle(DialogueLifecycleRecord record)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lifecycles.Add(record);
        _lifecycleWriter.WriteLine(JsonSerializer.Serialize(record, StudyJson.JsonLineOptions));
        _lifecycleWriter.Flush();
    }

    public ProviderAttemptRecord AddAttempt(
        string role,
        string? modelId,
        string outcome,
        int? httpStatus,
        long durationMilliseconds,
        TokenUsageSnapshot? usage,
        byte[] requestBody,
        byte[]? responseBody,
        string? failure)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int sequence = checked(++_attemptSequence);
        string attemptId = $"{RunId}-attempt-{sequence:D5}";
        string requestFile = $"{attemptId}-request.json";
        string? responseFile = responseBody is null ? null : $"{attemptId}-response.json";
        File.WriteAllBytes(Path.Combine(_payloadDirectory, requestFile), requestBody);
        if (responseBody is not null)
            File.WriteAllBytes(Path.Combine(_payloadDirectory, responseFile!), responseBody);
        var record = new ProviderAttemptRecord(
            "alice.dialogue_lod.provider_attempt.v1",
            attemptId,
            role,
            modelId,
            outcome,
            httpStatus,
            durationMilliseconds,
            usage?.InputTokens,
            usage?.OutputTokens,
            usage?.ReasoningTokens,
            usage?.CacheReadInputTokens,
            usage?.CacheCreationInputTokens,
            usage?.TotalTokens,
            usage is not null,
            StudyJson.Sha256(requestBody),
            responseBody is null ? null : StudyJson.Sha256(responseBody),
            Path.Combine("provider_payloads", requestFile).Replace('\\', '/'),
            responseFile is null ? null : Path.Combine("provider_payloads", responseFile).Replace('\\', '/'),
            failure);
        _attempts.Add(record);
        _attemptWriter.WriteLine(JsonSerializer.Serialize(record, StudyJson.JsonLineOptions));
        _attemptWriter.Flush();
        return record;
    }

    public void Complete(StudyInputs inputs, int expectedRecords)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CoverageDocument coverage = CreateCoverage(expectedRecords);
        SummaryDocument summary = CreateSummary(inputs, null);
        File.WriteAllBytes(
            Path.Combine(OutputDirectory, "dialogue_lod_coverage.json"),
            JsonSerializer.SerializeToUtf8Bytes(coverage, StudyJson.WriteOptions));
        File.WriteAllBytes(
            Path.Combine(OutputDirectory, "dialogue_lod_summary.json"),
            JsonSerializer.SerializeToUtf8Bytes(summary, StudyJson.WriteOptions));
    }

    public void WriteCheckpoint(StudyInputs inputs, int expectedRecords, string checkpointName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointName);
        CoverageDocument coverage = CreateCoverage(expectedRecords);
        long? lastDay = StringComparer.Ordinal.Equals(checkpointName, "day30") ? 30 : null;
        SummaryDocument summary = CreateSummary(inputs, lastDay);
        File.WriteAllBytes(
            Path.Combine(OutputDirectory, $"dialogue_lod_coverage_{checkpointName}.json"),
            JsonSerializer.SerializeToUtf8Bytes(coverage, StudyJson.WriteOptions));
        File.WriteAllBytes(
            Path.Combine(OutputDirectory, $"dialogue_lod_summary_{checkpointName}.json"),
            JsonSerializer.SerializeToUtf8Bytes(summary, StudyJson.WriteOptions));
    }

    public IReadOnlyList<string> AttemptIdsAfter(int priorCount, string role)
    {
        if (priorCount < 0 || priorCount > _attempts.Count)
            throw new ArgumentOutOfRangeException(nameof(priorCount));
        var result = new List<string>();
        for (int index = priorCount; index < _attempts.Count; index++)
        {
            ProviderAttemptRecord attempt = _attempts[index];
            if (StringComparer.Ordinal.Equals(attempt.Role, role)) result.Add(attempt.AttemptId);
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifecycleWriter.Dispose();
        _attemptWriter.Dispose();
    }

    private CoverageDocument CreateCoverage(int expectedRecords)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        int duplicates = 0;
        int unfinished = 0;
        foreach (DialogueLifecycleRecord record in _lifecycles)
        {
            string key = $"{record.CaseId ?? record.OpportunityId}/{record.Repeat?.ToString() ?? "natural"}";
            if (!keys.Add(key)) duplicates++;
            if (record.TerminalKind == "unfinished") unfinished++;
        }
        int missing = Math.Max(0, expectedRecords - keys.Count);
        return new CoverageDocument(
            "alice.dialogue_lod.coverage.v1",
            RunId,
            Source,
            expectedRecords,
            _lifecycles.Count,
            duplicates,
            missing,
            unfinished,
            _lifecycles.Count == expectedRecords && duplicates == 0 && missing == 0 && unfinished == 0);
    }

    private SummaryDocument CreateSummary(StudyInputs inputs, long? checkpointLastDay)
    {
        SummaryWindow[] windows;
        ProviderRoleSummary[] providerRoles;
        if (Source == "workload")
        {
            if (checkpointLastDay == 30)
            {
                windows = [CreateWindow("days_1_30", 1, 30)];
                providerRoles = CreateProviderRoleSummaries("days_1_30", 1, 30, 30);
            }
            else
            {
                windows =
                [
                    CreateWindow("days_1_30", 1, 30),
                    CreateWindow("days_31_60", 31, 60),
                    CreateWindow("days_1_60", 1, 60)
                ];
                providerRoles =
                [
                    .. CreateProviderRoleSummaries("days_1_30", 1, 30, 30),
                    .. CreateProviderRoleSummaries("days_31_60", 31, 60, 30),
                    .. CreateProviderRoleSummaries("days_1_60", 1, 60, 60)
                ];
            }
        }
        else
        {
            windows = [CreateWindow("controlled_suite", null, null)];
            providerRoles = CreateProviderRoleSummaries("controlled_suite", null, null, null);
        }
        return new SummaryDocument(
            "alice.dialogue_lod.summary.v1",
            RunId,
            Source,
            inputs.CasesSha256,
            inputs.ExpectedSha256,
            windows,
            providerRoles,
            CountDialogueAttempts(),
            SumKnown(GetInputTokens),
            SumKnown(GetOutputTokens),
            SumKnown(GetReasoningTokens),
            SumKnown(GetTotalTokens));
    }

    private SummaryWindow CreateWindow(string name, long? firstDay, long? lastDay)
    {
        var records = new List<DialogueLifecycleRecord>();
        foreach (DialogueLifecycleRecord record in _lifecycles)
        {
            if (firstDay is not null && record.SimulationDay < firstDay.Value) continue;
            if (lastDay is not null && record.SimulationDay > lastDay.Value) continue;
            records.Add(record);
        }
        int invites = Count(records, IsInvite);
        int l0 = Count(records, IsL0Settlement);
        int l1Entries = Count(records, EnteredL1);
        int l1Settled = Count(records, IsL1Settlement);
        int escalationRequests = Count(records, RequestedEscalation);
        int hostAccepted = Count(records, HostAcceptedEscalation);
        int l2Dispatches = Count(records, DispatchedL2);
        int routeScored = Count(records, HasRouteScore);
        int routeMatches = Count(records, IsRouteMatch);
        int terminalScored = Count(records, HasTerminalScore);
        int terminalSuccesses = Count(records, IsTerminalSuccess);
        return new SummaryWindow(
            name,
            records.Count,
            l0,
            invites,
            Rate(l0, invites),
            l1Settled,
            l1Entries,
            Rate(l1Settled, l1Entries),
            escalationRequests,
            Rate(escalationRequests, l1Entries),
            hostAccepted,
            Rate(hostAccepted, escalationRequests),
            l2Dispatches,
            Rate(l2Dispatches, records.Count),
            routeMatches,
            routeScored,
            Rate(routeMatches, routeScored),
            terminalSuccesses,
            terminalScored,
            Rate(terminalSuccesses, terminalScored));
    }

    private ProviderRoleSummary[] CreateProviderRoleSummaries(
        string window,
        long? firstDay,
        long? lastDay,
        int? gameDays)
    {
        var attemptIds = new HashSet<string>(StringComparer.Ordinal);
        int opportunities = 0;
        foreach (DialogueLifecycleRecord record in _lifecycles)
        {
            if (firstDay is not null && record.SimulationDay < firstDay.Value) continue;
            if (lastDay is not null && record.SimulationDay > lastDay.Value) continue;
            opportunities++;
            foreach (string attemptId in record.LocalAttemptIds) attemptIds.Add(attemptId);
            foreach (string attemptId in record.RemoteAttemptIds) attemptIds.Add(attemptId);
        }
        var values = new List<ProviderRoleSummary>();
        values.Add(CreateProviderRoleSummary(window, "dialogue_l1", attemptIds, opportunities, gameDays));
        values.Add(CreateProviderRoleSummary(window, "dialogue_l2", attemptIds, opportunities, gameDays));
        return values.ToArray();
    }

    private ProviderRoleSummary CreateProviderRoleSummary(
        string window,
        string role,
        IReadOnlySet<string> attemptIds,
        int opportunities,
        int? gameDays)
    {
        var attempts = new List<ProviderAttemptRecord>();
        foreach (ProviderAttemptRecord attempt in _attempts)
        {
            if (StringComparer.Ordinal.Equals(attempt.Role, role) && attemptIds.Contains(attempt.AttemptId))
                attempts.Add(attempt);
        }
        int failed = 0;
        int usageKnown = 0;
        var latency = new List<long>();
        foreach (ProviderAttemptRecord attempt in attempts)
        {
            if (!StringComparer.Ordinal.Equals(attempt.Outcome, "response_received")) failed++;
            if (attempt.UsageKnown) usageKnown++;
            latency.Add(attempt.DurationMilliseconds);
        }
        latency.Sort();
        double? median = latency.Count == 0
            ? null
            : latency.Count % 2 == 1
                ? latency[latency.Count / 2]
                : (latency[latency.Count / 2 - 1] + latency[latency.Count / 2]) / 2.0;
        long? totalTokens = SumKnown(attempts, GetTotalTokens);
        return new ProviderRoleSummary(
            window,
            role,
            attempts.Count,
            failed,
            usageKnown,
            SumKnown(attempts, GetInputTokens),
            SumKnown(attempts, GetOutputTokens),
            SumKnown(attempts, GetReasoningTokens),
            SumKnown(attempts, GetCacheReadTokens),
            SumKnown(attempts, GetCacheCreationTokens),
            totalTokens,
            median,
            gameDays is null ? null : attempts.Count * 10.0 / gameDays.Value,
            opportunities == 0 ? null : attempts.Count * 100.0 / opportunities,
            gameDays is null || totalTokens is null ? null : totalTokens.Value * 10.0 / gameDays.Value,
            opportunities == 0 || totalTokens is null ? null : totalTokens.Value * 100.0 / opportunities);
    }

    private long? SumKnown(Func<ProviderAttemptRecord, long?> selector)
    {
        long total = 0;
        foreach (ProviderAttemptRecord attempt in _attempts)
        {
            if (!IsDialogueAttempt(attempt)) continue;
            long? value = selector(attempt);
            if (value is null) return null;
            total = checked(total + value.Value);
        }
        return total;
    }

    private static long? SumKnown(
        IReadOnlyList<ProviderAttemptRecord> attempts,
        Func<ProviderAttemptRecord, long?> selector)
    {
        long total = 0;
        foreach (ProviderAttemptRecord attempt in attempts)
        {
            long? value = selector(attempt);
            if (value is null) return null;
            total = checked(total + value.Value);
        }
        return total;
    }

    private int CountDialogueAttempts()
    {
        int count = 0;
        foreach (ProviderAttemptRecord attempt in _attempts)
        {
            if (IsDialogueAttempt(attempt)) count++;
        }
        return count;
    }

    private static int Count(
        IEnumerable<DialogueLifecycleRecord> records,
        Func<DialogueLifecycleRecord, bool> predicate)
    {
        int count = 0;
        foreach (DialogueLifecycleRecord record in records)
        {
            if (predicate(record)) count++;
        }
        return count;
    }

    private static bool IsInvite(DialogueLifecycleRecord value) => value.SourceKind == "Invite";
    private static bool IsL0Settlement(DialogueLifecycleRecord value) => value.ActualRoute == "L0";
    private static bool EnteredL1(DialogueLifecycleRecord value) => value.L1Decision is not null;
    private static bool IsL1Settlement(DialogueLifecycleRecord value) =>
        value.ActualRoute == "L1" && value.TerminalKind == "LocalReply";
    private static bool RequestedEscalation(DialogueLifecycleRecord value) => value.EscalationRequested;
    private static bool HostAcceptedEscalation(DialogueLifecycleRecord value) => value.HostAccepted == true;
    private static bool DispatchedL2(DialogueLifecycleRecord value) => value.L2ProviderDispatched;
    private static bool HasRouteScore(DialogueLifecycleRecord value) => value.RouteMatch is not null;
    private static bool IsRouteMatch(DialogueLifecycleRecord value) => value.RouteMatch == true;
    private static bool HasTerminalScore(DialogueLifecycleRecord value) => value.TerminalAcceptable is not null;
    private static bool IsTerminalSuccess(DialogueLifecycleRecord value) => value.TerminalAcceptable == true;
    private static long? GetInputTokens(ProviderAttemptRecord value) => value.InputTokens;
    private static long? GetOutputTokens(ProviderAttemptRecord value) => value.OutputTokens;
    private static long? GetReasoningTokens(ProviderAttemptRecord value) => value.ReasoningTokens;
    private static long? GetCacheReadTokens(ProviderAttemptRecord value) => value.CacheReadInputTokens;
    private static long? GetCacheCreationTokens(ProviderAttemptRecord value) => value.CacheCreationInputTokens;
    private static long? GetTotalTokens(ProviderAttemptRecord value) => value.TotalTokens;
    private static bool IsDialogueAttempt(ProviderAttemptRecord value) =>
        value.Role is "dialogue_l1" or "dialogue_l2";
    private static double? Rate(int numerator, int denominator) =>
        denominator == 0 ? null : numerator / (double)denominator;

    private static StreamWriter CreateWriter(string path) =>
        new(path, false, new UTF8Encoding(false));
}

internal sealed record TokenUsageSnapshot(
    long? InputTokens,
    long? OutputTokens,
    long? ReasoningTokens,
    long? CacheReadInputTokens,
    long? CacheCreationInputTokens,
    long? TotalTokens);
