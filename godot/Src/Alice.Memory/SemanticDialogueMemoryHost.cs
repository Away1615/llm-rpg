using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Actors;
using Alice.Social;

namespace Alice.Memory;

public enum SemanticDialogueMemoryHostAdmissionKind
{
    Integrated,
    AlreadyIntegrated,
    SourceConflict,
    ExperienceConflict,
    VisibilityConflict,
    LifecycleConflict
}

public sealed record SemanticDialogueMemoryHostAdmissionResult
{
    private static readonly IReadOnlyList<SubjectiveMemoryRecord> NoRecords =
        new ReadOnlyCollection<SubjectiveMemoryRecord>([]);

    private SemanticDialogueMemoryHostAdmissionResult(
        SemanticDialogueMemoryHostAdmissionKind kind,
        SemanticDialogueSourceRecord? sourceRecord,
        IReadOnlyList<SubjectiveMemoryRecord> subjectiveRecords)
    {
        Kind = kind;
        SourceRecord = sourceRecord;
        SubjectiveRecords = subjectiveRecords;
    }

    public SemanticDialogueMemoryHostAdmissionKind Kind { get; }
    public SemanticDialogueSourceRecord? SourceRecord { get; }
    public IReadOnlyList<SubjectiveMemoryRecord> SubjectiveRecords { get; }

    internal static SemanticDialogueMemoryHostAdmissionResult Consistent(
        SemanticDialogueMemoryHostAdmissionKind kind,
        SemanticDialogueSourceRecord sourceRecord,
        IReadOnlyList<SubjectiveMemoryRecord> subjectiveRecords) =>
        new(
            kind,
            sourceRecord,
            new ReadOnlyCollection<SubjectiveMemoryRecord>(subjectiveRecords.ToArray()));

    internal static SemanticDialogueMemoryHostAdmissionResult Conflict(
        SemanticDialogueMemoryHostAdmissionKind kind) => new(kind, null, NoRecords);
}

/// <summary>Immutable audit and reconstruction evidence across the Host-owned memory lifecycle.</summary>
public sealed record SemanticDialogueMemoryHostAuditSnapshot
{
    internal SemanticDialogueMemoryHostAuditSnapshot(
        IReadOnlyList<SemanticDialogueSourceRecord> sourceRecords,
        IReadOnlyList<ActorExperienceReference> experienceReferences,
        IReadOnlyList<SubjectiveMemoryRecord> subjectiveRecords)
    {
        SourceRecords = new ReadOnlyCollection<SemanticDialogueSourceRecord>(sourceRecords.ToArray());
        ExperienceReferences = new ReadOnlyCollection<ActorExperienceReference>(experienceReferences.ToArray());
        SubjectiveRecords = new ReadOnlyCollection<SubjectiveMemoryRecord>(subjectiveRecords.ToArray());
    }

    public IReadOnlyList<SemanticDialogueSourceRecord> SourceRecords { get; }
    public IReadOnlyList<ActorExperienceReference> ExperienceReferences { get; }
    public IReadOnlyList<SubjectiveMemoryRecord> SubjectiveRecords { get; }
}

/// <summary>Owns one coherent semantic-dialogue memory lifecycle from admission through bounded retrieval.</summary>
public sealed class SemanticDialogueMemoryHost
{
    private readonly object _lifecycleSync = new();
    private readonly CanonicalEventStore _eventStore;
    private readonly ActorExperienceIndex _experienceIndex = new();
    private readonly SubjectiveMemoryStore _subjectiveMemoryStore = new();

    public SemanticDialogueMemoryHost()
        : this(new CanonicalEventStore())
    {
    }

