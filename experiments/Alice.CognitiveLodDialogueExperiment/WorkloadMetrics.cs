using System.Text;
using System.Text.Json;
using Alice.ProductRuntime;

namespace Alice.CognitiveLodDialogueExperiment;

internal sealed class WorkloadMetricsCollector : IDisposable
{
    private readonly string _outputDirectory;
    private readonly string _runId;
    private readonly long _ticksPerDay;
    private readonly long _tickIntervalMilliseconds;
    private readonly string[] _actorIds;
    private readonly StudyArtifactWriter _artifacts;
    private readonly StreamWriter _dailyWriter;
    private readonly Dictionary<long, DailyMutable> _days = [];
    private bool _disposed;

    public WorkloadMetricsCollector(
        string outputDirectory,
        string runId,
        long ticksPerDay,
        long tickIntervalMilliseconds,
        IEnumerable<string> actorIds,
        StudyArtifactWriter artifacts)
    {
        _outputDirectory = outputDirectory;
        _runId = runId;
        _ticksPerDay = ticksPerDay;
        _tickIntervalMilliseconds = tickIntervalMilliseconds;
        _actorIds = actorIds.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        _artifacts = artifacts;
        _dailyWriter = new StreamWriter(
            Path.Combine(outputDirectory, "cognitive_lod_workload_daily.jsonl"),
            false,
            new UTF8Encoding(false));
    }

    public void RecordExecutionBatch(ActorExecutionBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        long day = DayOf(batch.Now.Ticks);
        DailyMutable daily = GetDay(day);
        foreach (ActorExecutionReceipt receipt in batch.Receipts)
        {
            ActorMutable actor = daily.GetActor(receipt.ActorId.Value);
            if (receipt.CognitionRoute == AutonomousNpcCognitionRoute.L0)
            {
                daily.L0Receipts++;
                actor.L0Receipts++;
                if (receipt.Outcome == ActorExecutionOutcome.Completed)
                {
                    daily.L0Completed++;
                    actor.L0Completed++;
                }
                else
                {
                    daily.L0Rejected++;
                    actor.L0Rejected++;
                }
            }
        }
    }

    public void RecordAutonomyL1(
        string actorId,
        long tick,
        string outcomeKind,
        bool hostAccepted,
        IReadOnlyList<string> attemptIds)
    {
        DailyMutable daily = GetDay(DayOf(tick));
        ActorMutable actor = daily.GetActor(actorId);
        daily.L1Requests++;
        actor.L1Requests++;
        switch (outcomeKind)
        {
            case "choice": daily.L1Choices++; actor.L1Choices++; break;
            case "defer": daily.L1Defers++; actor.L1Defers++; break;
            case "escalation": daily.L1EscalationRequests++; actor.L1EscalationRequests++; break;
            default: daily.L1Failures++; actor.L1Failures++; break;
        }
        if (hostAccepted)
        {
            daily.L1HostAccepted++;
            actor.L1HostAccepted++;
        }
        foreach (string attemptId in attemptIds) daily.AutonomyL1AttemptIds.Add(attemptId);
    }

    public void RecordAutonomyL2(
        string actorId,
        long tick,
        string outcomeKind,
        bool providerDispatched,
        IReadOnlyList<string> attemptIds)
    {
        DailyMutable daily = GetDay(DayOf(tick));
        ActorMutable actor = daily.GetActor(actorId);
        if (providerDispatched)
        {
            daily.L2Dispatches++;
            actor.L2Dispatches++;
        }
        else
        {
            daily.L2NotDispatched++;
            actor.L2NotDispatched++;
        }
        if (outcomeKind == "not_dispatched")
        {
            foreach (string attemptId in attemptIds) daily.AutonomyL2AttemptIds.Add(attemptId);
            return;
        }
        if (outcomeKind == "settled")
        {
            daily.L2Settled++;
            actor.L2Settled++;
        }
        else if (outcomeKind == "travel_required")
        {
            daily.L2TravelRequired++;
            actor.L2TravelRequired++;
        }
        else
        {
            daily.L2Failures++;
            actor.L2Failures++;
        }
        foreach (string attemptId in attemptIds) daily.AutonomyL2AttemptIds.Add(attemptId);
    }

