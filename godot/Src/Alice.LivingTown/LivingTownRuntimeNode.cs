using Alice.Activities;
using Alice.Actors;
using Alice.Cognition;
using Alice.Npc;
using Alice.NpcExecution;
using Alice.ProductRuntime;
using Alice.PlayerControl;
using Alice.ModelRuntime;
using Alice.Social;
using Godot;
using System.Text.Json;

namespace Alice.LivingTown;

/// <summary>Production Godot owner for the shared Player + Living Town product composition.</summary>
public sealed partial class LivingTownRuntimeNode : Node2D
{
    public const string AutoValidateArgument = "--auto-validate";
    public const string LiveEscalationCheckArgument = "--live-escalation-check";

    private TownWorldConfiguration? _world;
    private LivingTownProductComposition? _composition;
    private LivingTownRosterSceneComposition? _scene;
    private LivingTownObservability? _observability;
    private DialogueSurfaceProfile? _dialogueProfile;
    private System.Net.Http.HttpClient? _httpClient;
    private readonly List<string> _logActorIds = [];
    private readonly Dictionary<string, string> _actorDisplayNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _dialogueNotices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _dialogueRouteDescriptions = new(StringComparer.Ordinal);
    private readonly Queue<InitialCognitionWork> _cognitionQueue = [];
    private readonly Queue<TownDialogueResponseNeed> _dialogueQueue = [];
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private ActorId? _selectedDialogueNpc;
    private ActorId? _pendingDialogueNpc;
    private string? _pendingDialoguePlayerText;
    private string? _selectedLogActorId;
    private ActivityLogSource _activityLogSource = ActivityLogSource.All;
    private string? _selectedResearchActorId;
    private string? _researchNotice;
    private double _elapsed;
    private long _nextTick;
    private bool _dialogueBusy;
    private bool _initialCognitionScheduled;
    private bool _autoValidate;
    private bool _liveEscalationCheck;
    private int _cognitionInFlight;
    private string? _npcDebugCameraActorId;

    [Export] public PackedScene? NpcEntityScene { get; set; }
    [Export] public Node? NpcContainer { get; set; }
    [Export] public Label? StatusLabel { get; set; }
    [Export] public RichTextLabel? TraceLabel { get; set; }
    [Export] public LineEdit? DialogueInput { get; set; }
    [Export] public RichTextLabel? DialogueTranscript { get; set; }
    [Export] public Label? DialogueTargetLabel { get; set; }
    [Export] public Label? DialogueRouteLabel { get; set; }
    [Export] public Control? PlayerInventoryPanel { get; set; }
    [Export] public RichTextLabel? PlayerInventoryLabel { get; set; }
    [Export] public OptionButton? Rq1ActivationOption { get; set; }
    [Export] public OptionButton? Rq2MemoryOption { get; set; }
    [Export] public OptionButton? ActivityLogActorOption { get; set; }
    [Export] public CheckButton? ActivityLogToggle { get; set; }
    [Export] public Control? ActivityLogPanel { get; set; }
    [Export] public CheckButton? ResearchDebugToggle { get; set; }
    [Export] public Control? ResearchDebugPanel { get; set; }
    [Export] public OptionButton? ResearchDebugActorOption { get; set; }
    [Export] public RichTextLabel? ResearchDebugText { get; set; }
    [Export] public Control? NpcDebugPanel { get; set; }
    [Export] public Label? TimeProgressLabel { get; set; }
    [Export] public ProgressBar? TimeProgressBar { get; set; }
    [Export] public NavigationRegion2D? TownNavigationRegion { get; set; }
    [Export] public PlayerEntity? PlayerEntity { get; set; }
    [Export] public Camera2D? NpcDebugCamera { get; set; }
    [Export] public TownMapInteractionShell? InteractionShell { get; set; }
    [Export(PropertyHint.File, "*.json")] public string TownWorldPath { get; set; } = "res://Config/town_world.json";
    [Export(PropertyHint.File, "*.json")] public string FormalReadinessPath { get; set; } = "res://Config/formal_readiness_v1.json";
    [Export] public string ProductSavePath { get; set; } = "user://living-town-save.json";

    public override void _Ready()
    {
        string[] arguments = OS.GetCmdlineUserArgs();
        _autoValidate = arguments.Contains(AutoValidateArgument, StringComparer.Ordinal);
        _liveEscalationCheck = arguments.Contains(LiveEscalationCheckArgument, StringComparer.Ordinal);
        if (_autoValidate && _liveEscalationCheck)
            throw new InvalidOperationException("Living Town validation modes are mutually exclusive.");
        _world = TownWorldConfiguration.Load(ProjectSettings.GlobalizePath(TownWorldPath));
        LivingTownRuntimeConfiguration configuration = _world.Runtime;
        DialogueSurfaceProfile dialogueProfile = DialogueSurfaceProfile.LoadFile(
            ProjectSettings.GlobalizePath(configuration.Dialogue.SurfaceProfilePath));
        _dialogueProfile = dialogueProfile;
        _httpClient = new System.Net.Http.HttpClient();
        IPlayerUtteranceInterpreter dialogueInterpreter = CreateDialogueInterpreter(dialogueProfile);
        var l1Client = new LiveTownL1DecisionClient(
            _httpClient,
            ProductModelClientComposition.CreateLocalProfile(
                configuration.ProviderProfiles,
                configuration.ProviderQueue));
        var dialogueRouteClient = new LiveTownL1DialogueRouteClient(
            _httpClient,
            ProductModelClientComposition.CreateLocalProfile(
                configuration.ProviderProfiles,
                configuration.ProviderQueue));
        IModelClient<RemotePlannerResponse> l2Client = ProductModelClientComposition.CreateRemotePlanner(
            _httpClient,
            configuration.ProviderProfiles,
            configuration.ProviderQueue);
        InstallComposition(LivingTownProductComposition.Create(
            _world,
            dialogueProfile,
            dialogueInterpreter,
            l1Client,
            dialogueRouteClient,
            l2Client));

        if (_autoValidate)
        {
            RunAutoValidationAfterNavigationSync();
            return;
        }
        if (_liveEscalationCheck)
        {
            RunLiveEscalationCheckAsync();
            return;
        }
        AdvanceOnce();
    }

    public override void _Process(double delta)
    {
        UpdateNpcDebugCamera();
        if (_world is null || _dialogueBusy || _autoValidate || _liveEscalationCheck) return;
        _elapsed += delta;
        double tickSeconds = _world.Runtime.SimulationTickIntervalMilliseconds / 1000.0;
        while (_elapsed >= tickSeconds)
        {
            _elapsed -= tickSeconds;
            AdvanceOnce();
        }
    }

    public override void _ExitTree()
    {
        _lifetimeCancellation.Cancel();
        if (PlayerEntity is not null)
            PlayerEntity.GameActionSpecProduced -= OnPlayerGameActionSpecProduced;
        if (InteractionShell is not null)
        {
            InteractionShell.PlayerActionSelected -= OnPlayerActionSelected;
            InteractionShell.NpcSelected -= OnNpcSelected;
            InteractionShell.NpcDebugOpened -= OnNpcDebugOpened;
            InteractionShell.NpcDebugClosed -= OnNpcDebugClosed;
        }
        _scene?.Dispose();
        _scene = null;
        _composition?.Dispose();
        _composition = null;
        _httpClient?.Dispose();
        _httpClient = null;
        _dialogueProfile = null;
    }

    public async ValueTask<PlayerDialogueSubmissionResult> SubmitPlayerDialogueAsync(
        string text,
        CancellationToken cancellationToken)
    {
        ActorId target = _selectedDialogueNpc ?? new ActorId(RequireConfiguration().Dialogue.NpcActorId);
        PlayerDialogueSubmissionResult result = await RequireComposition().PlayerDialogue.SubmitAsync(
            text,
            target,
            CurrentTime(),
            cancellationToken);
        if (result.Outcome == PlayerDialogueSubmissionOutcome.Submitted)
        {
            ConversationSession session = result.Session
                ?? throw new InvalidOperationException("Submitted dialogue lacks its session.");
            SemanticDialogueTurn turn = result.Turn
                ?? throw new InvalidOperationException("Submitted dialogue lacks its turn.");
            bool expectsResponse = session.PendingResponseOpportunities.Any(value =>
                value.SourceActId == turn.Act.ActId);
            if (expectsResponse)
            {
                TownDialogueRoutingOutcome dialogueOutcome = await RequireComposition().DialogueRouting.InvokeAsync(
                    session, turn, text, CurrentTime(), cancellationToken, ObserveDialogueTrace);
                _observability?.ObserveCognition(
                    CurrentTime(), target, dialogueOutcome.Route, "dialogue-routed", dialogueOutcome.Evidence,
                    dialogueOutcome.Failure is null);
                _dialogueRouteDescriptions[target.Value] = DescribeDialogueRoute(dialogueOutcome);
                string? failure = dialogueOutcome.Failure;
                if (failure is null) _dialogueNotices.Remove(target.Value);
                else _dialogueNotices[target.Value] = failure;
            }
            else
            {
                _dialogueNotices.Remove(target.Value);
                _dialogueRouteDescriptions[target.Value] = "L0 direct settlement — no NPC response required";
                _observability?.ObserveCognition(
                    CurrentTime(), target, LivingTownCognitionRoute.L0,
                    "dialogue-settled", "semantic act requires no response", true);
            }
        }
        else
        {
            _dialogueNotices[target.Value] = result.Interpretation.VisibleReason ?? result.Outcome.ToString();
            _dialogueRouteDescriptions[target.Value] =
                $"Local semantic interpretation unavailable — {result.Outcome}";
        }
        RefreshUi(CurrentTime(), result.Outcome.ToString());
        return result;
    }

    public ActorExecutionReceipt EquipPlayer()
    {
        ActorExecutionReceipt receipt = RequireComposition().EquipPlayer(CurrentTime());
        RefreshUi(CurrentTime(), $"Equip: {receipt.Outcome}");
        return receipt;
    }

    public ActorExecutionReceipt UnequipPlayer()
    {
        ActorExecutionReceipt receipt = RequireComposition().UnequipPlayer(CurrentTime());
        RefreshUi(CurrentTime(), $"Unequip: {receipt.Outcome}");
        return receipt;
    }

