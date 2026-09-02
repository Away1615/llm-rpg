using Alice.Activities;

namespace Alice.NpcExecution;

public enum NpcPlanTravelActivityResultDecisionKind
{
    StepCompleted,
    ResultPredicateUnsatisfied,
    NotApplicable
}

/// <summary>Bounded owner-side decision from one shared Travel result and current Plan Step.</summary>
public sealed class NpcPlanTravelActivityResultDecision
{
    public NpcPlanTravelActivityResultDecision(
        NpcPlanTravelActivityResultDecisionKind kind,
        TravelActivityResult? result)
    {
        if (!Enum.IsDefined(kind) ||
            kind == NpcPlanTravelActivityResultDecisionKind.NotApplicable && result is not null ||
            kind != NpcPlanTravelActivityResultDecisionKind.NotApplicable && result is null ||
            kind == NpcPlanTravelActivityResultDecisionKind.StepCompleted && result?.Kind != TravelActivityResultKind.Reached)
        {
            throw new ArgumentException("Travel Activity result decision properties are inconsistent.");
        }

        Kind = kind;
        Result = result;
    }

    public NpcPlanTravelActivityResultDecisionKind Kind { get; }
    public TravelActivityResult? Result { get; }
}
