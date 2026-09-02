using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Alice.Activities;
using Alice.Actors;
using Alice.ProductRuntime;
using Alice.Interaction;
using Alice.Navigation;
using Alice.Npc;
using Alice.Commitments;
using Alice.Items;
using Godot;

namespace Alice.LivingTown;

public enum LivingTownCognitionRoute
{
    None,
    L0,
    L1,
    L2
}

public sealed record LivingTownTraceEntry(
    long Sequence,
    SimTime SimTime,
    ActorId ActorId,
    LivingTownCognitionRoute Route,
    ActorExecutionMode Mode,
    string Evidence,
    ActorExecutionOutcome Outcome,
    ActorExecutionId ExecutionId)
{
    public string Stage { get; init; } = "execution";
}

public sealed record LivingTownActorDebugProjection(
    ActorId ActorId,
    string DisplayName,
    LivingTownPlaceRef? CurrentPlace,
    IReadOnlyList<string> OpenScheduleOpportunities,
    IReadOnlyList<string> ActiveGoalIds,
    string? CurrentPlanId,
    LivingTownActivityKind ActivityKind,
    string? ActivityRef,
    string? ActivityLabel,
    CurrentEmotionState CurrentEmotion,
    IReadOnlyList<string> MemoryIds,
    LivingTownCognitionRoute LastRoute,
    ActorExecutionReceipt? LastReceipt,
    bool IsProjected);

public sealed record LivingTownDebugSnapshot(
    SimTime SimTime,
    TownPopulationManifestId ManifestId,
    IReadOnlyList<LivingTownActorDebugProjection> Actors,
    IReadOnlyList<LivingTownTraceEntry> Trace);

/// <summary>Read-only product diagnostics. It observes typed receipts and never selects work or mutates Authority state.</summary>
public sealed class LivingTownObservability
{
    private readonly LivingTownPopulationRuntime _runtime;
    private readonly TownPopulationManifest _manifest;
    private readonly TownSocialRuntime _social;
    private readonly RegionSocialGameplayRuntime _gameplay;
    private readonly LivingTownRosterSceneComposition? _scene;
    private readonly int _traceRetentionEntries;
    private readonly List<LivingTownTraceEntry> _trace = [];
    private readonly Dictionary<ActorId, ActorExecutionReceipt> _lastReceipts = [];
    private readonly Dictionary<ActorId, LivingTownCognitionRoute> _lastRoutes = [];
    private readonly Dictionary<ActorId, StableNpcTraceState> _lastStableStates = [];
    private long _nextSequence;

    public LivingTownObservability(
        LivingTownPopulationRuntime runtime,
        TownPopulationManifest manifest,
        TownSocialRuntime social,
        RegionSocialGameplayRuntime gameplay,
        int traceRetentionEntries,
        LivingTownRosterSceneComposition? scene = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(social);
        ArgumentNullException.ThrowIfNull(gameplay);
        if (traceRetentionEntries <= 0) throw new ArgumentOutOfRangeException(nameof(traceRetentionEntries));
        if (runtime.ManifestId != manifest.ManifestId)
            throw new ArgumentException("Living Town diagnostics must exact-bind the active population.", nameof(manifest));
        _runtime = runtime;
        _manifest = manifest;
        _social = social;
        _gameplay = gameplay;
        _traceRetentionEntries = traceRetentionEntries;
        _scene = scene;
    }

    public void Observe(ActorExecutionBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        foreach (ActorExecutionReceipt receipt in batch.Receipts)
        {
            if (receipt.SourceTime != batch.Now)
                throw new ArgumentException("Living Town scheduling batch contains a receipt from another step.", nameof(batch));
            Observe(receipt);
        }
    }

    public void Observe(ActorExecutionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        _ = _runtime.GetNpc(receipt.ActorId);
        LivingTownCognitionRoute route = Route(receipt);
        _lastRoutes[receipt.ActorId] = route;
        _lastReceipts[receipt.ActorId] = receipt;
        bool stableMode = receipt.Mode is ActorExecutionMode.Wait or ActorExecutionMode.Navigate;
        var stableState = new StableNpcTraceState(route, receipt.Mode, receipt.Evidence, receipt.Outcome);
        if (stableMode && _lastStableStates.GetValueOrDefault(receipt.ActorId) == stableState) return;
        if (stableMode) _lastStableStates[receipt.ActorId] = stableState;
        else _lastStableStates.Remove(receipt.ActorId);
        _trace.Add(new LivingTownTraceEntry(
            checked(++_nextSequence),
            receipt.SourceTime,
            receipt.ActorId,
            route,
            receipt.Mode,
            receipt.Evidence,
            receipt.Outcome,
            receipt.ExecutionId));
        int overflow = _trace.Count - _traceRetentionEntries;
        if (overflow > 0) _trace.RemoveRange(0, overflow);
    }

