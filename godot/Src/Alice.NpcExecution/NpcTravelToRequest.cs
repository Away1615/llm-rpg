using Alice.Activities;
using Alice.World;

namespace Alice.NpcExecution;

/// <summary>Caller-supplied NPC travel request with a target-owned fixture stop range.</summary>
public sealed record NpcTravelToRequest
{
    public NpcTravelToRequest(ActivityId activityId, TargetRef targetRef, double stopRange)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(targetRef);
        if (!double.IsFinite(stopRange) || stopRange < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(stopRange));
        }

        ActivityId = activityId;
        TargetRef = targetRef;
        StopRange = stopRange;
    }

    public ActivityId ActivityId { get; }
    public TargetRef TargetRef { get; }
    public double StopRange { get; }
}