    public void CompleteDay(long day)
    {
        DailyMutable daily = GetDay(day);
        CognitiveDailyRecord record = daily.ToRecord(_runId);
        _dailyWriter.WriteLine(JsonSerializer.Serialize(record, StudyJson.JsonLineOptions));
        _dailyWriter.Flush();
    }

    public void WriteCheckpoint(long lastDay, string suffix)
    {
        WriteSummary(
            Path.Combine(_outputDirectory, $"cognitive_lod_workload_summary_{suffix}.json"),
            [new WindowDefinition($"days_1_{lastDay}", 1, lastDay)]);
    }

    public void Complete()
    {
        WriteSummary(
            Path.Combine(_outputDirectory, "cognitive_lod_workload_summary.json"),
            [
                new WindowDefinition("days_1_30", 1, 30),
                new WindowDefinition("days_31_60", 31, 60),
                new WindowDefinition("days_1_60", 1, 60)
            ]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dailyWriter.Dispose();
    }

    private void WriteSummary(string path, IReadOnlyList<WindowDefinition> definitions)
    {
        var windows = new List<CognitiveWindowSummary>();
        var actors = new List<CognitiveActorSummary>();
        foreach (WindowDefinition definition in definitions)
        {
            WindowAggregate aggregate = Aggregate(definition.FirstDay, definition.LastDay);
            double minutes = (definition.LastDay - definition.FirstDay + 1)
                * _ticksPerDay * _tickIntervalMilliseconds / 60000.0;
            ProviderAggregate l1Provider = AggregateProvider(aggregate.AutonomyL1AttemptIds);
            ProviderAggregate l2Provider = AggregateProvider(aggregate.AutonomyL2AttemptIds);
            windows.Add(new CognitiveWindowSummary(
                definition.Name,
                definition.FirstDay,
                definition.LastDay,
                minutes,
                _actorIds.Length,
                aggregate.L0Receipts,
                aggregate.L0Completed,
                aggregate.L0Rejected,
                aggregate.L1Requests,
                aggregate.L1Choices,
                aggregate.L1Defers,
                aggregate.L1EscalationRequests,
                aggregate.L1HostAccepted,
                aggregate.L1Failures,
                aggregate.L2Dispatches,
                aggregate.L2Settled,
                aggregate.L2TravelRequired,
                aggregate.L2Failures,
                aggregate.L2NotDispatched,
                RatePerNpcTenMinutes(aggregate.L1Requests, minutes),
                RatePerTownTenMinutes(aggregate.L1Requests, minutes),
                l1Provider,
                l2Provider));
            foreach (string actorId in _actorIds)
            {
                ActorMutable value = aggregate.GetActor(actorId);
                actors.Add(new CognitiveActorSummary(
                    definition.Name,
                    actorId,
                    value.L0Receipts,
                    value.L0Completed,
                    value.L0Rejected,
                    value.L1Requests,
                    value.L1Choices,
                    value.L1Defers,
                    value.L1EscalationRequests,
                    value.L1HostAccepted,
                    value.L1Failures,
                    value.L2Dispatches,
                    value.L2Settled,
                    value.L2TravelRequired,
                    value.L2Failures,
                    value.L2NotDispatched,
                    minutes == 0 ? null : value.L1Requests * 10.0 / minutes));
            }
        }
        var document = new CognitiveWorkloadSummaryDocument(
            "alice.cognitive_lod.workload_summary.v1",
            _runId,
            _ticksPerDay,
            _tickIntervalMilliseconds,
            windows.ToArray(),
            actors.ToArray());
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(document, StudyJson.WriteOptions));
    }

    private WindowAggregate Aggregate(long firstDay, long lastDay)
    {
        var result = new WindowAggregate();
        for (long day = firstDay; day <= lastDay; day++)
        {
            if (!_days.TryGetValue(day, out DailyMutable? value)) continue;
            result.Add(value);
        }
        return result;
    }

