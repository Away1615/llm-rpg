using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Items;
using Alice.Navigation;

namespace Alice.LivingTown;

public sealed record CurrentEmotionState
{
    public CurrentEmotionState(LivingTownEmotionKind kind, double valence, double intensity, SourceEventId? sourceEventId)
    {
        ValidateEmotionValue(valence, -1, 1, nameof(valence));
        ValidateEmotionValue(intensity, 0, 1, nameof(intensity));
        Kind = kind;
        Valence = valence;
        Intensity = intensity;
        SourceEventId = sourceEventId;
    }

    public LivingTownEmotionKind Kind { get; }
    public double Valence { get; }
    public double Intensity { get; }
    public SourceEventId? SourceEventId { get; }

    internal static void ValidateEmotionValue(double value, double minimum, double maximum, string parameterName)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public sealed record MemoryEmotion
{
    public MemoryEmotion(LivingTownEmotionKind kind, double valence, double intensity, SourceEventId sourceEventId, long capturedAtTicks)
    {
        ArgumentNullException.ThrowIfNull(sourceEventId);
        CurrentEmotionState.ValidateEmotionValue(valence, -1, 1, nameof(valence));
        CurrentEmotionState.ValidateEmotionValue(intensity, 0, 1, nameof(intensity));
        if (capturedAtTicks < 0) throw new ArgumentOutOfRangeException(nameof(capturedAtTicks));
        Kind = kind;
        Valence = valence;
        Intensity = intensity;
        SourceEventId = sourceEventId;
        CapturedAtTicks = capturedAtTicks;
    }

    public LivingTownEmotionKind Kind { get; }
    public double Valence { get; }
    public double Intensity { get; }
    public SourceEventId SourceEventId { get; }
    public long CapturedAtTicks { get; }
}

public sealed record LivingTownAppearanceProfile(string FillColor);
public sealed record LivingTownWeightedReference(string RefId, double Weight);
public sealed record LivingTownCapabilityProfile(string CapabilityId, int Value);
public sealed record LivingTownSkillProfile(string SkillId, int Level);
public sealed record LivingTownMemorySeed(
    string MemoryId,
    SourceEventId SourceEventId,
    long OccurredAtTicks,
    string ActorVisibleText,
    MemoryEmotion Emotion);

/// <summary>
/// Immutable configuration-derived NPC individuality outside the already-frozen Shared Actor/NpcState slices.
/// Fields guide arbitration and projection; they are not behavior scripts or Authority state writers.
/// </summary>
public sealed class LivingTownNpcProfile
{
    internal LivingTownNpcProfile(
        ActorId actorId,
        string displayName,
        LivingTownAppearanceProfile appearance,
        CurrentEmotionState currentEmotion,
        WorldPosition startingPosition,
        string settlementId,
        LivingTownPlaceRef? residence,
        LivingTownPlaceRef? privateRoom,
        string? householdId,
        string? occupationId,
        LivingTownPlaceRef? workplace,
        IEnumerable<string> roleIds,
        IEnumerable<TownScheduleEntryConfiguration> schedule,
        IEnumerable<LivingTownCapabilityProfile> capabilities,
        IEnumerable<LivingTownSkillProfile> skills,
        AssetContainerState assets,
        IEnumerable<string> accessRefs,
        IEnumerable<string> interestIds,
        IEnumerable<LivingTownWeightedReference> placePreferences,
        IEnumerable<LivingTownWeightedReference> socialPreferences,
        IEnumerable<string> aspirationIds,
        IEnumerable<string> initialGoalRefs,
        IEnumerable<string> commitmentRefs,
        IEnumerable<string> knownPlaceRefs,
        IEnumerable<string> knownActorIds,
        IEnumerable<SourceEventId> knowledgeSourceEventIds,
        IEnumerable<LivingTownMemorySeed> memories,
        string? dialogueStyleId,
        string? displayStyleId)
    {
        ActorId = actorId;
        DisplayName = displayName;
        Appearance = appearance;
        CurrentEmotion = currentEmotion;
        StartingPosition = startingPosition;
        SettlementId = settlementId;
        Residence = residence;
        PrivateRoom = privateRoom;
        HouseholdId = householdId;
        OccupationId = occupationId;
        Workplace = workplace;
        RoleIds = Snapshot(roleIds);
        Schedule = Snapshot(schedule);
        Capabilities = Snapshot(capabilities);
        Skills = Snapshot(skills);
        ArgumentNullException.ThrowIfNull(assets);
        if (assets.OwnerId.Kind != AssetContainerOwnerKind.Actor
            || !StringComparer.Ordinal.Equals(assets.OwnerId.Value, actorId.Value))
            throw new ArgumentException("Living Town assets must belong to the Profile Actor.", nameof(assets));
        Assets = assets;
        AccessRefs = Snapshot(accessRefs);
        InterestIds = Snapshot(interestIds);
        PlacePreferences = Snapshot(placePreferences);
        SocialPreferences = Snapshot(socialPreferences);
        AspirationIds = Snapshot(aspirationIds);
        InitialGoalRefs = Snapshot(initialGoalRefs);
        CommitmentRefs = Snapshot(commitmentRefs);
        KnownPlaceRefs = Snapshot(knownPlaceRefs);
        KnownActorIds = Snapshot(knownActorIds);
        KnowledgeSourceEventIds = Snapshot(knowledgeSourceEventIds);
        Memories = Snapshot(memories);
        DialogueStyleId = dialogueStyleId;
        DisplayStyleId = displayStyleId;
    }

    public ActorId ActorId { get; }
    public string DisplayName { get; }
    public LivingTownAppearanceProfile Appearance { get; }
    public CurrentEmotionState CurrentEmotion { get; }
    public WorldPosition StartingPosition { get; }
    public string SettlementId { get; }
    public LivingTownPlaceRef? Residence { get; }
    public LivingTownPlaceRef? PrivateRoom { get; }
    public string? HouseholdId { get; }
    public string? OccupationId { get; }
    public LivingTownPlaceRef? Workplace { get; }
    public IReadOnlyList<string> RoleIds { get; }
    public IReadOnlyList<TownScheduleEntryConfiguration> Schedule { get; }
    public IReadOnlyList<LivingTownCapabilityProfile> Capabilities { get; }
    public IReadOnlyList<LivingTownSkillProfile> Skills { get; }
    public AssetContainerState Assets { get; }
    public IReadOnlyList<string> AccessRefs { get; }
    public IReadOnlyList<string> InterestIds { get; }
    public IReadOnlyList<LivingTownWeightedReference> PlacePreferences { get; }
    public IReadOnlyList<LivingTownWeightedReference> SocialPreferences { get; }
    public IReadOnlyList<string> AspirationIds { get; }
    public IReadOnlyList<string> InitialGoalRefs { get; }
    public IReadOnlyList<string> CommitmentRefs { get; }
    public IReadOnlyList<string> KnownPlaceRefs { get; }
    public IReadOnlyList<string> KnownActorIds { get; }
    public IReadOnlyList<SourceEventId> KnowledgeSourceEventIds { get; }
    public IReadOnlyList<LivingTownMemorySeed> Memories { get; }
    public string? DialogueStyleId { get; }
    public string? DisplayStyleId { get; }

    private static IReadOnlyList<T> Snapshot<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<T>(values.ToArray());
    }
}

public sealed record LivingTownNpcInitialState(
    SharedActorState SharedActorState,
    Alice.Npc.NpcState NpcState,
    LivingTownNpcProfile Profile);
