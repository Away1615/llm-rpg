using Alice.Activities;
using Alice.Actors;
using Alice.Navigation;
using Alice.World;

namespace Alice.NpcExecution;

/// <summary>Correlated confirmation that one NPC navigation operation started.</summary>
public sealed record NpcTravelStartedFact
{
    public NpcTravelStartedFact(
        ActorId actorId,
        ActivityId activityId,
        NavigationOperationId navigationOperationId,
        TargetRef targetRef,
        WorldPosition confirmedStartingPosition)
    {
        ValidateCorrelation(actorId, activityId, navigationOperationId, targetRef);
        ValidateFinite(confirmedStartingPosition, nameof(confirmedStartingPosition));
        ActorId = actorId;
        ActivityId = activityId;
        NavigationOperationId = navigationOperationId;
        TargetRef = targetRef;
        ConfirmedStartingPosition = confirmedStartingPosition;
    }

    public ActorId ActorId { get; }
    public ActivityId ActivityId { get; }
    public NavigationOperationId NavigationOperationId { get; }
    public TargetRef TargetRef { get; }
    public WorldPosition ConfirmedStartingPosition { get; }

    internal static void ValidateCorrelation(
        ActorId actorId,
        ActivityId activityId,
        NavigationOperationId navigationOperationId,
        TargetRef targetRef)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(targetRef);
        if (navigationOperationId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(navigationOperationId));
        }
    }

    internal static void ValidateFinite(WorldPosition position, string parameterName)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

/// <summary>One collision-resolved NPC motion fact from the active on-screen spatial source.</summary>
public sealed record NpcTravelMotionFact
{
    public NpcTravelMotionFact(
        ActorId actorId,
        ActivityId activityId,
        NavigationOperationId navigationOperationId,
        TargetRef targetRef,
        WorldPosition previousConfirmedPosition,
        MotionResult motionResult,
        WorldPosition resultingConfirmedPosition)
    {
        NpcTravelStartedFact.ValidateCorrelation(actorId, activityId, navigationOperationId, targetRef);
        ArgumentNullException.ThrowIfNull(motionResult);
        NpcTravelStartedFact.ValidateFinite(previousConfirmedPosition, nameof(previousConfirmedPosition));
        NpcTravelStartedFact.ValidateFinite(resultingConfirmedPosition, nameof(resultingConfirmedPosition));
        ValidateFinite(motionResult.ActualDisplacement, nameof(motionResult));
        ValidateFinite(motionResult.ActualVelocity, nameof(motionResult));
        var expectedPosition = new WorldPosition(
            previousConfirmedPosition.X + motionResult.ActualDisplacement.X,
            previousConfirmedPosition.Y + motionResult.ActualDisplacement.Y);
        NpcTravelStartedFact.ValidateFinite(expectedPosition, nameof(motionResult));
        if (expectedPosition != resultingConfirmedPosition)
        {
            throw new ArgumentException("Motion fact positions must match the accepted actual displacement.", nameof(resultingConfirmedPosition));
        }

        ActorId = actorId;
        ActivityId = activityId;
        NavigationOperationId = navigationOperationId;
        TargetRef = targetRef;
        PreviousConfirmedPosition = previousConfirmedPosition;
        MotionResult = motionResult;
        ResultingConfirmedPosition = resultingConfirmedPosition;
    }

    public ActorId ActorId { get; }
    public ActivityId ActivityId { get; }
    public NavigationOperationId NavigationOperationId { get; }
    public TargetRef TargetRef { get; }
    public WorldPosition PreviousConfirmedPosition { get; }
    public MotionResult MotionResult { get; }
    public WorldPosition ResultingConfirmedPosition { get; }

    private static void ValidateFinite(MotionVector vector, string parameterName)
    {
        if (!double.IsFinite(vector.X) || !double.IsFinite(vector.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

/// <summary>Confirmed final on-screen position when one projection is explicitly released.</summary>
public sealed record NpcTravelProjectionReleasedFact
{
    public NpcTravelProjectionReleasedFact(
        ActorId actorId,
        ActivityId activityId,
        NavigationOperationId navigationOperationId,
        TargetRef targetRef,
        WorldPosition confirmedFinalPosition)
    {
        NpcTravelStartedFact.ValidateCorrelation(actorId, activityId, navigationOperationId, targetRef);
        NpcTravelStartedFact.ValidateFinite(confirmedFinalPosition, nameof(confirmedFinalPosition));
        ActorId = actorId;
        ActivityId = activityId;
        NavigationOperationId = navigationOperationId;
        TargetRef = targetRef;
        ConfirmedFinalPosition = confirmedFinalPosition;
    }

    public ActorId ActorId { get; }
    public ActivityId ActivityId { get; }
    public NavigationOperationId NavigationOperationId { get; }
    public TargetRef TargetRef { get; }
    public WorldPosition ConfirmedFinalPosition { get; }
}
