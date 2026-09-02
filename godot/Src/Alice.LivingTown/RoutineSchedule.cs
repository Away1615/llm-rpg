using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Actors;
using Alice.Npc;

namespace Alice.LivingTown;

public sealed record ScheduleEntryId
{
    public ScheduleEntryId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Schedule entry identity must be non-empty.", nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public sealed record ScheduleRecurrenceId
{
    public ScheduleRecurrenceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Schedule recurrence identity must be non-empty.", nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public sealed record ScheduleEntry
{
    public ScheduleEntry(
        ScheduleEntryId entryId,
        ScheduleRecurrenceId recurrenceId,
        long startsAtTickOfDay,
        long endsAtTickOfDay,
        LivingTownPlaceRef? placeRef,
        SchedulePurpose purpose,
        ScheduleObligation obligation,
        string? commitmentRef,
        string? sourceRef)
    {
        ArgumentNullException.ThrowIfNull(entryId);
        ArgumentNullException.ThrowIfNull(recurrenceId);
        if (startsAtTickOfDay < 0 || endsAtTickOfDay <= startsAtTickOfDay)
            throw new ArgumentOutOfRangeException(nameof(endsAtTickOfDay));
        if (!Enum.IsDefined(purpose) || !Enum.IsDefined(obligation)) throw new ArgumentException("Schedule entry enum is invalid.");
        ValidateOptionalIdentity(commitmentRef, nameof(commitmentRef));
        ValidateOptionalIdentity(sourceRef, nameof(sourceRef));
        EntryId = entryId;
        RecurrenceId = recurrenceId;
        StartsAtTickOfDay = startsAtTickOfDay;
        EndsAtTickOfDay = endsAtTickOfDay;
        PlaceRef = placeRef;
        Purpose = purpose;
        Obligation = obligation;
        CommitmentRef = commitmentRef;
        SourceRef = sourceRef;
    }

    public ScheduleEntryId EntryId { get; }
    public ScheduleRecurrenceId RecurrenceId { get; }
    public long StartsAtTickOfDay { get; }
    public long EndsAtTickOfDay { get; }
    public LivingTownPlaceRef? PlaceRef { get; }
    public SchedulePurpose Purpose { get; }
    public ScheduleObligation Obligation { get; }
    public string? CommitmentRef { get; }
    public string? SourceRef { get; }

    private static void ValidateOptionalIdentity(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Optional identity cannot be blank.", parameterName);
    }
}

public sealed class RoutineSchedule
{
    public const string DailyRecurrenceId = "daily";
    private readonly ReadOnlyCollection<ScheduleEntry> _entries;

    public RoutineSchedule(ActorId actorId, IEnumerable<ScheduleEntry> entries, long ticksPerDay)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(entries);
        if (ticksPerDay <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerDay));
        ScheduleEntry[] snapshot = entries.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ScheduleEntry entry in snapshot)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!ids.Add(entry.EntryId.Value)
                || !StringComparer.Ordinal.Equals(entry.RecurrenceId.Value, DailyRecurrenceId)
                || entry.EndsAtTickOfDay > ticksPerDay)
            {
                throw new ArgumentException("Routine schedule contains a duplicate, unsupported recurrence, or out-of-day window.", nameof(entries));
            }
        }
        Array.Sort(snapshot, ScheduleEntryComparer.Instance);
        ActorId = actorId;
        TicksPerDay = ticksPerDay;
        _entries = Array.AsReadOnly(snapshot);
    }

    public ActorId ActorId { get; }
    public long TicksPerDay { get; }
    public IReadOnlyList<ScheduleEntry> Entries => _entries;

    public static RoutineSchedule FromConfiguration(ActorId actorId, IEnumerable<TownScheduleEntryConfiguration> entries, long ticksPerDay)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var result = new List<ScheduleEntry>();
        foreach (TownScheduleEntryConfiguration entry in entries)
        {
            result.Add(new ScheduleEntry(
                new ScheduleEntryId(entry.EntryId),
                new ScheduleRecurrenceId(entry.RecurrenceId),
                entry.StartsAtTickOfDay,
                entry.EndsAtTickOfDay,
                entry.PlaceRef is null ? null : new LivingTownPlaceRef(entry.PlaceRef),
                Enum.Parse<SchedulePurpose>(entry.Purpose, false),
                Enum.Parse<ScheduleObligation>(entry.Obligation, false),
                entry.CommitmentRef,
                entry.SourceRef));
        }
        return new RoutineSchedule(actorId, result, ticksPerDay);
    }

    private sealed class ScheduleEntryComparer : IComparer<ScheduleEntry>
    {
        public static ScheduleEntryComparer Instance { get; } = new();
        public int Compare(ScheduleEntry? left, ScheduleEntry? right) => StringComparer.Ordinal.Compare(left?.EntryId.Value, right?.EntryId.Value);
    }
}

