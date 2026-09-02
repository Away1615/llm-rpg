using Alice.Activities;
using Alice.Actors;
using Alice.Cognition;
using Alice.Commitments;
using Alice.Interaction;
using Alice.Memory;
using Alice.ModelRuntime;
using Alice.Navigation;
using Alice.ProductRuntime;
using Alice.Social;

namespace Alice.LivingTown;

public sealed record TownDialogueResponseNeed(
    ConversationSession Session,
    SemanticDialogueTurn SourceTurn,
    string ActorVisibleText,
    SimTime QueuedAt);

/// <summary>Single Phase 13 product root for world, population, Player and dialogue ownership.</summary>
public sealed class LivingTownProductComposition : IDisposable
{
    private readonly Queue<TownDialogueResponseNeed> _pendingDialogueResponses = [];
    private LivingTownProductComposition(
        TownWorldConfiguration world,
        RegionSocialGameplayRuntime gameplay,
        TownHistoryRuntime history,
        TownSocialRuntime social,
        TownSocialAppraisalRuntime socialAppraisals,
        TownL1DecisionRuntime l1Decisions,
        TownL2DecisionContextRuntime l2DecisionContext,
        TownL2DecisionRuntime l2Decisions,
        TownL2DialogueRuntime l2Dialogue,
        TownDialogueRoutingRuntime dialogueRouting,
        TownAutonomyRuntime autonomy,
        LivingTownPopulationRuntime population,
        TownPlayerStateOwner player,
        SemanticDialogueMemoryHost dialogueMemory,
        ConversationRuntime conversations,
        DialogueSurfaceLedger dialogueSurface,
        PlayerNaturalLanguageDialogueRuntime playerDialogue)
    {
        World = world;
        Gameplay = gameplay;
        History = history;
        Social = social;
        SocialAppraisals = socialAppraisals;
        L1Decisions = l1Decisions;
        L2DecisionContext = l2DecisionContext;
        L2Decisions = l2Decisions;
        L2Dialogue = l2Dialogue;
        DialogueRouting = dialogueRouting;
        Autonomy = autonomy;
        Population = population;
        Player = player;
        DialogueMemory = dialogueMemory;
        Conversations = conversations;
        DialogueSurface = dialogueSurface;
        PlayerDialogue = playerDialogue;
    }

    public TownWorldConfiguration World { get; }
    public RegionSocialGameplayRuntime Gameplay { get; }
    public TownHistoryRuntime History { get; }
    public TownSocialRuntime Social { get; }
    public TownSocialAppraisalRuntime SocialAppraisals { get; }
    public TownL1DecisionRuntime L1Decisions { get; }
    public TownL2DecisionContextRuntime L2DecisionContext { get; }
    public TownL2DecisionRuntime L2Decisions { get; }
    public TownL2DialogueRuntime L2Dialogue { get; }
    public TownDialogueRoutingRuntime DialogueRouting { get; }
    public TownAutonomyRuntime Autonomy { get; }
    public LivingTownPopulationRuntime Population { get; }
    public TownPlayerStateOwner Player { get; }
    public SemanticDialogueMemoryHost DialogueMemory { get; }
    public ConversationRuntime Conversations { get; }
    public DialogueSurfaceLedger DialogueSurface { get; }
    public PlayerNaturalLanguageDialogueRuntime PlayerDialogue { get; }
    public LivingTownPublicEventRuntime PublicEvents => Population.PublicEvents;
    public DurableStateRegistry DurableStateRegistry => World.DurableStateRegistry;
    public SimTime? LastSettledAt { get; private set; }
    public bool HasInFlightProviderWork =>
        Autonomy.HasInFlightWork || L2Decisions.HasInFlightWork || L2Dialogue.HasInFlightWork;
    public bool HasPendingDialogueResponse => _pendingDialogueResponses.Count != 0;

    public bool TryDequeueDialogueResponse(out TownDialogueResponseNeed? need) =>
        _pendingDialogueResponses.TryDequeue(out need);

