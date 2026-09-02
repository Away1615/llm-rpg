using Alice.Actors;
using Alice.Capabilities;
using Alice.Interaction;
using Alice.Items;
using Alice.Npc;
using Alice.Navigation;
using Alice.World;

namespace Alice.LivingTown;

public sealed class NpcInitialStateBuilder
{
    public LivingTownNpcInitialState Build(TownNpcConfiguration configuration)
    {
        return Build(configuration, null);
    }

    public LivingTownNpcInitialState Build(
        TownNpcConfiguration configuration,
        TownPopulationManifest? manifest)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ActorId actorId = new(configuration.Identity.ActorId);
        InventoryState inventory = BuildInventory(actorId, configuration.Inventory);
        EquipmentState equipment = BuildEquipment(actorId, configuration.Inventory, inventory);
        var shared = new SharedActorState(
            new ActorIdentity(actorId, new ActorName(configuration.Identity.Name), new ActorAge(configuration.Identity.Age)),
            new ActorBodyState(
                actorId,
                new Health(configuration.Body.HealthCurrent, configuration.Body.HealthMaximum),
                new Satiety(configuration.Body.Satiety),
                new Spirit(configuration.Body.Spirit),
                Enum.Parse<Disease>(configuration.Body.Disease, false)),
            new ActorTraversalState(actorId, Enum.Parse<MovementMode>(configuration.Body.MovementMode, false)),
            inventory,
            equipment);

        var npc = new NpcState(
            actorId,
            BuildPersonality(configuration.Personality),
            manifest is null
                ? new NpcKnowledgeState(new NpcKnownTargetSpatialState([]), new NpcKnownOpportunityState([]))
                : BuildKnowledge(actorId, configuration, manifest),
            new NpcPlanningState([], null),
            BuildSocial(actorId, configuration.Relationships));

