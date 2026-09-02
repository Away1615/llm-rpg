using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alice.Activities;
using Alice.Actors;
using Alice.ModelRuntime;
using Alice.Social;

namespace Alice.ProductRuntime;

public enum PlayerUtteranceInterpretationKind
{
    ParsedAct,
    ParseClarificationRequired,
    UnrepresentableUtterance,
    InterpreterUnavailable
}

/// <summary>
/// Bounded Demo-only interpretation. It deliberately contains no world mutation, confidence score,
/// hidden entity reference or Authority assertion.
/// </summary>
public sealed record PlayerUtteranceInterpretation
{
    private PlayerUtteranceInterpretation(
        PlayerUtteranceInterpretationKind kind,
        SemanticDialogueActKind? actKind,
        DialogueTopicRef? topicRef,
        IReadOnlyList<string> unresolvedRefs,
        string? visibleReason)
    {
        Kind = kind;
        ActKind = actKind;
        TopicRef = topicRef;
        UnresolvedRefs = new ReadOnlyCollection<string>(unresolvedRefs.ToArray());
        VisibleReason = visibleReason;
    }

    public PlayerUtteranceInterpretationKind Kind { get; }
    public SemanticDialogueActKind? ActKind { get; }
    public DialogueTopicRef? TopicRef { get; }
    public IReadOnlyList<string> UnresolvedRefs { get; }
    public string? VisibleReason { get; }

    public static PlayerUtteranceInterpretation Parsed(
        SemanticDialogueActKind actKind,
        DialogueTopicRef? topicRef)
    {
        if (!Enum.IsDefined(actKind) || actKind == SemanticDialogueActKind.Invite)
            throw new ArgumentOutOfRangeException(nameof(actKind), "A free-text Invite requires the separate typed gathering binding flow.");
        return new PlayerUtteranceInterpretation(
            PlayerUtteranceInterpretationKind.ParsedAct,
            actKind,
            topicRef,
            [],
            null);
    }

    public static PlayerUtteranceInterpretation Clarification(
        IEnumerable<string> unresolvedRefs,
        string visibleReason)
    {
        ArgumentNullException.ThrowIfNull(unresolvedRefs);
        RequireVisibleText(visibleReason, nameof(visibleReason));
        string[] snapshot = unresolvedRefs.Select(value => RequireVisibleText(value, nameof(unresolvedRefs))).ToArray();
        return new PlayerUtteranceInterpretation(
            PlayerUtteranceInterpretationKind.ParseClarificationRequired,
            null,
            null,
            snapshot,
            visibleReason);
    }

    public static PlayerUtteranceInterpretation Unrepresentable(string visibleReason)
    {
        RequireVisibleText(visibleReason, nameof(visibleReason));
        return new PlayerUtteranceInterpretation(
            PlayerUtteranceInterpretationKind.UnrepresentableUtterance,
            null,
            null,
            [],
            visibleReason);
    }

    public static PlayerUtteranceInterpretation Unavailable(string visibleReason)
    {
        RequireVisibleText(visibleReason, nameof(visibleReason));
        return new PlayerUtteranceInterpretation(
            PlayerUtteranceInterpretationKind.InterpreterUnavailable,
            null,
            null,
            [],
            visibleReason);
    }

    private static string RequireVisibleText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Visible text must be non-empty.", name);
        return value;
    }
}

public sealed record PlayerUtteranceInterpretationRequest
{
    private readonly ReadOnlyCollection<string> _visibleRefs;

    public PlayerUtteranceInterpretationRequest(
        string requestId,
        ActorId playerActorId,
        ActorId npcActorId,
        string rawText,
        IEnumerable<string> visibleRefs,
        DialogueTopicRef defaultTopicRef)
    {
        if (string.IsNullOrWhiteSpace(requestId)) throw new ArgumentException("Request identity is required.", nameof(requestId));
        if (string.IsNullOrWhiteSpace(playerActorId.Value)) throw new ArgumentException("Player identity is required.", nameof(playerActorId));
        if (string.IsNullOrWhiteSpace(npcActorId.Value)) throw new ArgumentException("NPC identity is required.", nameof(npcActorId));
        if (playerActorId == npcActorId) throw new ArgumentException("Player and NPC must be distinct.", nameof(npcActorId));
        if (string.IsNullOrWhiteSpace(rawText)) throw new ArgumentException("Player utterance must be non-empty.", nameof(rawText));
        ArgumentNullException.ThrowIfNull(visibleRefs);
        string[] references = visibleRefs.ToArray();
        if (references.Any(string.IsNullOrWhiteSpace) || references.Distinct(StringComparer.Ordinal).Count() != references.Length)
            throw new ArgumentException("Actor-visible references must be non-empty and distinct.", nameof(visibleRefs));
        if (string.IsNullOrWhiteSpace(defaultTopicRef.Value)) throw new ArgumentException("Default topic is required.", nameof(defaultTopicRef));

        RequestId = requestId;
        PlayerActorId = playerActorId;
        NpcActorId = npcActorId;
        RawText = rawText;
        _visibleRefs = Array.AsReadOnly(references);
        DefaultTopicRef = defaultTopicRef;
    }

    public string RequestId { get; }
    public ActorId PlayerActorId { get; }
    public ActorId NpcActorId { get; }
    public string RawText { get; }
    public IReadOnlyList<string> VisibleRefs => _visibleRefs;
    public DialogueTopicRef DefaultTopicRef { get; }
}

