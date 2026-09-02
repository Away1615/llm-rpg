using System.Text.Json.Serialization;

namespace Alice.LivingTown;

public sealed record TownPopulationManifestDocument
{
    [JsonRequired, JsonPropertyName("manifest_id")] public string ManifestId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("town_id")] public string TownId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("place_refs")] public string[] PlaceRefs { get; init; } = [];
    [JsonRequired, JsonPropertyName("places")] public TownPlaceConfiguration[] Places { get; init; } = [];
    [JsonRequired, JsonPropertyName("sleep_facilities")] public TownSleepFacilityConfiguration[] SleepFacilities { get; init; } = [];
    [JsonRequired, JsonPropertyName("public_events")] public TownPublicEventConfiguration[] PublicEvents { get; init; } = [];
    [JsonRequired, JsonPropertyName("households")] public TownHouseholdConfiguration[] Households { get; init; } = [];
    [JsonRequired, JsonPropertyName("occupations")] public TownOccupationConfiguration[] Occupations { get; init; } = [];
    [JsonRequired, JsonPropertyName("actors")] public TownNpcConfiguration[] Actors { get; init; } = [];
}

public sealed record TownHouseholdConfiguration
{
    [JsonRequired, JsonPropertyName("household_id")] public string HouseholdId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("settlement_id")] public string SettlementId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("residence_place_ref")] public string ResidencePlaceRef { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("member_actor_ids")] public string[] MemberActorIds { get; init; } = [];
    [JsonRequired, JsonPropertyName("member_relations")] public TownHouseholdRelationConfiguration[] MemberRelations { get; init; } = [];
    [JsonRequired, JsonPropertyName("shared_access_place_ids")] public string[] SharedAccessPlaceIds { get; init; } = [];
    [JsonRequired, JsonPropertyName("limited_responsibility_asset_ids")] public string[] LimitedResponsibilityAssetIds { get; init; } = [];
}

public sealed record TownHouseholdRelationConfiguration
{
    [JsonRequired, JsonPropertyName("first_actor_id")] public string FirstActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("second_actor_id")] public string SecondActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("first_to_second")] public string FirstToSecond { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("second_to_first")] public string SecondToFirst { get; init; } = string.Empty;
}

public sealed record TownOccupationConfiguration
{
    [JsonRequired, JsonPropertyName("occupation_id")] public string OccupationId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("settlement_id")] public string SettlementId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("worker_actor_ids")] public string[] WorkerActorIds { get; init; } = [];
    [JsonRequired, JsonPropertyName("workplace_place_ref")] public string WorkplacePlaceRef { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("inputs")] public TownOccupationInputConfiguration[] Inputs { get; init; } = [];
    [JsonRequired, JsonPropertyName("output_asset_ids")] public string[] OutputAssetIds { get; init; } = [];
    [JsonRequired, JsonPropertyName("listing_ids")] public string[] ListingIds { get; init; } = [];
    [JsonRequired, JsonPropertyName("restock_ids")] public string[] RestockIds { get; init; } = [];
}

public sealed record TownOccupationInputConfiguration
{
    [JsonRequired, JsonPropertyName("asset_id")] public string AssetId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("source_ids")] public string[] SourceIds { get; init; } = [];
}

public sealed record TownPublicEventConfiguration
{
    [JsonRequired, JsonPropertyName("event_id")] public string EventId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("host_actor_id")] public string HostActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("place_ref")] public string PlaceRef { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("starts_at_tick_of_day")] public long StartsAtTickOfDay { get; init; }
    [JsonRequired, JsonPropertyName("ends_at_tick_of_day")] public long EndsAtTickOfDay { get; init; }
    [JsonRequired, JsonPropertyName("participant_actor_ids")] public string[] ParticipantActorIds { get; init; } = [];
    [JsonRequired, JsonPropertyName("source_ref")] public string SourceRef { get; init; } = string.Empty;
}

