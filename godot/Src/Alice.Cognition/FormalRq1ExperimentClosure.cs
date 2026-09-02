using System.Collections.ObjectModel;
using System.Text.Json;
using Alice.Activities;
using Alice.ModelRuntime;

namespace Alice.Cognition;

public readonly record struct FormalTerminalReceiptId
{
    public FormalTerminalReceiptId(string value)
    {
        FormalExperimentCanonical.RequireIdentity(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public sealed record FormalRq1OpportunityTestCaseMapEntry(
    Rq1OpportunityId OpportunityId,
    Rq1TestCaseId TestCaseId);

/// <summary>Hidden pre-branch opportunity-to-TestCase mapping. It is never supplied to a condition executor.</summary>
public sealed class FormalRq1OpportunityTestCaseMap
{
    private readonly ReadOnlyCollection<FormalRq1OpportunityTestCaseMapEntry> _entries;
    private readonly byte[] _canonicalBytes;

    public FormalRq1OpportunityTestCaseMap(
        ActorOpportunityLedger opportunityLedger,
        Rq1TestCaseLedger testCaseLedger,
        IEnumerable<FormalRq1OpportunityTestCaseMapEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(opportunityLedger);
        ArgumentNullException.ThrowIfNull(testCaseLedger);
        ArgumentNullException.ThrowIfNull(entries);
        FormalRq1OpportunityTestCaseMapEntry[] snapshot = entries.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(IsNullEntry))
        {
            throw new ArgumentException("RQ1 opportunity mapping requires non-empty entries.", nameof(entries));
        }

        Array.Sort(snapshot, EntryComparer.Instance);
        var mappedOpportunityIds = new HashSet<Rq1OpportunityId>();
        var ledgerOpportunityIds = new HashSet<Rq1OpportunityId>(
            opportunityLedger.Entries.Select(GetOpportunityId));
        var ledgerTestCaseIds = new HashSet<Rq1TestCaseId>(
            testCaseLedger.Entries.Select(GetTestCaseId));
        foreach (FormalRq1OpportunityTestCaseMapEntry entry in snapshot)
        {
            if (!mappedOpportunityIds.Add(entry.OpportunityId)
                || !ledgerOpportunityIds.Contains(entry.OpportunityId)
                || !ledgerTestCaseIds.Contains(entry.TestCaseId))
            {
                throw new ArgumentException(
                    "Every opportunity must map exactly once to one frozen TestCase.",
                    nameof(entries));
            }
        }

        if (!mappedOpportunityIds.SetEquals(ledgerOpportunityIds))
        {
            throw new ArgumentException(
                "Opportunity mapping must cover the exact frozen opportunity ledger.",
                nameof(entries));
        }

        _entries = Array.AsReadOnly(snapshot);
        OpportunityLedgerId = opportunityLedger.LedgerId;
        TestCaseLedgerId = testCaseLedger.LedgerId;
        _canonicalBytes = Serialize();
        MappingHash = FormalExperimentCanonical.Hash(_canonicalBytes);
    }

    public IReadOnlyList<FormalRq1OpportunityTestCaseMapEntry> Entries => _entries;
    public string OpportunityLedgerId { get; }
    public string TestCaseLedgerId { get; }
    public string MappingHash { get; }

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
            writer.WriteString("schema_version", "alice.formal-rq1-opportunity-testcase-map.v1");
            writer.WriteString("opportunity_ledger_id", OpportunityLedgerId);
            writer.WriteString("test_case_ledger_id", TestCaseLedgerId);
            writer.WritePropertyName("entries");
            writer.WriteStartArray();
            foreach (FormalRq1OpportunityTestCaseMapEntry entry in Entries)
            {
                writer.WriteStartObject();
                writer.WriteString("opportunity_id", entry.OpportunityId.Value);
                writer.WriteString("test_case_id", entry.TestCaseId.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static bool IsNullEntry(FormalRq1OpportunityTestCaseMapEntry entry)
    {
        return entry is null;
    }

    private static Rq1OpportunityId GetOpportunityId(ActorOpportunityLedgerEntry entry)
    {
        return entry.OpportunityId;
    }

    private static Rq1TestCaseId GetTestCaseId(Rq1TestCaseLedgerEntry entry)
    {
        return entry.TestCaseId;
    }

    private sealed class EntryComparer : IComparer<FormalRq1OpportunityTestCaseMapEntry>
    {
        public static EntryComparer Instance { get; } = new();

        public int Compare(
            FormalRq1OpportunityTestCaseMapEntry? left,
            FormalRq1OpportunityTestCaseMapEntry? right)
        {
            return StringComparer.Ordinal.Compare(
                left!.OpportunityId.Value,
                right!.OpportunityId.Value);
        }
    }
}

public sealed record FormalRq1HiddenTestCase
{
    public FormalRq1HiddenTestCase(
        Rq1TestCaseId testCaseId,
        FormalRq1TerminalOutcomeKind expectedTerminalKind,
        string? expectedGameActionId = null,
        string? expectedAuthorityActionFamily = null)
    {
        if (expectedTerminalKind is not FormalRq1TerminalOutcomeKind.AuthorityCommitted
            and not FormalRq1TerminalOutcomeKind.JustifiedDefer)
            throw new ArgumentOutOfRangeException(nameof(expectedTerminalKind));
        if (expectedTerminalKind == FormalRq1TerminalOutcomeKind.AuthorityCommitted)
            FormalExperimentCanonical.RequireIdentity(
                expectedGameActionId ?? throw new ArgumentNullException(nameof(expectedGameActionId)),
                nameof(expectedGameActionId));
        else if (expectedGameActionId is not null || expectedAuthorityActionFamily is not null)
            throw new ArgumentException("A justified defer has no GameActionId or action family.", nameof(expectedGameActionId));
        if (expectedAuthorityActionFamily is not null)
            FormalExperimentCanonical.RequireIdentity(expectedAuthorityActionFamily, nameof(expectedAuthorityActionFamily));

        TestCaseId = testCaseId;
        ExpectedTerminalKind = expectedTerminalKind;
        ExpectedGameActionId = expectedGameActionId;
        ExpectedAuthorityActionFamily = expectedAuthorityActionFamily;
    }

    public Rq1TestCaseId TestCaseId { get; }
    public FormalRq1TerminalOutcomeKind ExpectedTerminalKind { get; }
    public string? ExpectedGameActionId { get; }
    public string? ExpectedAuthorityActionFamily { get; }
}

public enum FormalRq1TerminalOutcomeKind
{
    InvalidDecision,
    TransportFailure,
    ValidatorRejected,
    JustifiedDefer,
    AuthorityCommitted
}

/// <summary>Post-run typed mapping from one hidden opportunity to its observed Need/session/receipt chain.</summary>
public sealed record FormalRq1OpportunityRunEvidence
{
    public FormalRq1OpportunityRunEvidence(
        Rq1OpportunityId opportunityId,
        SimTime? discoveredAt,
        SimTime? needCreatedAt,
        SimTime? admittedAt,
        SimTime? attemptedAt,
        bool? wasStarvationPromoted,
        DecisionNeedId? needId,
        Rq1SessionId? sessionId,
        FormalTerminalReceiptId? receiptId,
        FormalRq1TerminalOutcomeKind? terminalKind,
        string? terminalEvidenceHash,
        string? modelCallId = null,
        FormalTerminalOutcomeReceipt? terminalReceipt = null,
        string? gameActionId = null)
    {
        _ = new Rq1ActivationStageEvidence(
            opportunityId,
            discoveredAt,
            needCreatedAt,
            admittedAt,
            attemptedAt,
            wasStarvationPromoted);
        bool hasNeed = needId is not null;
        bool hasSession = sessionId is not null;
        bool hasTerminal = terminalKind is not null;
        if (hasNeed != (needCreatedAt is not null)
            || hasSession != (admittedAt is not null)
            || hasTerminal != (attemptedAt is not null)
            || hasTerminal != (receiptId is not null))
        {
            throw new ArgumentException(
                "RQ1 Need/session/receipt identities must exactly follow observed activation stages.");
        }

        if (terminalKind is FormalRq1TerminalOutcomeKind.AuthorityCommitted
            or FormalRq1TerminalOutcomeKind.JustifiedDefer)
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

        if (modelCallId is not null)
            FormalExperimentCanonical.RequireIdentity(modelCallId, nameof(modelCallId));
        if (!hasTerminal && modelCallId is not null)
            throw new ArgumentException("Only a terminal attempt can bind a model call.", nameof(modelCallId));
        if (terminalReceipt is not null
            && (!hasTerminal
                || !StringComparer.Ordinal.Equals(terminalReceipt.ModelCallId, modelCallId)
                || !StringComparer.Ordinal.Equals(terminalReceipt.TerminalEvidenceHash, terminalEvidenceHash)))
            throw new ArgumentException("Formal terminal receipt does not bind the observed terminal.", nameof(terminalReceipt));
        string? observedGameActionId = terminalReceipt?.GameActionId ?? gameActionId;
        if (terminalReceipt?.GameActionId is not null
            && gameActionId is not null
            && !StringComparer.Ordinal.Equals(terminalReceipt.GameActionId, gameActionId))
            throw new ArgumentException("Observed GameActionId does not match its terminal receipt.", nameof(gameActionId));
        if (terminalKind == FormalRq1TerminalOutcomeKind.AuthorityCommitted)
            FormalExperimentCanonical.RequireIdentity(
                observedGameActionId ?? throw new ArgumentNullException(nameof(gameActionId)),
                nameof(gameActionId));
        else if (observedGameActionId is not null)
            throw new ArgumentException("Only an Authority commit can carry a GameActionId.", nameof(gameActionId));

        OpportunityId = opportunityId;
        DiscoveredAt = discoveredAt;
        NeedCreatedAt = needCreatedAt;
        AdmittedAt = admittedAt;
        AttemptedAt = attemptedAt;
        WasStarvationPromoted = wasStarvationPromoted;
        NeedId = needId;
        SessionId = sessionId;
        ReceiptId = receiptId;
        TerminalKind = terminalKind;
        TerminalEvidenceHash = terminalEvidenceHash;
        GameActionId = observedGameActionId;
        ModelCallId = modelCallId;
        TerminalReceipt = terminalReceipt;
    }

    public Rq1OpportunityId OpportunityId { get; }
    public SimTime? DiscoveredAt { get; }
    public SimTime? NeedCreatedAt { get; }
    public SimTime? AdmittedAt { get; }
    public SimTime? AttemptedAt { get; }
    public bool? WasStarvationPromoted { get; }
    public DecisionNeedId? NeedId { get; }
    public Rq1SessionId? SessionId { get; }
    public FormalTerminalReceiptId? ReceiptId { get; }
    public FormalRq1TerminalOutcomeKind? TerminalKind { get; }
    public string? TerminalEvidenceHash { get; }
    public string? GameActionId { get; }
    public string? ModelCallId { get; }
    public FormalTerminalOutcomeReceipt? TerminalReceipt { get; }
}

public sealed class FormalRq1EvaluatedConditionEvidence
{
    private readonly ReadOnlyCollection<Rq1ActivationStageEvidence> _activationEvidence;
    private readonly ReadOnlyCollection<Rq1TestCaseOutcome> _testCaseOutcomes;

    internal FormalRq1EvaluatedConditionEvidence(
        IEnumerable<Rq1ActivationStageEvidence> activationEvidence,
        IEnumerable<Rq1TestCaseOutcome> testCaseOutcomes)
    {
        _activationEvidence = Array.AsReadOnly(activationEvidence.ToArray());
        _testCaseOutcomes = Array.AsReadOnly(testCaseOutcomes.ToArray());
    }

    public IReadOnlyList<Rq1ActivationStageEvidence> ActivationEvidence => _activationEvidence;
    public IReadOnlyList<Rq1TestCaseOutcome> TestCaseOutcomes => _testCaseOutcomes;
}

/// <summary>Offline evaluator; hidden TestCases and expected outcomes never enter a treatment executor.</summary>
public static class FormalRq1OutcomeEvaluator
{
    public static void ValidateFixture(
        ActorOpportunityLedger opportunityLedger,
        Rq1TestCaseLedger testCaseLedger,
        FormalRq1OpportunityTestCaseMap mapping,
        IEnumerable<FormalRq1HiddenTestCase> hiddenTestCases)
    {
        ArgumentNullException.ThrowIfNull(opportunityLedger);
        ArgumentNullException.ThrowIfNull(testCaseLedger);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(hiddenTestCases);
        if (!StringComparer.Ordinal.Equals(mapping.OpportunityLedgerId, opportunityLedger.LedgerId)
            || !StringComparer.Ordinal.Equals(mapping.TestCaseLedgerId, testCaseLedger.LedgerId))
        {
            throw new ArgumentException(
                "RQ1 opportunity mapping does not bind the supplied ledgers.",
                nameof(mapping));
        }

        ValidateHiddenCases(testCaseLedger, hiddenTestCases);
    }

    public static FormalRq1EvaluatedConditionEvidence Evaluate(
        ActorOpportunityLedger opportunityLedger,
        Rq1TestCaseLedger testCaseLedger,
        FormalRq1OpportunityTestCaseMap mapping,
        IEnumerable<FormalRq1HiddenTestCase> hiddenTestCases,
        IEnumerable<FormalRq1OpportunityRunEvidence> runEvidence)
    {
        ArgumentNullException.ThrowIfNull(opportunityLedger);
        ArgumentNullException.ThrowIfNull(testCaseLedger);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(hiddenTestCases);
        ArgumentNullException.ThrowIfNull(runEvidence);
        ValidateFixture(opportunityLedger, testCaseLedger, mapping, hiddenTestCases);
        FormalRq1HiddenTestCase[] hidden = hiddenTestCases.ToArray();
        FormalRq1OpportunityRunEvidence[] observed = runEvidence.ToArray();
        ValidateHiddenCases(testCaseLedger, hidden);
        ValidateObserved(opportunityLedger, observed);
        var observedByOpportunity = observed.ToDictionary(GetObservedOpportunityId);
        var ledgerByOpportunity = opportunityLedger.Entries.ToDictionary(GetLedgerOpportunityId);
        var mapByTestCase = new Dictionary<Rq1TestCaseId, List<Rq1OpportunityId>>();
        foreach (FormalRq1OpportunityTestCaseMapEntry entry in mapping.Entries)
        {
            if (!mapByTestCase.TryGetValue(entry.TestCaseId, out List<Rq1OpportunityId>? opportunities))
            {
                opportunities = [];
                mapByTestCase.Add(entry.TestCaseId, opportunities);
            }

            opportunities.Add(entry.OpportunityId);
        }

        var activation = new List<Rq1ActivationStageEvidence>(observed.Length);
        foreach (FormalRq1OpportunityRunEvidence evidence in observed)
        {
            activation.Add(new Rq1ActivationStageEvidence(
                evidence.OpportunityId,
                evidence.DiscoveredAt,
                evidence.NeedCreatedAt,
                evidence.AdmittedAt,
                IsValidAttempt(evidence.TerminalKind) ? evidence.AttemptedAt : null,
                evidence.WasStarvationPromoted));
        }

        var outcomes = new List<Rq1TestCaseOutcome>(hidden.Length);
        foreach (FormalRq1HiddenTestCase testCase in hidden.OrderBy(
                     GetHiddenTestCaseId,
                     StringComparer.Ordinal))
        {
            bool taskSucceeded = false;
            bool validAttempt = false;
            if (mapByTestCase.TryGetValue(testCase.TestCaseId, out List<Rq1OpportunityId>? opportunities))
            {
                foreach (Rq1OpportunityId opportunityId in opportunities)
                {
                    ActorOpportunityLedgerEntry ledgerEntry = ledgerByOpportunity[opportunityId];
                    FormalRq1OpportunityRunEvidence evidence = observedByOpportunity[opportunityId];
                    bool beforeClose = evidence.AttemptedAt is SimTime attempted
                        && attempted.Ticks <= ledgerEntry.ClosesAt.Ticks;
                    validAttempt |= beforeClose && IsValidAttempt(evidence.TerminalKind);
                    taskSucceeded |= beforeClose
                        && IsExpectedTerminal(testCase, evidence);
                }
            }

            outcomes.Add(new Rq1TestCaseOutcome(
                testCase.TestCaseId,
                taskSucceeded,
                validAttempt));
        }

        return new FormalRq1EvaluatedConditionEvidence(activation, outcomes);
    }

    private static bool IsExpectedTerminal(
        FormalRq1HiddenTestCase testCase,
        FormalRq1OpportunityRunEvidence evidence)
    {
        return IsGroundedTerminal(evidence.TerminalKind)
            && evidence.TerminalKind == testCase.ExpectedTerminalKind
            && StringComparer.Ordinal.Equals(evidence.GameActionId, testCase.ExpectedGameActionId)
            && MatchesAuthorityActionFamily(testCase, evidence);
    }

    private static bool MatchesAuthorityActionFamily(
        FormalRq1HiddenTestCase testCase,
        FormalRq1OpportunityRunEvidence evidence)
    {
        if (testCase.ExpectedAuthorityActionFamily is null) return true;
        if (evidence.TerminalReceipt is null) return false;
        using JsonDocument document = JsonDocument.Parse(evidence.TerminalReceipt.GetSourceReceiptBytes());
        JsonElement root = document.RootElement;
        return root.TryGetProperty("authority_action_family", out JsonElement family)
            && family.ValueKind == JsonValueKind.String
            && StringComparer.Ordinal.Equals(family.GetString(), testCase.ExpectedAuthorityActionFamily);
    }

    private static void ValidateHiddenCases(
        Rq1TestCaseLedger ledger,
        IEnumerable<FormalRq1HiddenTestCase> hiddenCases)
    {
        var hiddenById = new Dictionary<Rq1TestCaseId, FormalRq1HiddenTestCase>();
        foreach (FormalRq1HiddenTestCase? hidden in hiddenCases)
        {
            if (hidden is null || !hiddenById.TryAdd(hidden.TestCaseId, hidden))
            {
                throw new ArgumentException("Hidden RQ1 TestCases must be non-null and unique.", nameof(hiddenCases));
            }
        }

        if (!new HashSet<Rq1TestCaseId>(ledger.Entries.Select(GetLedgerTestCaseId)).SetEquals(hiddenById.Keys))
        {
            throw new ArgumentException("Hidden TestCases must cover the exact frozen ledger.", nameof(hiddenCases));
        }

    }

    private static void ValidateObserved(
        ActorOpportunityLedger ledger,
        IEnumerable<FormalRq1OpportunityRunEvidence> runEvidence)
    {
        var ids = new HashSet<Rq1OpportunityId>();
        foreach (FormalRq1OpportunityRunEvidence? evidence in runEvidence)
        {
            if (evidence is null || !ids.Add(evidence.OpportunityId))
            {
                throw new ArgumentException("RQ1 run evidence must be non-null and unique.", nameof(runEvidence));
            }
        }

        if (!new HashSet<Rq1OpportunityId>(ledger.Entries.Select(GetLedgerOpportunityId)).SetEquals(ids))
        {
            throw new ArgumentException("RQ1 run evidence must cover the exact opportunity ledger.", nameof(runEvidence));
        }
    }

    private static bool IsValidAttempt(FormalRq1TerminalOutcomeKind? kind)
    {
        return kind is FormalRq1TerminalOutcomeKind.AuthorityCommitted
            or FormalRq1TerminalOutcomeKind.JustifiedDefer;
    }

    private static bool IsGroundedTerminal(FormalRq1TerminalOutcomeKind? kind)
    {
        return IsValidAttempt(kind);
    }

    private static Rq1OpportunityId GetObservedOpportunityId(FormalRq1OpportunityRunEvidence evidence)
    {
        return evidence.OpportunityId;
    }

    private static Rq1OpportunityId GetLedgerOpportunityId(ActorOpportunityLedgerEntry entry)
    {
        return entry.OpportunityId;
    }

    private static Rq1TestCaseId GetLedgerTestCaseId(Rq1TestCaseLedgerEntry entry)
    {
        return entry.TestCaseId;
    }

    private static string GetHiddenTestCaseId(FormalRq1HiddenTestCase testCase)
    {
        return testCase.TestCaseId.Value;
    }
}

public sealed class FormalRq1ConditionExecutionInput
{
    private readonly byte[] _canonicalPublicFixtureBytes;

    public FormalRq1ConditionExecutionInput(
        FormalRq1ConditionManifest manifest,
        byte[] canonicalPublicFixtureBytes)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(canonicalPublicFixtureBytes);
        if (canonicalPublicFixtureBytes.Length == 0)
        {
            throw new ArgumentException("A public RQ1 fixture snapshot is required.", nameof(canonicalPublicFixtureBytes));
        }

        Manifest = manifest;
        _canonicalPublicFixtureBytes = canonicalPublicFixtureBytes.ToArray();
    }

    public FormalRq1ConditionManifest Manifest { get; }
    public FormalRq1Treatment Treatment => Manifest.Treatment;

    public byte[] GetCanonicalPublicFixtureBytes()
    {
        return _canonicalPublicFixtureBytes.ToArray();
    }
}

public sealed record FormalRq1RuntimeDiagnostics
{
    public FormalRq1RuntimeDiagnostics(
        int logicalSessionBudget,
        int reservedSessionBudget,
        int consumedSessionBudget,
        int remainingSessionBudget,
        int totalTransportAttempts,
        long pressureIndexLookupCount,
        long pressureEvaluationCount,
        long pressureStateChangeCount)
    {
        if (logicalSessionBudget < 0
            || reservedSessionBudget < 0
            || consumedSessionBudget < 0
            || remainingSessionBudget < 0
            || totalTransportAttempts < 0
            || pressureIndexLookupCount < 0
            || pressureEvaluationCount < 0
            || pressureStateChangeCount < 0
            || logicalSessionBudget != reservedSessionBudget + consumedSessionBudget + remainingSessionBudget)
            throw new ArgumentOutOfRangeException(nameof(logicalSessionBudget));
        LogicalSessionBudget = logicalSessionBudget;
        ReservedSessionBudget = reservedSessionBudget;
        ConsumedSessionBudget = consumedSessionBudget;
        RemainingSessionBudget = remainingSessionBudget;
        TotalTransportAttempts = totalTransportAttempts;
        PressureIndexLookupCount = pressureIndexLookupCount;
        PressureEvaluationCount = pressureEvaluationCount;
        PressureStateChangeCount = pressureStateChangeCount;
    }

    public static FormalRq1RuntimeDiagnostics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);

    public int LogicalSessionBudget { get; }
    public int ReservedSessionBudget { get; }
    public int ConsumedSessionBudget { get; }
    public int RemainingSessionBudget { get; }
    public int TotalTransportAttempts { get; }
    public long PressureIndexLookupCount { get; }
    public long PressureEvaluationCount { get; }
    public long PressureStateChangeCount { get; }
}

public sealed class FormalRq1ConditionExecutionResult
{
    private readonly ReadOnlyCollection<FormalRq1OpportunityRunEvidence> _opportunityEvidence;
    private readonly ReadOnlyCollection<Rq1SessionOutcome> _sessionOutcomes;
    private readonly ReadOnlyCollection<FormalModelCallEvidence> _modelCalls;

    public FormalRq1ConditionExecutionResult(
        FormalRq1Treatment treatment,
        IEnumerable<FormalRq1OpportunityRunEvidence> opportunityEvidence,
        IEnumerable<Rq1SessionOutcome> sessionOutcomes,
        IEnumerable<FormalModelCallEvidence> modelCalls,
        FormalRq1RuntimeDiagnostics? runtimeDiagnostics = null)
    {
        if (!Enum.IsDefined(treatment)) throw new ArgumentOutOfRangeException(nameof(treatment));
        ArgumentNullException.ThrowIfNull(opportunityEvidence);
        ArgumentNullException.ThrowIfNull(sessionOutcomes);
        ArgumentNullException.ThrowIfNull(modelCalls);
        Treatment = treatment;
        _opportunityEvidence = Array.AsReadOnly(opportunityEvidence.ToArray());
        _sessionOutcomes = Array.AsReadOnly(sessionOutcomes.ToArray());
        _modelCalls = Array.AsReadOnly(modelCalls.ToArray());
        RuntimeDiagnostics = runtimeDiagnostics ?? FormalRq1RuntimeDiagnostics.Empty;
    }

    public FormalRq1Treatment Treatment { get; }
    public IReadOnlyList<FormalRq1OpportunityRunEvidence> OpportunityEvidence => _opportunityEvidence;
    public IReadOnlyList<Rq1SessionOutcome> SessionOutcomes => _sessionOutcomes;
    public IReadOnlyList<FormalModelCallEvidence> ModelCalls => _modelCalls;
    public FormalRq1RuntimeDiagnostics RuntimeDiagnostics { get; }
}

public interface IFormalRq1ConditionExecutor
{
    string RuntimeInstanceId { get; }
    string ProviderSessionId { get; }
    ValueTask<FormalRq1ConditionExecutionResult> ExecuteAsync(
        FormalRq1ConditionExecutionInput input,
        CancellationToken cancellationToken);
}

public interface IFormalRq1ConditionExecutorFactory
{
    IFormalRq1ConditionExecutor Create(FormalRq1Treatment treatment);
}

public enum FormalRq1MatchedRunKind
{
    PreflightBlocked,
    PairEvidenceInvalid,
    Completed
}

public sealed record FormalRq1MatchedRunResult(
    FormalRq1MatchedRunKind Kind,
    FormalExperimentPreflightReport Preflight,
    FormalRq1MatchedPairScore? Score,
    FormalExperimentEvidenceSeal EvidenceSeal);

/// <summary>Sequential matched runner over isolated condition executors; hidden truth is evaluated only afterwards.</summary>
public sealed class FormalRq1MatchedRunner
{
    public async ValueTask<FormalRq1MatchedRunResult> RunAsync(
        FormalRq1MatchedPairManifest manifest,
        byte[] canonicalPublicFixtureBytes,
        ActorOpportunityLedger opportunityLedger,
        Rq1TestCaseLedger testCaseLedger,
        FormalRq1OpportunityTestCaseMap mapping,
        IEnumerable<FormalRq1HiddenTestCase> hiddenTestCases,
        IReadOnlyList<FormalRq1Treatment> conditionOrder,
        FormalRq1RunPurpose runPurpose,
        FormalCollectionAuthorization authorization,
        IEnumerable<string> unresolvedInputIds,
        IEnumerable<FormalEvidenceArtifactBinding> requiredArtifacts,
        IFormalRq1ConditionExecutorFactory executorFactory,
        IFormalExperimentRecorder recorder,
        CancellationToken cancellationToken,
        FormalExperimentCollectionPermit? collectionPermit = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(canonicalPublicFixtureBytes);
        ArgumentNullException.ThrowIfNull(opportunityLedger);
        ArgumentNullException.ThrowIfNull(testCaseLedger);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(hiddenTestCases);
        ArgumentNullException.ThrowIfNull(conditionOrder);
        ArgumentNullException.ThrowIfNull(executorFactory);
        ArgumentNullException.ThrowIfNull(recorder);
        if (!Enum.IsDefined(runPurpose)) throw new ArgumentOutOfRangeException(nameof(runPurpose));
        ValidateOrder(conditionOrder);
        bool formalCollection = runPurpose == FormalRq1RunPurpose.FormalCollection;
        string[] unresolved = unresolvedInputIds.ToArray();
        FormalEvidenceArtifactBinding[] artifacts = requiredArtifacts.ToArray();
        FormalExperimentPreflightReport preflight = FormalExperimentPreflight.Evaluate(
            FormalExperimentRq.Rq1,
            formalCollection,
            manifest.AgentCentric.PreregistrationArtifactVersion,
            manifest.PairManifestHash,
            manifest.AgentCentric.RuntimeVersion,
            manifest.AgentCentric.ModelProfileId,
            authorization,
            unresolved,
            artifacts,
            collectionPermit);
        if (formalCollection)
            preflight = AddBlocker(preflight, "formal_two_stage_runner_required");
        if (!StringComparer.Ordinal.Equals(
                opportunityLedger.LedgerId,
                manifest.AgentCentric.OpportunityLedgerId))
        {
            preflight = AddBlocker(preflight, "rq1_hidden_ledger_manifest_mismatch");
        }

        FormalRq1HiddenTestCase[] hidden = hiddenTestCases.ToArray();
        try
        {
            FormalRq1OutcomeEvaluator.ValidateFixture(
                opportunityLedger,
                testCaseLedger,
                mapping,
                hidden);
        }
        catch (ArgumentException)
        {
            preflight = AddBlocker(preflight, "rq1_hidden_fixture_invalid");
        }

        recorder.Append("collection_authorization", authorization.GetCanonicalBytes());
        if (collectionPermit is not null)
            recorder.Append("collection_permit", collectionPermit.GetCanonicalBytes());
        recorder.Append("preflight_inputs", FormalExperimentEvidencePayloads.SerializePreflightInputs(
            FormalExperimentRq.Rq1,
            formalCollection,
            manifest.AgentCentric.PreregistrationArtifactVersion,
            manifest.PairManifestHash,
            manifest.AgentCentric.RuntimeVersion,
            manifest.AgentCentric.ModelProfileId,
            unresolved,
            artifacts));
        recorder.Append("preflight", preflight.GetCanonicalBytes());
        if (!preflight.IsReady)
        {
            return new FormalRq1MatchedRunResult(
                FormalRq1MatchedRunKind.PreflightBlocked,
                preflight,
                null,
                recorder.Seal());
        }

        recorder.Append("rq1_pair_manifest", manifest.GetCanonicalBytes());
        recorder.Append("rq1_agent_centric_manifest", manifest.AgentCentric.GetCanonicalBytes());
        recorder.Append("rq1_event_centric_manifest", manifest.EventCentric.GetCanonicalBytes());
        recorder.Append("rq1_public_fixture", FormalExperimentEvidencePayloads.SerializeUnhashedBlob(
            "alice.formal-rq1-public-fixture-blob.v1",
            canonicalPublicFixtureBytes));
        var results = new Dictionary<FormalRq1Treatment, FormalRq1ConditionExecutionResult>();
        var runtimeIds = new HashSet<string>(StringComparer.Ordinal);
        var providerSessionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (FormalRq1Treatment treatment in conditionOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IFormalRq1ConditionExecutor executor = executorFactory.Create(treatment)
                ?? throw new InvalidOperationException("RQ1 executor factory returned null.");
            FormalExperimentCanonical.RequireIdentity(executor.RuntimeInstanceId, nameof(executor.RuntimeInstanceId));
            FormalExperimentCanonical.RequireIdentity(executor.ProviderSessionId, nameof(executor.ProviderSessionId));
            if (!runtimeIds.Add(executor.RuntimeInstanceId)
                || !providerSessionIds.Add(executor.ProviderSessionId))
            {
                throw new InvalidOperationException("Matched RQ1 conditions must use fresh runtime and Provider sessions.");
            }

            FormalRq1ConditionManifest conditionManifest = treatment == FormalRq1Treatment.AgentCentric
                ? manifest.AgentCentric
                : manifest.EventCentric;
            var input = new FormalRq1ConditionExecutionInput(
                conditionManifest,
                canonicalPublicFixtureBytes);
            FormalRq1ConditionExecutionResult result = await executor.ExecuteAsync(
                input,
                cancellationToken).ConfigureAwait(false);
            if (result.Treatment != treatment)
            {
                throw new InvalidOperationException("RQ1 condition executor returned the wrong treatment identity.");
            }

            ValidateConditionResult(result, formalCollection, opportunityLedger);

            results.Add(treatment, result);
            recorder.Append(TreatmentRecordKind(treatment), SerializeConditionResult(result));
        }

        FormalRq1ConditionExecutionResult agent = results[FormalRq1Treatment.AgentCentric];
        FormalRq1ConditionExecutionResult eventCentric = results[FormalRq1Treatment.EventCentric];
        recorder.Append("rq1_opportunity_ledger", opportunityLedger.GetCanonicalBytes());
        recorder.Append("rq1_test_case_ledger", testCaseLedger.GetCanonicalBytes());
        recorder.Append("rq1_opportunity_test_case_map", mapping.GetCanonicalBytes());
        recorder.Append("rq1_hidden_test_cases", SerializeHiddenTestCases(hidden));
        if (!HasConsistentFormalModelEvidence(
                manifest,
                agent,
                eventCentric,
                formalCollection))
        {
            recorder.Append("pair_evidence_invalid", SerializePairEvidenceInvalid());
            return new FormalRq1MatchedRunResult(
                FormalRq1MatchedRunKind.PairEvidenceInvalid,
                preflight,
                null,
                recorder.Seal());
        }

        FormalRq1EvaluatedConditionEvidence agentEvaluated = FormalRq1OutcomeEvaluator.Evaluate(
            opportunityLedger,
            testCaseLedger,
            mapping,
            hidden,
            agent.OpportunityEvidence);
        FormalRq1EvaluatedConditionEvidence eventEvaluated = FormalRq1OutcomeEvaluator.Evaluate(
            opportunityLedger,
            testCaseLedger,
            mapping,
            hidden,
            eventCentric.OpportunityEvidence);
        FormalRq1MatchedPairScore score = FormalRq1OfflineScorer.ScoreMatchedPair(
            manifest,
            opportunityLedger,
            testCaseLedger,
            agentEvaluated.ActivationEvidence,
            agentEvaluated.TestCaseOutcomes,
            agent.SessionOutcomes,
            eventEvaluated.ActivationEvidence,
            eventEvaluated.TestCaseOutcomes,
            eventCentric.SessionOutcomes);
        recorder.Append("matched_score", SerializeScore(score));
        return new FormalRq1MatchedRunResult(
            FormalRq1MatchedRunKind.Completed,
            preflight,
            score,
            recorder.Seal());
    }

    internal static void ValidateOrder(IReadOnlyList<FormalRq1Treatment> order)
    {
        if (order.Count != 2
            || !order.Contains(FormalRq1Treatment.AgentCentric)
            || !order.Contains(FormalRq1Treatment.EventCentric)
            || order[0] == order[1])
        {
            throw new ArgumentException("RQ1 order must contain both conditions exactly once.", nameof(order));
        }
    }

    internal static FormalExperimentPreflightReport AddBlocker(
        FormalExperimentPreflightReport report,
        string blocker)
    {
        return new FormalExperimentPreflightReport(report.Blockers.Append(blocker));
    }

    internal static void ValidateConditionResult(
        FormalRq1ConditionExecutionResult result,
        bool formalCollection,
        ActorOpportunityLedger opportunityLedger)
    {
        var sessionIds = new HashSet<Rq1SessionId>();
        foreach (Rq1SessionOutcome session in result.SessionOutcomes)
        {
            if (!sessionIds.Add(session.SessionId))
            {
                throw new ArgumentException("RQ1 session outcomes must be unique.", nameof(result));
            }
        }

        foreach (FormalRq1OpportunityRunEvidence evidence in result.OpportunityEvidence)
        {
            if (evidence.SessionId is Rq1SessionId sessionId && !sessionIds.Contains(sessionId))
            {
                throw new ArgumentException(
                    "Every mapped RQ1 session must have one session outcome.",
                    nameof(result));
            }
            if (formalCollection && evidence.TerminalKind is not null)
            {
                FormalModelCallEvidence? call = result.ModelCalls.SingleOrDefault(
                    value => StringComparer.Ordinal.Equals(value.CallId, evidence.ModelCallId));
                if (evidence.ModelCallId is null
                    || call is null
                    || evidence.NeedId is null
                    || !StringComparer.Ordinal.Equals(call.NeedId, evidence.NeedId.Value)
                    || evidence.TerminalReceipt is null
                    || !FormalTerminalMatches(
                        evidence,
                        opportunityLedger,
                        evidence.TerminalReceipt))
                    throw new ArgumentException(
                        "Every formal RQ1 terminal must bind its exact Need and live model call.",
                        nameof(result));
            }
        }

        var callIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (FormalModelCallEvidence call in result.ModelCalls)
        {
            if (!callIds.Add(call.CallId))
            {
                throw new ArgumentException("RQ1 model-call identities must be unique.", nameof(result));
            }
        }
    }

    internal static string TreatmentRecordKind(FormalRq1Treatment treatment)
    {
        return treatment == FormalRq1Treatment.AgentCentric
            ? "rq1_agent_centric_result"
            : "rq1_event_centric_result";
    }

    internal static byte[] SerializeConditionResult(FormalRq1ConditionExecutionResult result)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-rq1-condition-result.v1");
            writer.WriteString("treatment", TreatmentRecordKind(result.Treatment));
            writer.WritePropertyName("opportunity_evidence");
            writer.WriteStartArray();
            foreach (FormalRq1OpportunityRunEvidence evidence in result.OpportunityEvidence.OrderBy(
                         GetOpportunityEvidenceId,
                         StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("opportunity_id", evidence.OpportunityId.Value);
                WriteSimTime(writer, "discovered_at_ticks", evidence.DiscoveredAt);
                WriteSimTime(writer, "need_created_at_ticks", evidence.NeedCreatedAt);
                WriteSimTime(writer, "admitted_at_ticks", evidence.AdmittedAt);
                WriteSimTime(writer, "attempted_at_ticks", evidence.AttemptedAt);
                if (evidence.WasStarvationPromoted is bool promoted)
                {
                    writer.WriteBoolean("was_starvation_promoted", promoted);
                }
                else
                {
                    writer.WriteNull("was_starvation_promoted");
                }

                writer.WriteString("need_id", evidence.NeedId?.Value);
                writer.WriteString("session_id", evidence.SessionId?.Value);
                writer.WriteString("receipt_id", evidence.ReceiptId?.Value);
                writer.WriteString("terminal_kind", TerminalKindToken(evidence.TerminalKind));
                writer.WriteString("terminal_evidence_hash", evidence.TerminalEvidenceHash);
                writer.WriteString("game_action_id", evidence.GameActionId);
                writer.WriteString("model_call_id", evidence.ModelCallId);
                writer.WriteString("terminal_receipt_hash", evidence.TerminalReceipt?.ReceiptHash);
                if (evidence.TerminalReceipt is null)
                    writer.WriteNull("terminal_receipt");
                else writer.WriteBase64String(
                    "terminal_receipt",
                    evidence.TerminalReceipt.GetCanonicalBytes());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("session_outcomes");
            writer.WriteStartArray();
            foreach (Rq1SessionOutcome session in result.SessionOutcomes.OrderBy(
                         GetSessionId,
                         StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("session_id", session.SessionId.Value);
                writer.WriteString("productivity", session.Productivity.ToString());
                writer.WriteNumber("measured_tokens", session.MeasuredTokens);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("model_calls");
            writer.WriteStartArray();
            foreach (FormalModelCallEvidence call in result.ModelCalls)
            {
                writer.WriteStartObject();
                writer.WriteString("evidence_hash", call.EvidenceHash);
                writer.WriteBase64String("canonical_evidence", call.GetCanonicalBytes());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("runtime_diagnostics");
            writer.WriteStartObject();
            writer.WriteNumber("logical_session_budget", result.RuntimeDiagnostics.LogicalSessionBudget);
            writer.WriteNumber("reserved_session_budget", result.RuntimeDiagnostics.ReservedSessionBudget);
            writer.WriteNumber("consumed_session_budget", result.RuntimeDiagnostics.ConsumedSessionBudget);
            writer.WriteNumber("remaining_session_budget", result.RuntimeDiagnostics.RemainingSessionBudget);
            writer.WriteNumber("total_transport_attempts", result.RuntimeDiagnostics.TotalTransportAttempts);
            writer.WriteNumber("pressure_index_lookup_count", result.RuntimeDiagnostics.PressureIndexLookupCount);
            writer.WriteNumber("pressure_evaluation_count", result.RuntimeDiagnostics.PressureEvaluationCount);
            writer.WriteNumber("pressure_state_change_count", result.RuntimeDiagnostics.PressureStateChangeCount);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    internal static bool HasConsistentFormalModelEvidence(
        FormalRq1MatchedPairManifest manifest,
        FormalRq1ConditionExecutionResult agent,
        FormalRq1ConditionExecutionResult eventCentric,
        bool formalCollection)
    {
        FormalModelCallEvidence[] calls =
        [
            .. agent.ModelCalls,
            .. eventCentric.ModelCalls
        ];
        if (calls.Length == 0) return !formalCollection;

        if (formalCollection
            && (calls.Any(IsFormalCallIncomplete)
                || calls.Any(call => !StringComparer.Ordinal.Equals(
                    call.ProviderProfileId,
                    manifest.AgentCentric.ModelProfileId))
                || calls.Any(call => !MatchesRequestProtocol(
                    call,
                    manifest.AgentCentric.RequestProtocols))))
        {
            return false;
        }

        string? modelId = calls[0].ModelId;
        string? providerProtocol = calls[0].ProviderProtocolVersion;
        string? providerProfile = calls[0].ProviderProfileId;
        return !formalCollection || (modelId is not null
                && providerProtocol is not null
                && providerProfile is not null
                && calls.All(call => StringComparer.Ordinal.Equals(call.ModelId, modelId))
                && calls.All(call => StringComparer.Ordinal.Equals(
                    call.ProviderProtocolVersion,
                    providerProtocol))
                && calls.All(call => StringComparer.Ordinal.Equals(
                    call.ProviderProfileId,
                    providerProfile)));
    }

    private static bool MatchesRequestProtocol(
        FormalModelCallEvidence call,
        IEnumerable<FormalRq1RequestProtocolManifestEntry> protocols) =>
        protocols.Any(value => StringComparer.Ordinal.Equals(
                value.ProtocolVersion,
                call.RequestProtocolVersion));

    private static bool FormalTerminalMatches(
        FormalRq1OpportunityRunEvidence evidence,
        ActorOpportunityLedger ledger,
        FormalTerminalOutcomeReceipt receipt)
    {
        ActorOpportunityLedgerEntry entry = ledger.Entries.Single(
            value => value.OpportunityId == evidence.OpportunityId);
        FormalTerminalOutcomeReceiptKind expected = evidence.TerminalKind switch
        {
            FormalRq1TerminalOutcomeKind.AuthorityCommitted => FormalTerminalOutcomeReceiptKind.AuthorityCommit,
            FormalRq1TerminalOutcomeKind.JustifiedDefer => FormalTerminalOutcomeReceiptKind.ValidatedDefer,
            FormalRq1TerminalOutcomeKind.TransportFailure => FormalTerminalOutcomeReceiptKind.TransportFailure,
            FormalRq1TerminalOutcomeKind.InvalidDecision or FormalRq1TerminalOutcomeKind.ValidatorRejected =>
                FormalTerminalOutcomeReceiptKind.ValidatorRejection,
            _ => throw new InvalidOperationException("A formal terminal kind is required.")
        };
        return receipt.Kind == expected
            && StringComparer.Ordinal.Equals(receipt.ActorId, entry.ActorId.Value)
            && StringComparer.Ordinal.Equals(receipt.NeedId, evidence.NeedId?.Value)
            && StringComparer.Ordinal.Equals(receipt.ModelCallId, evidence.ModelCallId)
            && StringComparer.Ordinal.Equals(
                receipt.TerminalEvidenceHash,
                evidence.TerminalEvidenceHash);
    }

    private static bool IsFormalCallIncomplete(FormalModelCallEvidence call)
    {
        return !call.IsFormalPairingComplete;
    }

    internal static byte[] SerializePairEvidenceInvalid()
    {
        return EncodingUtf8("{\"schema_version\":\"alice.formal-rq1-pair-evidence-invalid.v1\"}");
    }

    internal static byte[] SerializeHiddenTestCases(
        IEnumerable<FormalRq1HiddenTestCase> hiddenTestCases)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-rq1-hidden-test-cases.v1");
            writer.WritePropertyName("test_cases");
            writer.WriteStartArray();
            foreach (FormalRq1HiddenTestCase value in hiddenTestCases.OrderBy(
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

    private static byte[] EncodingUtf8(string value)
    {
        return System.Text.Encoding.UTF8.GetBytes(value);
    }

    private static string GetOpportunityEvidenceId(FormalRq1OpportunityRunEvidence evidence)
    {
        return evidence.OpportunityId.Value;
    }

    private static string GetSessionId(Rq1SessionOutcome session)
    {
        return session.SessionId.Value;
    }

    private static void WriteSimTime(Utf8JsonWriter writer, string name, SimTime? value)
    {
        if (value is SimTime time)
        {
            writer.WriteNumber(name, time.Ticks);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static string? TerminalKindToken(FormalRq1TerminalOutcomeKind? kind)
    {
        return kind?.ToString();
    }

    internal static byte[] SerializeScore(FormalRq1MatchedPairScore score)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-rq1-matched-score.v1");
            writer.WriteString("opportunity_ledger_id", score.OpportunityLedgerId);
            writer.WriteString("test_case_ledger_id", score.TestCaseLedgerId);
            WriteConditionScore(writer, "agent_centric", score.AgentCentric);
            WriteConditionScore(writer, "event_centric", score.EventCentric);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteConditionScore(
        Utf8JsonWriter writer,
        string name,
        Rq1ConditionScore score)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        WriteRate(writer, "task_success_rate", score.TaskSuccessRate);
        WriteRate(writer, "timely_activation_recall", score.TimelyActivationRecall);
        WriteRate(writer, "unproductive_session_rate", score.UnproductiveSessionRate);
        writer.WritePropertyName("fairness_diagnostics");
        writer.WriteStartObject();
        WriteNullableInt64(writer, "actor_first_admission_delay_p95_ticks", score.FairnessDiagnostics.ActorFirstAdmissionDelayP95Ticks);
        WriteNullableInt64(writer, "actor_first_admission_delay_max_ticks", score.FairnessDiagnostics.ActorFirstAdmissionDelayMaxTicks);
        writer.WriteNumber("admitted_actor_count", score.FairnessDiagnostics.AdmittedActorCount);
        writer.WriteNumber("cohort_actor_count", score.FairnessDiagnostics.CohortActorCount);
        WriteRate(writer, "actor_never_admitted_rate", score.FairnessDiagnostics.ActorNeverAdmittedRate);
        writer.WriteNumber("never_discovered_actor_count", score.FairnessDiagnostics.NeverDiscoveredActorCount);
        writer.WriteNumber("discovered_but_never_admitted_actor_count", score.FairnessDiagnostics.DiscoveredButNeverAdmittedActorCount);
        WriteRate(writer, "starvation_promoted_actor_rate", score.FairnessDiagnostics.StarvationPromotedActorRate);
        WriteRate(writer, "admitted_after_promotion_actor_rate", score.FairnessDiagnostics.AdmittedAfterPromotionActorRate);
        writer.WritePropertyName("service_rows");
        writer.WriteStartArray();
        foreach (Rq1ActorOpportunityServiceRow row in score.FairnessDiagnostics.ServiceRows)
        {
            writer.WriteStartObject();
            writer.WriteString("actor_id", row.ActorId.Value);
            writer.WriteNumber("admission_count", row.AdmissionCount);
            writer.WriteNumber("served_by_close_count", row.ServedByCloseCount);
            writer.WriteNumber("opportunity_count", row.OpportunityCount);
            writer.WriteNumber("service_rate", row.ServiceRate);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteNumber("service_rate_macro_mean", score.FairnessDiagnostics.ServiceRateMacroMean);
        WriteNullableDecimal(writer, "service_rate_gini", score.FairnessDiagnostics.ServiceRateGini);
        WriteNullableDecimal(writer, "raw_admission_count_gini", score.FairnessDiagnostics.RawAdmissionCountGini);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteNullableInt64(Utf8JsonWriter writer, string name, long? value)
    {
        if (value is long number) writer.WriteNumber(name, number);
        else writer.WriteNull(name);
    }

    private static void WriteNullableDecimal(Utf8JsonWriter writer, string name, decimal? value)
    {
        if (value is decimal number) writer.WriteNumber(name, number);
        else writer.WriteNull(name);
    }

    private static void WriteRate(Utf8JsonWriter writer, string name, Rq1Rate rate)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteNumber("numerator", rate.Numerator);
        writer.WriteNumber("denominator", rate.Denominator);
        writer.WriteEndObject();
    }
}
