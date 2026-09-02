using System.Collections.ObjectModel;
using Alice.Actors;

namespace Alice.Memory;

public enum SubjectiveMemoryInspectionKind
{
    Missing,
    ExactExisting,
    IdentityConflict,
    SubjectConflict
}

public enum SubjectiveMemoryAdmissionKind
{
    Appended,
    ExactExisting,
    IdentityConflict,
    SubjectConflict
}

public sealed record SubjectiveMemoryInspectionResult
{
    internal SubjectiveMemoryInspectionResult(
        SubjectiveMemoryInspectionKind kind,
        SubjectiveMemoryRecord? existingRecord)
    {
        Kind = kind;
        ExistingRecord = existingRecord;
    }

    public SubjectiveMemoryInspectionKind Kind { get; }
    public SubjectiveMemoryRecord? ExistingRecord { get; }
}

public sealed record SubjectiveMemoryAdmissionResult
{
    internal SubjectiveMemoryAdmissionResult(
        SubjectiveMemoryAdmissionKind kind,
        SubjectiveMemoryRecord record)
    {
        Kind = kind;
        Record = record;
    }

    public SubjectiveMemoryAdmissionKind Kind { get; }
    public SubjectiveMemoryRecord Record { get; }
}

/// <summary>Append-only ownership for actor-specific subjective-memory records.</summary>
public sealed class SubjectiveMemoryStore
{
    private Dictionary<DecisionMemoryId, SubjectiveMemoryRecord> _recordsById = [];
    private Dictionary<MemorySubject, SubjectiveMemoryRecord> _recordsBySubject = [];
    private SubjectiveMemoryRecord[] _insertionOrder = [];

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

    public SubjectiveMemoryInspectionResult Inspect(SubjectiveMemoryRecord candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (SyncRoot)
        {
            return InspectUnderLock(candidate);
        }
    }

    public SubjectiveMemoryAdmissionResult Append(SubjectiveMemoryRecord candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (SyncRoot)
        {
            SubjectiveMemoryInspectionResult inspection = InspectUnderLock(candidate);
            if (inspection.Kind != SubjectiveMemoryInspectionKind.Missing)
            {
                SubjectiveMemoryAdmissionKind kind = inspection.Kind switch
                {
                    SubjectiveMemoryInspectionKind.ExactExisting => SubjectiveMemoryAdmissionKind.ExactExisting,
                    SubjectiveMemoryInspectionKind.IdentityConflict => SubjectiveMemoryAdmissionKind.IdentityConflict,
                    SubjectiveMemoryInspectionKind.SubjectConflict => SubjectiveMemoryAdmissionKind.SubjectConflict,
                    _ => throw new ArgumentOutOfRangeException(nameof(inspection))
                };
                return new SubjectiveMemoryAdmissionResult(kind, inspection.ExistingRecord!);
            }

            PreparedState prepared = PrepareAppendUnderLock(candidate);
            CommitUnderLock(prepared);
            return new SubjectiveMemoryAdmissionResult(SubjectiveMemoryAdmissionKind.Appended, candidate);
        }
    }

    public IReadOnlyList<SubjectiveMemoryRecord> GetInsertionOrderSnapshot()
    {
        lock (SyncRoot)
        {
            return new ReadOnlyCollection<SubjectiveMemoryRecord>((SubjectiveMemoryRecord[])_insertionOrder.Clone());
        }
    }

    internal SubjectiveMemoryInspectionResult InspectUnderLock(SubjectiveMemoryRecord candidate)
    {
        if (_recordsById.TryGetValue(candidate.MemoryId, out SubjectiveMemoryRecord? sameIdentity))
        {
            SubjectiveMemoryInspectionKind kind = sameIdentity.HasExactContent(candidate)
                ? SubjectiveMemoryInspectionKind.ExactExisting
                : SubjectiveMemoryInspectionKind.IdentityConflict;
            return new SubjectiveMemoryInspectionResult(kind, sameIdentity);
        }

        var subject = new MemorySubject(candidate.ActorId, candidate.SourceId);
        if (_recordsBySubject.TryGetValue(subject, out SubjectiveMemoryRecord? sameSubject))
        {
            return new SubjectiveMemoryInspectionResult(SubjectiveMemoryInspectionKind.SubjectConflict, sameSubject);
        }

        return new SubjectiveMemoryInspectionResult(SubjectiveMemoryInspectionKind.Missing, null);
    }

    internal SubjectiveMemoryRecord[] GetActorRecordsUnderLock(ActorId actorId)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        var records = new List<SubjectiveMemoryRecord>();
        foreach (SubjectiveMemoryRecord record in _insertionOrder)
        {
            if (record.ActorId == actorId)
            {
                records.Add(record);
            }
        }

        return records.ToArray();
    }

    internal PreparedState PrepareAppendUnderLock(SubjectiveMemoryRecord candidate)
    {
        return PrepareAppendUnderLock([candidate]);
    }

    internal PreparedState PrepareAppendUnderLock(IReadOnlyList<SubjectiveMemoryRecord> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var recordsById = new Dictionary<DecisionMemoryId, SubjectiveMemoryRecord>(_recordsById);
        var recordsBySubject = new Dictionary<MemorySubject, SubjectiveMemoryRecord>(_recordsBySubject);
        var insertionOrder = new SubjectiveMemoryRecord[_insertionOrder.Length + candidates.Count];
        Array.Copy(_insertionOrder, insertionOrder, _insertionOrder.Length);
        for (int index = 0; index < candidates.Count; index++)
        {
            SubjectiveMemoryRecord candidate = candidates[index];
            ArgumentNullException.ThrowIfNull(candidate);
            recordsById.Add(candidate.MemoryId, candidate);
            recordsBySubject.Add(new MemorySubject(candidate.ActorId, candidate.SourceId), candidate);
            insertionOrder[_insertionOrder.Length + index] = candidate;
        }

        return new PreparedState(recordsById, recordsBySubject, insertionOrder);
    }

    internal void CommitUnderLock(PreparedState state)
    {
        _recordsById = state.RecordsById;
        _recordsBySubject = state.RecordsBySubject;
        _insertionOrder = state.InsertionOrder;
    }

    internal readonly record struct MemorySubject(ActorId ActorId, DecisionMemorySourceId SourceId);

    internal sealed record PreparedState(
        Dictionary<DecisionMemoryId, SubjectiveMemoryRecord> RecordsById,
        Dictionary<MemorySubject, SubjectiveMemoryRecord> RecordsBySubject,
        SubjectiveMemoryRecord[] InsertionOrder);
}