public interface IPlayerUtteranceInterpreter
{
    ValueTask<PlayerUtteranceInterpretation> InterpretAsync(
        PlayerUtteranceInterpretationRequest request,
        CancellationToken cancellationToken);
}

public sealed record DialogueSurfaceProfileDocument
{
    [JsonRequired, JsonPropertyName("profile_id")] public string ProfileId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("clarification_message")] public string ClarificationMessage { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("unavailable_message")] public string UnavailableMessage { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("fallback_realization_template")] public string FallbackRealizationTemplate { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("realization_templates")] public Dictionary<string, string> RealizationTemplates { get; init; } = new(StringComparer.Ordinal);
}

public sealed class DialogueSurfaceProfile
{
    private readonly ReadOnlyDictionary<SemanticDialogueActKind, string> _templates;

    private DialogueSurfaceProfile(
        string profileId,
        string clarificationMessage,
        string unavailableMessage,
        string fallbackRealizationTemplate,
        IDictionary<SemanticDialogueActKind, string> templates)
    {
        ProfileId = profileId;
        ClarificationMessage = clarificationMessage;
        UnavailableMessage = unavailableMessage;
        FallbackRealizationTemplate = fallbackRealizationTemplate;
        _templates = new ReadOnlyDictionary<SemanticDialogueActKind, string>(
            new Dictionary<SemanticDialogueActKind, string>(templates));
    }

    public string ProfileId { get; }
    public string ClarificationMessage { get; }
    public string UnavailableMessage { get; }
    public string FallbackRealizationTemplate { get; }

    public static DialogueSurfaceProfile Load(ReadOnlySpan<byte> utf8)
    {
        byte[] bytes = utf8.ToArray();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        DialogueSurfaceProfileDocument document = JsonSerializer.Deserialize<DialogueSurfaceProfileDocument>(bytes, options)
            ?? throw new JsonException("Dialogue surface profile deserialized to null.");
        Require(document.ProfileId, nameof(document.ProfileId));
        Require(document.ClarificationMessage, nameof(document.ClarificationMessage));
        Require(document.UnavailableMessage, nameof(document.UnavailableMessage));
        Require(document.FallbackRealizationTemplate, nameof(document.FallbackRealizationTemplate));
        var templates = new Dictionary<SemanticDialogueActKind, string>();
        foreach ((string token, string template) in document.RealizationTemplates)
        {
            SemanticDialogueActKind kind = Enum.Parse<SemanticDialogueActKind>(token, false);
            Require(template, nameof(document.RealizationTemplates));
            if (!templates.TryAdd(kind, template))
                throw new InvalidDataException("Dialogue realization act kinds must be unique.");
        }
        return new DialogueSurfaceProfile(
            document.ProfileId,
            document.ClarificationMessage,
            document.UnavailableMessage,
            document.FallbackRealizationTemplate,
            templates);
    }

    public static DialogueSurfaceProfile LoadFile(string path) => Load(File.ReadAllBytes(path));

    public string Realize(SemanticDialogueAct act)
    {
        ArgumentNullException.ThrowIfNull(act);
        string template = _templates.TryGetValue(act.Kind, out string? configured)
            ? configured
            : FallbackRealizationTemplate;
        return template
            .Replace("{speaker}", act.Speaker.Value, StringComparison.Ordinal)
            .Replace("{recipient}", act.Recipients[0].Value, StringComparison.Ordinal)
            .Replace("{topic}", act.TopicRef?.Value ?? string.Empty, StringComparison.Ordinal);
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"Dialogue profile {name} must be non-empty.");
    }
}

public enum DialogueSurfaceLineKind
{
    PlayerOriginal,
    NpcRealization,
    SystemNotice
}

public enum DialogueSurfaceRoute
{
    Player,
    L0,
    L1,
    L2,
    System
}

public sealed record DialogueSurfaceLine(
    int Sequence,
    DialogueSurfaceLineKind Kind,
    ActorId? Speaker,
    ActorId? DialogueNpc,
    string Text,
    ConversationSessionId? SessionId,
    SemanticDialogueActId? ActId,
    SimTime OccurredAt,
    DialogueSurfaceRoute Route);

/// <summary>Persistent surface-only transcript. Semantic truth remains owned by ConversationRuntime.</summary>
public sealed class DialogueSurfaceLedger
{
    private readonly List<DialogueSurfaceLine> _lines = [];
    private readonly HashSet<SemanticDialogueActId> _surfacedActs = [];
    private int _submissionCount;
    private bool _submissionInFlight;
    private string? _profileId;

    public IReadOnlyList<DialogueSurfaceLine> Lines => new ReadOnlyCollection<DialogueSurfaceLine>(_lines.ToArray());
    public int NextSubmissionOrdinal => checked(_submissionCount + 1);

    public void BeginSubmission()
    {
        if (_submissionInFlight) throw new InvalidOperationException("Dialogue submission is already in flight.");
        _submissionInFlight = true;
    }

    public void EndSubmission()
    {
        if (!_submissionInFlight) throw new InvalidOperationException("Dialogue submission is not in flight.");
        _submissionInFlight = false;
    }