    private ProviderAggregate AggregateProvider(IReadOnlySet<string> attemptIds)
    {
        int attempts = 0;
        int failures = 0;
        int usageKnown = 0;
        long input = 0;
        long output = 0;
        long reasoning = 0;
        long total = 0;
        bool completeUsage = true;
        var latency = new List<long>();
        foreach (ProviderAttemptRecord attempt in _artifacts.Attempts)
        {
            if (!attemptIds.Contains(attempt.AttemptId)) continue;
            attempts++;
            if (!StringComparer.Ordinal.Equals(attempt.Outcome, "response_received")) failures++;
            if (attempt.UsageKnown) usageKnown++;
            if (attempt.InputTokens is null || attempt.OutputTokens is null || attempt.TotalTokens is null)
                completeUsage = false;
            else
            {
                input = checked(input + attempt.InputTokens.Value);
                output = checked(output + attempt.OutputTokens.Value);
                reasoning = checked(reasoning + (attempt.ReasoningTokens ?? 0));
                total = checked(total + attempt.TotalTokens.Value);
            }
            latency.Add(attempt.DurationMilliseconds);
        }
        latency.Sort();
        double? median = latency.Count == 0
            ? null
            : latency.Count % 2 == 1
                ? latency[latency.Count / 2]
                : (latency[latency.Count / 2 - 1] + latency[latency.Count / 2]) / 2.0;
        return new ProviderAggregate(
            attempts,
            failures,
            usageKnown,
            completeUsage ? input : null,
            completeUsage ? output : null,
            completeUsage ? reasoning : null,
            completeUsage ? total : null,
            median);
    }

    private double? RatePerNpcTenMinutes(long count, double minutes) =>
        minutes == 0 || _actorIds.Length == 0 ? null : count * 10.0 / minutes / _actorIds.Length;

    private static double? RatePerTownTenMinutes(long count, double minutes) =>
        minutes == 0 ? null : count * 10.0 / minutes;

    private long DayOf(long tick) => tick / _ticksPerDay + 1;

    private DailyMutable GetDay(long day)
    {
        if (!_days.TryGetValue(day, out DailyMutable? value))
        {
            value = new DailyMutable(day);
            _days.Add(day, value);
        }
        return value;
    }

    private sealed record WindowDefinition(string Name, long FirstDay, long LastDay);
}

internal class CounterSet
{
    public long L0Receipts;
    public long L0Completed;
    public long L0Rejected;
    public long L1Requests;
    public long L1Choices;
    public long L1Defers;
    public long L1EscalationRequests;
    public long L1HostAccepted;
    public long L1Failures;
    public long L2Dispatches;
    public long L2Settled;
    public long L2TravelRequired;
    public long L2Failures;
    public long L2NotDispatched;

    public void Add(CounterSet value)
    {
        L0Receipts += value.L0Receipts;
        L0Completed += value.L0Completed;
        L0Rejected += value.L0Rejected;
        L1Requests += value.L1Requests;
        L1Choices += value.L1Choices;
        L1Defers += value.L1Defers;
        L1EscalationRequests += value.L1EscalationRequests;
        L1HostAccepted += value.L1HostAccepted;
        L1Failures += value.L1Failures;
        L2Dispatches += value.L2Dispatches;
        L2Settled += value.L2Settled;
        L2TravelRequired += value.L2TravelRequired;
        L2Failures += value.L2Failures;
        L2NotDispatched += value.L2NotDispatched;
    }
}

internal sealed class ActorMutable : CounterSet
{
}

internal sealed class DailyMutable : CounterSet
{
    private readonly Dictionary<string, ActorMutable> _actors = new(StringComparer.Ordinal);

    public DailyMutable(long day) => Day = day;

    public long Day { get; }
    public HashSet<string> AutonomyL1AttemptIds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> AutonomyL2AttemptIds { get; } = new(StringComparer.Ordinal);

