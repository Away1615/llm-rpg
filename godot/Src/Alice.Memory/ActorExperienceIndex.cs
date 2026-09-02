using System.Collections.ObjectModel;
using Alice.Actors;

namespace Alice.Memory;

public enum ActorExperienceRole
{
    Caused,
    Received,
    Participant,
    Witness,
    Testimony
}

public sealed record ActorExperienceReference
{
    internal ActorExperienceReference(
        ActorId actorId,
        DecisionMemorySourceId sourceId,
        ActorExperienceRole role)
    {
        ActorId = actorId;
        SourceId = sourceId;
        Role = role;
    }

    public ActorId ActorId { get; }
    public DecisionMemorySourceId SourceId { get; }
    public ActorExperienceRole Role { get; }
}

public enum ActorExperienceInspectionKind
{
    Missing,
    ExactExisting,
    RoleConflict
}

public enum ActorExperienceAdmissionKind
{
    Appended,
    ExactExisting,
    RoleConflict
}

public sealed record ActorExperienceInspectionResult
{
    internal ActorExperienceInspectionResult(
        ActorExperienceInspectionKind kind,
        ActorExperienceReference? existingReference)
    {
        Kind = kind;
        ExistingReference = existingReference;
    }

    public ActorExperienceInspectionKind Kind { get; }
    public ActorExperienceReference? ExistingReference { get; }
}

public sealed record ActorExperienceAdmissionResult
{
    internal ActorExperienceAdmissionResult(
        ActorExperienceAdmissionKind kind,
        ActorExperienceReference reference)
    {
        Kind = kind;
        Reference = reference;
    }

    public ActorExperienceAdmissionKind Kind { get; }
    public ActorExperienceReference Reference { get; }
}

/// <summary>Append-only actor visibility references without duplicated source bodies.</summary>
public sealed class ActorExperienceIndex
{
    private Dictionary<ActorSourceKey, ActorExperienceReference> _referencesByKey = [];
    private ActorExperienceReference[] _insertionOrder = [];

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

    public ActorExperienceInspectionResult Inspect(ActorExperienceReference candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (SyncRoot)
        {
            return InspectUnderLock(candidate);
        }
    }

    public ActorExperienceAdmissionResult Append(ActorExperienceReference candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (SyncRoot)
        {
            ActorExperienceInspectionResult inspection = InspectUnderLock(candidate);
            if (inspection.Kind != ActorExperienceInspectionKind.Missing)
            {
                ActorExperienceAdmissionKind kind = inspection.Kind == ActorExperienceInspectionKind.ExactExisting
                    ? ActorExperienceAdmissionKind.ExactExisting
                    : ActorExperienceAdmissionKind.RoleConflict;
                return new ActorExperienceAdmissionResult(kind, inspection.ExistingReference!);
            }

            PreparedState prepared = PrepareAppendUnderLock([candidate]);
            CommitUnderLock(prepared);
            return new ActorExperienceAdmissionResult(ActorExperienceAdmissionKind.Appended, candidate);
        }
    }

    public IReadOnlyList<ActorExperienceReference> GetInsertionOrderSnapshot()
    {
        lock (SyncRoot)
        {
            return new ReadOnlyCollection<ActorExperienceReference>((ActorExperienceReference[])_insertionOrder.Clone());
        }
    }

    public IReadOnlyList<ActorExperienceReference> GetSourceReferencesSnapshot(DecisionMemorySourceId sourceId)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        lock (SyncRoot)
        {
            return new ReadOnlyCollection<ActorExperienceReference>(GetSourceReferencesUnderLock(sourceId));
        }
    }

    internal ActorExperienceInspectionResult InspectUnderLock(ActorExperienceReference candidate)
    {
        var key = new ActorSourceKey(candidate.ActorId, candidate.SourceId);
        if (!_referencesByKey.TryGetValue(key, out ActorExperienceReference? existing))
        {
            return new ActorExperienceInspectionResult(ActorExperienceInspectionKind.Missing, null);
        }

        ActorExperienceInspectionKind kind = existing.Role == candidate.Role
            ? ActorExperienceInspectionKind.ExactExisting
            : ActorExperienceInspectionKind.RoleConflict;
        return new ActorExperienceInspectionResult(kind, existing);
    }

    internal ActorExperienceReference? FindUnderLock(ActorId actorId, DecisionMemorySourceId sourceId)
    {
        _referencesByKey.TryGetValue(new ActorSourceKey(actorId, sourceId), out ActorExperienceReference? reference);
        return reference;
    }

    internal ActorExperienceReference[] GetSourceReferencesUnderLock(DecisionMemorySourceId sourceId) =>
        _insertionOrder.Where(reference => reference.SourceId == sourceId).ToArray();

    internal PreparedState PrepareAppendUnderLock(IReadOnlyList<ActorExperienceReference> references)
    {
        var entries = new Dictionary<ActorSourceKey, ActorExperienceReference>(_referencesByKey);
        foreach (ActorExperienceReference reference in references)
        {
            entries.Add(new ActorSourceKey(reference.ActorId, reference.SourceId), reference);
        }

        ActorExperienceReference[] insertionOrder = [.. _insertionOrder, .. references];
        return new PreparedState(entries, insertionOrder);
    }

    internal void CommitUnderLock(PreparedState state)
    {
        _referencesByKey = state.ReferencesByKey;
        _insertionOrder = state.InsertionOrder;
    }

    internal readonly record struct ActorSourceKey(ActorId ActorId, DecisionMemorySourceId SourceId);

    internal sealed record PreparedState(
        Dictionary<ActorSourceKey, ActorExperienceReference> ReferencesByKey,
        ActorExperienceReference[] InsertionOrder);
}