    public void BindProfile(DialogueSurfaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (_profileId is null)
        {
            _profileId = profile.ProfileId;
            return;
        }

        if (!StringComparer.Ordinal.Equals(_profileId, profile.ProfileId))
        {
            throw new InvalidDataException("Dialogue surface profile identity changed within the active composition.");
        }
    }

    public int ReserveSubmissionOrdinal() => checked(++_submissionCount);

    public void IgnoreExistingActs(IEnumerable<ConversationSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        foreach (SemanticDialogueActId actId in sessions
            .SelectMany(session => session.Transcript)
            .Select(turn => turn.Act.ActId))
        {
            _surfacedActs.Add(actId);
        }
    }

    public void Restore(IEnumerable<DialogueSurfaceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        DialogueSurfaceLine[] snapshot = lines.OrderBy(value => value.Sequence).ToArray();
        if (_lines.Count != 0 || _surfacedActs.Count != 0)
            throw new InvalidOperationException("Dialogue surface restore requires a fresh ledger.");
        for (int index = 0; index < snapshot.Length; index++)
        {
            DialogueSurfaceLine line = snapshot[index];
            if (line.Sequence != index + 1 || string.IsNullOrWhiteSpace(line.Text))
                throw new InvalidDataException("Saved dialogue surface sequence is invalid.");
            _lines.Add(line);
            if (line.ActId is SemanticDialogueActId actId) _surfacedActs.Add(actId);
        }
        _submissionCount = snapshot.Count(value => value.Kind == DialogueSurfaceLineKind.PlayerOriginal);
    }

    public void RecordPlayerTurn(string text, ConversationSession session, SemanticDialogueTurn turn, SimTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        if (!_surfacedActs.Add(turn.Act.ActId)) throw new InvalidOperationException("Dialogue act is already surfaced.");
        Append(
            DialogueSurfaceLineKind.PlayerOriginal,
            turn.Act.Speaker,
            turn.Act.Recipients.Single(),
            text,
            session.SessionId,
            turn.Act.ActId,
            occurredAt,
            DialogueSurfaceRoute.Player);
    }

    public void RecordUnparsedPlayerTurn(string text, ActorId playerActorId, ActorId npcActorId, SimTime occurredAt)
    {
        if (playerActorId == npcActorId)
            throw new ArgumentException("Player and NPC must be distinct.", nameof(npcActorId));
        Append(
            DialogueSurfaceLineKind.PlayerOriginal,
            playerActorId,
            npcActorId,
            text,
            null,
            null,
            occurredAt,
            DialogueSurfaceRoute.Player);
    }

    public void RecordNpcTurn(
        string text,
        ConversationSession session,
        SemanticDialogueTurn turn,
        SimTime occurredAt,
        DialogueSurfaceRoute route)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        if (route is not (DialogueSurfaceRoute.L1 or DialogueSurfaceRoute.L2))
            throw new ArgumentOutOfRangeException(nameof(route));
        if (!_surfacedActs.Add(turn.Act.ActId)) throw new InvalidOperationException("Dialogue act is already surfaced.");
        Append(
            DialogueSurfaceLineKind.NpcRealization,
            turn.Act.Speaker,
            turn.Act.Speaker,
            text,
            session.SessionId,
            turn.Act.ActId,
            occurredAt,
            route);
    }

    public void RecordSystemNotice(string text, SimTime occurredAt) =>
        Append(
            DialogueSurfaceLineKind.SystemNotice,
            null,
            null,
            text,
            null,
            null,
            occurredAt,
            DialogueSurfaceRoute.System);

    public void Synchronize(
        IEnumerable<ConversationSession> sessions,
        DialogueSurfaceProfile profile,
        ActorId playerActorId,
        SimTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(profile);
        foreach (ConversationSession session in sessions.OrderBy(value => value.SessionId.Value, StringComparer.Ordinal))
        {
            foreach (SemanticDialogueTurn turn in session.Transcript.OrderBy(value => value.Sequence))
            {
                if (turn.Act.Speaker == playerActorId || _surfacedActs.Contains(turn.Act.ActId)) continue;
                _surfacedActs.Add(turn.Act.ActId);
                Append(
                    DialogueSurfaceLineKind.NpcRealization,
                    turn.Act.Speaker,
                    turn.Act.Speaker,
                    profile.Realize(turn.Act),
                    session.SessionId,
                    turn.Act.ActId,
                    occurredAt,
                    DialogueSurfaceRoute.L0);
            }
        }
    }

    private void Append(
        DialogueSurfaceLineKind kind,
        ActorId? speaker,
        ActorId? dialogueNpc,
        string text,
        ConversationSessionId? sessionId,
        SemanticDialogueActId? actId,
        SimTime occurredAt,
        DialogueSurfaceRoute route)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Dialogue surface text must be non-empty.", nameof(text));
        _lines.Add(new DialogueSurfaceLine(
            _lines.Count + 1,
            kind,
            speaker,
            dialogueNpc,
            text,
            sessionId,
            actId,
            occurredAt,
            route));
    }

}

public enum PlayerDialogueSubmissionOutcome
{
    Submitted,
    ClarificationRequired,
    Unrepresentable,
    InterpreterUnavailable,
    AwaitingNpcResponse,
    SubmissionInFlight
}