public sealed record TownNpcConfiguration
{
    [JsonRequired, JsonPropertyName("identity")] public TownNpcIdentityConfiguration Identity { get; init; } = new();
    [JsonRequired, JsonPropertyName("appearance")] public TownNpcAppearanceConfiguration Appearance { get; init; } = new();
    [JsonRequired, JsonPropertyName("body")] public TownNpcBodyConfiguration Body { get; init; } = new();
    [JsonRequired, JsonPropertyName("current_emotion")] public TownNpcEmotionConfiguration CurrentEmotion { get; init; } = new();
    [JsonRequired, JsonPropertyName("start_world_x")] public double StartWorldX { get; init; }
    [JsonRequired, JsonPropertyName("start_world_y")] public double StartWorldY { get; init; }
    [JsonRequired, JsonPropertyName("settlement_id")] public string SettlementId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("residence_place_ref")] public string? ResidencePlaceRef { get; init; }
    [JsonRequired, JsonPropertyName("private_room_place_ref")] public string? PrivateRoomPlaceRef { get; init; }
    [JsonRequired, JsonPropertyName("household_id")] public string? HouseholdId { get; init; }
    [JsonRequired, JsonPropertyName("occupation_id")] public string? OccupationId { get; init; }
    [JsonRequired, JsonPropertyName("workplace_place_ref")] public string? WorkplacePlaceRef { get; init; }
    [JsonRequired, JsonPropertyName("role_ids")] public string[] RoleIds { get; init; } = [];
    [JsonRequired, JsonPropertyName("schedule")] public TownScheduleEntryConfiguration[] Schedule { get; init; } = [];
    [JsonRequired, JsonPropertyName("personality")] public TownNpcPersonalityConfiguration Personality { get; init; } = new();
    [JsonRequired, JsonPropertyName("capabilities")] public TownCapabilityConfiguration[] Capabilities { get; init; } = [];
    [JsonRequired, JsonPropertyName("skills")] public TownSkillConfiguration[] Skills { get; init; } = [];
    [JsonRequired, JsonPropertyName("inventory")] public TownInventoryConfiguration Inventory { get; init; } = new();
    [JsonRequired, JsonPropertyName("currency")] public TownCurrencyConfiguration[] Currency { get; init; } = [];
    [JsonRequired, JsonPropertyName("access_refs")] public string[] AccessRefs { get; init; } = [];
    [JsonRequired, JsonPropertyName("interest_ids")] public string[] InterestIds { get; init; } = [];
    [JsonRequired, JsonPropertyName("place_preferences")] public TownWeightedReferenceConfiguration[] PlacePreferences { get; init; } = [];
    [JsonRequired, JsonPropertyName("social_preferences")] public TownWeightedReferenceConfiguration[] SocialPreferences { get; init; } = [];
    [JsonRequired, JsonPropertyName("aspiration_ids")] public string[] AspirationIds { get; init; } = [];
    [JsonRequired, JsonPropertyName("initial_goal_refs")] public string[] InitialGoalRefs { get; init; } = [];
    [JsonRequired, JsonPropertyName("relationships")] public TownRelationshipConfiguration[] Relationships { get; init; } = [];
    [JsonRequired, JsonPropertyName("commitment_refs")] public string[] CommitmentRefs { get; init; } = [];
    [JsonRequired, JsonPropertyName("knowledge")] public TownKnowledgeConfiguration Knowledge { get; init; } = new();
    [JsonRequired, JsonPropertyName("memories")] public TownSourceMemoryConfiguration[] Memories { get; init; } = [];
    [JsonRequired, JsonPropertyName("dialogue_style_id")] public string? DialogueStyleId { get; init; }
    [JsonRequired, JsonPropertyName("display_style_id")] public string? DisplayStyleId { get; init; }
}

public sealed record TownPlaceConfiguration
{
    [JsonRequired, JsonPropertyName("place_ref")] public string PlaceRef { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("world_x")] public double WorldX { get; init; }
    [JsonRequired, JsonPropertyName("world_y")] public double WorldY { get; init; }
}

public sealed record TownSleepFacilityConfiguration
{
    [JsonRequired, JsonPropertyName("facility_id")] public string FacilityId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("place_ref")] public string PlaceRef { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("target_ref")] public string TargetRef { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("contract_id")] public string ContractId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("contract_version")] public long ContractVersion { get; init; }
    [JsonRequired, JsonPropertyName("interaction_range")] public double InteractionRange { get; init; }
    [JsonRequired, JsonPropertyName("capability_id")] public string CapabilityId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("access_policy")] public string AccessPolicy { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("required_access_ref")] public string? RequiredAccessRef { get; init; }
    [JsonRequired, JsonPropertyName("capacity")] public int Capacity { get; init; }
    [JsonRequired, JsonPropertyName("duration_ticks")] public long DurationTicks { get; init; }
    [JsonRequired, JsonPropertyName("spirit_restore")] public int SpiritRestore { get; init; }
}

public sealed record TownNpcIdentityConfiguration
{
    [JsonRequired, JsonPropertyName("actor_id")] public string ActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("age")] public int Age { get; init; }
}

public sealed record TownNpcAppearanceConfiguration
{
    [JsonRequired, JsonPropertyName("fill_color")] public string FillColor { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("border_color")] public string BorderColor { get; init; } = string.Empty;
}

public sealed record TownNpcBodyConfiguration
{
    [JsonRequired, JsonPropertyName("health_current")] public int HealthCurrent { get; init; }
    [JsonRequired, JsonPropertyName("health_maximum")] public int HealthMaximum { get; init; }
    [JsonRequired, JsonPropertyName("satiety")] public int Satiety { get; init; }
    [JsonRequired, JsonPropertyName("spirit")] public int Spirit { get; init; }
    [JsonRequired, JsonPropertyName("disease")] public string Disease { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("movement_mode")] public string MovementMode { get; init; } = string.Empty;
}