    public void OnDialogueSubmitPressed() => SubmitDialogueText(DialogueInput?.Text);

    public void OnDialogueTextSubmitted(string text) => SubmitDialogueText(text);

    private async void SubmitDialogueText(string? text)
    {
        if (_dialogueBusy || DialogueInput is null || string.IsNullOrWhiteSpace(text)) return;
        string submittedText = text.Trim();
        ActorId target = _selectedDialogueNpc ?? new ActorId(RequireConfiguration().Dialogue.NpcActorId);
        _dialogueBusy = true;
        _pendingDialogueNpc = target;
        _pendingDialoguePlayerText = submittedText;
        DialogueInput.Clear();
        DialogueInput.Editable = false;
        RefreshUi(CurrentTime(), "dialogue pending");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            _ = await SubmitPlayerDialogueAsync(
                submittedText,
                CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            GD.PushWarning("Living Town dialogue submission was cancelled.");
        }
        finally
        {
            _pendingDialogueNpc = null;
            _pendingDialoguePlayerText = null;
            _dialogueBusy = false;
            DialogueInput.Editable = true;
            DialogueInput.GrabFocus();
            if (_composition is not null) RefreshUi(CurrentTime(), "dialogue settled");
        }
    }

    public void OnPlayerEquipPressed() => _ = EquipPlayer();
    public void OnPlayerUnequipPressed() => _ = UnequipPlayer();