public sealed record PlayerDialogueSubmissionResult(
    PlayerDialogueSubmissionOutcome Outcome,
    PlayerUtteranceInterpretation Interpretation,
    ConversationSession? Session,
    SemanticDialogueTurn? Turn);

/// <summary>Player text → local interpreter → semantic Host/memory commit. NPC response stays dispatcher-owned.</summary>
public sealed class PlayerNaturalLanguageDialogueRuntime : IDisposable
{
    private readonly ConversationRuntime _conversations;
    private readonly DialogueSurfaceLedger _surface;
    private readonly DialogueSurfaceProfile _profile;
    private readonly IPlayerUtteranceInterpreter _interpreter;
    private readonly ActorId _playerActorId;
    private readonly ActorId _defaultNpcActorId;
    private readonly DialogueTopicRef _defaultTopicRef;
    private readonly SemaphoreSlim _singleFlight = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public PlayerNaturalLanguageDialogueRuntime(
        ConversationRuntime conversations,
        DialogueSurfaceLedger surface,
        DialogueSurfaceProfile profile,
        IPlayerUtteranceInterpreter interpreter,
        ActorId playerActorId,
        ActorId npcActorId,
        DialogueTopicRef defaultTopicRef)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(interpreter);
        _conversations = conversations;
        _surface = surface;
        _profile = profile;
        _interpreter = interpreter;
        _playerActorId = playerActorId;
        _defaultNpcActorId = npcActorId;
        _defaultTopicRef = defaultTopicRef;
        _surface.BindProfile(profile);
    }

    public bool HasInFlightSubmission => _singleFlight.CurrentCount == 0;

    public async ValueTask<PlayerDialogueSubmissionResult> SubmitAsync(
        string rawText,
        SimTime occurredAt,
        CancellationToken cancellationToken) =>
        await SubmitAsync(rawText, _defaultNpcActorId, occurredAt, cancellationToken);

    public async ValueTask<PlayerDialogueSubmissionResult> SubmitAsync(
        string rawText,
        ActorId npcActorId,
        SimTime occurredAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawText)) throw new ArgumentException("Player utterance must be non-empty.", nameof(rawText));
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!await _singleFlight.WaitAsync(0, cancellationToken))
        {
            PlayerUtteranceInterpretation waiting = PlayerUtteranceInterpretation.Clarification(
                [],
                "A dialogue submission is already being interpreted.");
            return new PlayerDialogueSubmissionResult(
                PlayerDialogueSubmissionOutcome.SubmissionInFlight,
                waiting,
                null,
                null);
        }

        try
        {
            _surface.BeginSubmission();
            if (_conversations.Sessions.SelectMany(value => value.PendingResponseOpportunities)
                .Any(value => value.Recipient == npcActorId))
            {
                PlayerUtteranceInterpretation waiting = PlayerUtteranceInterpretation.Clarification(
                    [],
                    $"{npcActorId.Value} is still considering the previous turn.");
                _surface.RecordSystemNotice(waiting.VisibleReason!, occurredAt);
                return new PlayerDialogueSubmissionResult(
                    PlayerDialogueSubmissionOutcome.AwaitingNpcResponse,
                    waiting,
                    null,
                    null);
            }

            int predictedOrdinal = _surface.NextSubmissionOrdinal;
            var request = new PlayerUtteranceInterpretationRequest(
                $"player-dialogue-request-{predictedOrdinal}",
                _playerActorId,
                npcActorId,
                rawText,
                [_playerActorId.Value, npcActorId.Value, _defaultTopicRef.Value],
                _defaultTopicRef);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
            PlayerUtteranceInterpretation interpretation = await _interpreter
                .InterpretAsync(request, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            int ordinal = _surface.ReserveSubmissionOrdinal();
            if (ordinal != predictedOrdinal)
                throw new InvalidOperationException("Dialogue submission ordinal changed during single-flight interpretation.");

            if (interpretation.Kind != PlayerUtteranceInterpretationKind.ParsedAct)
            {
                _surface.RecordUnparsedPlayerTurn(rawText, _playerActorId, npcActorId, occurredAt);
                _surface.RecordSystemNotice(interpretation.VisibleReason!, occurredAt);
                return new PlayerDialogueSubmissionResult(
                    interpretation.Kind switch
                    {
                        PlayerUtteranceInterpretationKind.ParseClarificationRequired => PlayerDialogueSubmissionOutcome.ClarificationRequired,
                        PlayerUtteranceInterpretationKind.UnrepresentableUtterance => PlayerDialogueSubmissionOutcome.Unrepresentable,
                        PlayerUtteranceInterpretationKind.InterpreterUnavailable => PlayerDialogueSubmissionOutcome.InterpreterUnavailable,
                        _ => throw new InvalidOperationException("Unexpected player interpretation outcome.")
                    },
                    interpretation,
                    null,
                    null);
            }

            var sessionId = new ConversationSessionId($"player-{npcActorId.Value}-{ordinal}");
            DialogueResponseExpectation responseExpectation = interpretation.ActKind == SemanticDialogueActKind.Thank
                ? DialogueResponseExpectation.None
                : DialogueResponseExpectation.Required;
            var act = new SemanticDialogueAct(
                new SemanticDialogueActId($"{sessionId.Value}-player-turn"),
                interpretation.ActKind!.Value,
                _playerActorId,
                [npcActorId],
                interpretation.TopicRef ?? _defaultTopicRef,
                [],
                null,
                responseExpectation);
            ConversationOpenResult opened = _conversations.Open(
                sessionId,
                [_playerActorId, npcActorId],
                act,
                occurredAt);
            _surface.RecordPlayerTurn(rawText, opened.Session, opened.InitialTurn, occurredAt);
            return new PlayerDialogueSubmissionResult(
                PlayerDialogueSubmissionOutcome.Submitted,
                interpretation,
                opened.Session,
                opened.InitialTurn);
        }
        finally
        {
            _surface.EndSubmission();
            _singleFlight.Release();
        }
    }

    public void SynchronizeSurface(SimTime occurredAt) =>
        _surface.Synchronize(_conversations.Sessions, _profile, _playerActorId, occurredAt);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}

