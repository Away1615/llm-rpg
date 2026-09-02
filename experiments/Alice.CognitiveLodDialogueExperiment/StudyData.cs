using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alice.CognitiveLodDialogueExperiment;

internal sealed record DialogueCaseDocument(
    [property: JsonRequired] string Protocol,
    [property: JsonRequired] DialogueCase[] Cases);

internal sealed record DialogueCase(
    [property: JsonRequired] string CaseId,
    [property: JsonRequired] string Group,
    [property: JsonRequired] string SpeakerId,
    [property: JsonRequired] string ResponderId,
    [property: JsonRequired] string SourceKind,
    [property: JsonRequired] string ActorVisibleText,
    L0CaseState? L0State,
    string? EscalationReason,
    string? StrategicEffect);

internal sealed record L0CaseState(
    [property: JsonRequired] double RoutineInviteAcceptance,
    [property: JsonRequired] double Familiarity,
    [property: JsonRequired] double Trust,
    [property: JsonRequired] double Affection,
    [property: JsonRequired] double Respect,
    [property: JsonRequired] double Fear,
    [property: JsonRequired] double Grievance);

internal sealed record ExpectedDocument(
    [property: JsonRequired] string Protocol,
    [property: JsonRequired] ExpectedCase[] Expected);

internal sealed record ExpectedCase(
    [property: JsonRequired] string CaseId,
    [property: JsonRequired] string Route,
    [property: JsonRequired] string TerminalKind,
    string? EscalationReason);

internal sealed class StudyInputs
{
    public const string CasesProtocol = "alice.dialogue_lod.cases.v1";
    public const string ExpectedProtocol = "alice.dialogue_lod.expected.v1";
    private static readonly string[] Groups = ["l0", "l1", "l2"];
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private StudyInputs(
        DialogueCaseDocument cases,
        ExpectedDocument expected,
        IReadOnlyDictionary<string, ExpectedCase> expectedById,
        string casesSha256,
        string expectedSha256)
    {
        Cases = cases;
        Expected = expected;
        ExpectedById = expectedById;
        CasesSha256 = casesSha256;
        ExpectedSha256 = expectedSha256;
    }

    public DialogueCaseDocument Cases { get; }
    public ExpectedDocument Expected { get; }
    public IReadOnlyDictionary<string, ExpectedCase> ExpectedById { get; }
    public string CasesSha256 { get; }
    public string ExpectedSha256 { get; }

    public static StudyInputs Load(string casesPath, string expectedPath)
    {
        byte[] casesBytes = File.ReadAllBytes(casesPath);
        byte[] expectedBytes = File.ReadAllBytes(expectedPath);
        DialogueCaseDocument cases = JsonSerializer.Deserialize<DialogueCaseDocument>(casesBytes, ReadOptions)
            ?? throw new InvalidDataException("Dialogue LOD case document is null.");
        ExpectedDocument expected = JsonSerializer.Deserialize<ExpectedDocument>(expectedBytes, ReadOptions)
            ?? throw new InvalidDataException("Dialogue LOD expected document is null.");
        var expectedById = new Dictionary<string, ExpectedCase>(StringComparer.Ordinal);
        foreach (ExpectedCase value in expected.Expected)
        {
            if (!expectedById.TryAdd(value.CaseId, value))
                throw new InvalidDataException($"Duplicate expected case ID {value.CaseId}.");
        }
        return new StudyInputs(
            cases,
            expected,
            expectedById,
            Convert.ToHexString(SHA256.HashData(casesBytes)).ToLowerInvariant(),
            Convert.ToHexString(SHA256.HashData(expectedBytes)).ToLowerInvariant());
    }