    public void OnSaveGamePressed()
    {
        if (_dialogueBusy || _cognitionInFlight > 0 || _cognitionQueue.Count > 0 || _dialogueQueue.Count > 0)
        {
            GD.PushWarning("Living Town save rejected while Provider work is in flight.");
            return;
        }
        TownProductSaveCaptureOutcome outcome = TownProductSaveRuntime.Capture(
            RequireComposition(), CurrentTime(), _nextTick);
        if (outcome is not TownProductSaveCaptured captured)
        {
            GD.PushWarning($"Living Town save rejected: {outcome}.");
            return;
        }
        string path = ProjectSettings.GlobalizePath(ProductSavePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Living Town save path has no directory."));
        TownProductSaveRuntime.SaveFile(path, captured.Document);
        RefreshUi(CurrentTime(), "saved");
    }

    public void OnLoadGamePressed()
    {
        if (_dialogueBusy || _cognitionInFlight > 0 || _cognitionQueue.Count > 0 || _dialogueQueue.Count > 0)
        {
            GD.PushWarning("Living Town load rejected while Provider work is in flight.");
            return;
        }
        TownProductSaveDocument document = TownProductSaveRuntime.LoadFile(
            ProjectSettings.GlobalizePath(ProductSavePath));
        DialogueSurfaceProfile profile = _dialogueProfile
            ?? throw new InvalidOperationException("Living Town dialogue profile is not loaded.");
        var l1Client = new LiveTownL1DecisionClient(
            _httpClient ?? throw new InvalidOperationException("Living Town HTTP client is not initialized."),
            ProductModelClientComposition.CreateLocalProfile(
                RequireConfiguration().ProviderProfiles,
                RequireConfiguration().ProviderQueue));
        var dialogueRouteClient = new LiveTownL1DialogueRouteClient(
            _httpClient ?? throw new InvalidOperationException("Living Town HTTP client is not initialized."),
            ProductModelClientComposition.CreateLocalProfile(
                RequireConfiguration().ProviderProfiles,
                RequireConfiguration().ProviderQueue));
        IModelClient<RemotePlannerResponse> l2Client = ProductModelClientComposition.CreateRemotePlanner(
            _httpClient ?? throw new InvalidOperationException("Living Town HTTP client is not initialized."),
            RequireConfiguration().ProviderProfiles,
            RequireConfiguration().ProviderQueue);
        LivingTownProductComposition replacement = LivingTownProductComposition.Create(
            RequireWorld(), profile, CreateDialogueInterpreter(profile), l1Client, dialogueRouteClient, l2Client);
        try
        {
            TownProductSaveRuntime.Restore(replacement, document);
            InstallComposition(replacement);
            _nextTick = document.NextTick;
            RefreshUi(CurrentTime(), "loaded");
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
    }

    public void OnRq1ActivationSelected(long index)
    {
        if (index is < 0 or > 1) return;
        RequireComposition().L2Decisions.Policy.SelectRq1((TownRq1ActivationMode)index);
        RefreshUi(CurrentTime(), "RQ1 pending");
    }

    public void OnRq2MemorySelected(long index)
    {
        if (index is < 0 or > 1) return;
        RequireComposition().L2Decisions.Policy.SelectRq2((TownRq2MemoryMode)index);
        RefreshUi(CurrentTime(), "RQ2 pending");
    }

    public void OnActivityLogActorSelected(long index)
    {
        _selectedLogActorId = index <= 0 || index > _logActorIds.Count
            ? null
            : _logActorIds[(int)index - 1];
        RefreshUi(CurrentTime(), "running");
    }

    public void OnActivityLogAllPressed() => SelectActivityLogSource(ActivityLogSource.All);
    public void OnActivityLogAuthorityPressed() => SelectActivityLogSource(ActivityLogSource.Authority);
    public void OnActivityLogRuntimePressed() => SelectActivityLogSource(ActivityLogSource.Runtime);
    public void OnActivityLogProviderPressed() => SelectActivityLogSource(ActivityLogSource.Provider);
    public void OnActivityLogDialoguePressed() => SelectActivityLogSource(ActivityLogSource.LiveDialogue);

    public void OnActivityLogToggled(bool visible)
    {
        if (ActivityLogPanel is not null) ActivityLogPanel.Visible = visible;
        if (visible)
        {
            if (ResearchDebugPanel is not null) ResearchDebugPanel.Visible = false;
            ResearchDebugToggle?.SetPressedNoSignal(false);
            OnNpcDebugClosed();
        }
        UpdateOverlayVisibility();
        if (visible) RefreshUi(CurrentTime(), "running");
    }

    public void OnResearchDebugToggled(bool visible)
    {
        if (ResearchDebugPanel is not null) ResearchDebugPanel.Visible = visible;
        if (visible)
        {
            if (ActivityLogPanel is not null) ActivityLogPanel.Visible = false;
            ActivityLogToggle?.SetPressedNoSignal(false);
            if (_selectedResearchActorId is string actorId) FocusDebugCameraOnNpc(actorId);
        }
        else OnNpcDebugClosed();
        UpdateOverlayVisibility();
        if (visible) RefreshUi(CurrentTime(), "research debug");
    }

    public void OnResearchDebugClosePressed()
    {
        ResearchDebugToggle?.SetPressedNoSignal(false);
        if (ResearchDebugPanel is not null) ResearchDebugPanel.Visible = false;
        OnNpcDebugClosed();
        UpdateOverlayVisibility();
    }

    public void OnResearchDebugActorSelected(long index)
    {
        _selectedResearchActorId = index < 0 || index >= _logActorIds.Count
            ? null
            : _logActorIds[(int)index];
        if (ResearchDebugPanel?.Visible == true && _selectedResearchActorId is string actorId)
            FocusDebugCameraOnNpc(actorId);
        RefreshUi(CurrentTime(), "research actor selected");
    }

    public void OnResearchDebugL0Pressed() => TriggerResearchScenario(TownAutonomyDebugScenario.ClearNeed);
    public void OnResearchDebugL1Pressed() => TriggerResearchScenario(TownAutonomyDebugScenario.AmbiguousNeed);
    public void OnResearchDebugL2Pressed() => TriggerResearchScenario(TownAutonomyDebugScenario.StrategicConflict);

    private void InstallComposition(LivingTownProductComposition replacement)
    {
        TownPopulationManifest manifest = RequireWorld().Population;
        LivingTownRuntimeConfiguration configuration = RequireConfiguration();
        NavigationRegion2D navigationRegion = TownNavigationRegion
            ?? throw new InvalidOperationException("Living Town navigation region is not configured.");
        navigationRegion.NavigationPolygon = TownGodotNavigationMap.Create(RequireWorld().Map);
        Node container = NpcContainer
            ?? throw new InvalidOperationException("Living Town NPC container is not configured.");
        PackedScene npcScene = NpcEntityScene
            ?? throw new InvalidOperationException("Living Town NpcEntity scene is not configured.");
        var newScene = new LivingTownRosterSceneComposition(
            manifest,
            replacement.Population,
            new GodotLivingTownActorSceneFactory(npcScene, container, RequireWorld().Map.SpeedProfile));
        newScene.RefreshAll();
        var newObservability = new LivingTownObservability(
            replacement.Population,
            manifest,
            replacement.Social,
            replacement.Gameplay,
            configuration.TraceRetentionEntries,
            newScene);

        LivingTownRosterSceneComposition? oldScene = _scene;
        LivingTownProductComposition? oldComposition = _composition;
        _composition = replacement;
        replacement.L2Decisions.EnableDebugSummaryFallback = true;
        _scene = newScene;
        _observability = newObservability;
        ConfigurePolicyControls(replacement.L2Decisions.Policy);
        ConfigureActivityLogActors(manifest);
        replacement.DialogueRouting.ResponseLanguage = "English";
        ConfigurePlayerScene(replacement);
        if (InteractionShell is not null)
        {
            InteractionShell.OnDebugClosePressed();
            InteractionShell.PlayerActionSelected -= OnPlayerActionSelected;
            InteractionShell.NpcSelected -= OnNpcSelected;
            InteractionShell.NpcDebugOpened -= OnNpcDebugOpened;
            InteractionShell.NpcDebugClosed -= OnNpcDebugClosed;
            InteractionShell.Configure(
                RequireWorld().Map,
                container,
                PlayerEntity ?? throw new InvalidOperationException("TownMap Player is not configured."),
                replacement.Gameplay,
                replacement.Player.ActorId,
                CurrentTime,
                ResolveNpcDebug);
            InteractionShell.PlayerActionSelected += OnPlayerActionSelected;
            InteractionShell.NpcSelected += OnNpcSelected;
            InteractionShell.NpcDebugOpened += OnNpcDebugOpened;
            InteractionShell.NpcDebugClosed += OnNpcDebugClosed;
        }
        SelectDialogueNpc(configuration.Dialogue.NpcActorId, false);
        ApplyEnglishLanguage();
        UpdateOverlayVisibility();
        oldScene?.Dispose();
        oldComposition?.Dispose();
    }

    private void AdvanceOnce()
    {
        LivingTownProductComposition composition = RequireComposition();
        LivingTownRosterSceneComposition scene = _scene
            ?? throw new InvalidOperationException("Living Town scene bridge is not initialized.");
        LivingTownObservability observability = _observability
            ?? throw new InvalidOperationException("Living Town diagnostics are not initialized.");
        var now = new SimTime(_nextTick);
        observability.Observe(composition.Advance(now, DateTimeOffset.UtcNow, CancellationToken.None));
        DrainCognitionQueues(composition);
        scene.RefreshAll();
        if (!_autoValidate && !_initialCognitionScheduled) EnqueueInitialCognition(now);
        else if (!_autoValidate) StartPendingCognition();
        _nextTick = checked(_nextTick + 1);
        if (now.Ticks % 4 == 0) RefreshUi(now, "running");
    }

    private void DrainCognitionQueues(LivingTownProductComposition composition)
    {
        while (composition.TryDequeueDialogueResponse(out TownDialogueResponseNeed? dialogueNeed))
            _dialogueQueue.Enqueue(dialogueNeed!);
        while (composition.TryDequeueAutonomyLocalDecision(out TownAutonomyLocalDecisionWork? localDecision))
            QueueCognition(new InitialCognitionWork(
                InitialCognitionKind.AutonomyL1,
                localDecision!.ActorId,
                localDecision.QueuedAt,
                null,
                null,
                localDecision));
        while (composition.TryDequeueAutonomyDecision(out TownAutonomyDecisionWork? decision))
            QueueCognition(new InitialCognitionWork(
                InitialCognitionKind.L2,
                decision!.ActorId,
                decision.QueuedAt,
                decision.Problem,
                decision.Need,
                null,
                decision));
    }

    private void TriggerResearchScenario(TownAutonomyDebugScenario scenario)
    {
        string? actorId = _selectedResearchActorId ?? _selectedDialogueNpc?.Value;
        if (actorId is null)
        {
            _researchNotice = T(
                "Select an NPC before starting a scenario.",
                "开始场景前请选择一个 NPC。");
            RefreshUi(CurrentTime(), "debug trigger rejected");
            return;
        }
        LivingTownProductComposition composition = RequireComposition();
        TownAutonomyDebugTriggerOutcome outcome = composition.Autonomy.TriggerDebugScenario(
            new ActorId(actorId), scenario, CurrentTime());
        _researchNotice = $"{T("Scenario", "场景")} {scenario} | "
            + $"{T("expected", "预期")} {outcome.ExpectedRoute}: {outcome.Evidence}";
        if (outcome.Receipt is not null)
        {
            _observability?.Observe(outcome.Receipt);
            if (outcome.Receipt.Outcome == ActorExecutionOutcome.Completed)
                composition.ProjectAutonomySettlement(outcome.Receipt);
        }
        DrainCognitionQueues(composition);
        StartPendingCognition();
        _scene?.RefreshAll();
        RefreshUi(CurrentTime(), outcome.Accepted
            ? $"{scenario} triggered"
            : $"{scenario} unavailable");
    }

    private void RunAutoValidation()
    {
        LivingTownRuntimeConfiguration configuration = RequireConfiguration();
        LivingTownProductComposition composition = RequireComposition();
        ValidateWaterNavigation();
        _ = composition.EquipPlayer(new SimTime(0));
        for (long tick = 0; tick < configuration.IntegrationValidationDurationTicks; tick++) AdvanceOnce();

        LivingTownPopulationRuntime runtime = composition.Population;
        LivingTownRosterSceneComposition scene = _scene
            ?? throw new InvalidOperationException("Living Town validation lacks its scene bridge.");
        LivingTownObservability observability = _observability
            ?? throw new InvalidOperationException("Living Town validation lacks diagnostics.");
        if (scene.Bindings.Count != RequireWorld().Population.Actors.Count
            || scene.SceneRegistry.ActorIds.Count != runtime.Npcs.Count
            || runtime.Npcs.Any(npc => npc.SchedulerNpc.DispatchSequence == 0)
            || runtime.Npcs.SelectMany(npc => npc.State.Memory.Snapshot()).Any(memory =>
                memory.ActorVisibleText.EndsWith(" completed Wait.", StringComparison.Ordinal)
                || memory.ActorVisibleText.EndsWith(" completed Navigate.", StringComparison.Ordinal))
            || observability.Snapshot(CurrentTime()).Trace.Count == 0
            || composition.PublicEvents.Gatherings.Count == 0)
            throw new InvalidOperationException("Living Town product did not advance player, exact roster, and public events together.");

        byte[] trace = observability.ExportCanonicalTrace();
        string recordingDirectory = ProjectSettings.GlobalizePath("user://recordings");
        Directory.CreateDirectory(recordingDirectory);
        File.WriteAllBytes(Path.Combine(recordingDirectory, configuration.TraceFileName), trace);
        GD.Print($"LIVING_TOWN_DEMO PASS actors={runtime.Npcs.Count} events={composition.PublicEvents.Gatherings.Count}");
        GetTree().Quit(0);
    }

    private async void RunAutoValidationAfterNavigationSync()
    {
        NavigationRegion2D navigationRegion = TownNavigationRegion
            ?? throw new InvalidOperationException("Living Town navigation region is not configured.");
        for (int frame = 0; frame < 30; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        if (NavigationServer2D.MapGetIterationId(navigationRegion.GetNavigationMap()) == 0)
            throw new InvalidOperationException("Town navigation map did not synchronize.");
        RunAutoValidation();
    }

    private async void RunLiveEscalationCheckAsync()
    {
        try
        {
            LiveEscalationGateThresholds thresholds = LoadLiveEscalationGateThresholds();
            LiveEscalationScenario[] scenarios =
            [
                new(
                    "clear-ordinary-turn",
                    LivingTownCognitionRoute.L1,
                    SemanticDialogueActKind.CasualComment,
                    "Hello! The weather is pleasant today."),
                new(
                    "ambiguous-bounded-choice",
                    LivingTownCognitionRoute.L1,
                    SemanticDialogueActKind.Ask,
                    "I am worried about today's shortage and cannot decide whether to help at the mill or the clinic. What would you recommend?"),
                new(
                    "strategic-debt-commitment",
                    LivingTownCognitionRoute.L2,
                    SemanticDialogueActKind.Offer,
                    "I promise to repay you 5 coins tomorrow, and I need you to rely on that debt commitment. Will you accept?")
            ];
            LiveEscalationLanguage[] languages = [new("en", "English")];
            if (thresholds.ScenarioCount != scenarios.Length
                || thresholds.LanguageCount != languages.Length)
                throw new InvalidDataException(
                    "The live escalation diagnostic matrix does not match formal readiness.");

            LivingTownProductComposition composition = RequireComposition();
            var targetActors = new List<ActorId>();
            foreach (LivingTownNpcRuntime npc in composition.Population.Npcs)
                targetActors.Add(npc.ActorId);
            targetActors.Sort(ActorIdValueComparer.Instance);
            if (targetActors.Count == 0)
                throw new InvalidDataException("The live escalation diagnostic requires at least one NPC.");

            var trials = new List<LiveEscalationTrialResult>();
            var correctRoutesByCell = new Dictionary<string, int>(StringComparer.Ordinal);
            int structuredDecodeSuccesses = 0;
            int hostInvariantSuccesses = 0;
            int remoteL2TerminalSuccesses = 0;
            int trialNumber = 0;
            foreach (LiveEscalationLanguage language in languages)
            {
                composition.DialogueRouting.ResponseLanguage = language.ResponseLanguage;
                foreach (LiveEscalationScenario scenario in scenarios)
                {
                    string cellId = $"{language.Id}/{scenario.Id}";
                    correctRoutesByCell[cellId] = 0;
                    for (int repeat = 1; repeat <= thresholds.RepeatsPerCell; repeat++)
                    {
                        trialNumber++;
                        ActorId target = targetActors[(trialNumber - 1) % targetActors.Count];
                        SimTime now = new(trialNumber);
                        PlayerDialogueSubmissionResult? submission = null;
                        TownDialogueRoutingOutcome? outcome = null;
                        Exception? fault = null;
                        string text = scenario.EnglishText;
                        try
                        {
                            submission = await composition.PlayerDialogue.SubmitAsync(
                                text,
                                target,
                                now,
                                _lifetimeCancellation.Token);
                            if (submission.Outcome == PlayerDialogueSubmissionOutcome.Submitted)
                            {
                                outcome = await composition.DialogueRouting.InvokeAsync(
                                    submission.Session!,
                                    submission.Turn!,
                                    text,
                                    now,
                                    _lifetimeCancellation.Token,
                                    ObserveDialogueTrace);
                            }
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            fault = exception;
                        }

                        bool semanticInterpretationDecoded = submission is
                        {
                            Outcome: PlayerDialogueSubmissionOutcome.Submitted
                        } && submission.Interpretation.ActKind == scenario.ActKind;
                        bool localDecoded = semanticInterpretationDecoded
                            && outcome?.LocalAppraisalDecoded == true;
                        bool routeCorrect = localDecoded
                            && outcome?.Route == scenario.ExpectedRoute
                            && (scenario.ExpectedRoute == LivingTownCognitionRoute.L2
                                || submission?.Session?.Transcript.Count == 2);
                        bool hostTerminalStatePreserved = HasValidHostTerminalState(submission, outcome, fault);
                        bool remoteL2Terminal = outcome?.L2Outcome is TownL2DialogueSettled;
                        if (localDecoded) structuredDecodeSuccesses++;
                        if (routeCorrect) correctRoutesByCell[cellId]++;
                        if (hostTerminalStatePreserved) hostInvariantSuccesses++;
                        if (remoteL2Terminal) remoteL2TerminalSuccesses++;

                        var result = new LiveEscalationTrialResult(
                            trialNumber,
                            cellId,
                            repeat,
                            target.Value,
                            scenario.ExpectedRoute.ToString(),
                            outcome?.Route.ToString() ?? "Faulted",
                            localDecoded,
                            routeCorrect,
                            hostTerminalStatePreserved,
                            remoteL2Terminal,
                            outcome?.Evidence,
                            fault is null ? outcome?.Failure : fault.GetType().Name);
                        trials.Add(result);
                        GD.Print($"LIVE_ESCALATION_TRIAL {JsonSerializer.Serialize(result)}");
                    }
                }
            }

            bool cellsPassed = true;
            foreach (KeyValuePair<string, int> cell in correctRoutesByCell)
                if (cell.Value < thresholds.MinimumCorrectRoutesPerCell) cellsPassed = false;
            int expectedRemoteL2Terminals = scenarios.Count(value =>
                    value.ExpectedRoute == LivingTownCognitionRoute.L2)
                * languages.Length
                * thresholds.RepeatsPerCell;
            bool passed = trials.Count
                    == thresholds.ScenarioCount * thresholds.LanguageCount * thresholds.RepeatsPerCell
                && structuredDecodeSuccesses >= thresholds.MinimumStructuredDecodeSuccesses
                && hostInvariantSuccesses >= thresholds.MinimumHostInvariantSuccesses
                && remoteL2TerminalSuccesses >= thresholds.MinimumRemoteL2TerminalSuccesses
                && cellsPassed;
            var summary = new LiveEscalationDiagnosticSummary(
                passed,
                trials.Count,
                structuredDecodeSuccesses,
                hostInvariantSuccesses,
                remoteL2TerminalSuccesses,
                correctRoutesByCell);
            ProviderProfilesConfiguration profiles = RequireConfiguration().ProviderProfiles;
            DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow;
            var document = new LiveEscalationDiagnosticDocument(
                "live-l0-l1-l2-upgrade-diagnostic",
                thresholds.ReadinessId,
                false,
                capturedAtUtc,
                profiles.LocalReasoner.ProfileId,
                profiles.LocalReasoner.ModelId,
                profiles.RemotePlanner.ProfileId,
                profiles.RemotePlanner.ModelId,
                thresholds,
                trials,
                summary);
            string recordingDirectory = ProjectSettings.GlobalizePath("user://recordings");
            Directory.CreateDirectory(recordingDirectory);
            string artifactPath = Path.Combine(
                recordingDirectory,
                $"live-escalation-diagnostic-{capturedAtUtc:yyyyMMddTHHmmssfffZ}.json");
            File.WriteAllBytes(
                artifactPath,
                JsonSerializer.SerializeToUtf8Bytes(
                    document,
                    new JsonSerializerOptions { WriteIndented = true }));
            GD.Print(
                $"LIVE_ESCALATION_CHECK {(passed ? "PASS" : "FAIL")} "
                + $"structured={structuredDecodeSuccesses}/{trials.Count} "
                + $"host_terminal={hostInvariantSuccesses}/{trials.Count} "
                + $"remote_l2={remoteL2TerminalSuccesses}/{expectedRemoteL2Terminals} "
                + $"artifact={artifactPath}");
            GetTree().Quit(passed ? 0 : 2);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            GetTree().Quit(3);
        }
        catch (Exception exception)
        {
            GD.PushError($"Live escalation diagnostic failed: {exception.GetType().Name}: {exception.Message}");
            GetTree().Quit(3);
        }
    }

    private LiveEscalationGateThresholds LoadLiveEscalationGateThresholds()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(ProjectSettings.GlobalizePath(FormalReadinessPath)));
        JsonElement root = document.RootElement;
        if (root.GetProperty("formal_collection_allowed").GetBoolean())
            throw new InvalidDataException("The diagnostic must not run as formal collection.");
        JsonElement gate = root.GetProperty("shared_resolved_parameters")
            .GetProperty("live_escalation_gate");
        return new LiveEscalationGateThresholds(
            root.GetProperty("readiness_id").GetString()
                ?? throw new InvalidDataException("Formal readiness lacks readiness_id."),
            gate.GetProperty("scenario_count").GetInt32(),
            gate.GetProperty("language_count").GetInt32(),
            gate.GetProperty("repeats_per_cell").GetInt32(),
            gate.GetProperty("minimum_structured_decode_successes").GetInt32(),
            gate.GetProperty("minimum_correct_routes_per_cell").GetInt32(),
            gate.GetProperty("minimum_host_invariant_successes").GetInt32(),
            gate.GetProperty("minimum_remote_l2_terminal_successes").GetInt32());
    }

    private static bool HasValidHostTerminalState(
        PlayerDialogueSubmissionResult? submission,
        TownDialogueRoutingOutcome? outcome,
        Exception? fault)
    {
        ConversationSession? session = submission?.Session;
        if (fault is not null
            || submission?.Outcome != PlayerDialogueSubmissionOutcome.Submitted
            || session is null
            || outcome is null
            || session.PendingResponseOpportunities.Count != 0)
            return false;

        int turns = session.Transcript.Count;
        if (!outcome.LocalAppraisalDecoded)
            return outcome is
            {
                Route: LivingTownCognitionRoute.L1,
                Failure: not null,
                L2Outcome: null
            } && turns == 1;
        if (outcome.Route == LivingTownCognitionRoute.L2)
            return outcome.L2Outcome switch
            {
                TownL2DialogueSettled => turns == 2,
                null => false,
                _ => turns == 1
            };
        if (outcome.L2Outcome is not null) return false;
        if (outcome.Failure is not null) return turns == 1;
        return outcome.Route == LivingTownCognitionRoute.L1 && turns == 2;
    }

    private void ValidateWaterNavigation()
    {
        NavigationRegion2D navigationRegion = TownNavigationRegion
            ?? throw new InvalidOperationException("Living Town navigation region is not configured.");
        TownSpatialMap map = RequireWorld().Map;
        TownBottleneckMapConfiguration bridge = map.Bottlenecks.First(value =>
            StringComparer.OrdinalIgnoreCase.Equals(value.Kind, "Bridge"));
        float cellSize = map.CellSizeMeters;
        var bridgeBounds = new Rect2(
            bridge.Bounds.X * cellSize,
            bridge.Bounds.Y * cellSize,
            bridge.Bounds.Width * cellSize,
            bridge.Bounds.Height * cellSize);
        float crossingY = Math.Max(4.0f, bridgeBounds.Position.Y - 20.0f);
        Rid navigationMap = navigationRegion.GetNavigationMap();
        Vector2[] crossing = NavigationServer2D.MapGetPath(
            navigationMap,
            new Vector2(bridgeBounds.Position.X - 20.0f, crossingY),
            new Vector2(bridgeBounds.End.X + 20.0f, crossingY),
            true,
            1);
        Rect2 admittedBridge = bridgeBounds.Grow(8.0f);
        if (crossing.Length < 3 || !crossing.Any(admittedBridge.HasPoint))
            throw new InvalidOperationException(
                $"Town navigation crossed the river away from its bridge: {string.Join(" -> ", crossing.Select(point => point.ToString()))}");
    }

    private void RefreshUi(SimTime now, string state)
    {
        LivingTownObservability? observability = _observability;
        LivingTownRuntimeConfiguration? configuration = _world?.Runtime;
        LivingTownProductComposition? composition = _composition;
        if (observability is null || configuration is null || composition is null) return;
        bool activityLogVisible = ActivityLogPanel?.Visible == true;
        bool researchDebugVisible = ResearchDebugPanel?.Visible == true;
        bool npcDebugVisible = NpcDebugPanel?.Visible == true;
        bool showCognitionBadges = activityLogVisible || researchDebugVisible || npcDebugVisible;
        LivingTownDebugSnapshot? snapshot = StatusLabel is not null || showCognitionBadges
            ? observability.Snapshot(now)
            : null;
        if (StatusLabel is not null)
        {
            if (snapshot is null) throw new InvalidOperationException("Living Town status requires a debug snapshot.");
            var actorLines = new List<string>();
            foreach (LivingTownActorDebugProjection actor in snapshot.Actors)
            {
                string activity = actor.ActivityLabel ?? actor.ActivityKind.ToString();
                actorLines.Add($"{actor.DisplayName}: {activity} | {actor.CurrentPlace?.Value ?? "between places"}");
            }
            StatusLabel.Text = $"Living Town: {state}\nSimTime {now.Ticks} | NPCs {snapshot.Actors.Count} | Events {composition.PublicEvents.Gatherings.Count}\n{string.Join("\n", actorLines)}";
        }
        if (TraceLabel is not null && activityLogVisible && snapshot is not null)
            TraceLabel.Text = BuildActivityLog(snapshot, composition);
        if (ResearchDebugText is not null && researchDebugVisible && snapshot is not null)
            ResearchDebugText.Text = BuildResearchDebug(snapshot, composition, now);
        if (snapshot is not null && _scene is not null)
        {
            foreach (LivingTownActorDebugProjection actor in snapshot.Actors)
                if (_scene.SceneRegistry.TryResolve(actor.ActorId, out INpcEntityProjectionPort? port)
                    && port is not null)
                    port.ApplyCognitionPresentation(actor.LastRoute, showCognitionBadges);
        }
        if (DialogueTranscript is not null)
        {
            HashSet<ConversationSessionId> visibleSessions = _selectedDialogueNpc is ActorId selected
                ? composition.Conversations.Sessions
                    .Where(session => session.Participants.Contains(composition.Player.ActorId)
                        && session.Participants.Contains(selected))
                    .Select(session => session.SessionId)
                    .ToHashSet()
                : [];
            var lines = composition.DialogueSurface.Lines
                .Where(line => _selectedDialogueNpc is ActorId selectedNpc
                    && (line.DialogueNpc == selectedNpc
                        || line.SessionId is ConversationSessionId sessionId
                        && visibleSessions.Contains(sessionId)))
                .Select(FormatDialogueLine)
                .ToList();
            if (_selectedDialogueNpc is ActorId selectedNpc
                && _pendingDialogueNpc == selectedNpc
                && !string.IsNullOrWhiteSpace(_pendingDialoguePlayerText))
            {
                lines.Add($"{DisplayActor(composition.Player.ActorId)}: {_pendingDialoguePlayerText}");
                lines.Add($"{DisplayActor(selectedNpc)}: Thinking…");
            }
            if (_selectedDialogueNpc is ActorId target
                && _dialogueNotices.TryGetValue(target.Value, out string? notice))
                lines.Add($"[{T("System", "系统")}] {notice}");
            DialogueTranscript.Text = string.Join("\n", lines);
        }
        if (DialogueRouteLabel is not null)
        {
            string route = _selectedDialogueNpc is ActorId target
                ? _dialogueRouteDescriptions.GetValueOrDefault(
                    target.Value,
                    T("waiting for a semantic turn", "等待语义回合"))
                : T("select an NPC", "请选择 NPC");
            DialogueRouteLabel.Text = $"{T("Cognition route", "认知路由")}: {route}";
        }
        if (PlayerInventoryLabel is not null)
        {
            TownPlayerViewSnapshot player = composition.Player.GetViewSnapshot();
            PlayerInventoryLabel.Text =
                $"{T("Bag", "背包")}: {string.Join(", ", player.InventoryEntries)}\n" +
                $"{T("Hand", "手持")}: {player.EquippedHandItem ?? T("empty", "空")}";
        }
        if (TimeProgressLabel is not null || TimeProgressBar is not null)
            UpdateTimeProgress(now, configuration.TicksPerDay);
        UpdateOverlayVisibility();
    }

    private void UpdateOverlayVisibility()
    {
        bool anyDebug = ActivityLogPanel?.Visible == true
            || ResearchDebugPanel?.Visible == true
            || NpcDebugPanel?.Visible == true;
        if (PlayerInventoryPanel is not null)
            PlayerInventoryPanel.Visible = !anyDebug;
        if (!anyDebug && _scene is not null)
            foreach (ActorId actorId in _scene.SceneRegistry.ActorIds)
                if (_scene.SceneRegistry.TryResolve(actorId, out INpcEntityProjectionPort? port)
                    && port is not null)
                    port.ApplyCognitionPresentation(LivingTownCognitionRoute.None, false);
    }

    private string ResolveNpcDebug(string actorId, TownNpcDebugSection section) =>
        _observability?.GetActorDebugText(new ActorId(actorId), section)
        ?? $"NPC debug unavailable for {actorId}.";

    private void UpdateTimeProgress(SimTime now, long ticksPerDay)
    {
        long day = now.Ticks / ticksPerDay + 1;
        long tickOfDay = now.Ticks % ticksPerDay;
        double progress = (double)tickOfDay / ticksPerDay;
        string phase = progress < 0.60 ? "Daytime"
            : progress < 0.75 ? "Evening" : "Night";
        if (TimeProgressLabel is not null)
            TimeProgressLabel.Text = $"Day {day} · {phase} · {tickOfDay}/{ticksPerDay}";
        if (TimeProgressBar is not null)
        {
            TimeProgressBar.MaxValue = ticksPerDay;
            TimeProgressBar.Value = tickOfDay;
        }
    }

    private string BuildActivityLog(
        LivingTownDebugSnapshot snapshot,
        LivingTownProductComposition composition)
    {
        var entries = new List<ActivityLogLine>();
        foreach (LivingTownTraceEntry trace in snapshot.Trace)
        {
            ActivityLogSource source = GetTraceSource(trace);
            if (!IsActivityLogSourceVisible(source)) continue;
            if (_selectedLogActorId is not null
                && !StringComparer.Ordinal.Equals(_selectedLogActorId, trace.ActorId.Value)) continue;
            string evidence = Compact(trace.Evidence, 150);
            string activity = trace.Stage == "execution"
                ? $"{trace.Mode} {trace.Outcome}"
                : trace.Stage;
            entries.Add(new ActivityLogLine(
                trace.SimTime.Ticks,
                trace.Sequence,
                $"[t{trace.SimTime.Ticks:D4}] [{ActivityLogSourceText(source)}] [{trace.Route}] {DisplayActor(trace.ActorId)} "
                + $"{activity} — {evidence}"));
        }

        var acts = new Dictionary<SemanticDialogueActId, SemanticDialogueAct>();
        foreach (ConversationSession session in composition.Conversations.Sessions)
        foreach (SemanticDialogueTurn turn in session.Transcript)
            acts[turn.Act.ActId] = turn.Act;
        foreach (DialogueSurfaceLine line in composition.DialogueSurface.Lines)
        {
            if (!IsActivityLogSourceVisible(ActivityLogSource.LiveDialogue)) continue;
            if (line.ActId is not SemanticDialogueActId actId || !acts.TryGetValue(actId, out SemanticDialogueAct? act))
                continue;
            if (_selectedLogActorId is not null
                && !StringComparer.Ordinal.Equals(_selectedLogActorId, act.Speaker.Value)
                && !act.Recipients.Any(IsSelectedLogActor)) continue;
            string recipients = string.Join(", ", act.Recipients.Select(DisplayActor));
            entries.Add(new ActivityLogLine(
                line.OccurredAt.Ticks,
                1_000_000 + line.Sequence,
                $"[t{line.OccurredAt.Ticks:D4}] [{T("Live dialogue", "实时对话")}] "
                + $"[{DialogueRouteText(line.Route)}] {DisplayActor(act.Speaker)} → {recipients}: {Compact(line.Text, 150)}"));
        }
        entries.Sort(ActivityLogLineComparer.Instance);
        return entries.Count == 0
            ? T("No activity recorded for this NPC yet.", "该 NPC 暂无活动记录。")
            : string.Join("\n", entries
                .TakeLast(RequireConfiguration().VisibleTraceEntries)
                .Select(value => value.Text));
    }

    private string BuildResearchDebug(
        LivingTownDebugSnapshot snapshot,
        LivingTownProductComposition composition,
        SimTime now)
    {
        TownL2PolicyRuntime policy = composition.L2Decisions.Policy;
        IReadOnlyList<TownL2AdmissionCandidate> candidates =
            composition.Autonomy.PreviewResearchAdmissionCandidates(now);
        string agent = DescribeAdmissionOrder(TownL2PolicyRuntime.OrderForAdmission(
            candidates, TownRq1ActivationMode.AgentCentric));
        string events = DescribeAdmissionOrder(TownL2PolicyRuntime.OrderForAdmission(
            candidates, TownRq1ActivationMode.EventCentric));
        LivingTownActorDebugProjection? actor = _selectedResearchActorId is null
            ? null
            : snapshot.Actors.SingleOrDefault(value =>
                StringComparer.Ordinal.Equals(value.ActorId.Value, _selectedResearchActorId));
        string packet = composition.L2Decisions.LastPacketDebug is TownL2PacketDebugSnapshot last
            ? $"{T("Active packet", "当前数据包")}: {last.Mode}; "
                + $"{T("candidates", "候选记忆")} {last.CandidateCount}; "
                + $"{T("included", "纳入")} {last.IncludedCount}; "
                + $"{T("truncated", "截断")} {last.TruncatedCount}; "
                + $"{T("tokens", "词元")} {last.ConsumedTokens} / +{last.UnspentTokens}\n"
                + $"Verbatim [{last.Verbatim.Status}; {last.Verbatim.ConsumedTokens} tokens]\n"
                + $"{last.Verbatim.ModelVisiblePreview}\n\n"
                + $"Summary [{last.Summary.Status}; {last.Summary.ConsumedTokens} tokens]\n"
                + last.Summary.ModelVisiblePreview
            : T(
                "No L2 memory packet has been built in this run yet.",
                "本次运行尚未构建 L2 记忆数据包。");
        string pending = policy.PendingRq1 == policy.Active.Rq1Activation
            && policy.PendingRq2 == policy.Active.Rq2Memory
            ? "none"
            : $"RQ1={policy.PendingRq1}, RQ2={policy.PendingRq2} (activates on next settled tick)";
        string selected = actor is null
            ? T("No debug actor selected.", "未选择调试 NPC。")
            : $"{T("Selected", "已选择")}: {actor.DisplayName} | "
                + $"{T("current route", "当前路由")} {actor.LastRoute} | "
                + $"{T("place", "地点")} {actor.CurrentPlace?.Value ?? T("travelling", "移动中")}";
        string notice = _researchNotice is null
            ? string.Empty
            : $"\n{T("Last scenario", "最近场景")}: {_researchNotice}\n";
        int activeNeeds = composition.Autonomy.DecisionNeeds.Needs.Count(value =>
            value.State is DecisionNeedState.Queued or DecisionNeedState.InFlight);
        return T(
            "DEBUG PREVIEW — not formal RQ1/RQ2 evidence",
            "调试预览 — 不是正式 RQ1/RQ2 证据") + "\n\n"
            + $"{T("Active policy", "当前策略")}: RQ1={policy.Active.Rq1Activation}, "
            + $"RQ2={policy.Active.Rq2Memory}, t{policy.Active.ActivatedAtTick}; "
            + $"{T("mixed run", "混合运行")}={policy.MixedPolicyDemo}\n"
            + $"{T("Pending selection", "待生效选择")}: {pending}\n{selected}{notice}\n"
            + $"RQ1 {T("same candidate set", "同一候选集")} (B={TownAutonomyRuntime.DemoL2AdmissionBudget}, "
            + $"{T("active DecisionNeeds", "活跃决策需求")}={activeNeeds})\n"
            + $"AgentCentric: {agent}\n"
            + $"EventCentric: {events}\n\n"
            + $"RQ2 {T("same admitted memories: actual model-visible previews", "相同准入记忆：模型实际可见预览")}\n"
            + packet
            + "\n\n"
            + T(
                "Summary is marked Demo-only when no frozen artifact exists; formal collection still requires a frozen summary.",
                "没有冻结摘要时，Summary 会明确标注为仅 Demo；正式采集仍要求冻结摘要。")
            + "\n\n"
            + T(
                "Scenarios: clear need → deterministic L0; ambiguous need → live local L1; strategic conflict → Host DecisionNeed + live remote L2.",
                "场景：明确需求 → 确定性 L0；模糊需求 → 真实本地 L1；战略冲突 → Host DecisionNeed + 真实远端 L2。");
    }

    private string DescribeAdmissionOrder(IReadOnlyList<TownL2AdmissionCandidate> candidates)
    {
        string[] selected = candidates.Take(TownAutonomyRuntime.DemoL2AdmissionBudget)
            .Select(value => $"{DisplayActor(value.ActorId)} [{value.Problem.SubjectRef}]")
            .ToArray();
        return selected.Length == 0 ? "no currently eligible aspiration candidates" : string.Join(" → ", selected);
    }

    private static string DescribeDialogueRoute(TownDialogueRoutingOutcome outcome)
    {
        return outcome.Route switch
        {
            LivingTownCognitionRoute.L1 => outcome.Evidence,
            LivingTownCognitionRoute.L2 => $"{outcome.Evidence}; remote result: {outcome.Failure ?? "settled"}",
            _ => outcome.Evidence
        };
    }

    private bool IsSelectedLogActor(ActorId actorId) =>
        StringComparer.Ordinal.Equals(_selectedLogActorId, actorId.Value);

    private string FormatDialogueLine(DialogueSurfaceLine line) =>
        line.Speaker is null
            ? $"[{T("System", "系统")}] {line.Text}"
            : $"{DisplayActor(line.Speaker.Value)}: {line.Text}";

    private static ActivityLogSource GetTraceSource(LivingTownTraceEntry trace) => trace.Stage switch
    {
        "provider-call-started" or "provider-unavailable" or "provider-rejected" or "provider-fault" =>
            ActivityLogSource.Provider,
        "execution" or "committed" or "validated" => ActivityLogSource.Authority,
        _ => ActivityLogSource.Runtime
    };

    private bool IsActivityLogSourceVisible(ActivityLogSource source) =>
        _activityLogSource == ActivityLogSource.All || _activityLogSource == source;

    private string ActivityLogSourceText(ActivityLogSource source) => source switch
    {
        ActivityLogSource.Authority => T("Authority", "权威层"),
        ActivityLogSource.Runtime => T("Runtime", "运行时"),
        ActivityLogSource.Provider => T("Provider", "模型服务"),
        ActivityLogSource.LiveDialogue => T("Live dialogue", "实时对话"),
        _ => T("All", "全部")
    };

    private string DialogueRouteText(DialogueSurfaceRoute route) => route switch
    {
        DialogueSurfaceRoute.Player => T("Player", "玩家"),
        DialogueSurfaceRoute.L0 => "L0",
        DialogueSurfaceRoute.L1 => "L1",
        DialogueSurfaceRoute.L2 => "L2",
        _ => T("System", "系统")
    };

    private string DisplayActor(ActorId actorId) => DisplayActor(actorId.Value);

    private string DisplayActor(string actorId) =>
        _actorDisplayNames.GetValueOrDefault(actorId, actorId);

    private static string Compact(string text, int limit)
    {
        string value = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= limit ? value : $"{value[..limit]}…";
    }

    private void ConfigurePlayerScene(LivingTownProductComposition composition)
    {
        PlayerEntity player = PlayerEntity
            ?? throw new InvalidOperationException("TownMap requires a PlayerEntity in Living Town mode.");
        if (!player.IsNodeReady())
        {
            CallDeferred(nameof(ConfigurePlayerSceneAfterReady));
            return;
        }
        if (!StringComparer.Ordinal.Equals(player.ActorIdentity, composition.Player.ActorState.Identity.ActorId.Value))
            throw new InvalidDataException("Living Town PlayerEntity identity does not match the product player.");
        player.ConfigureTravelSpeedProfile(RequireWorld().Map.SpeedProfile);
        player.ResetConfirmedPosition(composition.Player.ConfirmedPosition);
        player.GameActionSpecProduced -= OnPlayerGameActionSpecProduced;
        player.GameActionSpecProduced += OnPlayerGameActionSpecProduced;
    }

    private void ConfigurePolicyControls(TownL2PolicyRuntime policy)
    {
        if (Rq1ActivationOption is not null)
        {
            Rq1ActivationOption.Clear();
            Rq1ActivationOption.AddItem(
                T("AgentCentric", "角色中心"), (int)TownRq1ActivationMode.AgentCentric);
            Rq1ActivationOption.AddItem(
                T("EventCentric", "事件中心"), (int)TownRq1ActivationMode.EventCentric);
            Rq1ActivationOption.Select((int)policy.PendingRq1);
        }
        if (Rq2MemoryOption is not null)
        {
            Rq2MemoryOption.Clear();
            Rq2MemoryOption.AddItem(T("Verbatim", "原文"), (int)TownRq2MemoryMode.Verbatim);
            Rq2MemoryOption.AddItem(T("Summary", "摘要"), (int)TownRq2MemoryMode.Summary);
            Rq2MemoryOption.Select((int)policy.PendingRq2);
        }
    }

    private void ApplyEnglishLanguage()
    {
        if (_composition is not null)
        {
            _composition.DialogueRouting.ResponseLanguage = "English";
            ConfigurePolicyControls(_composition.L2Decisions.Policy);
        }
        SetLabelText("UI/Hint", T(
            "Left-click: move / select NPC · Right-click: actions / debug · Mouse wheel: zoom",
            "左键：移动 / 选择 NPC · 右键：操作 / 调试 · 滚轮：缩放"));
        SetButtonText("UI/ResearchPanel/Controls/Save", T("Save Game", "保存"));
        SetButtonText("UI/ResearchPanel/Controls/Load", T("Load Save", "读取"));
        SetButtonText("UI/ResearchPanel/Controls/ActivityLogToggle", T("Activity Log", "活动日志"));
        SetButtonText("UI/ResearchPanel/Controls/ResearchDebugToggle", T("Research Debug", "研究调试"));
        SetButtonText("UI/DetailPanel/DebugLayout/Sections/Overview", T("Overview", "概览"));
        SetButtonText("UI/DetailPanel/DebugLayout/Sections/Memories", T("Memories", "记忆"));
        SetButtonText("UI/DetailPanel/DebugLayout/Sections/Knowledge", T("Knowledge", "知识"));
        SetButtonText("UI/DialoguePanel/DialogueLayout/InputRow/Send", T("Send Message", "发送"));
        SetLabelText("UI/ActivityLogPanel/ActivityLogLayout/Header/Title", T(
            "NPC activity / cognition log", "NPC 活动 / 认知日志"));
        SetButtonText("UI/ActivityLogPanel/ActivityLogLayout/SourceFilters/All", T("All", "全部"));
        SetButtonText("UI/ActivityLogPanel/ActivityLogLayout/SourceFilters/Authority", T("Authority", "权威层"));
        SetButtonText("UI/ActivityLogPanel/ActivityLogLayout/SourceFilters/Runtime", T("Runtime", "运行时"));
        SetButtonText("UI/ActivityLogPanel/ActivityLogLayout/SourceFilters/Provider", T("Provider", "模型服务"));
        SetButtonText("UI/ActivityLogPanel/ActivityLogLayout/SourceFilters/LiveDialogue", T("Live Dialogue", "实时对话"));
        SetLabelText("UI/ResearchDebugPanel/Layout/Header/Title", T(
            "Research route debugger — DEBUG PREVIEW", "研究路由调试器 — 调试预览"));
        SetButtonText("UI/ResearchDebugPanel/Layout/Triggers/L0", T("Clear need", "明确需求"));
        SetButtonText("UI/ResearchDebugPanel/Layout/Triggers/L1", T("Ambiguous need", "模糊需求"));
        SetButtonText("UI/ResearchDebugPanel/Layout/Triggers/L2", T("Strategic conflict", "战略冲突"));
        if (ActivityLogActorOption is not null && ActivityLogActorOption.ItemCount > 0)
            ActivityLogActorOption.SetItemText(0, T("All NPCs", "全部 NPC"));
        UpdateSelectedDialoguePresentation();
    }

    private void SetButtonText(string path, string text)
    {
        Button? control = GetNodeOrNull<Button>(new NodePath(path));
        if (control is not null) control.Text = text;
    }

    private void SetLabelText(string path, string text)
    {
        Label? control = GetNodeOrNull<Label>(new NodePath(path));
        if (control is not null) control.Text = text;
    }

    private void SelectActivityLogSource(ActivityLogSource source)
    {
        _activityLogSource = source;
        SetActivityLogSourceButton("All", source == ActivityLogSource.All);
        SetActivityLogSourceButton("Authority", source == ActivityLogSource.Authority);
        SetActivityLogSourceButton("Runtime", source == ActivityLogSource.Runtime);
        SetActivityLogSourceButton("Provider", source == ActivityLogSource.Provider);
        SetActivityLogSourceButton("LiveDialogue", source == ActivityLogSource.LiveDialogue);
        if (_composition is not null) RefreshUi(CurrentTime(), "activity log filtered");
    }

    private void SetActivityLogSourceButton(string name, bool pressed)
    {
        Button? button = GetNodeOrNull<Button>(
            new NodePath($"UI/ActivityLogPanel/ActivityLogLayout/SourceFilters/{name}"));
        button?.SetPressedNoSignal(pressed);
    }

    private static string T(string english, string _) => english;

    private void ConfigureActivityLogActors(TownPopulationManifest manifest)
    {
        _actorDisplayNames.Clear();
        _actorDisplayNames[RequireConfiguration().Player.ActorId] = RequireConfiguration().Player.Name;
        foreach (TownNpcConfiguration actor in manifest.Actors)
            _actorDisplayNames[actor.Identity.ActorId] = actor.Identity.Name;

        _logActorIds.Clear();
        ActivityLogActorOption?.Clear();
        ActivityLogActorOption?.AddItem("All NPCs", 0);
        ResearchDebugActorOption?.Clear();
        TownNpcConfiguration[] actors = manifest.Actors.ToArray();
        Array.Sort(actors, ActorDisplayNameComparer.Instance);
        foreach (TownNpcConfiguration actor in actors)
        {
            _logActorIds.Add(actor.Identity.ActorId);
            ActivityLogActorOption?.AddItem(actor.Identity.Name, _logActorIds.Count);
            ResearchDebugActorOption?.AddItem(actor.Identity.Name, _logActorIds.Count - 1);
        }
        ActivityLogActorOption?.Select(0);
        _selectedLogActorId = null;
        _selectedResearchActorId = _logActorIds.FirstOrDefault();
        if (_selectedResearchActorId is not null) ResearchDebugActorOption?.Select(0);
    }

    private void OnNpcSelected(string actorId) => SelectDialogueNpc(actorId, true);

    private void OnNpcDebugOpened(string actorId) => FocusDebugCameraOnNpc(actorId);

    private void FocusDebugCameraOnNpc(string actorId)
    {
        NpcEntity? npc = FindNpcEntity(actorId);
        Camera2D debugCamera = NpcDebugCamera
            ?? throw new InvalidOperationException("TownMap NPC debug camera is not configured.");
        Camera2D playerCamera = PlayerEntity?.Camera
            ?? throw new InvalidOperationException("TownMap player camera is not configured.");
        if (npc is null) return;

        _npcDebugCameraActorId = actorId;
        debugCamera.GlobalPosition = npc.GlobalPosition;
        debugCamera.Zoom = playerCamera.Zoom;
        playerCamera.Enabled = false;
        debugCamera.Enabled = true;
        debugCamera.MakeCurrent();
    }

    private void OnNpcDebugClosed()
    {
        if (ResearchDebugPanel?.Visible == true && _selectedResearchActorId is string actorId)
        {
            FocusDebugCameraOnNpc(actorId);
            return;
        }
        _npcDebugCameraActorId = null;
        if (NpcDebugCamera is not null) NpcDebugCamera.Enabled = false;
        if (PlayerEntity?.Camera is not Camera2D playerCamera) return;
        playerCamera.Enabled = true;
        playerCamera.MakeCurrent();
    }

    private void UpdateNpcDebugCamera()
    {
        if (_npcDebugCameraActorId is not string actorId || NpcDebugCamera is not Camera2D debugCamera) return;
        NpcEntity? npc = FindNpcEntity(actorId);
        if (npc is null) return;
        debugCamera.GlobalPosition = npc.GlobalPosition;
        if (PlayerEntity?.Camera is Camera2D playerCamera) debugCamera.Zoom = playerCamera.Zoom;
    }

    private NpcEntity? FindNpcEntity(string actorId)
    {
        if (NpcContainer is null) return null;
        foreach (Node child in NpcContainer.GetChildren())
            if (child is NpcEntity npc && StringComparer.Ordinal.Equals(npc.ActorIdentity, actorId)) return npc;
        return null;
    }

    private void SelectDialogueNpc(string actorId, bool focusInput)
    {
        TownNpcConfiguration? actor = RequireWorld().Population.Actors.SingleOrDefault(
            value => StringComparer.Ordinal.Equals(value.Identity.ActorId, actorId));
        if (actor is null) return;
        _selectedDialogueNpc = new ActorId(actorId);
        UpdateSelectedDialoguePresentation();
        if (DialogueInput is not null && focusInput) DialogueInput.GrabFocus();
        int logIndex = _logActorIds.IndexOf(actorId);
        if (logIndex >= 0 && ActivityLogActorOption is not null)
        {
            ActivityLogActorOption.Select(logIndex + 1);
            _selectedLogActorId = actorId;
        }
        if (logIndex >= 0 && ResearchDebugActorOption is not null)
        {
            ResearchDebugActorOption.Select(logIndex);
            _selectedResearchActorId = actorId;
        }
        RefreshUi(CurrentTime(), "running");
    }

    private void UpdateSelectedDialoguePresentation()
    {
        if (_selectedDialogueNpc is not ActorId selected || _world is null || _composition is null)
        {
            if (DialogueTargetLabel is not null)
                DialogueTargetLabel.Text = T("Dialogue — select an NPC", "对话 — 请选择 NPC");
            if (DialogueInput is not null)
                DialogueInput.PlaceholderText = T(
                    "Select an NPC, then type a message…",
                    "选择 NPC 后输入消息……");
            return;
        }
        TownNpcConfiguration actor = _world.Population.Actors.Single(value =>
            StringComparer.Ordinal.Equals(value.Identity.ActorId, selected.Value));
        bool merchant = RequireComposition().Gameplay.GetActionOffers(
            RequireComposition().Player.ActorId,
            selected.Value,
            CurrentTime()).Count > 0;
        if (DialogueTargetLabel is not null)
            DialogueTargetLabel.Text = merchant
                ? T(
                    $"Dialogue — {actor.Identity.Name} · Merchant (right-click to buy)",
                    $"对话 — {actor.Identity.Name} · 商人（右键购买）")
                : T($"Dialogue — {actor.Identity.Name}", $"对话 — {actor.Identity.Name}");
        if (DialogueInput is not null)
            DialogueInput.PlaceholderText = T(
                $"Message {actor.Identity.Name}…",
                $"给 {actor.Identity.Name} 发消息……");
    }

    public void ConfigurePlayerSceneAfterReady()
    {
        if (_composition is not null) ConfigurePlayerScene(_composition);
    }

    private void OnPlayerGameActionSpecProduced(PlayerGameActionSpecProducedFact fact)
    {
        ActorExecutionReceipt receipt = RequireComposition().CommitPlayerInteraction(
            fact.ActionSpec,
            fact.ConfirmedActorPosition,
            CurrentTime());
        RefreshUi(CurrentTime(), $"{receipt.Mode}: {receipt.Outcome}");
        if (receipt.Outcome != ActorExecutionOutcome.Completed)
            GD.PushWarning($"Living Town player {receipt.Mode} did not complete: {receipt.Evidence}");
    }

    private void OnPlayerActionSelected(PlayerInteractionSelection selection)
    {
        LivingTownProductComposition composition = RequireComposition();
        PlayerEntity player = PlayerEntity
            ?? throw new InvalidOperationException("Living Town PlayerEntity is not configured.");
        bool started = player.TryStartInteraction(selection, composition.Gameplay, composition.Gameplay);
        RefreshUi(CurrentTime(), started ? "Action: approaching" : "Action: approach rejected");
    }

    private void EnqueueInitialCognition(SimTime now)
    {
        _initialCognitionScheduled = true;
        LivingTownProductComposition composition = RequireComposition();
        ActorId[] l1Actors = composition.L1Decisions.FindInitialConflictActors(3).ToArray();
        foreach (ActorId actorId in l1Actors)
            QueueCognition(new InitialCognitionWork(
                InitialCognitionKind.L1,
                actorId,
                now,
                null,
                null));
        StartPendingCognition();
    }

    private void QueueCognition(InitialCognitionWork work)
    {
        _cognitionQueue.Enqueue(work);
        LivingTownCognitionRoute route = work.Kind == InitialCognitionKind.L2
            ? LivingTownCognitionRoute.L2
            : LivingTownCognitionRoute.L1;
        string evidence = work.Kind switch
        {
            InitialCognitionKind.L1 => "configuration-derived overlapping schedule decision queued",
            InitialCognitionKind.AutonomyL1 =>
                $"{work.AutonomyWork!.Domain} local decision queued with {work.AutonomyWork.Candidates.Count} candidates",
            _ => $"DecisionNeed {work.Need?.NeedId.Value ?? "untracked"} queued for {work.Problem!.TargetId}"
        };
        _observability?.ObserveCognition(work.QueuedAt, work.ActorId, route, "queued", evidence, true);
    }

    private void StartPendingCognition()
    {
        int limit = RequireConfiguration().ProviderQueue.MaxInFlight;
        while (_cognitionInFlight < limit)
        {
            if (_dialogueQueue.TryDequeue(out TownDialogueResponseNeed? dialogueNeed))
            {
                _cognitionInFlight++;
                RunNpcDialogueAsync(dialogueNeed);
                continue;
            }
            if (!_cognitionQueue.TryDequeue(out InitialCognitionWork? work)) break;
            _cognitionInFlight++;
            RunCognitionAsync(work);
        }
    }

    private async void RunNpcDialogueAsync(TownDialogueResponseNeed need)
    {
        try
        {
            TownDialogueRoutingOutcome outcome = await RequireComposition().DialogueRouting.InvokeAsync(
                need.Session,
                need.SourceTurn,
                need.ActorVisibleText,
                need.QueuedAt,
                _lifetimeCancellation.Token,
                ObserveDialogueTrace);
            ActorId responder = need.SourceTurn.Act.Recipients[0];
            _dialogueRouteDescriptions[responder.Value] = DescribeDialogueRoute(outcome);
            _observability?.ObserveCognition(
                CurrentTime(), responder, outcome.Route,
                "dialogue-routed", outcome.Evidence, outcome.Failure is null);
            _scene?.RefreshAll();
            RefreshUi(CurrentTime(), "NPC dialogue updated");
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _observability?.ObserveCognition(
                CurrentTime(),
                need.SourceTurn.Act.Recipients[0],
                LivingTownCognitionRoute.L2,
                "provider-fault",
                exception.GetType().Name,
                false);
            GD.PushWarning($"Living Town NPC dialogue failed: {exception.GetType().Name}.");
        }
        finally
        {
            _cognitionInFlight--;
            if (!_lifetimeCancellation.IsCancellationRequested) StartPendingCognition();
        }
    }

    private async void RunCognitionAsync(InitialCognitionWork work)
    {
        LivingTownCognitionRoute route = work.Kind == InitialCognitionKind.L2
            ? LivingTownCognitionRoute.L2
            : LivingTownCognitionRoute.L1;
        try
        {
            LivingTownObservability observability = _observability
                ?? throw new InvalidOperationException("Living Town diagnostics are not initialized.");
            observability.ObserveCognition(
                CurrentTime(),
                work.ActorId,
                route,
                "provider-call-started",
                route == LivingTownCognitionRoute.L1
                    ? "calling configured live local Provider"
                    : "calling configured live remote Provider",
                true);
            if (work.Kind == InitialCognitionKind.L1)
            {
                TownL1InvocationOutcome outcome = await RequireComposition().L1Decisions.InvokeAsync(
                    work.ActorId,
                    _lifetimeCancellation.Token);
                observability.ObserveCognition(
                    CurrentTime(),
                    work.ActorId,
                    route,
                    outcome.ModelSelected ? "model-selected" : "deferred-or-failed",
                    outcome.Evidence,
                    outcome.ModelSelected);
            }
            else if (work.Kind == InitialCognitionKind.AutonomyL1)
            {
                TownAutonomyL1Outcome outcome = await RequireComposition().Autonomy.InvokeLocalDecisionAsync(
                    work.AutonomyWork!,
                    CurrentTime(),
                    _lifetimeCancellation.Token,
                    CurrentTime);
                if (outcome.Receipt is not null)
                {
                    observability.Observe(outcome.Receipt);
                    if (outcome.Receipt.Outcome == ActorExecutionOutcome.Completed)
                        RequireComposition().ProjectAutonomySettlement(outcome.Receipt);
                }
                observability.ObserveCognition(
                    CurrentTime(),
                    work.ActorId,
                    LivingTownCognitionRoute.L1,
                    outcome.Accepted ? "local-selected-or-escalated" : "deferred-or-failed",
                    outcome.Evidence,
                    outcome.Accepted);
            }
            else
            {
                work.Need?.BeginInFlightAttempt();
                TownL2InvocationOutcome outcome = await RequireComposition().L2Decisions.InvokeAsync(
                    work.ActorId,
                    work.Problem!,
                    CurrentTime(),
                    _lifetimeCancellation.Token,
                    CurrentTime);
                if (outcome is TownL2InvocationTravelRequired travel)
                    RequireComposition().Autonomy.QueueL2ActionIntent(
                        work.AutonomyDecisionWork!,
                        travel.Offer,
                        travel.CatalogueTargetId,
                        travel.TravelPlaceId);
                ObserveL2Outcome(observability, work.ActorId, outcome);
                SettleDecisionNeed(work, outcome);
            }
            _scene?.RefreshAll();
            RefreshUi(CurrentTime(), "cognition updated");
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (work.Need?.State is DecisionNeedState.Created or DecisionNeedState.Queued or DecisionNeedState.InFlight)
            {
                work.Need.Abort();
                RequireComposition().Autonomy.CompleteDecisionWork(work.Need);
            }
            _observability?.ObserveCognition(
                CurrentTime(),
                work.ActorId,
                route,
                "provider-fault",
                exception.GetType().Name,
                false);
            GD.PushWarning($"Living Town {route} call failed: {exception.GetType().Name}.");
        }
        finally
        {
            _cognitionInFlight--;
            if (!_lifetimeCancellation.IsCancellationRequested) StartPendingCognition();
        }
    }

    private void SettleDecisionNeed(InitialCognitionWork work, TownL2InvocationOutcome outcome)
    {
        DecisionNeed? need = work.Need;
        if (need is null) return;
        if (outcome is TownL2InvocationSettled settled)
        {
            need.Resolve(
                CurrentTime(),
                DecisionNeedResolutionKind.ExecuteAction,
                new DecisionNeedExecutionResultReference(settled.Receipt.ExecutionId));
            RequireComposition().Autonomy.CompleteDecisionWork(need);
            return;
        }
        if (outcome is TownL2InvocationTravelRequired) return;
        need.Abort();
        RequireComposition().Autonomy.CompleteDecisionWork(need);
    }

    private void ObserveL2Outcome(
        LivingTownObservability observability,
        ActorId actorId,
        TownL2InvocationOutcome outcome)
    {
        switch (outcome)
        {
            case TownL2InvocationSettled settled:
                RequireComposition().ProjectCognitionSettlement(settled.Receipt);
                observability.Observe(settled.Receipt);
                observability.ObserveCognition(
                    CurrentTime(), actorId, LivingTownCognitionRoute.L2, "settled",
                    $"remote proposal settled as {settled.Receipt.Evidence}", true);
                break;
            case TownL2InvocationTravelRequired travel:
                observability.ObserveCognition(
                    CurrentTime(), actorId, LivingTownCognitionRoute.L2, "travelling",
                    $"remote proposal selected {travel.Offer.EntryId}; travelling to {travel.TravelPlaceId}", true);
                break;
            case TownL2ProviderUnavailable unavailable:
                observability.ObserveCognition(
                    CurrentTime(), actorId, LivingTownCognitionRoute.L2, "provider-unavailable",
                    $"{unavailable.Mode}: {unavailable.Reason}", false);
                break;
            case TownL2ProviderRejected rejected:
                observability.ObserveCognition(
                    CurrentTime(), actorId, LivingTownCognitionRoute.L2, "provider-rejected",
                    rejected.Decision.GetType().Name, false);
                break;
            case TownL2InvocationNotReady notReady:
                observability.ObserveCognition(
                    CurrentTime(), actorId, LivingTownCognitionRoute.L2, "preparation-unavailable",
                    DescribeL2Preparation(notReady.Preparation), false);
                break;
        }
    }

    private void ObserveDialogueTrace(
        ActorId actorId,
        string stage,
        string evidence,
        bool accepted)
    {
        _observability?.ObserveCognition(
            CurrentTime(),
            actorId,
            LivingTownCognitionRoute.L2,
            stage,
            evidence,
            accepted);
    }

    private static string? DescribeDialogueFailure(TownL2DialogueInvocationOutcome outcome) => outcome switch
    {
        TownL2DialogueSettled => null,
        TownL2DialogueNotReady notReady => notReady.Reason,
        TownL2DialogueProviderUnavailable unavailable =>
            $"L2 dialogue unavailable: {unavailable.Mode}/{unavailable.Reason}",
        TownL2DialogueProviderRejected rejected =>
            $"L2 dialogue rejected: {rejected.Decision.GetType().Name}",
        _ => outcome.GetType().Name
    };

    private static string DescribeL2Preparation(TownL2RequestPreparationOutcome preparation) => preparation switch
    {
        TownL2PreparationUnavailable unavailable => unavailable.Reason,
        TownL2SummaryPending pending => $"summary pending for {pending.CandidateSetId.Value}",
        TownL2RequestReady => "request ready",
        _ => preparation.GetType().Name
    };

    private IPlayerUtteranceInterpreter CreateDialogueInterpreter(DialogueSurfaceProfile profile)
    {
        return ProductModelClientComposition.CreateDialogueInterpreter(
            _httpClient ?? throw new InvalidOperationException("Living Town HTTP client is not initialized."),
            RequireConfiguration().ProviderProfiles,
            RequireConfiguration().ProviderQueue,
            profile.UnavailableMessage);
    }

    private SimTime CurrentTime() => new(Math.Max(0, _nextTick - 1));

    private LivingTownRuntimeConfiguration RequireConfiguration() =>
        RequireWorld().Runtime;

    private TownWorldConfiguration RequireWorld() =>
        _world ?? throw new InvalidOperationException("Town world configuration is not loaded.");

    private LivingTownProductComposition RequireComposition() =>
        _composition ?? throw new InvalidOperationException("Living Town product composition is not initialized.");

    private sealed record LiveEscalationScenario(
        string Id,
        LivingTownCognitionRoute ExpectedRoute,
        SemanticDialogueActKind ActKind,
        string EnglishText);

    private sealed record LiveEscalationLanguage(
        string Id,
        string ResponseLanguage);

    private sealed record LiveEscalationGateThresholds(
        string ReadinessId,
        int ScenarioCount,
        int LanguageCount,
        int RepeatsPerCell,
        int MinimumStructuredDecodeSuccesses,
        int MinimumCorrectRoutesPerCell,
        int MinimumHostInvariantSuccesses,
        int MinimumRemoteL2TerminalSuccesses);

    private sealed record LiveEscalationTrialResult(
        int Trial,
        string CellId,
        int Repeat,
        string ActorId,
        string ExpectedRoute,
        string ObservedRoute,
        bool LocalStructuredDecode,
        bool RouteCorrect,
        bool HostTerminalStatePreserved,
        bool RemoteL2Terminal,
        string? Evidence,
        string? Failure);

    private sealed record LiveEscalationDiagnosticSummary(
        bool Passed,
        int TrialCount,
        int StructuredDecodeSuccesses,
        int HostInvariantSuccesses,
        int RemoteL2TerminalSuccesses,
        IReadOnlyDictionary<string, int> CorrectRoutesByCell);

    private sealed record LiveEscalationDiagnosticDocument(
        string Kind,
        string ReadinessId,
        bool FormalCollection,
        DateTimeOffset CapturedAtUtc,
        string LocalProfileId,
        string LocalConfiguredModelId,
        string RemoteProfileId,
        string RemoteConfiguredModelId,
        LiveEscalationGateThresholds Thresholds,
        IReadOnlyList<LiveEscalationTrialResult> Trials,
        LiveEscalationDiagnosticSummary Summary);

    private sealed record ActivityLogLine(long Tick, long Order, string Text);

    private enum ActivityLogSource
    {
        All,
        Authority,
        Runtime,
        Provider,
        LiveDialogue
    }

    private enum InitialCognitionKind
    {
        L1,
        AutonomyL1,
        L2
    }

    private sealed record InitialCognitionWork(
        InitialCognitionKind Kind,
        ActorId ActorId,
        SimTime QueuedAt,
        TownL2DecisionProblem? Problem,
        DecisionNeed? Need,
        TownAutonomyLocalDecisionWork? AutonomyWork = null,
        TownAutonomyDecisionWork? AutonomyDecisionWork = null);

    private sealed class ActivityLogLineComparer : IComparer<ActivityLogLine>
    {
        public static ActivityLogLineComparer Instance { get; } = new();

        public int Compare(ActivityLogLine? left, ActivityLogLine? right)
        {
            if (left is null) return right is null ? 0 : -1;
            if (right is null) return 1;
            int tick = left.Tick.CompareTo(right.Tick);
            return tick != 0 ? tick : left.Order.CompareTo(right.Order);
        }
    }

    private sealed class ActorIdValueComparer : IComparer<ActorId>
    {
        public static ActorIdValueComparer Instance { get; } = new();

        public int Compare(ActorId left, ActorId right) =>
            StringComparer.Ordinal.Compare(left.Value, right.Value);
    }

    private sealed class ActorDisplayNameComparer : IComparer<TownNpcConfiguration>
    {
        public static ActorDisplayNameComparer Instance { get; } = new();

        public int Compare(TownNpcConfiguration? left, TownNpcConfiguration? right) =>
            StringComparer.Ordinal.Compare(left?.Identity.Name, right?.Identity.Name);
    }
}
