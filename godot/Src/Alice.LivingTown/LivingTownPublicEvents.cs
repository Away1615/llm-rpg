using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Actors;
using Alice.Authority;
using Alice.Navigation;
using Alice.Npc;
using Alice.Social;

namespace Alice.LivingTown;

public sealed record LivingTownPublicEventAttendance(
    GatheringRef GatheringRef,
    string EventId,
    ActorId ActorId,
    PlaceRef PlaceRef,
    SimTime OccurredAt,
    int GatheringRevision);

/// <summary>
/// Materializes versioned daily gatherings from manifest templates and owns exact attendance facts.
/// Schedule arbitration decides whether an Actor attends; this owner only validates and records truth.
/// </summary>
public sealed class LivingTownPublicEventRuntime
{
    private readonly TownPopulationManifest _manifest;
    private readonly TownCalendar _calendar;
    private readonly List<LivingTownPublicEventAttendance> _attendance = [];
    private ScheduledGatheringAuthorityRuntime _authority;

    public LivingTownPublicEventRuntime(TownPopulationManifest manifest, long ticksPerDay)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        _manifest = manifest;
        _calendar = new TownCalendar(ticksPerDay);
        _authority = CreateAuthority([]);
    }

    public IReadOnlyList<ScheduledGathering> Gatherings => _authority.Gatherings;
    public IReadOnlyList<LivingTownPublicEventAttendance> Attendance =>
        new ReadOnlyCollection<LivingTownPublicEventAttendance>(_attendance.ToArray());

    public void Advance(SimTime now)
    {
        long day = _calendar.DayAt(now);
        foreach (TownPublicEventConfiguration template in _manifest.PublicEvents)
            EnsureGathering(template, day);
    }

    public bool TryAttend(
        ActorId actorId,
        ExperienceId experienceId,
        WorldPosition position,
        SimTime now,
        out LivingTownPublicEventAttendance? attendance)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(experienceId);
        attendance = null;
        if (!TryResolveExperience(experienceId, out TownPublicEventConfiguration? template, out long day)
            || template is null)
            return false;
        EnsureGathering(template, day);
        GatheringRef gatheringRef = DailyGatheringRef(template.EventId, day);
        ScheduledGathering gathering = _authority.FindCurrent(gatheringRef)
            ?? throw new InvalidOperationException("Public event gathering was not materialized.");
        WorldPosition expectedPosition = ResolvePlace(template.PlaceRef);
        if (!template.ParticipantActorIds.Contains(actorId.Value, StringComparer.Ordinal)
            || position != expectedPosition
            || now.CompareTo(gathering.StartsAt) < 0
            || now.CompareTo(gathering.EndsAt) >= 0
            || gathering.Lifecycle is ScheduledGatheringLifecycle.Cancelled or ScheduledGatheringLifecycle.Ended)
            return false;

        LivingTownPublicEventAttendance? existing = _attendance.SingleOrDefault(value =>
            value.GatheringRef == gatheringRef && value.ActorId == actorId);
        if (existing is not null)
        {
            attendance = existing;
            return true;
        }
        attendance = new LivingTownPublicEventAttendance(
            gatheringRef,
            template.EventId,
            actorId,
            gathering.PlaceRef,
            now,
            gathering.Revision);
        _attendance.Add(attendance);
        _attendance.Sort(AttendanceComparer.Instance);
        return true;
    }

    public static ExperienceId ExperienceIdFor(
        TownPublicEventConfiguration template,
        ScheduleOpportunityId opportunityId) =>
        new($"social/event/{template.EventId}/{opportunityId.Day}/{opportunityId.EntryId.Value}");

    private void EnsureGathering(TownPublicEventConfiguration template, long day)
    {
        GatheringRef gatheringRef = DailyGatheringRef(template.EventId, day);
        if (_authority.FindCurrent(gatheringRef) is not null) return;
        ScheduledGathering[] existing = _authority.Gatherings.ToArray();
        _authority = CreateAuthority(existing, new GatheringHostPlaceUseAuthorityFact(
            gatheringRef,
            new ActorId(template.HostActorId),
            new PlaceRef(template.PlaceRef)));
        ScheduledGatheringAuthorityResult result = _authority.TryCreate(new ScheduledGatheringCreationProposal(
            gatheringRef,
            new ActorId(template.HostActorId),
            new PlaceRef(template.PlaceRef),
            _calendar.AbsoluteTime(day, template.StartsAtTickOfDay),
            _calendar.AbsoluteTime(day, template.EndsAtTickOfDay),
            template.ParticipantActorIds.Select(value => new ActorId(value)),
            ScheduledGatheringLifecycle.Planned));
        if (!result.IsCommitted)
            throw new InvalidOperationException($"Public event {template.EventId} failed Authority creation: {result.Failure}.");
    }

    private ScheduledGatheringAuthorityRuntime CreateAuthority(
        IEnumerable<ScheduledGathering> gatherings,
        GatheringHostPlaceUseAuthorityFact? additionalFact = null)
    {
        ScheduledGathering[] snapshot = gatherings.ToArray();
        var facts = snapshot.Select(value => new GatheringHostPlaceUseAuthorityFact(
            value.GatheringRef,
            value.HostActorId,
            value.PlaceRef)).ToList();
        if (additionalFact is not null) facts.Add(additionalFact);
        return new ScheduledGatheringAuthorityRuntime(
            _manifest.Actors.Select(value => new ActorId(value.Identity.ActorId)),
            _manifest.Places.Select(value => new PlaceRef(value.PlaceRef)),
            facts,
            snapshot);
    }

    private bool TryResolveExperience(
        ExperienceId experienceId,
        out TownPublicEventConfiguration? template,
        out long day)
    {
        foreach (TownPublicEventConfiguration candidate in _manifest.PublicEvents)
        {
            string prefix = $"social/event/{candidate.EventId}/";
            if (!experienceId.Value.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string remainder = experienceId.Value[prefix.Length..];
            int separator = remainder.IndexOf('/', StringComparison.Ordinal);
            if (separator > 0 && long.TryParse(remainder[..separator], out day) && day >= 0)
            {
                template = candidate;
                return true;
            }
        }
        template = null;
        day = 0;
        return false;
    }

    private bool TryParseDailyGatheringRef(
        GatheringRef gatheringRef,
        out TownPublicEventConfiguration? template,
        out long day)
    {
        foreach (TownPublicEventConfiguration candidate in _manifest.PublicEvents)
        {
            string prefix = $"{candidate.EventId}/day/";
            if (gatheringRef.Value.StartsWith(prefix, StringComparison.Ordinal)
                && long.TryParse(gatheringRef.Value[prefix.Length..], out day)
                && day >= 0)
            {
                template = candidate;
                return true;
            }
        }
        template = null;
        day = 0;
        return false;
    }

    private WorldPosition ResolvePlace(string placeRef)
    {
        TownPlaceConfiguration place = _manifest.Places.Single(value =>
            StringComparer.Ordinal.Equals(value.PlaceRef, placeRef));
        return new WorldPosition(place.WorldX, place.WorldY);
    }

    private static GatheringRef DailyGatheringRef(string eventId, long day) => new($"{eventId}/day/{day}");

    private sealed class AttendanceComparer : IComparer<LivingTownPublicEventAttendance>
    {
        public static AttendanceComparer Instance { get; } = new();
        public int Compare(LivingTownPublicEventAttendance? left, LivingTownPublicEventAttendance? right)
        {
            int gathering = StringComparer.Ordinal.Compare(left?.GatheringRef.Value, right?.GatheringRef.Value);
            return gathering != 0
                ? gathering
                : StringComparer.Ordinal.Compare(left?.ActorId.Value, right?.ActorId.Value);
        }
    }
}
