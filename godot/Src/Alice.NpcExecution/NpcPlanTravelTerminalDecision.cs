using Alice.Navigation;

namespace Alice.NpcExecution;

public enum NpcPlanTravelTerminalKind
{
    StepCompleted,
    ArrivalPredicateUnsatisfied,
    TravelFailed,
    NotApplicable
}

public sealed class NpcPlanTravelTerminalDecision
{
    public NpcPlanTravelTerminalDecision(NpcPlanTravelTerminalKind kind, NpcTravelTerminalFact? fact)
    {
        if (!Enum.IsDefined(kind) || kind == NpcPlanTravelTerminalKind.NotApplicable && fact is not null || kind != NpcPlanTravelTerminalKind.NotApplicable && fact is null ||
            fact is not null && (kind is NpcPlanTravelTerminalKind.StepCompleted or NpcPlanTravelTerminalKind.ArrivalPredicateUnsatisfied) && fact.Status != NavigationStatus.Arrived ||
            fact is not null && kind == NpcPlanTravelTerminalKind.TravelFailed && fact.Status is NavigationStatus.Arrived or NavigationStatus.Moving)
        {
            throw new ArgumentException("Travel terminal decision properties are inconsistent.");
        }

        Kind = kind;
        Fact = fact;
    }

    public NpcPlanTravelTerminalKind Kind { get; }
    public NpcTravelTerminalFact? Fact { get; }
}