public sealed class TownCalendar
{
    public TownCalendar(long ticksPerDay)
    {
        if (ticksPerDay <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerDay));
        TicksPerDay = ticksPerDay;
    }

    public long TicksPerDay { get; }
    public long DayAt(SimTime time) => time.Ticks / TicksPerDay;
    public long TickOfDayAt(SimTime time) => time.Ticks % TicksPerDay;
    public SimTime AbsoluteTime(long day, long tickOfDay)
    {
        if (day < 0 || tickOfDay < 0 || tickOfDay > TicksPerDay) throw new ArgumentOutOfRangeException(nameof(tickOfDay));
        return new SimTime(checked(day * TicksPerDay + tickOfDay));
    }
}

public sealed record ScheduleOpportunityId
{
    public ScheduleOpportunityId(long day, ScheduleEntryId entryId)
    {
        if (day < 0) throw new ArgumentOutOfRangeException(nameof(day));
        ArgumentNullException.ThrowIfNull(entryId);
        Day = day;
        EntryId = entryId;
    }

    public long Day { get; }
    public ScheduleEntryId EntryId { get; }
    public override string ToString() => $"{Day}:{EntryId.Value}";
}

public enum ScheduleOpportunityStatus
{
    Open,
    Completed,
    Late,
    Absent,
    Interrupted
}

public sealed record ScheduleOpportunity(
    ScheduleOpportunityId OpportunityId,
    ActorId ActorId,
    ScheduleEntry Entry,
    SimTime StartsAt,
    SimTime EndsAt,
    ScheduleOpportunityStatus Status,
    SimTime UpdatedAt);

public sealed record ScheduleDeviationEvent(
    ScheduleOpportunityId OpportunityId,
    ActorId ActorId,
    ScheduleOpportunityStatus Outcome,
    SimTime OccurredAt,
    string? SourceRef);

public sealed record ScheduleOutcomeReceipt(
    ScheduleOpportunityId OpportunityId,
    ActorId ActorId,
    ScheduleOpportunityStatus Outcome,
    SimTime SettledAt,
    ScheduleDeviationEvent? Deviation);

/// <summary>Typed Goal evidence produced by schedule arbitration; it is not an executable action.</summary>
public sealed record ScheduleOpportunityObjective : GoalObjective
{
    public ScheduleOpportunityObjective(ScheduleOpportunityId opportunityId, SchedulePurpose purpose, LivingTownPlaceRef? placeRef)
    {
        ArgumentNullException.ThrowIfNull(opportunityId);
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        OpportunityId = opportunityId;
        Purpose = purpose;
        PlaceRef = placeRef;
    }

    public ScheduleOpportunityId OpportunityId { get; }
    public SchedulePurpose Purpose { get; }
    public LivingTownPlaceRef? PlaceRef { get; }
}

public sealed class ScheduleRuntime
{
    private readonly RoutineSchedule _schedule;
    private readonly TownCalendar _calendar;
    private readonly Dictionary<ScheduleOpportunityId, ScheduleOpportunity> _opportunities = [];
    private readonly List<ScheduleDeviationEvent> _deviations = [];
    private SimTime? _lastAdvancedAt;

    public ScheduleRuntime(RoutineSchedule schedule, TownCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(calendar);
        if (schedule.TicksPerDay != calendar.TicksPerDay) throw new ArgumentException("Schedule and Town calendar must use the same day scale.");
        _schedule = schedule;
        _calendar = calendar;
    }

    public ActorId ActorId => _schedule.ActorId;
    public IReadOnlyList<ScheduleOpportunity> Opportunities => SnapshotOpportunities();
    public IReadOnlyList<ScheduleDeviationEvent> Deviations => _deviations.AsReadOnly();

    public IReadOnlyList<ScheduleOpportunity> Advance(SimTime now)
    {
        if (_lastAdvancedAt is SimTime previous && now.CompareTo(previous) < 0)
            throw new ArgumentOutOfRangeException(nameof(now), "Schedule time cannot move backwards.");
        long firstDay = _lastAdvancedAt is null ? 0 : _calendar.DayAt(_lastAdvancedAt.Value);
        long currentDay = _calendar.DayAt(now);
        for (long day = firstDay; day <= currentDay; day++) AdvanceDay(day, now, currentDay);
        _lastAdvancedAt = now;
        return GetOpenOpportunities();
    }

