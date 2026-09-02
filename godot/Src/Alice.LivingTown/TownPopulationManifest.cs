using System.Collections.ObjectModel;

namespace Alice.LivingTown;

public sealed class TownPopulationManifest
{
    private TownPopulationManifest(TownPopulationManifestDocument document)
    {
        ManifestId = new TownPopulationManifestId(document.ManifestId);
        TownId = new TownId(document.TownId);
        PlaceRefs = Array.AsReadOnly(document.PlaceRefs.Select(CreatePlaceRef).ToArray());
        Places = Array.AsReadOnly(document.Places.OrderBy(GetPlaceId, StringComparer.Ordinal).ToArray());
        SleepFacilities = Array.AsReadOnly(document.SleepFacilities.OrderBy(GetSleepFacilityId, StringComparer.Ordinal).ToArray());
        PublicEvents = Array.AsReadOnly(document.PublicEvents.OrderBy(GetPublicEventId, StringComparer.Ordinal).ToArray());
        Households = Array.AsReadOnly(document.Households.OrderBy(GetHouseholdId, StringComparer.Ordinal).ToArray());
        Occupations = Array.AsReadOnly(document.Occupations.OrderBy(GetOccupationId, StringComparer.Ordinal).ToArray());
        Actors = Array.AsReadOnly(document.Actors.OrderBy(GetActorId, StringComparer.Ordinal).ToArray());
    }

    public TownPopulationManifestId ManifestId { get; }
    public TownId TownId { get; }
    public IReadOnlyList<LivingTownPlaceRef> PlaceRefs { get; }
    public IReadOnlyList<TownPlaceConfiguration> Places { get; }
    public IReadOnlyList<TownSleepFacilityConfiguration> SleepFacilities { get; }
    public IReadOnlyList<TownPublicEventConfiguration> PublicEvents { get; }
    public IReadOnlyList<TownHouseholdConfiguration> Households { get; }
    public IReadOnlyList<TownOccupationConfiguration> Occupations { get; }
    public IReadOnlyList<TownNpcConfiguration> Actors { get; }

    internal static TownPopulationManifest Create(TownPopulationManifestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        TownPopulationManifestValidator.Validate(document);
        return new TownPopulationManifest(document);
    }

    private static LivingTownPlaceRef CreatePlaceRef(string value) => new(value);
    private static string GetActorId(TownNpcConfiguration actor) => actor.Identity.ActorId;
    private static string GetPlaceId(TownPlaceConfiguration place) => place.PlaceRef;
    private static string GetSleepFacilityId(TownSleepFacilityConfiguration facility) => facility.FacilityId;
    private static string GetPublicEventId(TownPublicEventConfiguration publicEvent) => publicEvent.EventId;
    private static string GetHouseholdId(TownHouseholdConfiguration household) => household.HouseholdId;
    private static string GetOccupationId(TownOccupationConfiguration occupation) => occupation.OccupationId;
}