    public void Validate()
    {
        if (!StringComparer.Ordinal.Equals(Cases.Protocol, CasesProtocol))
            throw new InvalidDataException("Dialogue LOD case protocol is not supported.");
        if (!StringComparer.Ordinal.Equals(Expected.Protocol, ExpectedProtocol))
            throw new InvalidDataException("Dialogue LOD expected protocol is not supported.");
        if (Cases.Cases.Length != 36 || Expected.Expected.Length != 36)
            throw new InvalidDataException("Dialogue LOD suite requires exactly 36 cases and 36 expected rows.");

        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        var groupCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string group in Groups) groupCounts.Add(group, 0);
        foreach (DialogueCase studyCase in Cases.Cases)
        {
            ValidateIdentity(studyCase);
            if (!caseIds.Add(studyCase.CaseId))
                throw new InvalidDataException($"Duplicate dialogue case ID {studyCase.CaseId}.");
            if (!groupCounts.ContainsKey(studyCase.Group))
                throw new InvalidDataException($"Unknown dialogue group {studyCase.Group}.");
            groupCounts[studyCase.Group]++;
            if (!ExpectedById.TryGetValue(studyCase.CaseId, out ExpectedCase? expected))
                throw new InvalidDataException($"Expected row is missing for {studyCase.CaseId}.");
            ValidateTreatmentShape(studyCase, expected);
        }
        foreach (string group in Groups)
        {
            if (groupCounts[group] != 12)
                throw new InvalidDataException($"Dialogue group {group} requires exactly 12 cases.");
        }
        foreach (ExpectedCase expected in Expected.Expected)
        {
            if (!caseIds.Contains(expected.CaseId))
                throw new InvalidDataException($"Expected row {expected.CaseId} has no case.");
        }
    }

    private static void ValidateIdentity(DialogueCase studyCase)
    {
        if (string.IsNullOrWhiteSpace(studyCase.CaseId)
            || string.IsNullOrWhiteSpace(studyCase.SpeakerId)
            || string.IsNullOrWhiteSpace(studyCase.ResponderId)
            || string.IsNullOrWhiteSpace(studyCase.ActorVisibleText)
            || StringComparer.Ordinal.Equals(studyCase.SpeakerId, studyCase.ResponderId))
            throw new InvalidDataException("Dialogue case identity or actor-visible text is invalid.");
    }

    private static void ValidateTreatmentShape(DialogueCase studyCase, ExpectedCase expected)
    {
        string requiredRoute = studyCase.Group.ToUpperInvariant();
        if (!StringComparer.Ordinal.Equals(expected.Route, requiredRoute))
            throw new InvalidDataException($"Case {studyCase.CaseId} route does not match its group.");
        if (studyCase.Group == "l0")
        {
            if (studyCase.L0State is null || studyCase.SourceKind != "Invite"
                || studyCase.EscalationReason is not null || studyCase.StrategicEffect is not null
                || expected.TerminalKind is not ("Accept" or "Decline"))
                throw new InvalidDataException($"L0 case {studyCase.CaseId} has an invalid shape.");
            ValidateNormalized(studyCase.L0State);
            return;
        }
        if (studyCase.L0State is not null)
            throw new InvalidDataException($"Non-L0 case {studyCase.CaseId} contains L0 state.");
        if (studyCase.Group == "l1")
        {
            if (studyCase.EscalationReason is not null || studyCase.StrategicEffect is not null
                || expected.TerminalKind != "LocalReply")
                throw new InvalidDataException($"L1 case {studyCase.CaseId} has an invalid shape.");
            return;
        }
        if (string.IsNullOrWhiteSpace(studyCase.EscalationReason)
            || studyCase.StrategicEffect is not ("Promise" or "Breach" or "Threat" or "Harm")
            || !StringComparer.Ordinal.Equals(studyCase.EscalationReason, expected.EscalationReason)
            || expected.TerminalKind != "StrategicReply")
            throw new InvalidDataException($"L2 case {studyCase.CaseId} has an invalid shape.");
    }

    private static void ValidateNormalized(L0CaseState state)
    {
        double[] values =
        [
            state.RoutineInviteAcceptance, state.Familiarity, state.Trust, state.Affection,
            state.Respect, state.Fear, state.Grievance
        ];
        foreach (double value in values)
        {
            if (!double.IsFinite(value) || value < 0 || value > 1)
                throw new InvalidDataException("L0 score input must be between zero and one.");
        }
    }
}

