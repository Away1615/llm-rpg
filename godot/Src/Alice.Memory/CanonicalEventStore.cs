using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Navigation;

namespace Alice.Memory;

public enum CanonicalHistoryExperienceRole
{
    Participant,
    Witness,
    Testimony
}

public sealed record CanonicalHistoryExperience(
    ActorId ActorId,
    CanonicalHistoryExperienceRole Role,
    ActorId? TellerActorId,
    DecisionMemorySourceId? OriginalSourceId);

public sealed record CanonicalHistoryActorVisibleFact(ActorId ActorId, string Text);
public sealed record CanonicalHistorySourceReference(string Kind, string Value);

/// <summary>One shared product-history event; actor-local memories retain only its source identity and visible fact.</summary>
public sealed class CanonicalHistoryEventRecord
{
    public CanonicalHistoryEventRecord(
        DecisionMemorySourceId sourceId,
        string eventKind,
        long occurredAtTicks,
        string locationId,
        WorldPosition position,
        string spatialLayer,
        IEnumerable<CanonicalHistoryExperience> experiences,
        IEnumerable<CanonicalHistoryActorVisibleFact> actorVisibleFacts,
        IEnumerable<CanonicalHistorySourceReference> sourceReferences)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(spatialLayer);
        ArgumentNullException.ThrowIfNull(experiences);
        ArgumentNullException.ThrowIfNull(actorVisibleFacts);
        ArgumentNullException.ThrowIfNull(sourceReferences);
        if (occurredAtTicks < 0) throw new ArgumentOutOfRangeException(nameof(occurredAtTicks));
        SourceId = sourceId;
        EventKind = eventKind;
        OccurredAtTicks = occurredAtTicks;
        LocationId = locationId;
        Position = position;
        SpatialLayer = spatialLayer;
        Experiences = Array.AsReadOnly(experiences.ToArray());
        ActorVisibleFacts = Array.AsReadOnly(actorVisibleFacts.ToArray());
        SourceReferences = Array.AsReadOnly(sourceReferences.ToArray());
    }

    public DecisionMemorySourceId SourceId { get; }
    public string EventKind { get; }
    public long OccurredAtTicks { get; }
    public string LocationId { get; }
    public WorldPosition Position { get; }
    public string SpatialLayer { get; }
    public IReadOnlyList<CanonicalHistoryExperience> Experiences { get; }
    public IReadOnlyList<CanonicalHistoryActorVisibleFact> ActorVisibleFacts { get; }
    public IReadOnlyList<CanonicalHistorySourceReference> SourceReferences { get; }

    internal bool HasExactContent(CanonicalHistoryEventRecord other) =>
        SourceId == other.SourceId
        && EventKind == other.EventKind
        && OccurredAtTicks == other.OccurredAtTicks
        && LocationId == other.LocationId
        && Position == other.Position
        && SpatialLayer == other.SpatialLayer
        && Experiences.SequenceEqual(other.Experiences)
        && ActorVisibleFacts.SequenceEqual(other.ActorVisibleFacts)
        && SourceReferences.SequenceEqual(other.SourceReferences);
}

public sealed record CanonicalHistoryEventAdmissionResult(
    CanonicalEventAdmissionKind Kind,
    CanonicalHistoryEventRecord Record);

public enum CanonicalEventInspectionKind
{
    Missing,
    ExactExisting,
    IdentityConflict
}

public enum CanonicalEventAdmissionKind
{
    Appended,
    ExactExisting,
    IdentityConflict
}

public sealed record CanonicalEventInspectionResult
{
    internal CanonicalEventInspectionResult(
        CanonicalEventInspectionKind kind,
        SemanticDialogueSourceRecord? existingRecord)
    {
        Kind = kind;
        ExistingRecord = existingRecord;
    }

    public CanonicalEventInspectionKind Kind { get; }
    public SemanticDialogueSourceRecord? ExistingRecord { get; }
}

public sealed record CanonicalEventAdmissionResult
{
    internal CanonicalEventAdmissionResult(
        CanonicalEventAdmissionKind kind,
        SemanticDialogueSourceRecord record)
    {
        Kind = kind;
        Record = record;
    }

    public CanonicalEventAdmissionKind Kind { get; }
    public SemanticDialogueSourceRecord Record { get; }
}

/// <summary>Append-only shared ownership for canonical semantic event sources.</summary>
public sealed class CanonicalEventStore
{
    private Dictionary<DecisionMemorySourceId, object> _recordsById = [];
    private object[] _insertionOrder = [];

    public int Count
    {
        get
        {
            lock (SyncRoot)
            {
                return _insertionOrder.Length;
            }
        }
    }

    internal object SyncRoot { get; } = new();

