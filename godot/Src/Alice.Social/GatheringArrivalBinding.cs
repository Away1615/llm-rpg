using Alice.World;

namespace Alice.Social;

/// <summary>Immutable Authority correlation from one gathering place to its Travel target.</summary>
public sealed record GatheringArrivalBinding
{
    public GatheringArrivalBinding(GatheringRef gatheringRef, PlaceRef placeRef, TargetRef targetRef)
    {
        SemanticDialogueIdentity.Validate(gatheringRef.Value, nameof(gatheringRef));
        SemanticDialogueIdentity.Validate(placeRef.Value, nameof(placeRef));
        ArgumentNullException.ThrowIfNull(targetRef);

        GatheringRef = gatheringRef;
        PlaceRef = placeRef;
        TargetRef = targetRef;
    }

    public GatheringRef GatheringRef { get; }
    public PlaceRef PlaceRef { get; }
    public TargetRef TargetRef { get; }
}