public sealed record TownNpcEmotionConfiguration
{
    [JsonRequired, JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("valence")] public double Valence { get; init; }
    [JsonRequired, JsonPropertyName("intensity")] public double Intensity { get; init; }
    [JsonRequired, JsonPropertyName("source_event_id")] public string? SourceEventId { get; init; }
}

public sealed record TownScheduleEntryConfiguration
{
    [JsonRequired, JsonPropertyName("entry_id")] public string EntryId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("recurrence_id")] public string RecurrenceId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("starts_at_tick_of_day")] public long StartsAtTickOfDay { get; init; }
    [JsonRequired, JsonPropertyName("ends_at_tick_of_day")] public long EndsAtTickOfDay { get; init; }
    [JsonRequired, JsonPropertyName("place_ref")] public string? PlaceRef { get; init; }
    [JsonRequired, JsonPropertyName("purpose")] public string Purpose { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("obligation")] public string Obligation { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("commitment_ref")] public string? CommitmentRef { get; init; }
    [JsonRequired, JsonPropertyName("source_ref")] public string? SourceRef { get; init; }
    [JsonRequired, JsonPropertyName("execution_mode")] public string ExecutionMode { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("action_family_id")] public string? ActionFamilyId { get; init; }
    [JsonRequired, JsonPropertyName("target_ref")] public string? TargetRef { get; init; }
}

public sealed record TownNpcPersonalityConfiguration
{
    [JsonRequired, JsonPropertyName("cognitive_function_values")] public double[] CognitiveFunctionValues { get; init; } = [];
    [JsonRequired, JsonPropertyName("trait_ids")] public string[] TraitIds { get; init; } = [];
    [JsonRequired, JsonPropertyName("values")] public TownWeightedReferenceConfiguration[] Values { get; init; } = [];
}

public sealed record TownWeightedReferenceConfiguration
{
    [JsonRequired, JsonPropertyName("ref_id")] public string RefId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("weight")] public double Weight { get; init; }
}

public sealed record TownCapabilityConfiguration
{
    [JsonRequired, JsonPropertyName("capability_id")] public string CapabilityId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("value")] public int Value { get; init; }
}

public sealed record TownSkillConfiguration
{
    [JsonRequired, JsonPropertyName("skill_id")] public string SkillId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("level")] public int Level { get; init; }
}

public sealed record TownInventoryConfiguration
{
    [JsonRequired, JsonPropertyName("version")] public int Version { get; init; }
    [JsonRequired, JsonPropertyName("equipment_version")] public int EquipmentVersion { get; init; }
    [JsonRequired, JsonPropertyName("stacks")] public TownStackConfiguration[] Stacks { get; init; } = [];
    [JsonRequired, JsonPropertyName("instances")] public TownItemInstanceConfiguration[] Instances { get; init; } = [];
    [JsonRequired, JsonPropertyName("equipped_hand_instance_id")] public string? EquippedHandInstanceId { get; init; }
}

public sealed record TownStackConfiguration
{
    [JsonRequired, JsonPropertyName("item_type_id")] public string ItemTypeId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("quantity")] public int Quantity { get; init; }
}

public sealed record TownItemInstanceConfiguration
{
    [JsonRequired, JsonPropertyName("item_instance_id")] public string ItemInstanceId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("item_type_id")] public string ItemTypeId { get; init; } = string.Empty;
}

public sealed record TownCurrencyConfiguration
{
    [JsonRequired, JsonPropertyName("currency_id")] public string CurrencyId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("quantity")] public long Quantity { get; init; }
}

public sealed record TownRelationshipConfiguration
{
    [JsonRequired, JsonPropertyName("other_actor_id")] public string OtherActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("familiarity")] public double Familiarity { get; init; }
    [JsonRequired, JsonPropertyName("trust")] public double Trust { get; init; }
    [JsonRequired, JsonPropertyName("affection")] public double Affection { get; init; }
    [JsonRequired, JsonPropertyName("respect")] public double Respect { get; init; }
    [JsonRequired, JsonPropertyName("fear")] public double Fear { get; init; }
    [JsonRequired, JsonPropertyName("grievance")] public double Grievance { get; init; }
}

public sealed record TownKnowledgeConfiguration
{
    [JsonRequired, JsonPropertyName("known_place_refs")] public string[] KnownPlaceRefs { get; init; } = [];
    [JsonRequired, JsonPropertyName("known_actor_ids")] public string[] KnownActorIds { get; init; } = [];
    [JsonRequired, JsonPropertyName("source_event_ids")] public string[] SourceEventIds { get; init; } = [];
}

public sealed record TownSourceMemoryConfiguration
{
    [JsonRequired, JsonPropertyName("memory_id")] public string MemoryId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("source_event_id")] public string SourceEventId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("occurred_at_ticks")] public long OccurredAtTicks { get; init; }
    [JsonRequired, JsonPropertyName("actor_visible_text")] public string ActorVisibleText { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("emotion")] public TownNpcEmotionConfiguration Emotion { get; init; } = new();
}