    public bool Contains(DecisionMemorySourceId sourceId)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        lock (SyncRoot)
        {
            return _recordsById.ContainsKey(sourceId);
        }
    }

    public CanonicalEventInspectionResult Inspect(SemanticDialogueSourceRecord candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (SyncRoot)
        {
            return InspectUnderLock(candidate);
        }
    }

    public CanonicalEventAdmissionResult Append(SemanticDialogueSourceRecord candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (SyncRoot)
        {
            CanonicalEventInspectionResult inspection = InspectUnderLock(candidate);
            if (inspection.Kind != CanonicalEventInspectionKind.Missing)
            {
                CanonicalEventAdmissionKind kind = inspection.Kind == CanonicalEventInspectionKind.ExactExisting
                    ? CanonicalEventAdmissionKind.ExactExisting
                    : CanonicalEventAdmissionKind.IdentityConflict;
                return new CanonicalEventAdmissionResult(kind, inspection.ExistingRecord ?? candidate);
            }

            PreparedState prepared = PrepareAppendUnderLock(candidate);
            CommitUnderLock(prepared);
            return new CanonicalEventAdmissionResult(CanonicalEventAdmissionKind.Appended, candidate);
        }
    }

    public CanonicalHistoryEventAdmissionResult Append(CanonicalHistoryEventRecord candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (SyncRoot)
        {
            if (_recordsById.TryGetValue(candidate.SourceId, out object? existing))
            {
                CanonicalEventAdmissionKind kind = existing is CanonicalHistoryEventRecord history
                    && history.HasExactContent(candidate)
                    ? CanonicalEventAdmissionKind.ExactExisting
                    : CanonicalEventAdmissionKind.IdentityConflict;
                return new CanonicalHistoryEventAdmissionResult(
                    kind,
                    existing as CanonicalHistoryEventRecord ?? candidate);
            }

            PreparedState prepared = PrepareAppendUnderLock(candidate);
            CommitUnderLock(prepared);
            return new CanonicalHistoryEventAdmissionResult(CanonicalEventAdmissionKind.Appended, candidate);
        }
    }

    public IReadOnlyList<SemanticDialogueSourceRecord> GetInsertionOrderSnapshot()
    {
        lock (SyncRoot)
        {
            return new ReadOnlyCollection<SemanticDialogueSourceRecord>(_insertionOrder.OfType<SemanticDialogueSourceRecord>().ToArray());
        }
    }

    public IReadOnlyList<CanonicalHistoryEventRecord> GetHistoryInsertionOrderSnapshot()
    {
        lock (SyncRoot)
        {
            return new ReadOnlyCollection<CanonicalHistoryEventRecord>(_insertionOrder.OfType<CanonicalHistoryEventRecord>().ToArray());
        }
    }

    internal CanonicalEventInspectionResult InspectUnderLock(SemanticDialogueSourceRecord candidate)
    {
        if (!_recordsById.TryGetValue(candidate.SourceId, out object? value))
        {
            return new CanonicalEventInspectionResult(CanonicalEventInspectionKind.Missing, null);
        }

        SemanticDialogueSourceRecord? existing = value as SemanticDialogueSourceRecord;
        CanonicalEventInspectionKind kind = existing is not null && existing.HasExactContent(candidate)
            ? CanonicalEventInspectionKind.ExactExisting
            : CanonicalEventInspectionKind.IdentityConflict;
        return new CanonicalEventInspectionResult(kind, existing);
    }

    internal SemanticDialogueSourceRecord? FindUnderLock(DecisionMemorySourceId sourceId)
    {
        _recordsById.TryGetValue(sourceId, out object? record);
        return record as SemanticDialogueSourceRecord;
    }

    internal PreparedState PrepareAppendUnderLock(SemanticDialogueSourceRecord candidate)
        => PrepareAppendUnderLock(candidate.SourceId, candidate);

    private PreparedState PrepareAppendUnderLock(CanonicalHistoryEventRecord candidate)
        => PrepareAppendUnderLock(candidate.SourceId, candidate);

    private PreparedState PrepareAppendUnderLock(DecisionMemorySourceId sourceId, object candidate)
    {
        var records = new Dictionary<DecisionMemorySourceId, object>(_recordsById)
        {
            [sourceId] = candidate
        };
        object[] insertionOrder = [.. _insertionOrder, candidate];
        return new PreparedState(records, insertionOrder);
    }

    internal void CommitUnderLock(PreparedState state)
    {
        _recordsById = state.RecordsById;
        _insertionOrder = state.InsertionOrder;
    }

    internal sealed record PreparedState(
        Dictionary<DecisionMemorySourceId, object> RecordsById,
        object[] InsertionOrder);
}