    public void ObserveCognition(
        SimTime now,
        ActorId actorId,
        LivingTownCognitionRoute route,
        string stage,
        string evidence,
        bool successful)
    {
        _ = _runtime.GetNpc(actorId);
        if (route is not (LivingTownCognitionRoute.L0 or LivingTownCognitionRoute.L1
            or LivingTownCognitionRoute.L2))
            throw new ArgumentOutOfRangeException(nameof(route));
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);
        _lastRoutes[actorId] = route;
        long sequence = checked(++_nextSequence);
        _trace.Add(new LivingTownTraceEntry(
            sequence,
            now,
            actorId,
            route,
            ActorExecutionMode.Wait,
            evidence,
            successful ? ActorExecutionOutcome.Completed : ActorExecutionOutcome.Rejected,
            new ActorExecutionId($"town-cognition/{actorId.Value}/{sequence}"))
        {
            Stage = stage
        });
        GD.Print($"COGNITION_TRACE actor={actorId.Value} route={route} stage={stage} success={successful} evidence={evidence}");
        int overflow = _trace.Count - _traceRetentionEntries;
        if (overflow > 0) _trace.RemoveRange(0, overflow);
    }

    public LivingTownDebugSnapshot Snapshot(SimTime now)
    {
        var actors = new List<LivingTownActorDebugProjection>();
        foreach (LivingTownNpcRuntime npc in _runtime.Npcs)
        {
            LivingTownCurrentActivity currentActivity = npc.GetCurrentActivity();
            NpcRuntimeProjection projection = _scene?.Projection.Snapshot(npc.ActorId)
                ?? new NpcRuntimeProjection(
                    npc.ActorId,
                    npc.State.Position,
                    currentActivity.Kind,
                    currentActivity.ActivityRef,
                    0);
            LivingTownCharacterProjection presentation = LivingTownPresentationProjector.Project(projection.ActivityKind);
            LivingTownPlaceRef? place = ResolvePlace(npc.State.Position);
            ActorExecutionReceipt? lastReceipt = _lastReceipts.GetValueOrDefault(npc.ActorId);
            actors.Add(new LivingTownActorDebugProjection(
                npc.ActorId,
                npc.State.SharedActorState.Identity.Name.Value,
                place,
                npc.State.OpenScheduleOpportunities.Select(DescribeOpportunity).ToArray(),
                npc.State.NpcState.Planning.ActiveGoals.Select(goal => goal.GoalId.Value).ToArray(),
                npc.State.NpcState.Planning.CurrentPlan?.PlanId.Value,
                projection.ActivityKind,
                projection.ActivityRef,
                presentation.ActivityLabel,
                npc.State.CurrentEmotion,
                npc.State.Memory.Snapshot().Select(memory => memory.MemoryId).ToArray(),
                _lastRoutes.GetValueOrDefault(npc.ActorId,
                    lastReceipt is null ? LivingTownCognitionRoute.None : Route(lastReceipt)),
                lastReceipt,
                IsProjected(npc.ActorId)));
        }
        return new LivingTownDebugSnapshot(
            now,
            _runtime.ManifestId,
            Array.AsReadOnly(actors.ToArray()),
            new ReadOnlyCollection<LivingTownTraceEntry>(_trace.ToArray()));
    }

    public byte[] ExportCanonicalTrace()
    {
        using var stream = new MemoryStream();
        foreach (LivingTownTraceEntry entry in _trace)
        {
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("sequence", entry.Sequence);
                writer.WriteNumber("sim_time_ticks", entry.SimTime.Ticks);
                writer.WriteString("actor_id", entry.ActorId.Value);
                writer.WriteString("route", entry.Route.ToString());
                writer.WriteString("stage", entry.Stage);
                writer.WriteString("mode", entry.Mode.ToString());
                writer.WriteString("evidence", entry.Evidence);
                writer.WriteString("outcome", entry.Outcome.ToString());
                writer.WriteString("execution_id", entry.ExecutionId.Value);
                writer.WriteEndObject();
            }
            stream.WriteByte((byte)'\n');
        }
        return stream.ToArray();
    }

    public string GetActorDebugText(ActorId actorId, TownNpcDebugSection section)
    {
        LivingTownNpcRuntime npc = _runtime.GetNpc(actorId);
        if (section == TownNpcDebugSection.Memories) return BuildMemoryDebugText(npc);
        if (section == TownNpcDebugSection.Knowledge) return BuildKnowledgeDebugText(npc);
        LivingTownCurrentActivity currentActivity = npc.GetCurrentActivity();
        NpcRuntimeProjection projection = _scene?.Projection.Snapshot(actorId)
            ?? new NpcRuntimeProjection(
                actorId,
                npc.State.Position,
                currentActivity.Kind,
                currentActivity.ActivityRef,
                0);
        LivingTownCharacterProjection presentation = LivingTownPresentationProjector.Project(projection.ActivityKind);
        return BuildOverviewDebugText(
            npc,
            ResolvePlace(npc.State.Position),
            presentation.ActivityLabel,
            _lastReceipts.GetValueOrDefault(actorId));
    }

    private bool IsProjected(ActorId actorId)
    {
        if (_scene is null) return false;
        return _scene.SceneRegistry.TryResolve(actorId, out INpcEntityProjectionPort? port)
            && port is not null
            && port.IsProjected;
    }

    private LivingTownPlaceRef? ResolvePlace(WorldPosition position)
    {
        return _runtime.ResolvePlace(position);
    }

    private static string DescribeOpportunity(ScheduleOpportunity opportunity) =>
        $"{opportunity.OpportunityId}:{opportunity.Entry.Purpose}:{opportunity.Status}";

    private string BuildOverviewDebugText(
        LivingTownNpcRuntime npc,
        LivingTownPlaceRef? place,
        string? activityLabel,
        ActorExecutionReceipt? lastReceipt)
    {
        SharedActorState shared = npc.State.SharedActorState;
        NpcPersonalityState personality = npc.State.NpcState.Personality;
        LivingTownNpcProfile profile = npc.State.Profile;
        var text = new StringBuilder();
        text.AppendLine(profile.DisplayName);
        text.AppendLine($"Actor ID: {npc.ActorId.Value} | Age: {shared.Identity.Age.WholeYears}");
        text.AppendLine($"Settlement: {profile.SettlementId}");
        text.AppendLine($"Position: ({npc.State.Position.X:0.0}, {npc.State.Position.Y:0.0}) | Place: {place?.Value ?? "travelling"}");
        text.AppendLine($"Activity: {activityLabel ?? npc.ActivityTracker.ActivityKind.ToString()} | Ref: {npc.ActivityTracker.ActivityRef ?? "none"}");
        text.AppendLine($"Cognition: {_lastRoutes.GetValueOrDefault(npc.ActorId, LivingTownCognitionRoute.None)} | Last execution: {lastReceipt?.ExecutionId.Value ?? "none"}");

        text.AppendLine();
        text.AppendLine("BODY");
        text.AppendLine($"Health: {shared.Body.Health.Current}/{shared.Body.Health.Maximum} | Satiety: {shared.Body.Satiety.Value}/100 | Spirit: {shared.Body.Spirit.Value}/100");
        text.AppendLine($"Disease: {shared.Body.Disease} | Movement: {shared.Traversal.MovementMode}");
        text.AppendLine($"Emotion: {npc.State.CurrentEmotion.Kind} (valence {npc.State.CurrentEmotion.Valence:0.00}, intensity {npc.State.CurrentEmotion.Intensity:0.00}, source {npc.State.CurrentEmotion.SourceEventId?.Value ?? "none"})");

        text.AppendLine();
        text.AppendLine("PERSONALITY");
        text.AppendLine($"Traits: {Join(personality.Traits.Select(value => value.Value))}");
        text.AppendLine($"Values: {Join(personality.Values.Select(value => $"{value.ValueIdentity.Value}={value.Weight:0.00}"))}");
        text.AppendLine($"Functions: Se {personality.CognitiveStyle.Se:0.00}, Si {personality.CognitiveStyle.Si:0.00}, Ne {personality.CognitiveStyle.Ne:0.00}, Ni {personality.CognitiveStyle.Ni:0.00}, Te {personality.CognitiveStyle.Te:0.00}, Ti {personality.CognitiveStyle.Ti:0.00}, Fe {personality.CognitiveStyle.Fe:0.00}, Fi {personality.CognitiveStyle.Fi:0.00}");

        text.AppendLine();
        text.AppendLine("LIFE AND WORK");
        text.AppendLine($"Household: {profile.HouseholdId ?? "none"} | Residence: {profile.Residence?.Value ?? "none"} | Private room: {profile.PrivateRoom?.Value ?? "none"}");
        text.AppendLine($"Occupation: {profile.OccupationId ?? "none"} | Workplace: {profile.Workplace?.Value ?? "none"}");
        text.AppendLine($"Roles: {Join(profile.RoleIds)}");
        text.AppendLine($"Capabilities: {Join(profile.Capabilities.Select(value => $"{value.CapabilityId}={value.Value}"))}");
        text.AppendLine($"Skills: {Join(profile.Skills.Select(value => $"{value.SkillId}={value.Level}"))}");
        text.AppendLine($"Access: {Join(profile.AccessRefs)}");

        text.AppendLine();
        text.AppendLine("GOALS AND SCHEDULE");
        text.AppendLine($"Configured goals: {Join(profile.InitialGoalRefs)}");
        text.AppendLine($"Runtime goals: {Join(npc.State.NpcState.Planning.ActiveGoals.Select(value => value.GoalId.Value))}");
        text.AppendLine($"Plan: {npc.State.NpcState.Planning.CurrentPlan?.PlanId.Value ?? "none"} | Preferred schedule: {npc.State.PreferredScheduleEntryId ?? "none"}");
        foreach (TownScheduleEntryConfiguration entry in profile.Schedule)
            text.AppendLine($"- {entry.EntryId}: {entry.Purpose}/{entry.Obligation} at {entry.PlaceRef ?? "none"}, t{entry.StartsAtTickOfDay}-{entry.EndsAtTickOfDay}");
        text.AppendLine($"Open: {Join(npc.State.OpenScheduleOpportunities.Select(DescribeOpportunity))}");

        text.AppendLine();
        text.AppendLine("INVENTORY AND ECONOMY");
        text.AppendLine($"Inventory: {Join(shared.Inventory.Entries.Select(DescribeInventoryEntry))}");
        text.AppendLine($"Equipped hand: {DescribeHand(shared.Equipment.HandItemRef)}");
        AssetContainerState assets = _gameplay.GetContainer(new AssetContainerOwnerId(
            AssetContainerOwnerKind.Actor, npc.ActorId.Value));
        text.AppendLine($"Current assets: {Join(assets.Balances.Select(value => $"{value.AssetId.Value}={value.Quantity}"))}");
        text.AppendLine($"Carried durable items: {Join(_gameplay.GetItemInstances(assets.OwnerId).Select(value =>
            value.Durability is int durability
                ? $"{value.ItemTypeId.Value}:{value.ItemInstanceId.Value} durability={durability}"
                : $"{value.ItemTypeId.Value}:{value.ItemInstanceId.Value}"))}");
        text.AppendLine($"Gameplay equipped tool: {_gameplay.GetEquippedTool(npc.ActorId.Value) ?? "none"}");

        text.AppendLine();
        text.AppendLine("RELATIONSHIPS AND OBLIGATIONS");
        TownSocialDurableState social = _social.CaptureDurableState();
        foreach (TownDirectedRelationshipSnapshot relationship in social.Relationships.Where(value => value.SubjectActorId == npc.ActorId))
            text.AppendLine($"- {relationship.OtherActorId.Value}: familiarity {relationship.Familiarity:0.00}, trust {relationship.Trust:0.00}, affection {relationship.Affection:0.00}, respect {relationship.Respect:0.00}, fear {relationship.Fear:0.00}, grievance {relationship.Grievance:0.00}");
        foreach (TownSocialBondSnapshot bond in social.Bonds.Where(value => value.FirstActorId == npc.ActorId || value.SecondActorId == npc.ActorId))
            text.AppendLine($"- bond {bond.FirstActorId.Value}/{bond.SecondActorId.Value}: {bond.Stage} (source {bond.SourceEventId ?? "none"})");
        foreach (Commitment commitment in _social.Commitments.Where(value => value.Debtor == npc.ActorId || value.Creditor == npc.ActorId))
            text.AppendLine($"- obligation {DescribeCommitment(commitment)}");

        text.AppendLine();
        text.AppendLine("Use the Memories or Knowledge button to inspect the complete lists.");
        text.AppendLine();
        text.AppendLine($"Presentation: dialogue={profile.DialogueStyleId ?? "none"}, display={profile.DisplayStyleId ?? "none"}, projected={IsProjected(npc.ActorId)}");
        return text.ToString().TrimEnd();
    }

    private static string BuildMemoryDebugText(LivingTownNpcRuntime npc)
    {
        IReadOnlyList<LivingTownMemorySeed> memories = npc.State.Memory.Snapshot();
        var text = new StringBuilder();
        text.AppendLine($"MEMORIES — {npc.State.Profile.DisplayName} ({memories.Count})");
        foreach (LivingTownMemorySeed memory in memories)
            text.AppendLine($"- {memory.MemoryId} | t{memory.OccurredAtTicks} | {memory.Emotion.Kind}/{memory.Emotion.Intensity:0.00} | {memory.ActorVisibleText}");
        return text.ToString().TrimEnd();
    }

    private static string BuildKnowledgeDebugText(LivingTownNpcRuntime npc)
    {
        LivingTownNpcProfile profile = npc.State.Profile;
        var text = new StringBuilder();
        text.AppendLine($"KNOWLEDGE — {profile.DisplayName}");
        text.AppendLine($"Interests: {Join(profile.InterestIds)}");
        text.AppendLine($"Aspirations: {Join(profile.AspirationIds)}");
        text.AppendLine($"Place preferences: {Join(profile.PlacePreferences.Select(value => $"{value.RefId}={value.Weight:0.00}"))}");
        text.AppendLine($"Social preferences: {Join(profile.SocialPreferences.Select(value => $"{value.RefId}={value.Weight:0.00}"))}");
        text.AppendLine($"Known places: {Join(profile.KnownPlaceRefs)}");
        text.AppendLine($"Known actors: {Join(profile.KnownActorIds)}");
        text.AppendLine($"Knowledge sources: {Join(profile.KnowledgeSourceEventIds.Select(value => value.Value))}");
        return text.ToString().TrimEnd();
    }

    private static string DescribeInventoryEntry(InventoryEntry entry) => entry switch
    {
        StackEntry stack => $"{stack.ItemTypeId.Value} x{stack.Quantity}",
        InstanceEntry instance => instance.ItemInstanceId.Value,
        _ => entry.GetType().Name
    };

    private static string DescribeHand(HandItemRef? hand) => hand switch
    {
        StackHandItemRef stack => stack.ItemTypeId.Value,
        InstanceHandItemRef instance => instance.ItemInstanceId.Value,
        null => "empty",
        _ => hand.GetType().Name
    };

    private static string DescribeCommitment(Commitment commitment)
    {
        string term = commitment.Term is CoinOrResourceTransferTerm transfer
            ? $"{transfer.Amount} {transfer.AssetRef.Value}"
            : commitment.Term.GetType().Name;
        return $"{commitment.CommitmentId.Value}: {commitment.Debtor.Value} -> {commitment.Creditor.Value}, {term}, {commitment.Status}, due t{commitment.Deadline.Ticks}";
    }

    private static string Join(IEnumerable<string> values)
    {
        string joined = string.Join(", ", values);
        return joined.Length == 0 ? "none" : joined;
    }

    private static LivingTownCognitionRoute Route(ActorExecutionReceipt receipt) =>
        receipt.CognitionRoute switch
        {
            AutonomousNpcCognitionRoute.None => LivingTownCognitionRoute.None,
            AutonomousNpcCognitionRoute.L0 => LivingTownCognitionRoute.L0,
            AutonomousNpcCognitionRoute.L1 => LivingTownCognitionRoute.L1,
            AutonomousNpcCognitionRoute.L2 => LivingTownCognitionRoute.L2,
            _ => throw new InvalidOperationException("Living Town trace requires typed cognition-route provenance.")
        };

    private readonly record struct StableNpcTraceState(
        LivingTownCognitionRoute Route,
        ActorExecutionMode Mode,
        string Evidence,
        ActorExecutionOutcome Outcome);
}