    public ActorMutable GetActor(string actorId)
    {
        if (!_actors.TryGetValue(actorId, out ActorMutable? value))
        {
            value = new ActorMutable();
            _actors.Add(actorId, value);
        }
        return value;
    }

    public IEnumerable<KeyValuePair<string, ActorMutable>> Actors => _actors;

    public CognitiveDailyRecord ToRecord(string runId) => new(
        "alice.cognitive_lod.workload_daily.v1",
        runId,
        Day,
        L0Receipts,
        L0Completed,
        L0Rejected,
        L1Requests,
        L1Choices,
        L1Defers,
        L1EscalationRequests,
        L1HostAccepted,
        L1Failures,
        L2Dispatches,
        L2Settled,
        L2TravelRequired,
        L2Failures,
        L2NotDispatched);
}

internal sealed class WindowAggregate : CounterSet
{
    private readonly Dictionary<string, ActorMutable> _actors = new(StringComparer.Ordinal);

    public HashSet<string> AutonomyL1AttemptIds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> AutonomyL2AttemptIds { get; } = new(StringComparer.Ordinal);

    public void Add(DailyMutable value)
    {
        base.Add(value);
        foreach (string attemptId in value.AutonomyL1AttemptIds) AutonomyL1AttemptIds.Add(attemptId);
        foreach (string attemptId in value.AutonomyL2AttemptIds) AutonomyL2AttemptIds.Add(attemptId);
        foreach (KeyValuePair<string, ActorMutable> item in value.Actors) GetActor(item.Key).Add(item.Value);
    }

    public ActorMutable GetActor(string actorId)
    {
        if (!_actors.TryGetValue(actorId, out ActorMutable? value))
        {
            value = new ActorMutable();
            _actors.Add(actorId, value);
        }
        return value;
    }
}

internal sealed record CognitiveDailyRecord(
    string Protocol,
    string RunId,
    long Day,
    long L0Receipts,
    long L0Completed,
    long L0Rejected,
    long L1Requests,
    long L1Choices,
    long L1Defers,
    long L1EscalationRequests,
    long L1HostAccepted,
    long L1Failures,
    long L2Dispatches,
    long L2Settled,
    long L2TravelRequired,
    long L2Failures,
    long L2NotDispatched);

internal sealed record CognitiveWorkloadSummaryDocument(
    string Protocol,
    string RunId,
    long TicksPerDay,
    long TickIntervalMilliseconds,
    CognitiveWindowSummary[] Windows,
    CognitiveActorSummary[] Actors);

internal sealed record CognitiveWindowSummary(
    string Window,
    long FirstDay,
    long LastDay,
    double DemoMinutes,
    int NpcCount,
    long L0Receipts,
    long L0Completed,
    long L0Rejected,
    long L1Requests,
    long L1Choices,
    long L1Defers,
    long L1EscalationRequests,
    long L1HostAccepted,
    long L1Failures,
    long L2Dispatches,
    long L2Settled,
    long L2TravelRequired,
    long L2Failures,
    long L2NotDispatched,
    double? L1RequestsPerNpcTenMinutes,
    double? L1RequestsPerTownTenMinutes,
    ProviderAggregate AutonomyL1Provider,
    ProviderAggregate AutonomyL2Provider);

internal sealed record CognitiveActorSummary(
    string Window,
    string ActorId,
    long L0Receipts,
    long L0Completed,
    long L0Rejected,
    long L1Requests,
    long L1Choices,
    long L1Defers,
    long L1EscalationRequests,
    long L1HostAccepted,
    long L1Failures,
    long L2Dispatches,
    long L2Settled,
    long L2TravelRequired,
    long L2Failures,
    long L2NotDispatched,
    double? L1RequestsPerTenMinutes);

internal sealed record ProviderAggregate(
    int Attempts,
    int FailedAttempts,
    int UsageKnownAttempts,
    long? InputTokens,
    long? OutputTokens,
    long? ReasoningTokens,
    long? TotalTokens,
    double? MedianLatencyMilliseconds);
