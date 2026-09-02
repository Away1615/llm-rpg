namespace Alice.NpcExecution;

public enum NpcPlanTravelPreparationKind
{
    Ready,
    NotApplicable,
    TargetSpatialEvidenceMissing
}

public sealed class NpcPlanTravelPreparationDecision
{
    public NpcPlanTravelPreparationDecision(NpcPlanTravelPreparationKind kind, NpcTravelToRequest? request)
    {
        if (!Enum.IsDefined(kind) || kind == NpcPlanTravelPreparationKind.Ready && request is null || kind != NpcPlanTravelPreparationKind.Ready && request is not null)
        {
            throw new ArgumentException("Travel preparation decision properties are inconsistent.");
        }

        Kind = kind;
        Request = request;
    }

    public NpcPlanTravelPreparationKind Kind { get; }
    public NpcTravelToRequest? Request { get; }
}