public static class TownPopulationManifestValidator
{
    public static void Validate(TownPopulationManifestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        RequireIdentity(document.ManifestId, nameof(document.ManifestId));
        RequireIdentity(document.TownId, nameof(document.TownId));
        ArgumentNullException.ThrowIfNull(document.PlaceRefs);
        ArgumentNullException.ThrowIfNull(document.Places);
        ArgumentNullException.ThrowIfNull(document.SleepFacilities);
        ArgumentNullException.ThrowIfNull(document.PublicEvents);
        ArgumentNullException.ThrowIfNull(document.Households);
        ArgumentNullException.ThrowIfNull(document.Occupations);
        ArgumentNullException.ThrowIfNull(document.Actors);
        if (document.PlaceRefs.Length == 0 || document.Actors.Length == 0)
        {
            throw new ArgumentException("A Town population requires at least one place and one Actor.", nameof(document));
        }

        HashSet<string> places = RequireUniqueIdentities(document.PlaceRefs, nameof(document.PlaceRefs));
        ValidatePlaces(document.Places, places);
        ValidateSleepFacilities(document.SleepFacilities, places);
        var actors = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownNpcConfiguration actor in document.Actors)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(actor.Identity);
            RequireIdentity(actor.Identity.ActorId, nameof(actor.Identity.ActorId));
            if (!actors.Add(actor.Identity.ActorId))
            {
                throw new ArgumentException("Population ActorIds must be unique.", nameof(document.Actors));
            }
        }

        foreach (TownNpcConfiguration actor in document.Actors)
        {
            ValidateActor(actor, actors, places);
        }
        ValidateHouseholds(document.Households, document.Actors, actors, places);
        ValidateOccupations(document.Occupations, document.Actors, actors, places);
        ValidatePublicEvents(document.PublicEvents, document.Actors, actors, places);
    }

    private static void ValidateHouseholds(
        TownHouseholdConfiguration[] households,
        TownNpcConfiguration[] actorConfigurations,
        HashSet<string> actorIds,
        HashSet<string> placeRefs)
    {
        var householdIds = new HashSet<string>(StringComparer.Ordinal);
        var assignedActors = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownHouseholdConfiguration household in households)
        {
            RequireIdentity(household.HouseholdId, nameof(household.HouseholdId));
            RequireIdentity(household.SettlementId, nameof(household.SettlementId));
            RequireIdentity(household.ResidencePlaceRef, nameof(household.ResidencePlaceRef));
            HashSet<string> members = RequireUniqueIdentities(household.MemberActorIds, nameof(household.MemberActorIds));
            if (!householdIds.Add(household.HouseholdId) || members.Count == 0
                || !placeRefs.Contains(household.ResidencePlaceRef)
                || members.Any(actorId => !actorIds.Contains(actorId) || !assignedActors.Add(actorId)))
                throw new ArgumentException("Household identity, residence or membership is invalid.", nameof(households));

            HashSet<string> sharedAccess = RequireUniqueIdentities(household.SharedAccessPlaceIds, nameof(household.SharedAccessPlaceIds));
            if (!sharedAccess.Contains(household.ResidencePlaceRef) || sharedAccess.Any(value => !placeRefs.Contains(value)))
                throw new ArgumentException("Household shared access must include its residence.", nameof(households));
            RequireUniqueIdentities(household.LimitedResponsibilityAssetIds, nameof(household.LimitedResponsibilityAssetIds));

            var relationPairs = new HashSet<string>(StringComparer.Ordinal);
            foreach (TownHouseholdRelationConfiguration relation in household.MemberRelations)
            {
                RequireIdentity(relation.FirstToSecond, nameof(relation.FirstToSecond));
                RequireIdentity(relation.SecondToFirst, nameof(relation.SecondToFirst));
                if (relation.FirstActorId == relation.SecondActorId
                    || !members.Contains(relation.FirstActorId) || !members.Contains(relation.SecondActorId))
                    throw new ArgumentException("Household relation must join two admitted members.", nameof(households));
                string pair = StringComparer.Ordinal.Compare(relation.FirstActorId, relation.SecondActorId) < 0
                    ? $"{relation.FirstActorId}/{relation.SecondActorId}"
                    : $"{relation.SecondActorId}/{relation.FirstActorId}";
                if (!relationPairs.Add(pair)) throw new ArgumentException("Household relation pair is duplicated.", nameof(households));
            }
            if (relationPairs.Count != members.Count * (members.Count - 1) / 2)
                throw new ArgumentException("Household member relations must explicitly cover every reciprocal pair.", nameof(households));

            foreach (string memberId in members)
            {
                TownNpcConfiguration actor = actorConfigurations.Single(value => value.Identity.ActorId == memberId);
                if (actor.HouseholdId != household.HouseholdId || actor.SettlementId != household.SettlementId
                    || actor.ResidencePlaceRef != household.ResidencePlaceRef
                    || actor.PrivateRoomPlaceRef is null)
                    throw new ArgumentException("Actor household projection does not match the household graph.", nameof(households));
            }
        }
        if (assignedActors.Count != actorIds.Count)
            throw new ArgumentException("Every Actor must belong to exactly one household.", nameof(households));
    }

    private static void ValidateOccupations(
        TownOccupationConfiguration[] occupations,
        TownNpcConfiguration[] actorConfigurations,
        HashSet<string> actorIds,
        HashSet<string> placeRefs)
    {
        var occupationIds = new HashSet<string>(StringComparer.Ordinal);
        var assignedActors = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownOccupationConfiguration occupation in occupations)
        {
            RequireIdentity(occupation.OccupationId, nameof(occupation.OccupationId));
            RequireIdentity(occupation.SettlementId, nameof(occupation.SettlementId));
            RequireIdentity(occupation.WorkplacePlaceRef, nameof(occupation.WorkplacePlaceRef));
            HashSet<string> workers = RequireUniqueIdentities(occupation.WorkerActorIds, nameof(occupation.WorkerActorIds));
            if (!occupationIds.Add(occupation.OccupationId) || workers.Count == 0
                || !placeRefs.Contains(occupation.WorkplacePlaceRef)
                || workers.Any(actorId => !actorIds.Contains(actorId) || !assignedActors.Add(actorId)))
                throw new ArgumentException("Occupation identity, workplace or worker membership is invalid.", nameof(occupations));
            RequireUniqueIdentities(occupation.OutputAssetIds, nameof(occupation.OutputAssetIds));
            RequireUniqueIdentities(occupation.ListingIds, nameof(occupation.ListingIds));
            RequireUniqueIdentities(occupation.RestockIds, nameof(occupation.RestockIds));
            var inputAssets = new HashSet<string>(StringComparer.Ordinal);
            foreach (TownOccupationInputConfiguration input in occupation.Inputs)
            {
                RequireIdentity(input.AssetId, nameof(input.AssetId));
                if (!inputAssets.Add(input.AssetId)
                    || RequireUniqueIdentities(input.SourceIds, nameof(input.SourceIds)).Count == 0)
                    throw new ArgumentException("Occupation inputs require at least one source.", nameof(occupations));
            }
            foreach (string workerId in workers)
            {
                TownNpcConfiguration actor = actorConfigurations.Single(value => value.Identity.ActorId == workerId);
                if (actor.OccupationId != occupation.OccupationId || actor.WorkplacePlaceRef != occupation.WorkplacePlaceRef)
                    throw new ArgumentException("Actor occupation projection does not match the occupation graph.", nameof(occupations));
            }
        }
        if (assignedActors.Count != actorIds.Count)
            throw new ArgumentException("Every Actor must belong to exactly one occupation.", nameof(occupations));
    }

    private static void ValidatePublicEvents(
        TownPublicEventConfiguration[] publicEvents,
        TownNpcConfiguration[] actorConfigurations,
        HashSet<string> actorIds,
        HashSet<string> placeRefs)
    {
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownPublicEventConfiguration publicEvent in publicEvents)
        {
            ArgumentNullException.ThrowIfNull(publicEvent);
            RequireIdentity(publicEvent.EventId, nameof(publicEvent.EventId));
            RequireIdentity(publicEvent.Title, nameof(publicEvent.Title));
            RequireIdentity(publicEvent.HostActorId, nameof(publicEvent.HostActorId));
            RequireIdentity(publicEvent.PlaceRef, nameof(publicEvent.PlaceRef));
            RequireIdentity(publicEvent.SourceRef, nameof(publicEvent.SourceRef));
            if (!eventIds.Add(publicEvent.EventId)
                || !actorIds.Contains(publicEvent.HostActorId)
                || !placeRefs.Contains(publicEvent.PlaceRef)
                || publicEvent.StartsAtTickOfDay < 0
                || publicEvent.EndsAtTickOfDay <= publicEvent.StartsAtTickOfDay)
            {
                throw new ArgumentException("Public event identity, host, place, or time window is invalid.", nameof(publicEvents));
            }

            HashSet<string> participants = RequireUniqueIdentities(
                publicEvent.ParticipantActorIds,
                nameof(publicEvent.ParticipantActorIds));
            if (participants.Count < 2
                || !participants.Contains(publicEvent.HostActorId)
                || participants.Any(actorId => !actorIds.Contains(actorId)))
            {
                throw new ArgumentException("Public events require at least two known population participants including the host.", nameof(publicEvents));
            }

            foreach (string participantId in participants)
            {
                TownNpcConfiguration participant = actorConfigurations.Single(value =>
                    StringComparer.Ordinal.Equals(value.Identity.ActorId, participantId));
                TownScheduleEntryConfiguration[] bindings = participant.Schedule.Where(entry =>
                    StringComparer.Ordinal.Equals(entry.CommitmentRef, publicEvent.EventId)).ToArray();
                if (bindings.Length != 1
                    || !StringComparer.Ordinal.Equals(bindings[0].Purpose, SchedulePurpose.Social.ToString())
                    || !StringComparer.Ordinal.Equals(bindings[0].PlaceRef, publicEvent.PlaceRef)
                    || bindings[0].StartsAtTickOfDay != publicEvent.StartsAtTickOfDay
                    || bindings[0].EndsAtTickOfDay != publicEvent.EndsAtTickOfDay
                    || !StringComparer.Ordinal.Equals(bindings[0].SourceRef, publicEvent.SourceRef)
                    || !participant.CommitmentRefs.Contains(publicEvent.EventId, StringComparer.Ordinal)
                    || !participant.Knowledge.KnownPlaceRefs.Contains(publicEvent.PlaceRef, StringComparer.Ordinal))
                {
                    throw new ArgumentException($"Public event {publicEvent.EventId} is not exactly bound to participant {participantId}.", nameof(publicEvents));
                }
            }
        }

        foreach (TownNpcConfiguration actor in actorConfigurations)
        {
            foreach (TownScheduleEntryConfiguration entry in actor.Schedule)
            {
                if (entry.CommitmentRef is not null && !eventIds.Contains(entry.CommitmentRef))
                    throw new ArgumentException($"Schedule {entry.EntryId} references an unknown public event.", nameof(publicEvents));
            }
            foreach (string commitmentRef in actor.CommitmentRefs)
            {
                if (!eventIds.Contains(commitmentRef))
                    throw new ArgumentException($"Actor {actor.Identity.ActorId} references an unknown public event commitment.", nameof(publicEvents));
            }
        }
    }

    private static void ValidateActor(TownNpcConfiguration actor, HashSet<string> actorIds, HashSet<string> placeRefs)
    {
        TownNpcIdentityConfiguration identity = actor.Identity;
        RequireIdentity(identity.Name, nameof(identity.Name));
        if (identity.Age < 0) throw new ArgumentOutOfRangeException(nameof(identity.Age));

        ArgumentNullException.ThrowIfNull(actor.Appearance);
        RequireIdentity(actor.Appearance.FillColor, nameof(actor.Appearance.FillColor));
        RequireIdentity(actor.Appearance.BorderColor, nameof(actor.Appearance.BorderColor));
        ValidateBody(actor.Body);
        ValidateEmotion(actor.CurrentEmotion, false);
        if (!double.IsFinite(actor.StartWorldX) || !double.IsFinite(actor.StartWorldY))
            throw new ArgumentException("Actor starting position must be finite.", nameof(actor));
        RequireIdentity(actor.SettlementId, nameof(actor.SettlementId));
        ValidateOptionalIdentity(actor.ResidencePlaceRef, nameof(actor.ResidencePlaceRef));
        ValidateOptionalIdentity(actor.PrivateRoomPlaceRef, nameof(actor.PrivateRoomPlaceRef));
        ValidateOptionalIdentity(actor.HouseholdId, nameof(actor.HouseholdId));
        ValidateOptionalIdentity(actor.OccupationId, nameof(actor.OccupationId));
        ValidateOptionalIdentity(actor.WorkplacePlaceRef, nameof(actor.WorkplacePlaceRef));
        RequireKnownPlace(actor.ResidencePlaceRef, placeRefs, nameof(actor.ResidencePlaceRef));
        RequireKnownPlace(actor.PrivateRoomPlaceRef, placeRefs, nameof(actor.PrivateRoomPlaceRef));
        RequireKnownPlace(actor.WorkplacePlaceRef, placeRefs, nameof(actor.WorkplacePlaceRef));

        RequireUniqueIdentities(actor.RoleIds, nameof(actor.RoleIds));
        ValidateSchedule(actor.Schedule, placeRefs);
        ValidatePersonality(actor.Personality);
        ValidateCapabilities(actor.Capabilities);
        ValidateSkills(actor.Skills);
        ValidateInventory(actor.Inventory);
        ValidateCurrency(actor.Currency);
        RequireUniqueIdentities(actor.AccessRefs, nameof(actor.AccessRefs));
        RequireUniqueIdentities(actor.InterestIds, nameof(actor.InterestIds));
        ValidateWeightedReferences(actor.PlacePreferences, nameof(actor.PlacePreferences));
        foreach (TownWeightedReferenceConfiguration preference in actor.PlacePreferences)
        {
            RequireKnownPlace(preference.RefId, placeRefs, nameof(actor.PlacePreferences));
        }
        ValidateWeightedReferences(actor.SocialPreferences, nameof(actor.SocialPreferences));
        foreach (TownWeightedReferenceConfiguration preference in actor.SocialPreferences)
        {
            if (!actorIds.Contains(preference.RefId))
            {
                throw new ArgumentException("Social preference references an Actor outside the population.", nameof(actor.SocialPreferences));
            }
        }
        RequireUniqueIdentities(actor.AspirationIds, nameof(actor.AspirationIds));
        RequireUniqueIdentities(actor.InitialGoalRefs, nameof(actor.InitialGoalRefs));
        ValidateRelationships(actor.Identity.ActorId, actor.Relationships, actorIds);
        RequireUniqueIdentities(actor.CommitmentRefs, nameof(actor.CommitmentRefs));
        ValidateKnowledge(actor.Knowledge, actorIds, placeRefs);
        ValidateMemories(actor.Memories);
        ValidateOptionalIdentity(actor.DialogueStyleId, nameof(actor.DialogueStyleId));
        ValidateOptionalIdentity(actor.DisplayStyleId, nameof(actor.DisplayStyleId));
    }

    private static void ValidateBody(TownNpcBodyConfiguration body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.HealthMaximum <= 0 || body.HealthCurrent < 0 || body.HealthCurrent > body.HealthMaximum
            || body.Satiety is < 0 or > 100 || body.Spirit is < 0 or > 100
            || !Enum.TryParse(body.Disease, false, out Alice.Actors.Disease _)
            || !Enum.TryParse(body.MovementMode, false, out Alice.Actors.MovementMode _))
        {
            throw new ArgumentException("Actor body configuration is invalid.", nameof(body));
        }
    }

    private static void ValidatePlaces(TownPlaceConfiguration[] configurations, HashSet<string> placeRefs)
    {
        if (configurations.Length != placeRefs.Count) throw new ArgumentException("Every PlaceRef requires one exact spatial configuration.", nameof(configurations));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownPlaceConfiguration place in configurations)
        {
            ArgumentNullException.ThrowIfNull(place);
            RequireIdentity(place.PlaceRef, nameof(place.PlaceRef));
            if (!placeRefs.Contains(place.PlaceRef)
                || !seen.Add(place.PlaceRef)
                || !double.IsFinite(place.WorldX)
                || !double.IsFinite(place.WorldY))
            {
                throw new ArgumentException("Town place configuration is invalid or duplicated.", nameof(configurations));
            }
        }
    }

    private static void ValidateSleepFacilities(TownSleepFacilityConfiguration[] facilities, HashSet<string> placeRefs)
    {
        var facilityIds = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        var contracts = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownSleepFacilityConfiguration facility in facilities)
        {
            ArgumentNullException.ThrowIfNull(facility);
            RequireIdentity(facility.FacilityId, nameof(facility.FacilityId));
            RequireIdentity(facility.PlaceRef, nameof(facility.PlaceRef));
            RequireIdentity(facility.TargetRef, nameof(facility.TargetRef));
            RequireIdentity(facility.ContractId, nameof(facility.ContractId));
            RequireIdentity(facility.CapabilityId, nameof(facility.CapabilityId));
            ValidateOptionalIdentity(facility.RequiredAccessRef, nameof(facility.RequiredAccessRef));
            if (!facilityIds.Add(facility.FacilityId)
                || !targets.Add(facility.TargetRef)
                || !contracts.Add(facility.ContractId)
                || !placeRefs.Contains(facility.PlaceRef)
                || !Enum.TryParse(facility.AccessPolicy, false, out SleepAccessPolicy accessPolicy)
                || facility.ContractVersion <= 0
                || !double.IsFinite(facility.InteractionRange) || facility.InteractionRange < 0
                || facility.Capacity <= 0
                || facility.DurationTicks <= 0
                || facility.SpiritRestore <= 0
                || accessPolicy == SleepAccessPolicy.Public && facility.RequiredAccessRef is not null
                || accessPolicy != SleepAccessPolicy.Public && facility.RequiredAccessRef is null)
            {
                throw new ArgumentException("Sleep facility configuration is invalid or duplicated.", nameof(facilities));
            }
        }
    }

    private static void ValidateEmotion(TownNpcEmotionConfiguration emotion, bool sourceRequired)
    {
        ArgumentNullException.ThrowIfNull(emotion);
        if (!Enum.TryParse(emotion.Kind, false, out LivingTownEmotionKind _)
            || !double.IsFinite(emotion.Valence) || emotion.Valence < -1 || emotion.Valence > 1
            || !double.IsFinite(emotion.Intensity) || emotion.Intensity < 0 || emotion.Intensity > 1)
        {
            throw new ArgumentException("Emotion configuration is invalid.", nameof(emotion));
        }
        ValidateOptionalIdentity(emotion.SourceEventId, nameof(emotion.SourceEventId));
        if (sourceRequired && emotion.SourceEventId is null)
        {
            throw new ArgumentException("Memory emotion must preserve its source event identity.", nameof(emotion));
        }
    }

    private static void ValidateSchedule(TownScheduleEntryConfiguration[] schedule, HashSet<string> placeRefs)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownScheduleEntryConfiguration entry in schedule)
        {
            ArgumentNullException.ThrowIfNull(entry);
            RequireIdentity(entry.EntryId, nameof(entry.EntryId));
            RequireIdentity(entry.RecurrenceId, nameof(entry.RecurrenceId));
            if (!ids.Add(entry.EntryId)
                || entry.StartsAtTickOfDay < 0 || entry.EndsAtTickOfDay <= entry.StartsAtTickOfDay
                || !Enum.TryParse(entry.Purpose, false, out SchedulePurpose _)
                || !Enum.TryParse(entry.Obligation, false, out ScheduleObligation _))
            {
                throw new ArgumentException("Schedule entry is invalid or duplicated.", nameof(schedule));
            }
            ValidateOptionalIdentity(entry.PlaceRef, nameof(entry.PlaceRef));
            ValidateOptionalIdentity(entry.CommitmentRef, nameof(entry.CommitmentRef));
            ValidateOptionalIdentity(entry.SourceRef, nameof(entry.SourceRef));
            RequireIdentity(entry.ExecutionMode, nameof(entry.ExecutionMode));
            ValidateOptionalIdentity(entry.ActionFamilyId, nameof(entry.ActionFamilyId));
            ValidateOptionalIdentity(entry.TargetRef, nameof(entry.TargetRef));
            if (entry.ExecutionMode is not ("Navigate" or "Interact" or "Communicate" or "Wait")
                || (entry.ExecutionMode == "Interact" && (entry.ActionFamilyId is null || entry.TargetRef is null))
                || (entry.ExecutionMode != "Interact" && entry.ActionFamilyId is not null))
                throw new ArgumentException("Schedule execution binding is invalid.", nameof(schedule));
            RequireKnownPlace(entry.PlaceRef, placeRefs, nameof(entry.PlaceRef));
        }
    }

    private static void ValidatePersonality(TownNpcPersonalityConfiguration personality)
    {
        ArgumentNullException.ThrowIfNull(personality);
        ArgumentNullException.ThrowIfNull(personality.CognitiveFunctionValues);
        if (personality.CognitiveFunctionValues.Length != 8)
        {
            throw new ArgumentException("Personality requires eight cognitive-function values.", nameof(personality));
        }
        foreach (double value in personality.CognitiveFunctionValues)
        {
            RequireNormalized(value, nameof(personality.CognitiveFunctionValues));
        }
        HashSet<string> traits = RequireUniqueIdentities(personality.TraitIds, nameof(personality.TraitIds));
        if (traits.Count is < 2 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(personality.TraitIds), "Personality requires two through four traits.");
        }
        ValidateWeightedReferences(personality.Values, nameof(personality.Values));
    }

    private static void ValidateCapabilities(TownCapabilityConfiguration[] capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownCapabilityConfiguration capability in capabilities)
        {
            ArgumentNullException.ThrowIfNull(capability);
            RequireIdentity(capability.CapabilityId, nameof(capability.CapabilityId));
            if (!ids.Add(capability.CapabilityId) || capability.Value < 0)
            {
                throw new ArgumentException("Capability identity must be unique and value non-negative.", nameof(capabilities));
            }
        }
    }

    private static void ValidateSkills(TownSkillConfiguration[] skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownSkillConfiguration skill in skills)
        {
            ArgumentNullException.ThrowIfNull(skill);
            RequireIdentity(skill.SkillId, nameof(skill.SkillId));
            if (!ids.Add(skill.SkillId) || skill.Level < 0)
            {
                throw new ArgumentException("Skill identity must be unique and level non-negative.", nameof(skills));
            }
        }
    }

    private static void ValidateInventory(TownInventoryConfiguration inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(inventory.Stacks);
        ArgumentNullException.ThrowIfNull(inventory.Instances);
        if (inventory.Version <= 0 || inventory.EquipmentVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inventory));
        }
        var stackTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownStackConfiguration stack in inventory.Stacks)
        {
            ArgumentNullException.ThrowIfNull(stack);
            RequireIdentity(stack.ItemTypeId, nameof(stack.ItemTypeId));
            if (!stackTypes.Add(stack.ItemTypeId) || stack.Quantity is < 1 or > 10)
            {
                throw new ArgumentException("Inventory stack is invalid or duplicated.", nameof(inventory));
            }
        }
        var instances = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownItemInstanceConfiguration instance in inventory.Instances)
        {
            ArgumentNullException.ThrowIfNull(instance);
            RequireIdentity(instance.ItemInstanceId, nameof(instance.ItemInstanceId));
            RequireIdentity(instance.ItemTypeId, nameof(instance.ItemTypeId));
            if (!instances.Add(instance.ItemInstanceId))
            {
                throw new ArgumentException("Inventory instance identity must be unique.", nameof(inventory));
            }
        }
        ValidateOptionalIdentity(inventory.EquippedHandInstanceId, nameof(inventory.EquippedHandInstanceId));
        if (inventory.EquippedHandInstanceId is not null && !instances.Contains(inventory.EquippedHandInstanceId))
        {
            throw new ArgumentException("Equipped hand instance must be carried.", nameof(inventory));
        }
    }

    private static void ValidateCurrency(TownCurrencyConfiguration[] currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownCurrencyConfiguration balance in currency)
        {
            ArgumentNullException.ThrowIfNull(balance);
            RequireIdentity(balance.CurrencyId, nameof(balance.CurrencyId));
            if (!ids.Add(balance.CurrencyId) || balance.Quantity < 0)
            {
                throw new ArgumentException("Currency balance is invalid or duplicated.", nameof(currency));
            }
        }
    }

    private static void ValidateWeightedReferences(TownWeightedReferenceConfiguration[] values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownWeightedReferenceConfiguration value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            RequireIdentity(value.RefId, nameof(value.RefId));
            RequireNormalized(value.Weight, nameof(value.Weight));
            if (!identities.Add(value.RefId)) throw new ArgumentException("Weighted references must be unique.", parameterName);
        }
    }

    private static void ValidateRelationships(string actorId, TownRelationshipConfiguration[] relationships, HashSet<string> actorIds)
    {
        ArgumentNullException.ThrowIfNull(relationships);
        var others = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownRelationshipConfiguration relationship in relationships)
        {
            ArgumentNullException.ThrowIfNull(relationship);
            RequireIdentity(relationship.OtherActorId, nameof(relationship.OtherActorId));
            if (StringComparer.Ordinal.Equals(actorId, relationship.OtherActorId)
                || !actorIds.Contains(relationship.OtherActorId)
                || !others.Add(relationship.OtherActorId))
            {
                throw new ArgumentException("Relationship target must be a distinct population Actor.", nameof(relationships));
            }
            RequireNormalized(relationship.Familiarity, nameof(relationship.Familiarity));
            RequireNormalized(relationship.Trust, nameof(relationship.Trust));
            RequireNormalized(relationship.Affection, nameof(relationship.Affection));
            RequireNormalized(relationship.Respect, nameof(relationship.Respect));
            RequireNormalized(relationship.Fear, nameof(relationship.Fear));
            RequireNormalized(relationship.Grievance, nameof(relationship.Grievance));
        }
    }

    private static void ValidateKnowledge(TownKnowledgeConfiguration knowledge, HashSet<string> actorIds, HashSet<string> placeRefs)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        HashSet<string> knownPlaces = RequireUniqueIdentities(knowledge.KnownPlaceRefs, nameof(knowledge.KnownPlaceRefs));
        foreach (string knownPlace in knownPlaces) RequireKnownPlace(knownPlace, placeRefs, nameof(knowledge.KnownPlaceRefs));
        HashSet<string> knownActors = RequireUniqueIdentities(knowledge.KnownActorIds, nameof(knowledge.KnownActorIds));
        foreach (string knownActor in knownActors)
        {
            if (!actorIds.Contains(knownActor)) throw new ArgumentException("Knowledge references an Actor outside the population.", nameof(knowledge));
        }
        RequireUniqueIdentities(knowledge.SourceEventIds, nameof(knowledge.SourceEventIds));
    }

    private static void ValidateMemories(TownSourceMemoryConfiguration[] memories)
    {
        ArgumentNullException.ThrowIfNull(memories);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownSourceMemoryConfiguration memory in memories)
        {
            ArgumentNullException.ThrowIfNull(memory);
            RequireIdentity(memory.MemoryId, nameof(memory.MemoryId));
            RequireIdentity(memory.SourceEventId, nameof(memory.SourceEventId));
            RequireIdentity(memory.ActorVisibleText, nameof(memory.ActorVisibleText));
            if (!ids.Add(memory.MemoryId) || memory.OccurredAtTicks < 0)
            {
                throw new ArgumentException("Source memory is invalid or duplicated.", nameof(memories));
            }
            ValidateEmotion(memory.Emotion, true);
            if (!StringComparer.Ordinal.Equals(memory.SourceEventId, memory.Emotion.SourceEventId))
            {
                throw new ArgumentException("Memory emotion must exact-bind the memory source event.", nameof(memories));
            }
        }
    }

    private static HashSet<string> RequireUniqueIdentities(string[] values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in values)
        {
            RequireIdentity(value, parameterName);
            if (!identities.Add(value)) throw new ArgumentException("Identities must be unique.", parameterName);
        }
        return identities;
    }

    private static void RequireKnownPlace(string? value, HashSet<string> placeRefs, string parameterName)
    {
        if (value is not null && !placeRefs.Contains(value))
        {
            throw new ArgumentException("Place reference is outside this Town manifest.", parameterName);
        }
    }

    private static void RequireNormalized(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void ValidateOptionalIdentity(string? value, string parameterName)
    {
        if (value is not null) RequireIdentity(value, parameterName);
    }

    private static void RequireIdentity(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty identity is required.", parameterName);
    }

}
