using Alice.Activities;
using Alice.Actors;
using Alice.Commitments;
using Alice.Social;
using Alice.World;

namespace Alice.Authority;

/// <summary>Authority audit evidence for one committed attendance arrival.</summary>
public sealed record PresenceCommitReceipt
{
    internal PresenceCommitReceipt(
        CommitmentId commitmentId,
        ActorId debtor,
        GatheringRef gatheringRef,
        int gatheringRevision,
        PlaceRef placeRef,
        TargetRef targetRef,
        ActivityId sourceActivityId,
        SimTime arrivalTime,
        CommitmentStatus previousStatus,
        CommitmentStatus currentStatus)
    {
        CommitmentId = commitmentId;
        Debtor = debtor;
        GatheringRef = gatheringRef;
        GatheringRevision = gatheringRevision;
        PlaceRef = placeRef;
        TargetRef = targetRef;
        SourceActivityId = sourceActivityId;
        ArrivalTime = arrivalTime;
        PreviousStatus = previousStatus;
        CurrentStatus = currentStatus;
    }

    public CommitmentId CommitmentId { get; }
    public ActorId Debtor { get; }
    public GatheringRef GatheringRef { get; }
    public int GatheringRevision { get; }
    public PlaceRef PlaceRef { get; }
    public TargetRef TargetRef { get; }
    public ActivityId SourceActivityId { get; }
    public SimTime ArrivalTime { get; }
    public CommitmentStatus PreviousStatus { get; }
    public CommitmentStatus CurrentStatus { get; }
}
