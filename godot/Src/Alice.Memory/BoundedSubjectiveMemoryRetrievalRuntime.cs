using Alice.Activities;
using Alice.Actors;

namespace Alice.Memory;

public enum BoundedSubjectiveMemoryRetrievalKind
{
    Retrieved,
    Empty,
    SourceMissing,
    SourceConflict
}

public sealed record BoundedSubjectiveMemoryRetrievalResult
{
    private BoundedSubjectiveMemoryRetrievalResult(
        BoundedSubjectiveMemoryRetrievalKind kind,
        DecisionMemoryCandidateSet? candidateSet)
    {
        Kind = kind;
        CandidateSet = candidateSet;
    }

    public BoundedSubjectiveMemoryRetrievalKind Kind { get; }
    public DecisionMemoryCandidateSet? CandidateSet { get; }

    internal static BoundedSubjectiveMemoryRetrievalResult Retrieved(
        DecisionMemoryCandidateSet candidateSet) =>
        new(BoundedSubjectiveMemoryRetrievalKind.Retrieved, candidateSet);

    internal static BoundedSubjectiveMemoryRetrievalResult Closed(
        BoundedSubjectiveMemoryRetrievalKind kind) => new(kind, null);
}

/// <summary>Retrieves bounded actor-owned memories and projects exact semantic sources for one decision.</summary>
public static class BoundedSubjectiveMemoryRetrievalRuntime
{
    private static readonly DecisionMemoryKind SemanticDialogueExperienceKind =
        new("semantic_dialogue_experience");
    private static readonly DecisionMemoryProjectorVersion ProjectorVersion =
        new("semantic_dialogue_subjective_memory_projection_v1");

    public static BoundedSubjectiveMemoryRetrievalResult Retrieve(
        ActorId actorId,
        SimTime cueSimTime,
        int recordCountLimit,
        SubjectiveMemoryStore memoryStore,
        CanonicalEventStore eventStore)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(memoryStore);
        ArgumentNullException.ThrowIfNull(eventStore);
        if (recordCountLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recordCountLimit),
                "Record-count limit must be positive.");
        }

        lock (eventStore.SyncRoot)
        {
            lock (memoryStore.SyncRoot)
            {
                SubjectiveMemoryRecord[] records = memoryStore.GetActorRecordsUnderLock(actorId);
                Array.Sort(records, SubjectiveMemoryRecordRetrievalComparer.Instance);

                var selectedRecords = new List<SubjectiveMemoryRecord>(
                    Math.Min(recordCountLimit, records.Length));
                foreach (SubjectiveMemoryRecord record in records)
                {
                    if (record.SourceOccurredAt.Ticks > cueSimTime.Ticks)
                    {
                        continue;
                    }

                    selectedRecords.Add(record);
                    if (selectedRecords.Count == recordCountLimit)
                    {
                        break;
                    }
                }

                if (selectedRecords.Count == 0)
                {
                    return BoundedSubjectiveMemoryRetrievalResult.Closed(
                        BoundedSubjectiveMemoryRetrievalKind.Empty);
                }

                var sourceRecords = new List<SemanticDialogueSourceRecord>(selectedRecords.Count);
                foreach (SubjectiveMemoryRecord record in selectedRecords)
                {
                    SemanticDialogueSourceRecord? sourceRecord = eventStore.FindUnderLock(record.SourceId);
                    if (sourceRecord is null)
                    {
                        return BoundedSubjectiveMemoryRetrievalResult.Closed(
                            BoundedSubjectiveMemoryRetrievalKind.SourceMissing);
                    }

                    if (!record.HasExactSource(sourceRecord))
                    {
                        return BoundedSubjectiveMemoryRetrievalResult.Closed(
                            BoundedSubjectiveMemoryRetrievalKind.SourceConflict);
                    }

                    sourceRecords.Add(sourceRecord);
                }

                var slices = new DecisionMemorySlice[selectedRecords.Count];
                for (int index = 0; index < selectedRecords.Count; index++)
                {
                    SubjectiveMemoryRecord record = selectedRecords[index];
                    SemanticDialogueSourceRecord sourceRecord = sourceRecords[index];
                    slices[index] = DecisionMemorySlice.Create(
                        actorId,
                        SemanticDialogueExperienceKind,
                        record.SourceOccurredAt,
                        ProjectorVersion,
                        0,
                        DecisionMemoryEvidenceStatus.Current,
                        [record.SourceId],
                        [],
                        [],
                        sourceRecord.GetCanonicalBytes());
                }

                return BoundedSubjectiveMemoryRetrievalResult.Retrieved(
                    DecisionMemoryCandidateSet.Create(slices));
            }
        }
    }

    private sealed class SubjectiveMemoryRecordRetrievalComparer : IComparer<SubjectiveMemoryRecord>
    {
        public static SubjectiveMemoryRecordRetrievalComparer Instance { get; } = new();

        public int Compare(SubjectiveMemoryRecord? left, SubjectiveMemoryRecord? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            int timeComparison = right.SourceOccurredAt.Ticks.CompareTo(left.SourceOccurredAt.Ticks);
            return timeComparison != 0
                ? timeComparison
                : StringComparer.Ordinal.Compare(left.MemoryId.Value, right.MemoryId.Value);
        }
    }
}
