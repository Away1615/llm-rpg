using Alice.Interaction;
using Alice.Npc;

namespace Alice.Cognition;

/// <summary>Pure Host revalidation and score-hidden context projection.</summary>
public static class LocalReasonerContextBuilder
{
    public static LocalReasonerContext Build(
        ActorCognitionView view,
        DecisionGateDecision decision)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(decision);
        var candidateSet = new LocalDecisionCandidateSet(view, decision.RankedCandidates);
        DecisionGateDecision expectedDecision = DecisionGate.Evaluate(candidateSet);
        if (expectedDecision.Route != LocalDecisionRoute.L1 ||
            expectedDecision.CandidateCount < 2 ||
            expectedDecision.CandidateCount > DecisionGate.MAX_L1_CANDIDATES ||
            !expectedDecision.Equals(decision))
        {
            throw new ArgumentException("Local reasoner context requires one exact revalidated L1 Gate decision.", nameof(decision));
        }

        LocalReasonerOption[] options = BuildOptions(view, expectedDecision.RankedCandidates);
        Array.Sort(options, OptionComparer.Instance);
        return new LocalReasonerContext(
            new LocalReasonerSelfView(view.Self),
            view.Personality,
            view.CurrentPlan.Goal,
            view.CurrentStep,
            options);
    }

    private static LocalReasonerOption[] BuildOptions(
        ActorCognitionView view,
        IReadOnlyList<LocalDecisionCandidate> candidates)
    {
        var options = new LocalReasonerOption[candidates.Count];
        for (int index = 0; index < candidates.Count; index++)
        {
            LocalDecisionCandidate candidate = candidates[index];
            options[index] = BuildOption(view.Knowledge.KnownOpportunities, candidate);
        }

        return options;
    }

    private static LocalReasonerOption BuildOption(
        NpcKnownOpportunityState opportunities,
        LocalDecisionCandidate candidate)
    {
        InteractionBinding binding = candidate.Action.Binding;
        if (candidate.Action.Arguments is DamageActionArguments)
        {
            if (opportunities.TryResolveDamage(binding.ContractRef, out KnownDamageOpportunity? damage) && damage is not null)
            {
                return new LocalReasonerDamageOption(candidate.CandidateId, candidate.Action, damage);
            }
        }
        else if (candidate.Action.Arguments is ConsumptionActionArguments)
        {
            if (opportunities.TryResolveConsumption(binding.ContractRef, out KnownConsumptionOpportunity? consumption) && consumption is not null)
            {
                return new LocalReasonerConsumptionOption(candidate.CandidateId, candidate.Action, consumption);
            }
        }
        else if (candidate.Action.Arguments is PickupActionArguments)
        {
            if (opportunities.TryResolvePickup(binding.ContractRef, out KnownPickupOpportunity? pickup) && pickup is not null)
            {
                return new LocalReasonerPickupOption(candidate.CandidateId, candidate.Action, pickup);
            }
        }

        throw new InvalidOperationException("Revalidated local candidate has no matching actor-known option.");
    }

    private sealed class OptionComparer : IComparer<LocalReasonerOption>
    {
        public static OptionComparer Instance { get; } = new();

        public int Compare(LocalReasonerOption? left, LocalReasonerOption? right)
        {
            return StringComparer.Ordinal.Compare(left?.CandidateId.Value, right?.CandidateId.Value);
        }
    }
}