    public SemanticDialogueMemoryHost(CanonicalEventStore eventStore)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        _eventStore = eventStore;
    }

    public CanonicalEventStore EventStore => _eventStore;

    public SemanticDialogueMemoryHostAdmissionResult AdmitAcceptedTurn(
        ConversationSession session,
        SemanticDialogueTurn turn,
        SimTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);

        lock (_lifecycleSync)
        {
            SemanticDialogueMemoryFormationResult sourceResult =
                SemanticDialogueMemoryFormationRuntime.Form(
                    session,
                    turn,
                    occurredAt,
                    _eventStore,
                    _experienceIndex);
            if (sourceResult.Kind is not SemanticDialogueMemoryFormationKind.Formed
                and not SemanticDialogueMemoryFormationKind.AlreadyFormed)
            {
                return MapFormationConflict(sourceResult.Kind);
            }

            SemanticDialogueSourceRecord sourceRecord = sourceResult.SourceRecord!;
            var candidates = new SubjectiveMemoryRecord[sourceResult.References.Count];
            for (int index = 0; index < candidates.Length; index++)
            {
                candidates[index] = SubjectiveMemoryRecord.Create(
                    sourceRecord,
                    sourceResult.References[index]);
            }

            lock (_subjectiveMemoryStore.SyncRoot)
            {
                var inspections = new SubjectiveMemoryInspectionResult[candidates.Length];
                for (int index = 0; index < candidates.Length; index++)
                {
                    inspections[index] = _subjectiveMemoryStore.InspectUnderLock(candidates[index]);
                }

                bool everyMissing = HasOnlyInspectionKind(
                    inspections,
                    SubjectiveMemoryInspectionKind.Missing);
                bool everyExact = HasOnlyInspectionKind(
                    inspections,
                    SubjectiveMemoryInspectionKind.ExactExisting);

                if (sourceResult.Kind == SemanticDialogueMemoryFormationKind.Formed && everyMissing)
                {
                    SubjectiveMemoryStore.PreparedState prepared =
                        _subjectiveMemoryStore.PrepareAppendUnderLock(candidates);
                    _subjectiveMemoryStore.CommitUnderLock(prepared);
                    return SemanticDialogueMemoryHostAdmissionResult.Consistent(
                        SemanticDialogueMemoryHostAdmissionKind.Integrated,
                        sourceRecord,
                        candidates);
                }

                if (sourceResult.Kind == SemanticDialogueMemoryFormationKind.AlreadyFormed && everyExact)
                {
                    var existingRecords = new SubjectiveMemoryRecord[inspections.Length];
                    for (int index = 0; index < existingRecords.Length; index++)
                    {
                        existingRecords[index] = inspections[index].ExistingRecord!;
                    }

                    return SemanticDialogueMemoryHostAdmissionResult.Consistent(
                        SemanticDialogueMemoryHostAdmissionKind.AlreadyIntegrated,
                        sourceRecord,
                        existingRecords);
                }

                return SemanticDialogueMemoryHostAdmissionResult.Conflict(
                    SemanticDialogueMemoryHostAdmissionKind.LifecycleConflict);
            }
        }
    }

    public SemanticDialogueMemoryHostAuditSnapshot GetAuditSnapshot()
    {
        lock (_lifecycleSync)
        {
            return new SemanticDialogueMemoryHostAuditSnapshot(
                _eventStore.GetInsertionOrderSnapshot(),
                _experienceIndex.GetInsertionOrderSnapshot(),
                _subjectiveMemoryStore.GetInsertionOrderSnapshot());
        }
    }

    public BoundedSubjectiveMemoryRetrievalResult Retrieve(
        ActorId actorId,
        SimTime cueSimTime,
        int recordCountLimit)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        lock (_lifecycleSync)
        {
            return BoundedSubjectiveMemoryRetrievalRuntime.Retrieve(
                actorId,
                cueSimTime,
                recordCountLimit,
                _subjectiveMemoryStore,
                _eventStore);
        }
    }

    private static SemanticDialogueMemoryHostAdmissionResult MapFormationConflict(
        SemanticDialogueMemoryFormationKind kind)
    {
        SemanticDialogueMemoryHostAdmissionKind hostKind = kind switch
        {
            SemanticDialogueMemoryFormationKind.SourceConflict =>
                SemanticDialogueMemoryHostAdmissionKind.SourceConflict,
            SemanticDialogueMemoryFormationKind.ExperienceConflict =>
                SemanticDialogueMemoryHostAdmissionKind.ExperienceConflict,
            SemanticDialogueMemoryFormationKind.VisibilityConflict =>
                SemanticDialogueMemoryHostAdmissionKind.VisibilityConflict,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return SemanticDialogueMemoryHostAdmissionResult.Conflict(hostKind);
    }

    private static bool HasOnlyInspectionKind(
        IReadOnlyList<SubjectiveMemoryInspectionResult> inspections,
        SubjectiveMemoryInspectionKind expectedKind)
    {
        foreach (SubjectiveMemoryInspectionResult inspection in inspections)
        {
            if (inspection.Kind != expectedKind)
            {
                return false;
            }
        }

        return true;
    }
}
