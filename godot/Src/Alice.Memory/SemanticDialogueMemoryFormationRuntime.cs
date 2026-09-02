using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Activities;
using Alice.Social;

namespace Alice.Memory;

public enum SemanticDialogueMemoryFormationKind
{
    Formed,
    AlreadyFormed,
    SourceConflict,
    ExperienceConflict,
    VisibilityConflict
}

public sealed record SemanticDialogueMemoryFormationResult
{
    private static readonly IReadOnlyList<ActorExperienceReference> NoReferences =
        new ReadOnlyCollection<ActorExperienceReference>([]);

    private SemanticDialogueMemoryFormationResult(
        SemanticDialogueMemoryFormationKind kind,
        SemanticDialogueSourceRecord? sourceRecord,
        IReadOnlyList<ActorExperienceReference> references)
    {
        Kind = kind;
        SourceRecord = sourceRecord;
        References = references;
    }

    public SemanticDialogueMemoryFormationKind Kind { get; }
    public SemanticDialogueSourceRecord? SourceRecord { get; }
    public IReadOnlyList<ActorExperienceReference> References { get; }

    internal static SemanticDialogueMemoryFormationResult Consistent(
        SemanticDialogueMemoryFormationKind kind,
        SemanticDialogueSourceRecord sourceRecord,
        IReadOnlyList<ActorExperienceReference> references) =>
        new(kind, sourceRecord, new ReadOnlyCollection<ActorExperienceReference>(references.ToArray()));

    internal static SemanticDialogueMemoryFormationResult Conflict(SemanticDialogueMemoryFormationKind kind) =>
        new(kind, null, NoReferences);
}

/// <summary>Deterministically forms one semantic source and its exact actor-visible references.</summary>
public static class SemanticDialogueMemoryFormationRuntime
{
    public static SemanticDialogueMemoryFormationResult Form(
        ConversationSession session,
        SemanticDialogueTurn turn,
        SimTime occurredAt,
        CanonicalEventStore eventStore,
        ActorExperienceIndex experienceIndex)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(experienceIndex);

        if (!HasExactOwnedExperienceSet(session, turn))
        {
            return SemanticDialogueMemoryFormationResult.Conflict(
                SemanticDialogueMemoryFormationKind.ExperienceConflict);
        }

        SemanticDialogueSourceRecord sourceRecord = SemanticDialogueSourceRecord.Create(session, turn, occurredAt);
        ActorExperienceReference[] expectedReferences = CreateExpectedReferences(sourceRecord, turn.Act);

        lock (eventStore.SyncRoot)
        {
            lock (experienceIndex.SyncRoot)
            {
                CanonicalEventInspectionResult sourceInspection = eventStore.InspectUnderLock(sourceRecord);
                if (sourceInspection.Kind == CanonicalEventInspectionKind.IdentityConflict)
                {
                    return SemanticDialogueMemoryFormationResult.Conflict(
                        SemanticDialogueMemoryFormationKind.SourceConflict);
                }

                ActorExperienceReference[] existingSourceReferences =
                    experienceIndex.GetSourceReferencesUnderLock(sourceRecord.SourceId);
                ActorExperienceInspectionResult[] referenceInspections = expectedReferences
                    .Select(experienceIndex.InspectUnderLock)
                    .ToArray();

                if (existingSourceReferences.Length != expectedReferences.Length && existingSourceReferences.Length != 0
                    || referenceInspections.Any(result => result.Kind == ActorExperienceInspectionKind.RoleConflict))
                {
                    return SemanticDialogueMemoryFormationResult.Conflict(
                        SemanticDialogueMemoryFormationKind.VisibilityConflict);
                }

                bool everyReferenceMissing = referenceInspections.All(
                    result => result.Kind == ActorExperienceInspectionKind.Missing);
                bool everyReferenceExact = referenceInspections.All(
                    result => result.Kind == ActorExperienceInspectionKind.ExactExisting);

                if (sourceInspection.Kind == CanonicalEventInspectionKind.ExactExisting)
                {
                    if (everyReferenceExact && existingSourceReferences.Length == expectedReferences.Length)
                    {
                        Array.Sort(existingSourceReferences, CompareReferences);
                        return SemanticDialogueMemoryFormationResult.Consistent(
                            SemanticDialogueMemoryFormationKind.AlreadyFormed,
                            sourceInspection.ExistingRecord!,
                            existingSourceReferences);
                    }

                    return SemanticDialogueMemoryFormationResult.Conflict(
                        SemanticDialogueMemoryFormationKind.VisibilityConflict);
                }

                if (!everyReferenceMissing || existingSourceReferences.Length != 0)
                {
                    return SemanticDialogueMemoryFormationResult.Conflict(
                        SemanticDialogueMemoryFormationKind.VisibilityConflict);
                }

                CanonicalEventStore.PreparedState preparedEventState =
                    eventStore.PrepareAppendUnderLock(sourceRecord);
                ActorExperienceIndex.PreparedState preparedExperienceState =
                    experienceIndex.PrepareAppendUnderLock(expectedReferences);
                eventStore.CommitUnderLock(preparedEventState);
                experienceIndex.CommitUnderLock(preparedExperienceState);

                return SemanticDialogueMemoryFormationResult.Consistent(
                    SemanticDialogueMemoryFormationKind.Formed,
                    sourceRecord,
                    expectedReferences);
            }
        }
    }

    private static bool HasExactOwnedExperienceSet(
        ConversationSession session,
        SemanticDialogueTurn turn)
    {
        if (session.Transcript.Count(candidate => ReferenceEquals(candidate, turn)) != 1)
        {
            return false;
        }

        SemanticDialogueExperience[] sourceExperiences = session.Experiences
            .Where(experience => experience.SessionId == session.SessionId
                && experience.SourceActId == turn.Act.ActId)
            .ToArray();
        ActorId[] expectedActors = [turn.Act.Speaker, .. turn.Act.Recipients];
        if (sourceExperiences.Length != expectedActors.Length)
        {
            return false;
        }

        return expectedActors.All(expectedActor =>
            sourceExperiences.Count(experience => experience.VisibleToActorId == expectedActor) == 1);
    }

    private static ActorExperienceReference[] CreateExpectedReferences(
        SemanticDialogueSourceRecord sourceRecord,
        SemanticDialogueAct act)
    {
        ActorExperienceReference[] references =
        [
            new ActorExperienceReference(act.Speaker, sourceRecord.SourceId, ActorExperienceRole.Caused),
            .. act.Recipients.Select(recipient =>
                new ActorExperienceReference(recipient, sourceRecord.SourceId, ActorExperienceRole.Received))
        ];
        Array.Sort(references, CompareReferences);
        return references;
    }

    private static int CompareReferences(ActorExperienceReference left, ActorExperienceReference right) =>
        StringComparer.Ordinal.Compare(left.ActorId.Value, right.ActorId.Value);
}
