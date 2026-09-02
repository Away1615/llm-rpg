using Alice.Actors;
using Alice.Commitments;
using System.Globalization;
using Alice.Interaction;
using Alice.Social;
using Alice.World;

namespace Alice.Npc;



/// <summary>Deterministically materializes the bounded two-step attendance Plan from actor-local Knowledge.</summary>
public static class AttendancePlanMaterializer
{
    public static bool TryMaterialize(
        ActorId actorId,
        Commitment commitment,
        KnownAttendanceDestination destination,
        out NpcPlan? plan)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(destination);
        if (commitment.Debtor != actorId || commitment.Status != CommitmentStatus.Active || commitment.Term is not PresenceWindowTerm term ||
            destination.CommitmentId != commitment.CommitmentId || destination.GatheringRef != term.GatheringRef ||
            destination.ExpectedGatheringRevision != term.ExpectedGatheringRevision || destination.CommitmentSourceRef != commitment.SourceRef)
        {
            plan = null;
            return false;
        }

        AttendancePlanIdentitySet identities = AttendancePlanIdentitySet.Derive(commitment.CommitmentId);
        var goalObjective = new FulfillCommitmentObjective(commitment.CommitmentId);
        var goal = new NpcGoal(identities.GoalId, goalObjective);
        var reachObjective = new ReachTargetObjective(destination.TargetRef);
        var reach = new PlanStep(
            identities.ReachStepId,
            reachObjective,
            null,
            destination.TargetRef,
            new InteractionTargetReached(actorId, destination.TargetRef, destination.InteractionRange));
        var fulfill = new PlanStep(
            identities.FulfillStepId,
            goalObjective,
            null,
            null,
            new CommitmentStatusMatches(actorId, commitment.CommitmentId, CommitmentStatus.Fulfilled));
        plan = new NpcPlan(identities.PlanId, actorId, goal, 1, [reach, fulfill]);
        return true;
    }
}

/// <summary>Deterministic identities derived injectively from one full attendance CommitmentId.</summary>
public sealed record AttendancePlanIdentitySet
{
    private const string GoalPrefix = "attendance-goal-v1:";
    private const string PlanPrefix = "attendance-plan-v1:";
    private const string ReachStepPrefix = "attendance-reach-step-v1:";
    private const string FulfillStepPrefix = "attendance-fulfill-step-v1:";

    private AttendancePlanIdentitySet(GoalId goalId, PlanId planId, PlanStepId reachStepId, PlanStepId fulfillStepId)
    {
        GoalId = goalId;
        PlanId = planId;
        ReachStepId = reachStepId;
        FulfillStepId = fulfillStepId;
    }

    public GoalId GoalId { get; }
    public PlanId PlanId { get; }
    public PlanStepId ReachStepId { get; }
    public PlanStepId FulfillStepId { get; }

    public static AttendancePlanIdentitySet Derive(CommitmentId commitmentId)
    {
        AttendancePlanningIdentity.Validate(commitmentId.Value, nameof(commitmentId));
        string encoded = Encode(commitmentId.Value);
        return new AttendancePlanIdentitySet(
            new GoalId(GoalPrefix + encoded),
            new PlanId(PlanPrefix + encoded),
            new PlanStepId(ReachStepPrefix + encoded),
            new PlanStepId(FulfillStepPrefix + encoded));
    }

    private static string Encode(string value) =>
        string.Concat(value.Length.ToString(CultureInfo.InvariantCulture), ":", value);
}

internal static class AttendancePlanningIdentity
{
    public static void Validate(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Attendance planning identity must be non-empty.", parameterName);
        }
    }
}



/// <summary>Immutable actor-local destination belief for one exact attendance Commitment.</summary>
public sealed record KnownAttendanceDestination
{
    internal KnownAttendanceDestination(
        CommitmentId commitmentId,
        GatheringRef gatheringRef,
        int expectedGatheringRevision,
        PlaceRef placeRef,
        TargetRef targetRef,
        InteractionRange interactionRange,
        CommitmentSourceRef commitmentSourceRef,
        DialogueClaimReference claimReference)
    {
        AttendancePlanningIdentity.Validate(commitmentId.Value, nameof(commitmentId));
        AttendancePlanningIdentity.Validate(gatheringRef.Value, nameof(gatheringRef));
        AttendancePlanningIdentity.Validate(placeRef.Value, nameof(placeRef));
        ArgumentNullException.ThrowIfNull(targetRef);
        ArgumentNullException.ThrowIfNull(commitmentSourceRef);
        ArgumentNullException.ThrowIfNull(claimReference);
        if (expectedGatheringRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedGatheringRevision));
        }

        if (!double.IsFinite(interactionRange.Value) || interactionRange.Value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(interactionRange));
        }

        CommitmentId = commitmentId;
        GatheringRef = gatheringRef;
        ExpectedGatheringRevision = expectedGatheringRevision;
        PlaceRef = placeRef;
        TargetRef = targetRef;
        InteractionRange = interactionRange;
        CommitmentSourceRef = commitmentSourceRef;
        ClaimReference = claimReference;
    }

    public CommitmentId CommitmentId { get; }
    public GatheringRef GatheringRef { get; }
    public int ExpectedGatheringRevision { get; }
    public PlaceRef PlaceRef { get; }
    public TargetRef TargetRef { get; }
    public InteractionRange InteractionRange { get; }
    public CommitmentSourceRef CommitmentSourceRef { get; }
    public DialogueClaimReference ClaimReference { get; }
}