public enum LiveLocalDialogueInterpreterFailureKind
{
    Timeout,
    NetworkFailure,
    HttpFailure,
    ResponseBodyTooLarge,
    InvalidResponseEnvelope,
    InvalidStructuredOutput
}

public sealed record LiveLocalDialogueInterpreterEvidence(
    OpenAiCompatibleProfileId ProfileId,
    OpenAiCompatibleModelId ModelId,
    LiveLocalDialogueInterpreterFailureKind? FailureKind,
    int? HttpStatus);

/// <summary>
/// Demo-only single-shot local interpreter. It accepts strict JSON and leaves fallback policy to its caller.
/// </summary>
public sealed class LiveLocalPlayerUtteranceInterpreter : IPlayerUtteranceInterpreter
{
    internal const string SystemPrompt =
        "Interpret the player's utterance as exactly one bounded semantic dialogue act. " +
        "Return every required field. For parsed_act use an empty unresolved_refs array and null visible_reason. " +
        "For clarification_required or unrepresentable_utterance use null act_kind and topic_ref. " +
        "Use only the supplied visible_refs. Never assert world truth, invent an entity, emit confidence, " +
        "or produce an Invite because Invite needs a separate typed gathering binding. " +
        "Use CasualComment for greetings and small talk, Ask for information questions, Inform for statements, " +
        "Request for requested actions or transfers, and Offer for promises or offered actions. " +
        "If a reference is ambiguous, return clarification_required; if the utterance cannot be represented, " +
        "return unrepresentable_utterance.";

    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleProviderProfile _profile;
    private readonly string _unavailableMessage;

