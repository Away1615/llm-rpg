using System.Text.Json;
using System.Text.Json.Serialization;
using Alice.Activities;
using Alice.Actors;
using Alice.Memory;
using Alice.Navigation;
using Alice.ProductRuntime;
using Alice.Social;

namespace Alice.LivingTown;

public sealed record TownHistoryExperienceSave(
    string ActorId,
    CanonicalHistoryExperienceRole Role,
    string? TellerActorId,
    string? OriginalSourceId);

public sealed record TownHistoryVisibleFactSave(string ActorId, string Text);
public sealed record TownHistorySourceReferenceSave(string Kind, string Value);

public sealed record TownHistoryEventSave(
    string SourceEventId,
    string EventKind,
    long OccurredAtTicks,
    string LocationId,
    double WorldX,
    double WorldY,
    string SpatialLayer,
    IReadOnlyList<TownHistoryExperienceSave> Experiences,
    IReadOnlyList<TownHistoryVisibleFactSave> ActorVisibleFacts,
    IReadOnlyList<TownHistorySourceReferenceSave> SourceReferences);

public sealed record TownDialogueClaimSave(string ClaimRef, string ProvenanceRef);

public sealed record TownDialogueInviteSave(
    string GatheringRef,
    int ExpectedGatheringRevision,
    string InvitedActorId,
    string? BelievedAuthorizationRef);

public sealed record TownDialogueActSave(
    string ActId,
    SemanticDialogueActKind Kind,
    string SpeakerActorId,
    IReadOnlyList<string> RecipientActorIds,
    string? TopicRef,
    IReadOnlyList<TownDialogueClaimSave> ClaimReferences,
    TownDialogueInviteSave? Invite,
    DialogueResponseExpectation ResponseExpectation);

public sealed record TownConversationSave(
    string SessionId,
    IReadOnlyList<string> ParticipantActorIds,
    IReadOnlyList<TownDialogueActSave> AcceptedActs,
    IReadOnlyList<string> PendingOpportunityIds,
    IReadOnlyList<long> TurnOccurredAtTicks);

public sealed record TownDialogueSurfaceLineSave(
    int Sequence,
    DialogueSurfaceLineKind Kind,
    DialogueSurfaceRoute Route,
    string? SpeakerActorId,
    string? DialogueNpcActorId,
    string Text,
    string? SessionId,
    string? ActId,
    long OccurredAtTicks);

public sealed record TownProductSaveDocument(
    string WorldId,
    long SettledTick,
    long NextTick,
    IReadOnlyList<string> RegisteredAggregateIds,
    TownPlayerDurableState Player,
    IReadOnlyList<LivingTownNpcDurableState> Npcs,
    TownGameplayDurableState Gameplay,
    TownSocialDurableState Social,
    IReadOnlyList<TownHistoryEventSave> HistoryEvents,
    IReadOnlyList<TownConversationSave> Conversations,
    IReadOnlyList<TownDialogueSurfaceLineSave> DialogueSurface,
    TownL2PolicyDurableState L2Policy,
    TownAutonomyDurableState Autonomy);

public enum TownProductSaveFailure
{
    NotAtSettledTick,
    ProviderWorkInFlight
}

public abstract record TownProductSaveCaptureOutcome
{
    private protected TownProductSaveCaptureOutcome() { }
}

public sealed record TownProductSaveCaptured(TownProductSaveDocument Document) : TownProductSaveCaptureOutcome;
public sealed record TownProductSaveRejected(TownProductSaveFailure Failure) : TownProductSaveCaptureOutcome;

