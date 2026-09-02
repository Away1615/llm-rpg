using Alice.Interaction;
using Alice.World;

namespace Alice.Social;

/// <summary>Untrusted actor-visible destination details carried by one sourced dialogue claim.</summary>
public sealed record AttendanceDestinationClaim
{
    public AttendanceDestinationClaim(
        GatheringRef gatheringRef,
        int expectedGatheringRevision,
        PlaceRef placeRef,
        TargetRef targetRef,
        InteractionRange interactionRange,
        DialogueClaimReference claimReference)
    {
        SemanticDialogueIdentity.Validate(gatheringRef.Value, nameof(gatheringRef));
        SemanticDialogueIdentity.Validate(placeRef.Value, nameof(placeRef));
        ArgumentNullException.ThrowIfNull(targetRef);
        ArgumentNullException.ThrowIfNull(claimReference);
        if (expectedGatheringRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedGatheringRevision));
        }

        if (!double.IsFinite(interactionRange.Value) || interactionRange.Value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(interactionRange), "Attendance interaction range must be positive and finite.");
        }

        GatheringRef = gatheringRef;
        ExpectedGatheringRevision = expectedGatheringRevision;
        PlaceRef = placeRef;
        TargetRef = targetRef;
        InteractionRange = interactionRange;
        ClaimReference = claimReference;
    }

    public GatheringRef GatheringRef { get; }
    public int ExpectedGatheringRevision { get; }
    public PlaceRef PlaceRef { get; }
    public TargetRef TargetRef { get; }
    public InteractionRange InteractionRange { get; }
    public DialogueClaimReference ClaimReference { get; }
}