    public ScheduleOutcomeReceipt Resolve(ScheduleOpportunityId opportunityId, ScheduleOpportunityStatus outcome, SimTime now)
    {
        ArgumentNullException.ThrowIfNull(opportunityId);
        if (outcome == ScheduleOpportunityStatus.Open || !Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if (!_opportunities.TryGetValue(opportunityId, out ScheduleOpportunity? opportunity)
            || opportunity.Status != ScheduleOpportunityStatus.Open
            || opportunity.ActorId != ActorId)
        {
            throw new InvalidOperationException("Only one open schedule opportunity owned by this Actor may be resolved.");
        }
        if (now.CompareTo(opportunity.UpdatedAt) < 0) throw new ArgumentOutOfRangeException(nameof(now));
        ScheduleDeviationEvent? deviation = outcome is ScheduleOpportunityStatus.Late or ScheduleOpportunityStatus.Absent or ScheduleOpportunityStatus.Interrupted
            ? new ScheduleDeviationEvent(opportunityId, ActorId, outcome, now, opportunity.Entry.SourceRef)
            : null;
        _opportunities[opportunityId] = opportunity with { Status = outcome, UpdatedAt = now };
        if (deviation is not null) _deviations.Add(deviation);
        return new ScheduleOutcomeReceipt(opportunityId, ActorId, outcome, now, deviation);
    }

    public IReadOnlyList<ScheduleOpportunity> GetOpenOpportunities()
    {
        var open = new List<ScheduleOpportunity>();
        foreach (ScheduleOpportunity opportunity in SnapshotOpportunities())
        {
            if (opportunity.Status == ScheduleOpportunityStatus.Open) open.Add(opportunity);
        }
        return open.AsReadOnly();
    }

    public ScheduleEntry? ResolveTravelEntry(SimTime now, string? preferredEntryId = null)
    {
        ScheduleOpportunity[] openOpportunities = GetOpenOpportunities()
            .Where(value => value.Entry.PlaceRef is not null)
            .OrderBy(value => value.Entry.Obligation)
            .ThenBy(value => value.Entry.StartsAtTickOfDay)
            .ThenBy(value => value.Entry.EntryId.Value, StringComparer.Ordinal)
            .ToArray();
        ScheduleEntry? preferred = preferredEntryId is null
            ? null
            : openOpportunities.FirstOrDefault(value =>
                StringComparer.Ordinal.Equals(value.Entry.EntryId.Value, preferredEntryId))?.Entry;
        if (preferred is not null) return preferred;
        ScheduleEntry? open = openOpportunities.Select(value => value.Entry).FirstOrDefault();
        if (open is not null) return open;

        long tickOfDay = _calendar.TickOfDayAt(now);
        ScheduleEntry[] destinations = _schedule.Entries
            .Where(value => value.PlaceRef is not null)
            .OrderBy(value => value.StartsAtTickOfDay)
            .ThenBy(value => value.EntryId.Value, StringComparer.Ordinal)
            .ToArray();
        return destinations.FirstOrDefault(value => value.StartsAtTickOfDay > tickOfDay)
            ?? destinations.FirstOrDefault();
    }

    private void AdvanceDay(long day, SimTime now, long currentDay)
    {
        long tickOfDay = day == currentDay ? _calendar.TickOfDayAt(now) : _calendar.TicksPerDay;
        foreach (ScheduleEntry entry in _schedule.Entries)
        {
            var id = new ScheduleOpportunityId(day, entry.EntryId);
            if (_opportunities.ContainsKey(id))
            {
                ScheduleOpportunity existing = _opportunities[id];
                if (existing.Status == ScheduleOpportunityStatus.Open && tickOfDay >= entry.EndsAtTickOfDay)
                {
                    Resolve(id, ScheduleOpportunityStatus.Absent, now);
                }
                continue;
            }
            if (tickOfDay < entry.StartsAtTickOfDay) continue;
            SimTime startsAt = _calendar.AbsoluteTime(day, entry.StartsAtTickOfDay);
            SimTime endsAt = _calendar.AbsoluteTime(day, entry.EndsAtTickOfDay);
            ScheduleOpportunityStatus status = tickOfDay < entry.EndsAtTickOfDay
                ? ScheduleOpportunityStatus.Open
                : ScheduleOpportunityStatus.Absent;
            var opportunity = new ScheduleOpportunity(id, ActorId, entry, startsAt, endsAt, status, now);
            _opportunities.Add(id, opportunity);
            if (status == ScheduleOpportunityStatus.Absent)
            {
                _deviations.Add(new ScheduleDeviationEvent(id, ActorId, status, now, entry.SourceRef));
            }
        }
    }

    private IReadOnlyList<ScheduleOpportunity> SnapshotOpportunities()
    {
        ScheduleOpportunity[] values = _opportunities.Values.ToArray();
        Array.Sort(values, ScheduleOpportunityComparer.Instance);
        return Array.AsReadOnly(values);
    }

    private sealed class ScheduleOpportunityComparer : IComparer<ScheduleOpportunity>
    {
        public static ScheduleOpportunityComparer Instance { get; } = new();
        public int Compare(ScheduleOpportunity? left, ScheduleOpportunity? right)
        {
            if (left is null) return right is null ? 0 : -1;
            if (right is null) return 1;
            int day = left.OpportunityId.Day.CompareTo(right.OpportunityId.Day);
            return day != 0 ? day : StringComparer.Ordinal.Compare(left.OpportunityId.EntryId.Value, right.OpportunityId.EntryId.Value);
        }
    }
}