    public LiveLocalPlayerUtteranceInterpreter(
        HttpClient httpClient,
        OpenAiCompatibleProviderProfile profile,
        string unavailableMessage)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.Capabilities.SupportsJsonSchemaStructuredOutput || profile.CredentialReference is not null)
            throw new ArgumentException("Player utterance interpretation requires one credential-free local JSON-schema profile.", nameof(profile));
        if (string.IsNullOrWhiteSpace(unavailableMessage))
            throw new ArgumentException("A visible unavailable message is required.", nameof(unavailableMessage));
        _httpClient = httpClient;
        _profile = profile.Snapshot();
        _unavailableMessage = unavailableMessage;
    }

    public LiveLocalDialogueInterpreterEvidence? LastEvidence { get; private set; }

    public async ValueTask<PlayerUtteranceInterpretation> InterpretAsync(
        PlayerUtteranceInterpretationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        using HttpRequestMessage message = CreateRequest(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_profile.Timeout);
        int? responseStatus = null;
        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            responseStatus = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
                return Unavailable(LiveLocalDialogueInterpreterFailureKind.HttpFailure, responseStatus);
            BoundedResponseBodyReadResult body = await OpenAiCompatibleChatCompletions.ReadResponseBodyAsync(
                response.Content,
                _profile.MaxResponseBodyBytes,
                timeout.Token).ConfigureAwait(false);
            if (!body.IsComplete || body.Body is null)
                return Unavailable(LiveLocalDialogueInterpreterFailureKind.ResponseBodyTooLarge, responseStatus);
            if (!OpenAiCompatibleChatCompletions.TryReadAssistantContent(body.Body, out string? content)
                || content is null)
                return Unavailable(LiveLocalDialogueInterpreterFailureKind.InvalidResponseEnvelope, responseStatus);
            if (!TryDecode(content, request, out PlayerUtteranceInterpretation? interpretation)
                || interpretation is null)
                return Unavailable(LiveLocalDialogueInterpreterFailureKind.InvalidStructuredOutput, responseStatus);
            LastEvidence = new LiveLocalDialogueInterpreterEvidence(
                _profile.ProfileId,
                _profile.ModelId,
                null,
                responseStatus);
            return interpretation;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Unavailable(LiveLocalDialogueInterpreterFailureKind.Timeout, responseStatus);
        }
        catch (HttpRequestException)
        {
            return Unavailable(LiveLocalDialogueInterpreterFailureKind.NetworkFailure, responseStatus);
        }
        catch (IOException)
        {
            return Unavailable(LiveLocalDialogueInterpreterFailureKind.NetworkFailure, responseStatus);
        }
    }

    private HttpRequestMessage CreateRequest(PlayerUtteranceInterpretationRequest request)
    {
        byte[] body;
        using (var stream = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("model", _profile.ModelId.Value);
                writer.WritePropertyName("messages");
                writer.WriteStartArray();
                WriteMessage(writer, "system", SystemPrompt);
                writer.WriteStartObject();
                writer.WriteString("role", "user");
                writer.WriteString("content", CreateUserContext(request));
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WritePropertyName("response_format");
                writer.WriteStartObject();
                writer.WriteString("type", "json_schema");
                writer.WritePropertyName("json_schema");
                writer.WriteStartObject();
                writer.WriteString("name", "player_utterance_interpretation");
                writer.WriteBoolean("strict", true);
                writer.WritePropertyName("schema");
                WriteOutputSchema(writer, request);
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteNumber("max_tokens", _profile.MaxTokens);
                writer.WriteBoolean("stream", false);
                writer.WriteEndObject();
            }
            body = stream.ToArray();
        }
        var message = new HttpRequestMessage(HttpMethod.Post, _profile.ChatCompletionsEndpoint.Value);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new ByteArrayContent(body);
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return message;
    }

    internal static string CreateUserContext(PlayerUtteranceInterpretationRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteUserContext(writer, request);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteUserContext(Utf8JsonWriter writer, PlayerUtteranceInterpretationRequest request)
    {
        writer.WriteStartObject();
        writer.WriteString("request_id", request.RequestId);
        writer.WriteString("player_actor_id", request.PlayerActorId.Value);
        writer.WriteString("npc_actor_id", request.NpcActorId.Value);
        writer.WriteString("raw_text", request.RawText);
        writer.WriteString("default_topic_ref", request.DefaultTopicRef.Value);
        writer.WritePropertyName("visible_refs");
        writer.WriteStartArray();
        foreach (string visibleRef in request.VisibleRefs) writer.WriteStringValue(visibleRef);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal static void WriteOutputSchema(
        Utf8JsonWriter writer,
        PlayerUtteranceInterpretationRequest request)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        WriteStringEnum(writer, "kind", ["parsed_act", "clarification_required", "unrepresentable_utterance"]);
        WriteNullableActKind(writer);
        WriteNullableVisibleRef(writer, "topic_ref", request.VisibleRefs);
        writer.WritePropertyName("unresolved_refs");
        writer.WriteStartObject();
        writer.WriteString("type", "array");
        writer.WritePropertyName("items");
        WriteVisibleRefSchema(writer, request.VisibleRefs);
        writer.WriteEndObject();
        WriteNullableString(writer, "visible_reason");
        writer.WriteEndObject();
        writer.WritePropertyName("required");
        writer.WriteStartArray();
        foreach (string name in new[] { "kind", "act_kind", "topic_ref", "unresolved_refs", "visible_reason" })
            writer.WriteStringValue(name);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteStringEnum(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WritePropertyName("enum");
        writer.WriteStartArray();
        foreach (string value in values) writer.WriteStringValue(value);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteNullableActKind(Utf8JsonWriter writer)
    {
        writer.WritePropertyName("act_kind");
        writer.WriteStartObject();
        writer.WritePropertyName("type");
        writer.WriteStartArray();
        writer.WriteStringValue("string");
        writer.WriteStringValue("null");
        writer.WriteEndArray();
        writer.WritePropertyName("enum");
        writer.WriteStartArray();
        foreach (SemanticDialogueActKind kind in Enum.GetValues<SemanticDialogueActKind>())
        {
            if (kind != SemanticDialogueActKind.Invite) writer.WriteStringValue(kind.ToString());
        }
        writer.WriteNullValue();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WritePropertyName("type");
        writer.WriteStartArray();
        writer.WriteStringValue("string");
        writer.WriteStringValue("null");
        writer.WriteEndArray();
        writer.WriteNumber("minLength", 1);
        writer.WriteEndObject();
    }

    private static void WriteNullableVisibleRef(
        Utf8JsonWriter writer,
        string name,
        IEnumerable<string> visibleRefs)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WritePropertyName("type");
        writer.WriteStartArray();
        writer.WriteStringValue("string");
        writer.WriteStringValue("null");
        writer.WriteEndArray();
        writer.WritePropertyName("enum");
        writer.WriteStartArray();
        foreach (string visibleRef in visibleRefs) writer.WriteStringValue(visibleRef);
        writer.WriteNullValue();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteVisibleRefSchema(Utf8JsonWriter writer, IEnumerable<string> visibleRefs)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WritePropertyName("enum");
        writer.WriteStartArray();
        foreach (string visibleRef in visibleRefs) writer.WriteStringValue(visibleRef);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteMessage(Utf8JsonWriter writer, string role, string content)
    {
        writer.WriteStartObject();
        writer.WriteString("role", role);
        writer.WriteString("content", content);
        writer.WriteEndObject();
    }

    internal static bool TryDecode(
        string content,
        PlayerUtteranceInterpretationRequest request,
        out PlayerUtteranceInterpretation? interpretation)
    {
        interpretation = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            string[] names = root.EnumerateObject().Select(value => value.Name).ToArray();
            if (!names.Order(StringComparer.Ordinal).SequenceEqual(
                    new[] { "act_kind", "kind", "topic_ref", "unresolved_refs", "visible_reason" },
                    StringComparer.Ordinal)
                || root.GetProperty("kind").ValueKind != JsonValueKind.String
                || root.GetProperty("unresolved_refs").ValueKind != JsonValueKind.Array)
                return false;
            string? kind = root.GetProperty("kind").GetString();
            string? actKind = ReadNullableString(root.GetProperty("act_kind"));
            string? topicRef = ReadNullableString(root.GetProperty("topic_ref"));
            string? visibleReason = ReadNullableString(root.GetProperty("visible_reason"));
            string[] unresolved = root.GetProperty("unresolved_refs").EnumerateArray()
                .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                .OfType<string>()
                .ToArray();
            if (unresolved.Length != root.GetProperty("unresolved_refs").GetArrayLength()
                || unresolved.Any(string.IsNullOrWhiteSpace))
                return false;

            if (StringComparer.Ordinal.Equals(kind, "parsed_act")
                && actKind is not null
                && Enum.TryParse(actKind, false, out SemanticDialogueActKind parsedKind)
                && Enum.IsDefined(parsedKind)
                && StringComparer.Ordinal.Equals(actKind, parsedKind.ToString())
                && parsedKind != SemanticDialogueActKind.Invite
                && topicRef is not null
                && request.VisibleRefs.Contains(topicRef, StringComparer.Ordinal)
                && unresolved.Length == 0
                && visibleReason is null)
            {
                interpretation = PlayerUtteranceInterpretation.Parsed(parsedKind, new DialogueTopicRef(topicRef));
                return true;
            }

            if (unresolved.Any(value => !request.VisibleRefs.Contains(value, StringComparer.Ordinal)))
                return false;
            switch (kind)
            {
                case "clarification_required" when actKind is null && topicRef is null
                    && !string.IsNullOrWhiteSpace(visibleReason):
                    interpretation = PlayerUtteranceInterpretation.Clarification(unresolved, visibleReason);
                    return true;
                case "unrepresentable_utterance" when actKind is null && topicRef is null
                    && unresolved.Length == 0 && !string.IsNullOrWhiteSpace(visibleReason):
                    interpretation = PlayerUtteranceInterpretation.Unrepresentable(visibleReason);
                    return true;
                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private PlayerUtteranceInterpretation Unavailable(
        LiveLocalDialogueInterpreterFailureKind kind,
        int? httpStatus)
    {
        LastEvidence = new LiveLocalDialogueInterpreterEvidence(_profile.ProfileId, _profile.ModelId, kind, httpStatus);
        string detail = httpStatus is null ? kind.ToString() : $"{kind}, HTTP {httpStatus.Value}";
        return PlayerUtteranceInterpretation.Unavailable($"{_unavailableMessage} ({detail})");
    }

    private static string? ReadNullableString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Null => null,
        _ => throw new JsonException("Expected string or null.")
    };
}

public enum PlayerUtteranceInterpreterRoute
{
    Local,
    RemoteL2Fallback
}

/// <summary>Uses remote L2 semantic interpretation only when the local bounded interpreter cannot settle the text.</summary>
public sealed class FallbackPlayerUtteranceInterpreter : IPlayerUtteranceInterpreter
{
    private readonly IPlayerUtteranceInterpreter _local;
    private readonly IPlayerUtteranceInterpreter _remote;

    public FallbackPlayerUtteranceInterpreter(
        IPlayerUtteranceInterpreter local,
        IPlayerUtteranceInterpreter remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        _local = local;
        _remote = remote;
    }

    public PlayerUtteranceInterpreterRoute LastRoute { get; private set; } = PlayerUtteranceInterpreterRoute.Local;

    public async ValueTask<PlayerUtteranceInterpretation> InterpretAsync(
        PlayerUtteranceInterpretationRequest request,
        CancellationToken cancellationToken)
    {
        PlayerUtteranceInterpretation local = await _local
            .InterpretAsync(request, cancellationToken).ConfigureAwait(false);
        if (local.Kind is PlayerUtteranceInterpretationKind.ParsedAct
            or PlayerUtteranceInterpretationKind.ParseClarificationRequired)
        {
            LastRoute = PlayerUtteranceInterpreterRoute.Local;
            return local;
        }

        LastRoute = PlayerUtteranceInterpreterRoute.RemoteL2Fallback;
        PlayerUtteranceInterpretation remote = await _remote
            .InterpretAsync(request, cancellationToken).ConfigureAwait(false);
        if (remote.Kind != PlayerUtteranceInterpretationKind.InterpreterUnavailable)
            return remote;
        if (local.Kind == PlayerUtteranceInterpretationKind.UnrepresentableUtterance)
            return local;
        return PlayerUtteranceInterpretation.Unavailable(
            $"{local.VisibleReason} Remote L2 fallback also failed: {remote.VisibleReason}");
    }
}

public enum LiveRemoteDialogueInterpreterFailureKind
{
    Timeout,
    NetworkFailure,
    HttpFailure,
    ResponseBodyTooLarge,
    InvalidResponseEnvelope,
    InvalidStructuredOutput
}

public sealed record LiveRemoteDialogueInterpreterEvidence(
    AnthropicMessagesProfileId ProfileId,
    AnthropicMessagesModelId ModelId,
    LiveRemoteDialogueInterpreterFailureKind? FailureKind,
    int? HttpStatus);

/// <summary>Remote L2 fallback for typed player-text interpretation; it cannot mutate Authority state.</summary>
public sealed class LiveRemotePlayerUtteranceInterpreter : IPlayerUtteranceInterpreter
{
    private const string ToolName = "interpret_player_utterance";
    private readonly HttpClient _httpClient;
    private readonly AnthropicMessagesProviderProfile _profile;
    private readonly ProviderApiKey _apiKey;
    private readonly string _unavailableMessage;

    public LiveRemotePlayerUtteranceInterpreter(
        HttpClient httpClient,
        AnthropicMessagesProviderProfile profile,
        ProviderApiKey apiKey,
        string unavailableMessage)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(apiKey);
        if (profile.CredentialReference != apiKey.CredentialReference)
            throw new ArgumentException("Remote dialogue credential must match its profile.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(unavailableMessage))
            throw new ArgumentException("A visible unavailable message is required.", nameof(unavailableMessage));
        _httpClient = httpClient;
        _profile = profile.Snapshot();
        _apiKey = apiKey;
        _unavailableMessage = unavailableMessage;
    }

    public LiveRemoteDialogueInterpreterEvidence? LastEvidence { get; private set; }

    public async ValueTask<PlayerUtteranceInterpretation> InterpretAsync(
        PlayerUtteranceInterpretationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        byte[] body = CreateRequestBody(request);
        using HttpRequestMessage message = AnthropicMessagesRemotePlannerProtocol.CreateRequest(
            _profile,
            _apiKey,
            body);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_profile.Timeout);
        int? responseStatus = null;
        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            responseStatus = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
                return Unavailable(LiveRemoteDialogueInterpreterFailureKind.HttpFailure, responseStatus);
            BoundedResponseBodyReadResult responseBody = await OpenAiCompatibleChatCompletions.ReadResponseBodyAsync(
                response.Content,
                _profile.MaxResponseBodyBytes,
                timeout.Token).ConfigureAwait(false);
            if (!responseBody.IsComplete || responseBody.Body is null)
                return Unavailable(LiveRemoteDialogueInterpreterFailureKind.ResponseBodyTooLarge, responseStatus);
            if (!AnthropicMessagesRemotePlannerProtocol.TryReadToolCalls(
                    responseBody.Body,
                    out IReadOnlyList<RemotePlannerToolCall>? calls)
                || calls is null
                || calls.Single() is not { Name: ToolName, ArgumentsJson: not null } call)
                return Unavailable(LiveRemoteDialogueInterpreterFailureKind.InvalidResponseEnvelope, responseStatus);
            if (!LiveLocalPlayerUtteranceInterpreter.TryDecode(
                    call.ArgumentsJson,
                    request,
                    out PlayerUtteranceInterpretation? interpretation)
                || interpretation is null)
                return Unavailable(LiveRemoteDialogueInterpreterFailureKind.InvalidStructuredOutput, responseStatus);
            LastEvidence = new LiveRemoteDialogueInterpreterEvidence(
                _profile.ProfileId,
                _profile.ModelId,
                null,
                responseStatus);
            return interpretation;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Unavailable(LiveRemoteDialogueInterpreterFailureKind.Timeout, responseStatus);
        }
        catch (HttpRequestException)
        {
            return Unavailable(LiveRemoteDialogueInterpreterFailureKind.NetworkFailure, responseStatus);
        }
        catch (IOException)
        {
            return Unavailable(LiveRemoteDialogueInterpreterFailureKind.NetworkFailure, responseStatus);
        }
    }

    private byte[] CreateRequestBody(PlayerUtteranceInterpretationRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", _profile.ModelId.Value);
            writer.WriteNumber("max_tokens", _profile.MaxTokens);
            writer.WriteString("system", LiveLocalPlayerUtteranceInterpreter.SystemPrompt);
            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", LiveLocalPlayerUtteranceInterpreter.CreateUserContext(request));
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WritePropertyName("tools");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("name", ToolName);
            writer.WriteString("description", "Return one bounded semantic interpretation of the supplied player text.");
            writer.WritePropertyName("input_schema");
            LiveLocalPlayerUtteranceInterpreter.WriteOutputSchema(writer, request);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WritePropertyName("tool_choice");
            writer.WriteStartObject();
            writer.WriteString("type", "any");
            writer.WriteBoolean("disable_parallel_tool_use", true);
            writer.WriteEndObject();
            writer.WritePropertyName("thinking");
            writer.WriteStartObject();
            writer.WriteString("type", _profile.ThinkingEnabled ? "enabled" : "disabled");
            if (_profile.ThinkingEnabled) writer.WriteNumber("budget_tokens", _profile.MaxTokens);
            writer.WriteEndObject();
            if (_profile.ThinkingEnabled)
            {
                writer.WritePropertyName("output_config");
                writer.WriteStartObject();
                writer.WriteString(
                    "effort",
                    _profile.ThinkingEffort == AnthropicThinkingEffort.High ? "high" : "max");
                writer.WriteEndObject();
            }
            writer.WriteBoolean("stream", false);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private PlayerUtteranceInterpretation Unavailable(
        LiveRemoteDialogueInterpreterFailureKind kind,
        int? httpStatus)
    {
        LastEvidence = new LiveRemoteDialogueInterpreterEvidence(
            _profile.ProfileId,
            _profile.ModelId,
            kind,
            httpStatus);
        string detail = httpStatus is null ? kind.ToString() : $"{kind}, HTTP {httpStatus.Value}";
        return PlayerUtteranceInterpretation.Unavailable($"{_unavailableMessage} ({detail})");
    }
}