/// <summary>One readable unversioned JSON snapshot over the product's registered durable owners.</summary>
public static class TownProductSaveRuntime
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static TownProductSaveCaptureOutcome Capture(
        LivingTownProductComposition product,
        SimTime settledAt,
        long nextTick)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (product.LastSettledAt is not SimTime actual || actual != settledAt || nextTick != settledAt.Ticks + 1)
            return new TownProductSaveRejected(TownProductSaveFailure.NotAtSettledTick);
        if (product.HasInFlightProviderWork)
            return new TownProductSaveRejected(TownProductSaveFailure.ProviderWorkInFlight);

        var document = new TownProductSaveDocument(
            product.World.WorldId,
            settledAt.Ticks,
            nextTick,
            product.DurableStateRegistry.Registrations.Select(value => value.AggregateId.Value).ToArray(),
            product.Player.CaptureDurableState(),
            product.Population.CaptureDurableState(),
            product.Gameplay.CaptureDurableState(),
            product.Social.CaptureDurableState(),
            product.History.Events.Select(CaptureHistory).ToArray(),
            product.Conversations.CaptureDurableState().Select(CaptureConversation).ToArray(),
            product.DialogueSurface.Lines.Select(CaptureSurfaceLine).ToArray(),
            product.L2Decisions.Policy.CaptureDurableState(),
            product.Autonomy.CaptureDurableState());
        return new TownProductSaveCaptured(document);
    }

    public static byte[] Serialize(TownProductSaveDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.SerializeToUtf8Bytes(document, Options);
    }

    public static TownProductSaveDocument Deserialize(ReadOnlySpan<byte> bytes)
    {
        TownProductSaveDocument? document = JsonSerializer.Deserialize<TownProductSaveDocument>(bytes, Options);
        return document ?? throw new InvalidDataException("Product save JSON is empty.");
    }

    public static void SaveFile(string path, TownProductSaveDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllBytes(path, Serialize(document));
    }

    public static TownProductSaveDocument LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Deserialize(File.ReadAllBytes(path));
    }

    public static void Restore(
        LivingTownProductComposition product,
        TownProductSaveDocument document)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(document);
        if (!StringComparer.Ordinal.Equals(product.World.WorldId, document.WorldId)
            || document.NextTick != document.SettledTick + 1)
            throw new InvalidDataException("Product save belongs to another world or has an invalid clock boundary.");
        string[] registered = product.DurableStateRegistry.Registrations
            .Select(value => value.AggregateId.Value).ToArray();
        if (!registered.SequenceEqual(document.RegisteredAggregateIds, StringComparer.Ordinal))
            throw new InvalidDataException("Product save durable-owner registry does not match the current world.");

        var settledAt = new SimTime(document.SettledTick);
        product.Gameplay.RestoreDurableState(document.Gameplay);
        product.Player.RestoreDurableState(document.Player);
        product.Player.ApplyVitals(product.Gameplay.GetVitals(product.Player.ActorId.Value));
        product.Population.RestoreDurableState(document.Npcs, settledAt);
        foreach (LivingTownNpcRuntime npc in product.Population.Npcs)
            npc.State.ApplyVitals(product.Gameplay.GetVitals(npc.ActorId.Value));
        CanonicalHistoryEventRecord[] events = document.HistoryEvents.Select(RestoreHistory).ToArray();
        product.History.RestoreDurableEvents(events, product.Population.Npcs);
        product.Conversations.RestoreDurableState(document.Conversations.Select(RestoreConversation));
        product.DialogueSurface.Restore(document.DialogueSurface.Select(RestoreSurfaceLine));
        product.RebuildPendingDialogueResponses(settledAt);
        product.Social.RestoreDurableState(document.Social);
        product.L2Decisions.Policy.RestoreDurableState(document.L2Policy);
        product.Autonomy.RestoreDurableState(document.Autonomy, settledAt);
        product.RestoreSettledAt(settledAt);
    }

    private static TownHistoryEventSave CaptureHistory(CanonicalHistoryEventRecord value) => new(
        value.SourceId.Value,
        value.EventKind,
        value.OccurredAtTicks,
        value.LocationId,
        value.Position.X,
        value.Position.Y,
        value.SpatialLayer,
        value.Experiences.Select(experience => new TownHistoryExperienceSave(
            experience.ActorId.Value,
            experience.Role,
            experience.TellerActorId?.Value,
            experience.OriginalSourceId?.Value)).ToArray(),
        value.ActorVisibleFacts.Select(fact =>
            new TownHistoryVisibleFactSave(fact.ActorId.Value, fact.Text)).ToArray(),
        value.SourceReferences.Select(reference =>
            new TownHistorySourceReferenceSave(reference.Kind, reference.Value)).ToArray());

    private static CanonicalHistoryEventRecord RestoreHistory(TownHistoryEventSave value) => new(
        new DecisionMemorySourceId(value.SourceEventId),
        value.EventKind,
        value.OccurredAtTicks,
        value.LocationId,
        new WorldPosition(value.WorldX, value.WorldY),
        value.SpatialLayer,
        value.Experiences.Select(experience => new CanonicalHistoryExperience(
            new ActorId(experience.ActorId),
            experience.Role,
            experience.TellerActorId is null ? null : new ActorId(experience.TellerActorId),
            experience.OriginalSourceId is null ? null : new DecisionMemorySourceId(experience.OriginalSourceId))),
        value.ActorVisibleFacts.Select(fact =>
            new CanonicalHistoryActorVisibleFact(new ActorId(fact.ActorId), fact.Text)),
        value.SourceReferences.Select(reference =>
            new CanonicalHistorySourceReference(reference.Kind, reference.Value)));

    private static TownConversationSave CaptureConversation(ConversationDurableSnapshot value) => new(
        value.Session.SessionId.Value,
        value.Session.Participants.Select(actor => actor.Value).ToArray(),
        value.Session.Transcript.Select(turn => CaptureAct(turn.Act)).ToArray(),
        value.Session.PendingResponseOpportunities.Select(opportunity => opportunity.OpportunityId.Value).ToArray(),
        value.TurnTimes.Select(time => time.Ticks).ToArray());

    private static TownDialogueSurfaceLineSave CaptureSurfaceLine(DialogueSurfaceLine value) => new(
        value.Sequence,
        value.Kind,
        value.Route,
        value.Speaker?.Value,
        value.DialogueNpc?.Value,
        value.Text,
        value.SessionId?.Value,
        value.ActId?.Value,
        value.OccurredAt.Ticks);

    private static DialogueSurfaceLine RestoreSurfaceLine(TownDialogueSurfaceLineSave value) => new(
        value.Sequence,
        value.Kind,
        value.SpeakerActorId is null ? null : new ActorId(value.SpeakerActorId),
        value.DialogueNpcActorId is null ? null : new ActorId(value.DialogueNpcActorId),
        value.Text,
        value.SessionId is null ? null : new ConversationSessionId(value.SessionId),
        value.ActId is null ? null : new SemanticDialogueActId(value.ActId),
        new SimTime(value.OccurredAtTicks),
        value.Route);

    private static ConversationDurableSnapshot RestoreConversation(TownConversationSave value)
    {
        SemanticDialogueAct[] acts = value.AcceptedActs.Select(RestoreAct).ToArray();
        ConversationSession session = ConversationSession.Restore(
            new ConversationSessionId(value.SessionId),
            value.ParticipantActorIds.Select(actor => new ActorId(actor)),
            acts,
            value.PendingOpportunityIds.Select(id => new DialogueResponseOpportunityId(id)));
        return new ConversationDurableSnapshot(
            session,
            value.TurnOccurredAtTicks.Select(tick => new SimTime(tick)).ToArray());
    }

    private static TownDialogueActSave CaptureAct(SemanticDialogueAct value) => new(
        value.ActId.Value,
        value.Kind,
        value.Speaker.Value,
        value.Recipients.Select(actor => actor.Value).ToArray(),
        value.TopicRef?.Value,
        value.ClaimReferences.Select(claim =>
            new TownDialogueClaimSave(claim.ClaimRef.Value, claim.ProvenanceRef.Value)).ToArray(),
        value.InvitePayload is null ? null : new TownDialogueInviteSave(
            value.InvitePayload.GatheringRef.Value,
            value.InvitePayload.ExpectedGatheringRevision,
            value.InvitePayload.InvitedActorId.Value,
            value.InvitePayload.BelievedAuthorizationRef?.Value),
        value.ResponseExpectation);

    private static SemanticDialogueAct RestoreAct(TownDialogueActSave value) => new(
        new SemanticDialogueActId(value.ActId),
        value.Kind,
        new ActorId(value.SpeakerActorId),
        value.RecipientActorIds.Select(actor => new ActorId(actor)),
        value.TopicRef is null ? null : new DialogueTopicRef(value.TopicRef),
        value.ClaimReferences.Select(claim => new DialogueClaimReference(
            new DialogueClaimRef(claim.ClaimRef),
            new ClaimProvenanceRef(claim.ProvenanceRef))),
        value.Invite is null ? null : new DialogueInvitePayload(
            new GatheringRef(value.Invite.GatheringRef),
            value.Invite.ExpectedGatheringRevision,
            new ActorId(value.Invite.InvitedActorId),
            value.Invite.BelievedAuthorizationRef is null
                ? null
                : new BelievedAuthorizationRef(value.Invite.BelievedAuthorizationRef)),
        value.ResponseExpectation);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
