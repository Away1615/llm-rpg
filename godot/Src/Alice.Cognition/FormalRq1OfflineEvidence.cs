using System.Collections.ObjectModel;
using System.Text.Json;
using Alice.Activities;
using Alice.Actors;

namespace Alice.Cognition;

public readonly record struct Rq1OpportunityId
{
    public Rq1OpportunityId(string value)
    {
        DependencyContractIdentity.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public readonly record struct Rq1TestCaseId
{
    public Rq1TestCaseId(string value)
    {
        DependencyContractIdentity.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public readonly record struct Rq1SessionId
{
    public Rq1SessionId(string value)
    {
        DependencyContractIdentity.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public sealed record ActorOpportunityLedgerEntry
{
    public ActorOpportunityLedgerEntry(
        Rq1OpportunityId opportunityId,
        ActorId actorId,
        SimTime eligibleAt,
        SimTime closesAt,
        string sharedEligibilityEvidenceHash,
        int baselineDependencyDegree)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        if (closesAt.Ticks < eligibleAt.Ticks)
        {
            throw new ArgumentOutOfRangeException(nameof(closesAt));
        }

        if (baselineDependencyDegree < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baselineDependencyDegree));
        }

        OpportunityId = opportunityId;
        ActorId = actorId;
        EligibleAt = eligibleAt;
        ClosesAt = closesAt;
        SharedEligibilityEvidenceHash = L2PlanningContextCanonicalJson.ValidateSha256(
            sharedEligibilityEvidenceHash,
            nameof(sharedEligibilityEvidenceHash));
        BaselineDependencyDegree = baselineDependencyDegree;
    }

    public Rq1OpportunityId OpportunityId { get; }
    public ActorId ActorId { get; }
    public SimTime EligibleAt { get; }
    public SimTime ClosesAt { get; }
    public string SharedEligibilityEvidenceHash { get; }
    public int BaselineDependencyDegree { get; }
}

/// <summary>Hidden pre-branch denominator. Runtime condition owners never receive this type.</summary>
public sealed class ActorOpportunityLedger
{
    private const string ProtocolVersion = "actor-opportunity-ledger-v1";
    private readonly ReadOnlyCollection<ActorOpportunityLedgerEntry> _entries;
    private readonly byte[] _canonicalBytes;

    public ActorOpportunityLedger(
        string ledgerId,
        IEnumerable<ActorOpportunityLedgerEntry> entries)
    {
        DependencyContractIdentity.Validate(ledgerId, nameof(ledgerId));
        ArgumentNullException.ThrowIfNull(entries);
        ActorOpportunityLedgerEntry[] snapshot = entries.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(IsNullEntry))
        {
            throw new ArgumentException("Actor opportunity ledger requires non-empty entries.", nameof(entries));
        }

        Array.Sort(snapshot, LedgerEntryComparer.Instance);
        for (int index = 1; index < snapshot.Length; index++)
        {
            if (snapshot[index - 1].OpportunityId == snapshot[index].OpportunityId)
            {
                throw new ArgumentException("Opportunity IDs must be unique.", nameof(entries));
            }
        }

        LedgerId = ledgerId;
        _entries = Array.AsReadOnly(snapshot);
        _canonicalBytes = Serialize(ledgerId, snapshot);
    }

    public string LedgerId { get; }
    public IReadOnlyList<ActorOpportunityLedgerEntry> Entries => _entries;

    public byte[] GetCanonicalBytes()
    {
        return _canonicalBytes.ToArray();
    }

    private static byte[] Serialize(
        string ledgerId,
        IReadOnlyList<ActorOpportunityLedgerEntry> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", ProtocolVersion);
            writer.WriteString("ledger_id", ledgerId);
            writer.WritePropertyName("entries");
            writer.WriteStartArray();
            foreach (ActorOpportunityLedgerEntry entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("opportunity_id", entry.OpportunityId.Value);
                writer.WriteString("actor_id", entry.ActorId.Value);
                writer.WriteNumber("eligible_at_ticks", entry.EligibleAt.Ticks);
                writer.WriteNumber("closes_at_ticks", entry.ClosesAt.Ticks);
                writer.WriteString("shared_eligibility_evidence_hash", entry.SharedEligibilityEvidenceHash);
                writer.WriteNumber("baseline_dependency_degree", entry.BaselineDependencyDegree);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static bool IsNullEntry(ActorOpportunityLedgerEntry entry)
    {
        return entry is null;
    }

    private sealed class LedgerEntryComparer : IComparer<ActorOpportunityLedgerEntry>
    {
        public static LedgerEntryComparer Instance { get; } = new();

        public int Compare(ActorOpportunityLedgerEntry? left, ActorOpportunityLedgerEntry? right)
        {
            return StringComparer.Ordinal.Compare(left!.OpportunityId.Value, right!.OpportunityId.Value);
        }
    }
}

public sealed record Rq1TestCaseLedgerEntry
{
    public Rq1TestCaseLedgerEntry(Rq1TestCaseId testCaseId)
    {
        TestCaseId = testCaseId;
    }

    public Rq1TestCaseId TestCaseId { get; }
}

/// <summary>Hidden pre-branch test-case denominator shared byte-for-byte by both conditions.</summary>
public sealed class Rq1TestCaseLedger
{
    private readonly ReadOnlyCollection<Rq1TestCaseLedgerEntry> _entries;

    public Rq1TestCaseLedger(
        string ledgerId,
        IEnumerable<Rq1TestCaseLedgerEntry> entries)
    {
        DependencyContractIdentity.Validate(ledgerId, nameof(ledgerId));
        ArgumentNullException.ThrowIfNull(entries);
        Rq1TestCaseLedgerEntry[] snapshot = entries.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(entry => entry is null))
        {
            throw new ArgumentException("RQ1 test-case ledger requires non-empty entries.", nameof(entries));
        }

        Array.Sort(snapshot, TestCaseLedgerEntryComparer.Instance);
        for (int index = 1; index < snapshot.Length; index++)
        {
            if (snapshot[index - 1].TestCaseId == snapshot[index].TestCaseId)
            {
                throw new ArgumentException("RQ1 test-case ledger IDs must be unique.", nameof(entries));
            }
        }

        LedgerId = ledgerId;
        _entries = Array.AsReadOnly(snapshot);
    }

    public string LedgerId { get; }
    public IReadOnlyList<Rq1TestCaseLedgerEntry> Entries => _entries;

    public byte[] GetCanonicalBytes() => Serialize(LedgerId, Entries);

    private static byte[] Serialize(
        string ledgerId,
        IEnumerable<Rq1TestCaseLedgerEntry> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", "rq1-test-case-ledger-v1");
            writer.WriteString("ledger_id", ledgerId);
            writer.WritePropertyName("entries");
            writer.WriteStartArray();
            foreach (Rq1TestCaseLedgerEntry entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("test_case_id", entry.TestCaseId.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private sealed class TestCaseLedgerEntryComparer : IComparer<Rq1TestCaseLedgerEntry>
    {
        public static TestCaseLedgerEntryComparer Instance { get; } = new();

        public int Compare(Rq1TestCaseLedgerEntry? left, Rq1TestCaseLedgerEntry? right)
        {
            return StringComparer.Ordinal.Compare(left!.TestCaseId.Value, right!.TestCaseId.Value);
        }
    }
}

public sealed record Rq1ActivationStageEvidence
{
    public Rq1ActivationStageEvidence(
        Rq1OpportunityId opportunityId,
        SimTime? discoveredAt,
        SimTime? needCreatedAt,
        SimTime? admittedAt,
        SimTime? firstValidAttemptAt,
        bool? wasStarvationPromoted)
    {
        ValidateStageOrder(discoveredAt, needCreatedAt, admittedAt, firstValidAttemptAt);
        if (wasStarvationPromoted is not null && needCreatedAt is null)
        {
            throw new ArgumentException("Starvation evidence requires a mapped created Need.");
        }

        if (needCreatedAt is not null && wasStarvationPromoted is null)
        {
            throw new ArgumentException("Every mapped created Need requires an observed starvation-promotion flag.");
        }

        if (admittedAt is not null && wasStarvationPromoted is null)
        {
            throw new ArgumentException("Mapped admission evidence requires an observed starvation-promotion flag.");
        }

        OpportunityId = opportunityId;
        DiscoveredAt = discoveredAt;
        NeedCreatedAt = needCreatedAt;
        AdmittedAt = admittedAt;
        FirstValidAttemptAt = firstValidAttemptAt;
        WasStarvationPromoted = wasStarvationPromoted;
    }

    public Rq1OpportunityId OpportunityId { get; }
    public SimTime? DiscoveredAt { get; }
    public SimTime? NeedCreatedAt { get; }
    public SimTime? AdmittedAt { get; }
    public SimTime? FirstValidAttemptAt { get; }
    public bool? WasStarvationPromoted { get; }

    private static void ValidateStageOrder(params SimTime?[] stages)
    {
        SimTime? prior = null;
        bool gapSeen = false;
        foreach (SimTime? stage in stages)
        {
            if (stage is null)
            {
                gapSeen = true;
                continue;
            }

            if (gapSeen || (prior is SimTime priorValue && stage.Value.Ticks < priorValue.Ticks))
            {
                throw new ArgumentException("Activation stage evidence must be contiguous and monotonic.");
            }

            prior = stage;
        }
    }
}

public sealed record Rq1TestCaseOutcome(
    Rq1TestCaseId TestCaseId,
    bool TaskSucceededBeforeDeadline,
    bool ValidDecisionAttemptBeforeDeadline);

public enum Rq1SessionProductivity
{
    Productive,
    Unproductive,
    ValidOutsideFrozenTestCases
}

public sealed record Rq1SessionOutcome
{
    public Rq1SessionOutcome(
        Rq1SessionId sessionId,
        Rq1SessionProductivity productivity,
        long measuredTokens)
    {
        if (!Enum.IsDefined(productivity))
        {
            throw new ArgumentOutOfRangeException(nameof(productivity));
        }

        if (measuredTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(measuredTokens));
        }

        SessionId = sessionId;
        Productivity = productivity;
        MeasuredTokens = measuredTokens;
    }

    public Rq1SessionId SessionId { get; }
    public Rq1SessionProductivity Productivity { get; }
    public long MeasuredTokens { get; }
}

public readonly record struct Rq1Rate
{
    public Rq1Rate(int numerator, int denominator)
    {
        if (numerator < 0 || denominator < 0 || numerator > denominator)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator));
        }

        Numerator = numerator;
        Denominator = denominator;
    }

    public int Numerator { get; }
    public int Denominator { get; }
    public decimal? Value => Denominator == 0 ? null : (decimal)Numerator / Denominator;
}

public sealed record Rq1ActorOpportunityServiceRow(
    ActorId ActorId,
    int AdmissionCount,
    int ServedByCloseCount,
    int OpportunityCount,
    decimal ServiceRate);

public sealed class Rq1FairnessDiagnostics
{
    private readonly ReadOnlyCollection<Rq1ActorOpportunityServiceRow> _serviceRows;

    internal Rq1FairnessDiagnostics(
        long? actorFirstAdmissionDelayP95Ticks,
        long? actorFirstAdmissionDelayMaxTicks,
        int admittedActorCount,
        int cohortActorCount,
        Rq1Rate actorNeverAdmittedRate,
        int neverDiscoveredActorCount,
        int discoveredButNeverAdmittedActorCount,
        Rq1Rate starvationPromotedActorRate,
        Rq1Rate admittedAfterPromotionActorRate,
        IEnumerable<Rq1ActorOpportunityServiceRow> serviceRows,
        decimal serviceRateMacroMean,
        decimal? serviceRateGini,
        decimal? rawAdmissionCountGini)
    {
        ActorFirstAdmissionDelayP95Ticks = actorFirstAdmissionDelayP95Ticks;
        ActorFirstAdmissionDelayMaxTicks = actorFirstAdmissionDelayMaxTicks;
        AdmittedActorCount = admittedActorCount;
        CohortActorCount = cohortActorCount;
        ActorNeverAdmittedRate = actorNeverAdmittedRate;
        NeverDiscoveredActorCount = neverDiscoveredActorCount;
        DiscoveredButNeverAdmittedActorCount = discoveredButNeverAdmittedActorCount;
        StarvationPromotedActorRate = starvationPromotedActorRate;
        AdmittedAfterPromotionActorRate = admittedAfterPromotionActorRate;
        _serviceRows = Array.AsReadOnly(serviceRows.ToArray());
        ServiceRateMacroMean = serviceRateMacroMean;
        ServiceRateGini = serviceRateGini;
        RawAdmissionCountGini = rawAdmissionCountGini;
    }

    public long? ActorFirstAdmissionDelayP95Ticks { get; }
    public long? ActorFirstAdmissionDelayMaxTicks { get; }
    public int AdmittedActorCount { get; }
    public int CohortActorCount { get; }
    public Rq1Rate ActorNeverAdmittedRate { get; }
    public int NeverDiscoveredActorCount { get; }
    public int DiscoveredButNeverAdmittedActorCount { get; }
    public Rq1Rate StarvationPromotedActorRate { get; }
    public Rq1Rate AdmittedAfterPromotionActorRate { get; }
    public IReadOnlyList<Rq1ActorOpportunityServiceRow> ServiceRows => _serviceRows;
    public decimal ServiceRateMacroMean { get; }
    public decimal? ServiceRateGini { get; }
    public decimal? RawAdmissionCountGini { get; }
}

public sealed record Rq1ConditionScore(
    Rq1Rate TaskSuccessRate,
    Rq1Rate TimelyActivationRecall,
    Rq1Rate UnproductiveSessionRate,
    Rq1FairnessDiagnostics FairnessDiagnostics);

public sealed record FormalRq1MatchedPairScore(
    string OpportunityLedgerId,
    string TestCaseLedgerId,
    Rq1ConditionScore AgentCentric,
    Rq1ConditionScore EventCentric);

/// <summary>Offline-only scorer over frozen hidden denominators and post-run mapped evidence.</summary>
public static class FormalRq1OfflineScorer
{
    public const int FirstAdmissionDelayPercentile = 95;
    private const int PercentScale = 100;

    public static Rq1ConditionScore Score(
        ActorOpportunityLedger ledger,
        Rq1TestCaseLedger testCaseLedger,
        IEnumerable<Rq1ActivationStageEvidence> activationEvidence,
        IEnumerable<Rq1TestCaseOutcome> testCaseOutcomes,
        IEnumerable<Rq1SessionOutcome> sessionOutcomes)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(testCaseLedger);
        Dictionary<Rq1OpportunityId, Rq1ActivationStageEvidence> evidenceByOpportunity =
            SnapshotEvidence(ledger, activationEvidence);
        Rq1TestCaseOutcome[] testCases = SnapshotTestCases(testCaseLedger, testCaseOutcomes);
        Rq1SessionOutcome[] sessions = SnapshotSessions(sessionOutcomes);
        int taskSuccesses = CountTaskSuccesses(testCases);
        int timelyActivations = CountTimelyActivations(testCases);
        int unproductiveSessions = CountUnproductiveSessions(sessions);
        return new Rq1ConditionScore(
            new Rq1Rate(taskSuccesses, testCases.Length),
            new Rq1Rate(timelyActivations, testCases.Length),
            new Rq1Rate(unproductiveSessions, sessions.Length),
            CalculateFairness(ledger, evidenceByOpportunity));
    }

    public static FormalRq1MatchedPairScore ScoreMatchedPair(
        FormalRq1MatchedPairManifest manifest,
        ActorOpportunityLedger opportunityLedger,
        Rq1TestCaseLedger testCaseLedger,
        IEnumerable<Rq1ActivationStageEvidence> agentActivationEvidence,
        IEnumerable<Rq1TestCaseOutcome> agentTestCaseOutcomes,
        IEnumerable<Rq1SessionOutcome> agentSessionOutcomes,
        IEnumerable<Rq1ActivationStageEvidence> eventActivationEvidence,
        IEnumerable<Rq1TestCaseOutcome> eventTestCaseOutcomes,
        IEnumerable<Rq1SessionOutcome> eventSessionOutcomes)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(opportunityLedger);
        ArgumentNullException.ThrowIfNull(testCaseLedger);
        if (!StringComparer.Ordinal.Equals(manifest.AgentCentric.OpportunityLedgerId, opportunityLedger.LedgerId)
            || !StringComparer.Ordinal.Equals(manifest.EventCentric.OpportunityLedgerId, opportunityLedger.LedgerId))
        {
            throw new ArgumentException("Matched-pair scoring ledgers must exactly match both condition manifests.");
        }

        return new FormalRq1MatchedPairScore(
            opportunityLedger.LedgerId,
            testCaseLedger.LedgerId,
            Score(opportunityLedger, testCaseLedger, agentActivationEvidence, agentTestCaseOutcomes, agentSessionOutcomes),
            Score(opportunityLedger, testCaseLedger, eventActivationEvidence, eventTestCaseOutcomes, eventSessionOutcomes));
    }

    private static Dictionary<Rq1OpportunityId, Rq1ActivationStageEvidence> SnapshotEvidence(
        ActorOpportunityLedger ledger,
        IEnumerable<Rq1ActivationStageEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var result = new Dictionary<Rq1OpportunityId, Rq1ActivationStageEvidence>();
        foreach (Rq1ActivationStageEvidence? item in evidence)
        {
            if (item is null || !result.TryAdd(item.OpportunityId, item))
            {
                throw new ArgumentException("Activation evidence must be non-null and unique by OpportunityId.", nameof(evidence));
            }
        }

        var ledgerIds = new HashSet<Rq1OpportunityId>();
        foreach (ActorOpportunityLedgerEntry entry in ledger.Entries)
        {
            ledgerIds.Add(entry.OpportunityId);
        }

        if (!ledgerIds.SetEquals(result.Keys))
        {
            throw new ArgumentException("Activation evidence must cover exactly the frozen opportunity ledger.", nameof(evidence));
        }

        foreach (ActorOpportunityLedgerEntry entry in ledger.Entries)
        {
            ValidateMappedEvidenceTime(entry, result[entry.OpportunityId]);
        }

        return result;
    }

    private static void ValidateMappedEvidenceTime(
        ActorOpportunityLedgerEntry entry,
        Rq1ActivationStageEvidence evidence)
    {
        SimTime?[] stages =
        [
            evidence.DiscoveredAt,
            evidence.NeedCreatedAt,
            evidence.AdmittedAt,
            evidence.FirstValidAttemptAt
        ];
        if (stages.Any(stage => stage is SimTime value && value.Ticks < entry.EligibleAt.Ticks))
        {
            throw new ArgumentException(
                "Mapped activation evidence cannot precede the frozen opportunity eligibility time.",
                nameof(evidence));
        }
    }

    private static Rq1TestCaseOutcome[] SnapshotTestCases(
        Rq1TestCaseLedger ledger,
        IEnumerable<Rq1TestCaseOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        Rq1TestCaseOutcome[] snapshot = outcomes.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(IsNullTestCase))
        {
            throw new ArgumentException("RQ1 scoring requires non-empty test-case outcomes.", nameof(outcomes));
        }

        var ids = new HashSet<Rq1TestCaseId>();
        foreach (Rq1TestCaseOutcome item in snapshot)
        {
            if (!ids.Add(item.TestCaseId))
            {
                throw new ArgumentException("Test-case outcomes must be unique.", nameof(outcomes));
            }
        }

        var ledgerIds = new HashSet<Rq1TestCaseId>(ledger.Entries.Select(entry => entry.TestCaseId));
        if (!ledgerIds.SetEquals(ids))
        {
            throw new ArgumentException("Test-case outcomes must cover exactly the frozen test-case ledger.", nameof(outcomes));
        }

        return snapshot;
    }

    private static Rq1SessionOutcome[] SnapshotSessions(IEnumerable<Rq1SessionOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        Rq1SessionOutcome[] snapshot = outcomes.ToArray();
        if (snapshot.Any(IsNullSession))
        {
            throw new ArgumentException("Session outcomes cannot contain null.", nameof(outcomes));
        }

        var ids = new HashSet<Rq1SessionId>();
        foreach (Rq1SessionOutcome item in snapshot)
        {
            if (!ids.Add(item.SessionId))
            {
                throw new ArgumentException("Session outcomes must be unique.", nameof(outcomes));
            }
        }

        return snapshot;
    }

    private static Rq1FairnessDiagnostics CalculateFairness(
        ActorOpportunityLedger ledger,
        IReadOnlyDictionary<Rq1OpportunityId, Rq1ActivationStageEvidence> evidenceByOpportunity)
    {
        var entriesByActor = new SortedDictionary<string, List<ActorOpportunityLedgerEntry>>(StringComparer.Ordinal);
        var actorByValue = new Dictionary<string, ActorId>(StringComparer.Ordinal);
        foreach (ActorOpportunityLedgerEntry entry in ledger.Entries)
        {
            if (!entriesByActor.TryGetValue(entry.ActorId.Value, out List<ActorOpportunityLedgerEntry>? actorEntries))
            {
                actorEntries = [];
                entriesByActor.Add(entry.ActorId.Value, actorEntries);
                actorByValue.Add(entry.ActorId.Value, entry.ActorId);
            }

            actorEntries.Add(entry);
        }

        var admissionDelays = new List<long>();
        var serviceRows = new List<Rq1ActorOpportunityServiceRow>();
        int admittedActors = 0;
        int neverDiscoveredActors = 0;
        int discoveredButNeverAdmittedActors = 0;
        int queuedEvidenceActors = 0;
        int promotedActors = 0;
        int admittedAfterPromotionActors = 0;
        foreach (KeyValuePair<string, List<ActorOpportunityLedgerEntry>> actorGroup in entriesByActor)
        {
            List<ActorOpportunityLedgerEntry> entries = actorGroup.Value;
            SimTime firstEligible = EarliestEligible(entries);
            Rq1ActivationStageEvidence? firstAdmission = FindFirstAdmission(entries, evidenceByOpportunity);
            bool anyDiscovered = HasAnyDiscovery(entries, evidenceByOpportunity);
            if (firstAdmission is null)
            {
                if (anyDiscovered)
                {
                    discoveredButNeverAdmittedActors++;
                }
                else
                {
                    neverDiscoveredActors++;
                }
            }
            else
            {
                admittedActors++;
                admissionDelays.Add(checked(firstAdmission.AdmittedAt!.Value.Ticks - firstEligible.Ticks));
                if (firstAdmission.WasStarvationPromoted == true)
                {
                    admittedAfterPromotionActors++;
                }
            }

            bool hasQueuedEvidence = false;
            bool hasPromotion = false;
            int admissionCount = 0;
            int servedByClose = 0;
            foreach (ActorOpportunityLedgerEntry entry in entries)
            {
                Rq1ActivationStageEvidence evidence = evidenceByOpportunity[entry.OpportunityId];
                if (evidence.NeedCreatedAt is not null)
                {
                    hasQueuedEvidence = true;
                    hasPromotion |= evidence.WasStarvationPromoted == true;
                }

                if (evidence.AdmittedAt is SimTime admitted)
                {
                    admissionCount++;
                    if (admitted.Ticks <= entry.ClosesAt.Ticks)
                    {
                        servedByClose++;
                    }
                }
            }

            if (hasQueuedEvidence)
            {
                queuedEvidenceActors++;
                if (hasPromotion)
                {
                    promotedActors++;
                }
            }

            serviceRows.Add(new Rq1ActorOpportunityServiceRow(
                actorByValue[actorGroup.Key],
                admissionCount,
                servedByClose,
                entries.Count,
                (decimal)servedByClose / entries.Count));
        }

        int cohortActors = entriesByActor.Count;
        int neverAdmittedActors = cohortActors - admittedActors;
        return new Rq1FairnessDiagnostics(
            NearestRankPercentile(admissionDelays, FirstAdmissionDelayPercentile),
            admissionDelays.Count == 0 ? null : admissionDelays.Max(),
            admittedActors,
            cohortActors,
            new Rq1Rate(neverAdmittedActors, cohortActors),
            neverDiscoveredActors,
            discoveredButNeverAdmittedActors,
            new Rq1Rate(promotedActors, queuedEvidenceActors),
            new Rq1Rate(admittedAfterPromotionActors, admittedActors),
            serviceRows,
            serviceRows.Average(row => row.ServiceRate),
            Gini(serviceRows.Select(row => row.ServiceRate)),
            Gini(serviceRows.Select(row => (decimal)row.AdmissionCount)));
    }

    private static SimTime EarliestEligible(IEnumerable<ActorOpportunityLedgerEntry> entries)
    {
        long earliest = long.MaxValue;
        foreach (ActorOpportunityLedgerEntry entry in entries)
        {
            earliest = Math.Min(earliest, entry.EligibleAt.Ticks);
        }

        return new SimTime(earliest);
    }

    private static Rq1ActivationStageEvidence? FindFirstAdmission(
        IEnumerable<ActorOpportunityLedgerEntry> entries,
        IReadOnlyDictionary<Rq1OpportunityId, Rq1ActivationStageEvidence> evidenceByOpportunity)
    {
        Rq1ActivationStageEvidence? first = null;
        foreach (ActorOpportunityLedgerEntry entry in entries)
        {
            Rq1ActivationStageEvidence candidate = evidenceByOpportunity[entry.OpportunityId];
            if (candidate.AdmittedAt is null)
            {
                continue;
            }

            if (first is null
                || candidate.AdmittedAt.Value.Ticks < first.AdmittedAt!.Value.Ticks
                || (candidate.AdmittedAt.Value == first.AdmittedAt.Value
                    && StringComparer.Ordinal.Compare(
                        candidate.OpportunityId.Value,
                        first.OpportunityId.Value) < 0))
            {
                first = candidate;
            }
        }

        return first;
    }

    private static bool HasAnyDiscovery(
        IEnumerable<ActorOpportunityLedgerEntry> entries,
        IReadOnlyDictionary<Rq1OpportunityId, Rq1ActivationStageEvidence> evidenceByOpportunity)
    {
        foreach (ActorOpportunityLedgerEntry entry in entries)
        {
            if (evidenceByOpportunity[entry.OpportunityId].DiscoveredAt is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static long? NearestRankPercentile(List<long> values, int percentile)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();
        int rank = checked((percentile * values.Count + PercentScale - 1) / PercentScale);
        return values[rank - 1];
    }

    private static decimal? Gini(IEnumerable<decimal> source)
    {
        decimal[] values = source.ToArray();
        decimal sum = 0m;
        foreach (decimal value in values)
        {
            sum += value;
        }

        if (sum == 0m)
        {
            return null;
        }

        decimal absoluteDifferenceSum = 0m;
        foreach (decimal left in values)
        {
            foreach (decimal right in values)
            {
                absoluteDifferenceSum += Math.Abs(left - right);
            }
        }

        decimal mean = sum / values.Length;
        return absoluteDifferenceSum / (2m * values.Length * values.Length * mean);
    }

    private static int CountTaskSuccesses(IEnumerable<Rq1TestCaseOutcome> outcomes)
    {
        int count = 0;
        foreach (Rq1TestCaseOutcome outcome in outcomes)
        {
            if (outcome.TaskSucceededBeforeDeadline)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountTimelyActivations(IEnumerable<Rq1TestCaseOutcome> outcomes)
    {
        int count = 0;
        foreach (Rq1TestCaseOutcome outcome in outcomes)
        {
            if (outcome.ValidDecisionAttemptBeforeDeadline)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountUnproductiveSessions(IEnumerable<Rq1SessionOutcome> outcomes)
    {
        int count = 0;
        foreach (Rq1SessionOutcome outcome in outcomes)
        {
            if (outcome.Productivity == Rq1SessionProductivity.Unproductive)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsNullTestCase(Rq1TestCaseOutcome outcome)
    {
        return outcome is null;
    }

    private static bool IsNullSession(Rq1SessionOutcome outcome)
    {
        return outcome is null;
    }
}