        return new LivingTownNpcInitialState(shared, npc, BuildProfile(actorId, configuration));
    }

    internal static TargetRef PlaceTargetRef(LivingTownPlaceRef placeRef) => new($"place/{placeRef.Value}");

    private static NpcKnowledgeState BuildKnowledge(
        ActorId actorId,
        TownNpcConfiguration actor,
        TownPopulationManifest manifest)
    {
        var targets = new List<ActorVisibleTargetSpatialSnapshot>();
        foreach (string knownPlace in actor.Knowledge.KnownPlaceRefs)
        {
            TownPlaceConfiguration place = manifest.Places.Single(value => StringComparer.Ordinal.Equals(value.PlaceRef, knownPlace));
            targets.Add(new ActorVisibleTargetSpatialSnapshot(
                PlaceTargetRef(new LivingTownPlaceRef(knownPlace)),
                TargetKind.PointOfInterest,
                new WorldPosition(place.WorldX, place.WorldY)));
        }

        return new NpcKnowledgeState(
            new NpcKnownTargetSpatialState(targets),
            new NpcKnownOpportunityState([]));
    }

    private static InventoryState BuildInventory(ActorId actorId, TownInventoryConfiguration configuration)
    {
        var entries = new List<InventoryEntry>();
        foreach (TownStackConfiguration stack in configuration.Stacks)
        {
            entries.Add(new StackEntry(new ItemTypeId(stack.ItemTypeId), stack.Quantity));
        }
        foreach (TownItemInstanceConfiguration instance in configuration.Instances)
        {
            entries.Add(new InstanceEntry(new ItemInstanceId(instance.ItemInstanceId)));
        }
        return new InventoryState(actorId, entries, configuration.Version);
    }

    private static EquipmentState BuildEquipment(
        ActorId actorId,
        TownInventoryConfiguration configuration,
        InventoryState inventory)
    {
        HandItemRef? hand = configuration.EquippedHandInstanceId is null
            ? null
            : new InstanceHandItemRef(new ItemInstanceId(configuration.EquippedHandInstanceId));
        return new EquipmentState(actorId, hand, configuration.EquipmentVersion, inventory);
    }

    private static NpcPersonalityState BuildPersonality(TownNpcPersonalityConfiguration configuration)
    {
        double[] values = configuration.CognitiveFunctionValues;
        var traits = new List<PersonalityTagId>();
        foreach (string trait in configuration.TraitIds) traits.Add(new PersonalityTagId(trait));
        var weightedValues = new List<WeightedPersonalityValue>();
        foreach (TownWeightedReferenceConfiguration value in configuration.Values)
        {
            weightedValues.Add(new WeightedPersonalityValue(new ValueIdentity(value.RefId), value.Weight));
        }
        return new NpcPersonalityState(
            new CognitiveFunctionProfile(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]),
            traits,
            weightedValues);
    }

    private static NpcSocialState BuildSocial(ActorId actorId, TownRelationshipConfiguration[] configurations)
    {
        var relationships = new List<NpcRelationshipAppraisal>();
        foreach (TownRelationshipConfiguration relationship in configurations)
        {
            relationships.Add(new NpcRelationshipAppraisal(
                new ActorId(relationship.OtherActorId),
                relationship.Familiarity,
                relationship.Trust,
                relationship.Affection,
                relationship.Respect,
                relationship.Fear,
                relationship.Grievance));
        }
        return new NpcSocialState(actorId, relationships);
    }

    private static LivingTownNpcProfile BuildProfile(ActorId actorId, TownNpcConfiguration configuration)
    {
        return new LivingTownNpcProfile(
            actorId,
            configuration.Identity.Name,
            new LivingTownAppearanceProfile(configuration.Appearance.FillColor),
            BuildCurrentEmotion(configuration.CurrentEmotion),
            new WorldPosition(configuration.StartWorldX, configuration.StartWorldY),
            configuration.SettlementId,
            CreateOptionalPlace(configuration.ResidencePlaceRef),
            CreateOptionalPlace(configuration.PrivateRoomPlaceRef),
            configuration.HouseholdId,
            configuration.OccupationId,
            CreateOptionalPlace(configuration.WorkplacePlaceRef),
            SortedStrings(configuration.RoleIds),
            SortedSchedule(configuration.Schedule),
            BuildCapabilities(configuration.Capabilities),
            BuildSkills(configuration.Skills),
            BuildAssets(actorId, configuration.Inventory, configuration.Currency),
            SortedStrings(configuration.AccessRefs),
            SortedStrings(configuration.InterestIds),
            BuildWeightedReferences(configuration.PlacePreferences),
            BuildWeightedReferences(configuration.SocialPreferences),
            SortedStrings(configuration.AspirationIds),
            SortedStrings(configuration.InitialGoalRefs),
            SortedStrings(configuration.CommitmentRefs),
            SortedStrings(configuration.Knowledge.KnownPlaceRefs),
            SortedStrings(configuration.Knowledge.KnownActorIds),
            BuildSourceIds(configuration.Knowledge.SourceEventIds),
            BuildMemories(configuration.Memories),
            configuration.DialogueStyleId,
            configuration.DisplayStyleId);
    }

    private static CurrentEmotionState BuildCurrentEmotion(TownNpcEmotionConfiguration configuration)
    {
        SourceEventId? source = configuration.SourceEventId is null ? null : new SourceEventId(configuration.SourceEventId);
        return new CurrentEmotionState(
            Enum.Parse<LivingTownEmotionKind>(configuration.Kind, false),
            configuration.Valence,
            configuration.Intensity,
            source);
    }

    private static LivingTownPlaceRef? CreateOptionalPlace(string? value) => value is null ? null : new LivingTownPlaceRef(value);

    private static string[] SortedStrings(string[] values)
    {
        string[] copy = values.ToArray();
        Array.Sort(copy, StringComparer.Ordinal);
        return copy;
    }

    private static TownScheduleEntryConfiguration[] SortedSchedule(TownScheduleEntryConfiguration[] values)
    {
        TownScheduleEntryConfiguration[] copy = values.ToArray();
        Array.Sort(copy, TownScheduleEntryComparer.Instance);
        return copy;
    }

    private static LivingTownCapabilityProfile[] BuildCapabilities(TownCapabilityConfiguration[] values)
    {
        var result = new LivingTownCapabilityProfile[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            result[index] = new LivingTownCapabilityProfile(values[index].CapabilityId, values[index].Value);
        }
        Array.Sort(result, LivingTownCapabilityComparer.Instance);
        return result;
    }

    private static LivingTownSkillProfile[] BuildSkills(TownSkillConfiguration[] values)
    {
        var result = new LivingTownSkillProfile[values.Length];
        for (int index = 0; index < values.Length; index++) result[index] = new LivingTownSkillProfile(values[index].SkillId, values[index].Level);
        Array.Sort(result, LivingTownSkillComparer.Instance);
        return result;
    }

    private static AssetContainerState BuildAssets(
        ActorId actorId,
        TownInventoryConfiguration inventory,
        TownCurrencyConfiguration[] currency)
    {
        var balances = new List<FungibleAssetBalance>();
        foreach (TownStackConfiguration stack in inventory.Stacks)
            balances.Add(new FungibleAssetBalance(new FungibleAssetId(stack.ItemTypeId), stack.Quantity));
        foreach (TownCurrencyConfiguration balance in currency)
            balances.Add(new FungibleAssetBalance(new FungibleAssetId(balance.CurrencyId), balance.Quantity));
        var itemInstances = new List<ItemInstanceId>();
        foreach (TownItemInstanceConfiguration instance in inventory.Instances)
            itemInstances.Add(new ItemInstanceId(instance.ItemInstanceId));
        return new AssetContainerState(
            new AssetContainerOwnerId(AssetContainerOwnerKind.Actor, actorId.Value),
            balances,
            itemInstances,
            inventory.Version);
    }

    private static LivingTownWeightedReference[] BuildWeightedReferences(TownWeightedReferenceConfiguration[] values)
    {
        var result = new LivingTownWeightedReference[values.Length];
        for (int index = 0; index < values.Length; index++) result[index] = new LivingTownWeightedReference(values[index].RefId, values[index].Weight);
        Array.Sort(result, LivingTownWeightedReferenceComparer.Instance);
        return result;
    }

    private static SourceEventId[] BuildSourceIds(string[] values)
    {
        var result = new SourceEventId[values.Length];
        for (int index = 0; index < values.Length; index++) result[index] = new SourceEventId(values[index]);
        Array.Sort(result, SourceEventIdComparer.Instance);
        return result;
    }

    private static LivingTownMemorySeed[] BuildMemories(TownSourceMemoryConfiguration[] values)
    {
        var result = new LivingTownMemorySeed[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            TownSourceMemoryConfiguration value = values[index];
            SourceEventId source = new(value.SourceEventId);
            result[index] = new LivingTownMemorySeed(
                value.MemoryId,
                source,
                value.OccurredAtTicks,
                value.ActorVisibleText,
                new MemoryEmotion(
                    Enum.Parse<LivingTownEmotionKind>(value.Emotion.Kind, false),
                    value.Emotion.Valence,
                    value.Emotion.Intensity,
                    source,
                    value.OccurredAtTicks));
        }
        Array.Sort(result, LivingTownMemoryComparer.Instance);
        return result;
    }

    private sealed class TownScheduleEntryComparer : IComparer<TownScheduleEntryConfiguration>
    {
        public static TownScheduleEntryComparer Instance { get; } = new();
        public int Compare(TownScheduleEntryConfiguration? left, TownScheduleEntryConfiguration? right) =>
            StringComparer.Ordinal.Compare(left?.EntryId, right?.EntryId);
    }

    private sealed class LivingTownCapabilityComparer : IComparer<LivingTownCapabilityProfile>
    {
        public static LivingTownCapabilityComparer Instance { get; } = new();
        public int Compare(LivingTownCapabilityProfile? left, LivingTownCapabilityProfile? right) => StringComparer.Ordinal.Compare(left?.CapabilityId, right?.CapabilityId);
    }

    private sealed class LivingTownSkillComparer : IComparer<LivingTownSkillProfile>
    {
        public static LivingTownSkillComparer Instance { get; } = new();
        public int Compare(LivingTownSkillProfile? left, LivingTownSkillProfile? right) => StringComparer.Ordinal.Compare(left?.SkillId, right?.SkillId);
    }

    private sealed class LivingTownWeightedReferenceComparer : IComparer<LivingTownWeightedReference>
    {
        public static LivingTownWeightedReferenceComparer Instance { get; } = new();
        public int Compare(LivingTownWeightedReference? left, LivingTownWeightedReference? right) => StringComparer.Ordinal.Compare(left?.RefId, right?.RefId);
    }

    private sealed class SourceEventIdComparer : IComparer<SourceEventId>
    {
        public static SourceEventIdComparer Instance { get; } = new();
        public int Compare(SourceEventId? left, SourceEventId? right) => StringComparer.Ordinal.Compare(left?.Value, right?.Value);
    }

    private sealed class LivingTownMemoryComparer : IComparer<LivingTownMemorySeed>
    {
        public static LivingTownMemoryComparer Instance { get; } = new();
        public int Compare(LivingTownMemorySeed? left, LivingTownMemorySeed? right) => StringComparer.Ordinal.Compare(left?.MemoryId, right?.MemoryId);
    }
}