internal sealed record DialogueLifecycleRecord(
    string Protocol,
    string RunId,
    string Source,
    string? CaseId,
    int? Repeat,
    long SimulationDay,
    long SimulationTicks,
    string OpportunityId,
    string SessionId,
    string SourceActId,
    string SpeakerId,
    string ResponderId,
    string SourceKind,
    string? ExpectedRoute,
    string ActualRoute,
    bool L0Eligible,
    string? L0Outcome,
    string? L0Reason,
    string? L1Decision,
    bool? L1OutputValid,
    bool EscalationRequested,
    string? EscalationReason,
    IReadOnlyList<string> ActorVisibleEvidenceRefs,
    bool? HostAccepted,
    string? DecisionNeedId,
    bool L2ProviderDispatched,
    string? L2Outcome,
    string TerminalKind,
    string? TerminalReference,
    string? Failure,
    IReadOnlyList<string> LocalAttemptIds,
    IReadOnlyList<string> RemoteAttemptIds,
    bool? RouteMatch,
    bool? TerminalAcceptable);

internal sealed record ProviderAttemptRecord(
    string Protocol,
    string AttemptId,
    string Role,
    string? ModelId,
    string Outcome,
    int? HttpStatus,
    long DurationMilliseconds,
    long? InputTokens,
    long? OutputTokens,
    long? ReasoningTokens,
    long? CacheReadInputTokens,
    long? CacheCreationInputTokens,
    long? TotalTokens,
    bool UsageKnown,
    string RequestSha256,
    string? ResponseSha256,
    string RequestBodyPath,
    string? ResponseBodyPath,
    string? Failure);

internal sealed record CoverageDocument(
    string Protocol,
    string RunId,
    string Source,
    int ExpectedRecords,
    int ActualRecords,
    int DuplicateKeys,
    int MissingRecords,
    int UnfinishedLifecycles,
    bool Complete);

internal sealed record RunManifestDocument(
    string Protocol,
    string RunId,
    string Source,
    string Mode,
    DateTimeOffset StartedAtUtc,
    bool LiveProvider,
    int NpcCount,
    int? ControlledRepeats,
    int? WorkloadDays,
    string CasesSha256,
    string ExpectedSha256,
    string WorldConfigurationSha256,
    string DialogueSurfaceSha256);

internal sealed record SummaryDocument(
    string Protocol,
    string RunId,
    string Source,
    string CasesSha256,
    string ExpectedSha256,
    SummaryWindow[] Windows,
    ProviderRoleSummary[] ProviderRoles,
    int ProviderAttempts,
    long? InputTokens,
    long? OutputTokens,
    long? ReasoningTokens,
    long? TotalTokens);

internal sealed record SummaryWindow(
    string Window,
    int Opportunities,
    int L0SettledInvites,
    int InviteOpportunities,
    double? L0InviteResolutionRate,
    int L1LocalSettlements,
    int L1Entries,
    double? L1LocalSettlementRate,
    int L1EscalationRequests,
    double? L1EscalationRequestRate,
    int HostAcceptedEscalations,
    double? HostEscalationAcceptanceRate,
    int L2Dispatches,
    double? L2DispatchRate,
    int RouteMatches,
    int RouteScored,
    double? RouteMatchRate,
    int TerminalSuccesses,
    int TerminalScored,
    double? TerminalSuccessRate);

internal sealed record ProviderRoleSummary(
    string Window,
    string Role,
    int Attempts,
    int FailedAttempts,
    int UsageKnownAttempts,
    long? InputTokens,
    long? OutputTokens,
    long? ReasoningTokens,
    long? CacheReadInputTokens,
    long? CacheCreationInputTokens,
    long? TotalTokens,
    double? MedianLatencyMilliseconds,
    double? CallsPerTenGameDays,
    double? CallsPerHundredOpportunities,
    double? TotalTokensPerTenGameDays,
    double? TotalTokensPerHundredOpportunities);

internal static class StudyJson
{
    public static JsonSerializerOptions WriteOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public static JsonSerializerOptions JsonLineOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