    public bool TryDequeueAutonomyDecision(out TownAutonomyDecisionWork? work) =>
        Autonomy.TryDequeueDecisionWork(out work);

    public bool TryDequeueAutonomyLocalDecision(out TownAutonomyLocalDecisionWork? work) =>
        Autonomy.TryDequeueLocalDecisionWork(out work);

    internal void RebuildPendingDialogueResponses(SimTime queuedAt)
    {
        _pendingDialogueResponses.Clear();
        foreach (ConversationSession session in Conversations.Sessions)
        foreach (DialogueResponseOpportunity opportunity in session.PendingResponseOpportunities)
        {
            if (opportunity.OriginalSpeaker == Player.ActorId) continue;
            SemanticDialogueTurn source = session.Transcript.Single(value =>
                value.Act.ActId == opportunity.SourceActId);
            string speakerName = Population.GetNpc(source.Act.Speaker).State.Profile.DisplayName;
            string recipientName = Population.GetNpc(opportunity.Recipient).State.Profile.DisplayName;
            _pendingDialogueResponses.Enqueue(new TownDialogueResponseNeed(
                session,
                source,
                $"{speakerName} raised a current town concern with {recipientName}.",
                queuedAt));
        }
    }

    public static LivingTownProductComposition Create(
        TownWorldConfiguration world,
        DialogueSurfaceProfile dialogueProfile,
        IPlayerUtteranceInterpreter interpreter)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Create(
            world,
            dialogueProfile,
            interpreter,
            new FixedUnavailableModelClient<TownL1DecisionResponse>(
                ModelClientExecutionMode.LiveLocal,
                ModelClientUnavailableReason.UnsupportedRequestType),
            new FixedUnavailableModelClient<RemotePlannerResponse>(
                ModelClientExecutionMode.LiveRemote,
                ModelClientUnavailableReason.MissingCredential));
    }

    public static LivingTownProductComposition Create(
        TownWorldConfiguration world,
        DialogueSurfaceProfile dialogueProfile,
        IPlayerUtteranceInterpreter interpreter,
        IModelClient<RemotePlannerResponse> l2Client)
    {
        return Create(
            world,
            dialogueProfile,
            interpreter,
            new FixedUnavailableModelClient<TownL1DecisionResponse>(
                ModelClientExecutionMode.LiveLocal,
                ModelClientUnavailableReason.UnsupportedRequestType),
            l2Client);
    }

    public static LivingTownProductComposition Create(
        TownWorldConfiguration world,
        DialogueSurfaceProfile dialogueProfile,
        IPlayerUtteranceInterpreter interpreter,
        IModelClient<TownL1DecisionResponse> l1Client,
        IModelClient<RemotePlannerResponse> l2Client)
    {
        return Create(
            world,
            dialogueProfile,
            interpreter,
            l1Client,
            new FixedUnavailableModelClient<TownL1DialogueRouteResponse>(
                ModelClientExecutionMode.LiveLocal,
                ModelClientUnavailableReason.UnsupportedRequestType),
            l2Client);
    }

    public static LivingTownProductComposition Create(
        TownWorldConfiguration world,
        DialogueSurfaceProfile dialogueProfile,
        IPlayerUtteranceInterpreter interpreter,
        IModelClient<TownL1DecisionResponse> l1Client,
        IModelClient<TownL1DialogueRouteResponse> dialogueRouteClient,
        IModelClient<RemotePlannerResponse> l2Client)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(dialogueProfile);
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentNullException.ThrowIfNull(l1Client);
        ArgumentNullException.ThrowIfNull(dialogueRouteClient);
        ArgumentNullException.ThrowIfNull(l2Client);
        LivingTownRuntimeConfiguration configuration = world.Runtime;
        var canonicalEvents = new CanonicalEventStore();
        var dialogueMemory = new SemanticDialogueMemoryHost(canonicalEvents);
        var memoryOwner = new ConversationTurnMemoryAdmissionOwner(dialogueMemory);
        var conversations = new ConversationRuntime(memoryOwner);
        var surface = new DialogueSurfaceLedger();
        RegionSocialGameplayRuntime gameplay = RegionSocialGameplayRuntime.Create(
            world.Gameplay, configuration.Player, world.Population, configuration.TicksPerDay);
        var player = new TownPlayerStateOwner(configuration.Player, gameplay);
        TownHistoryRuntime history = TownHistoryRuntime.Create(world.History, world.Map, world.Population, canonicalEvents);
        TownSocialRuntime social = TownSocialRuntime.Create(
            world.Social,
            world.Population,
            new ActorId(configuration.Player.ActorId),
            history,
            gameplay);
        LivingTownPopulationRuntime population = new NpcRuntimeFactory().Create(
            world.Population, configuration, gameplay, history, world.Map);
        TownL2DecisionContextRuntime l2DecisionContext = TownL2DecisionContextRuntime.Create(
            world, population, history, social, gameplay);
        var l1Decisions = new TownL1DecisionRuntime(population, l1Client);
        var l2Decisions = new TownL2DecisionRuntime(
            l2DecisionContext,
            population,
            gameplay,
            l2Client,
            configuration.ProviderQueue);
        var socialAppraisals = new TownSocialAppraisalRuntime(social, population);
        var decisionNeeds = new DecisionNeedStore();
        var l2Dialogue = new TownL2DialogueRuntime(
            l2DecisionContext,
            l2Decisions,
            l2Client,
            conversations,
            surface,
            history,
            socialAppraisals,
            population,
            player);
        var dialogueRouting = new TownDialogueRoutingRuntime(
            population, dialogueRouteClient, l2Dialogue, decisionNeeds);
        var autonomy = new TownAutonomyRuntime(
            population,
            gameplay,
            l2Decisions.Policy,
            l1Client,
            configuration.TicksPerDay,
            decisionNeeds);
        try
        {
            var playerDialogue = new PlayerNaturalLanguageDialogueRuntime(
                conversations,
                surface,
                dialogueProfile,
                interpreter,
                new ActorId(configuration.Dialogue.PlayerActorId),
                new ActorId(configuration.Dialogue.NpcActorId),
                new DialogueTopicRef(configuration.Dialogue.DefaultTopicRef));
            return new LivingTownProductComposition(
                world,
                gameplay,
                history,
                social,
                socialAppraisals,
                l1Decisions,
                l2DecisionContext,
                l2Decisions,
                l2Dialogue,
                dialogueRouting,
                autonomy,
                population,
                player,
                dialogueMemory,
                conversations,
                surface,
                playerDialogue);
        }
        catch
        {
            population.Dispose();
            throw;
        }
    }

    public ActorExecutionBatch Advance(
        SimTime now,
        DateTimeOffset wallTime,
        CancellationToken cancellationToken)
    {
        Gameplay.AdvanceWorld(now);
        TownBodyRuleCommitReceipt? playerBody = Gameplay.CommitNeeds(Player.ActorId.Value, now);
        if (playerBody is not null) Player.ApplyVitals(playerBody.Current);
        ActorExecutionBatch scheduled = Population.Advance(now, wallTime, cancellationToken);
        IReadOnlyList<ActorExecutionReceipt> autonomous = Autonomy.Advance(now);
        IReadOnlyList<ActorExecutionReceipt> obligations = AdvanceAutonomousObligations(now);
        var batch = new ActorExecutionBatch(now, scheduled.Receipts.Concat(autonomous).Concat(obligations));
        foreach (ActorExecutionReceipt receipt in batch.Receipts)
            if (receipt.Outcome == ActorExecutionOutcome.Completed
                && receipt.Mode is ActorExecutionMode.Interact or ActorExecutionMode.Communicate)
                if (!receipt.ExecutionId.Value.StartsWith("commitment/", StringComparison.Ordinal))
                _ = History.ProjectAcceptedAction(
                    receipt,
                    ResolveActorPosition(receipt.ActorId),
                    "outdoor",
                    CreateHistoryPresences());
        _ = Social.Advance(now, CreateSocialContext);
        AdvanceNpcConversation(now);
        PlayerDialogue.SynchronizeSurface(now);
        _ = L2Decisions.Policy.SettleTick(now);
        LastSettledAt = now;
        return batch;
    }

    public ActorExecutionReceipt CommitPlayerInteraction(
        GameActionSpec action,
        WorldPosition confirmedActorPosition,
        SimTime now)
    {
        Player.ConfirmPosition(confirmedActorPosition);
        ActorExecutionRequest request = Player.CreateInteractRequest(action, now);
        ActorExecutionReceipt receipt = ActorExecutionPipeline.Dispatch(request, Player);
        if (receipt.Outcome == ActorExecutionOutcome.Completed)
            _ = History.ProjectAcceptedAction(receipt, confirmedActorPosition, "outdoor", CreateHistoryPresences());
        return receipt;
    }

    public void ProjectCognitionSettlement(ActorExecutionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.CognitionRoute != AutonomousNpcCognitionRoute.L2
            || receipt.Outcome != ActorExecutionOutcome.Completed)
            throw new ArgumentException("Only a completed L2 receipt may enter the cognition settlement bridge.", nameof(receipt));
        _ = History.ProjectAcceptedAction(
            receipt,
            ResolveActorPosition(receipt.ActorId),
            "outdoor",
            CreateHistoryPresences());
    }

    public void ProjectAutonomySettlement(ActorExecutionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.CognitionRoute is not (AutonomousNpcCognitionRoute.L0 or AutonomousNpcCognitionRoute.L1)
            || receipt.Outcome != ActorExecutionOutcome.Completed)
            throw new ArgumentException("Only a completed local-autonomy receipt may enter this bridge.", nameof(receipt));
        _ = History.ProjectAcceptedAction(
            receipt,
            ResolveActorPosition(receipt.ActorId),
            "outdoor",
            CreateHistoryPresences());
    }

    public ActorExecutionReceipt EquipPlayer(SimTime now) =>
        ActorExecutionPipeline.Dispatch(Player.CreateEquipmentRequest(true, now), Player);

    public ActorExecutionReceipt UnequipPlayer(SimTime now) =>
        ActorExecutionPipeline.Dispatch(Player.CreateEquipmentRequest(false, now), Player);

    private WorldPosition ResolveActorPosition(ActorId actorId) =>
        actorId == Player.ActorId ? Player.ConfirmedPosition : Population.GetNpc(actorId).State.Position;

    private TownHistoryActorPresence[] CreateHistoryPresences()
    {
        var result = new List<TownHistoryActorPresence>
        {
            new(Player.ActorId, Player.ConfirmedPosition, "outdoor", null)
        };
        foreach (LivingTownNpcRuntime npc in Population.Npcs)
            result.Add(new TownHistoryActorPresence(npc.ActorId, npc.State.Position, "outdoor", npc.State.Memory));
        return result.ToArray();
    }

    private TownSocialEventContext CreateSocialContext(ActorId actorId)
    {
        WorldPosition position = ResolveActorPosition(actorId);
        string locationId = actorId == Player.ActorId
            ? World.Map.Settlements.OrderBy(value => DistanceSquared(World.Map.ToWorld(value.CenterCell), position))
                .First().SettlementId
            : Population.GetNpc(actorId).State.Profile.SettlementId;
        return new TownSocialEventContext(locationId, position, "outdoor", CreateHistoryPresences());
    }

    private IReadOnlyList<ActorExecutionReceipt> AdvanceAutonomousObligations(SimTime now)
    {
        var receipts = new List<ActorExecutionReceipt>();
        long tickOfDay = now.Ticks % World.Runtime.TicksPerDay;
        if (tickOfDay != World.Runtime.TicksPerDay * 3 / 4) return receipts;
        long day = now.Ticks / World.Runtime.TicksPerDay;
        foreach (Commitment commitment in Social.Commitments.Where(value =>
                     value.Status is CommitmentStatus.Active or CommitmentStatus.Overdue
                     && Population.Npcs.Any(npc => npc.ActorId == value.Debtor)))
        {
            TownObligationTransitionResult result = Social.FulfillObligation(
                commitment.CommitmentId,
                $"history/autonomy/obligation/{commitment.CommitmentId.Value}/day-{day}",
                now,
                CreateSocialContext(commitment.Debtor),
                AutonomousNpcCognitionRoute.L0);
            if (result.TransferReceipt is not null) receipts.Add(result.TransferReceipt);
            if (!result.Accepted)
                receipts.AddRange(Autonomy.DiscoverCommitmentPressure(
                    commitment, result.Reason ?? "transfer unavailable", now));
        }
        return receipts;
    }

    private void AdvanceNpcConversation(SimTime now)
    {
        long interval = Math.Max(1, World.Runtime.TicksPerDay / 4);
        if (now.Ticks % interval != 0) return;

        double range = World.Map.CellSizeMeters * 2.0;
        double rangeSquared = range * range;
        var nearbyPairs = new List<(LivingTownNpcRuntime Speaker, LivingTownNpcRuntime Recipient)>();
        LivingTownNpcRuntime[] actors = Population.Npcs
            .OrderBy(value => value.ActorId.Value, StringComparer.Ordinal)
            .ToArray();
        for (int left = 0; left < actors.Length; left++)
        for (int right = left + 1; right < actors.Length; right++)
        {
            if (CanConverse(actors[left], actors[right], rangeSquared))
                nearbyPairs.Add((actors[left], actors[right]));
        }
        if (nearbyPairs.Count == 0) return;

        int pairIndex = (int)((now.Ticks / interval) % nearbyPairs.Count);
        (LivingTownNpcRuntime speaker, LivingTownNpcRuntime recipient) = nearbyPairs[pairIndex];
        var sessionId = new ConversationSessionId(
            $"npc-chat-{now.Ticks}-{speaker.ActorId.Value}-{recipient.ActorId.Value}");
        var topic = new DialogueTopicRef(World.Runtime.Dialogue.DefaultTopicRef);
        var opening = new SemanticDialogueAct(
            new SemanticDialogueActId($"{sessionId.Value}-opening"),
            SemanticDialogueActKind.CasualComment,
            speaker.ActorId,
            [recipient.ActorId],
            topic,
            [],
            null,
            DialogueResponseExpectation.Required);
        ConversationOpenResult opened = Conversations.Open(
            sessionId,
            [speaker.ActorId, recipient.ActorId],
            opening,
            now);
        _pendingDialogueResponses.Enqueue(new TownDialogueResponseNeed(
            opened.Session,
            opened.InitialTurn,
            $"{speaker.State.Profile.DisplayName} raised a current town concern with {recipient.State.Profile.DisplayName}.",
            now));
    }

    private bool CanConverse(
        LivingTownNpcRuntime left,
        LivingTownNpcRuntime right,
        double outdoorRangeSquared)
    {
        if (DistanceSquared(left.State.Position, right.State.Position) <= outdoorRangeSquared) return true;
        LivingTownPlaceRef? residence = left.State.Profile.Residence;
        if (residence is null || right.State.Profile.Residence != residence) return false;
        TownBuildingMapConfiguration? building = World.Map.Buildings.SingleOrDefault(value =>
            StringComparer.Ordinal.Equals(value.BuildingId, residence.Value));
        return building is not null
            && IsInside(building.Bounds, left.State.Position)
            && IsInside(building.Bounds, right.State.Position);
    }

    private bool IsInside(TownMapRect bounds, WorldPosition position)
    {
        double cellSize = World.Map.CellSizeMeters;
        return position.X >= bounds.X * cellSize
            && position.Y >= bounds.Y * cellSize
            && position.X <= (bounds.X + bounds.Width) * cellSize
            && position.Y <= (bounds.Y + bounds.Height) * cellSize;
    }

    private static double DistanceSquared(WorldPosition left, WorldPosition right)
    {
        double dx = left.X - right.X;
        double dy = left.Y - right.Y;
        return dx * dx + dy * dy;
    }

    internal void RestoreSettledAt(SimTime settledAt) => LastSettledAt = settledAt;

    public void Dispose()
    {
        PlayerDialogue.Dispose();
        Population.Dispose();
    }
}
