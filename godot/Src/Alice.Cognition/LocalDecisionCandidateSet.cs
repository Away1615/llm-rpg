using System.Collections.ObjectModel;
using Alice.Interaction;
using Alice.Npc;

namespace Alice.Cognition;

/// <summary>Defensive canonical set of zero through four actor-visible local candidates.</summary>
public sealed class LocalDecisionCandidateSet : IEquatable<LocalDecisionCandidateSet>
{
    private readonly ReadOnlyCollection<LocalDecisionCandidate> _rankedCandidates;

    public LocalDecisionCandidateSet(
        ActorCognitionView view,
        IEnumerable<LocalDecisionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(candidates);
        LocalDecisionCandidate[] snapshot = candidates.ToArray();
        if (snapshot.Length > DecisionGate.MAX_L1_CANDIDATES)
        {
            throw new ArgumentOutOfRangeException(nameof(candidates));
        }

        for (int index = 0; index < snapshot.Length; index++)
        {
            LocalDecisionCandidate candidate = snapshot[index] ??
                throw new ArgumentNullException(nameof(candidates));
            EnsureCorrelated(view, candidate);
            for (int previous = 0; previous < index; previous++)
            {
                if (snapshot[previous].CandidateId == candidate.CandidateId)
                {
                    throw new ArgumentException("Local candidates must have unique CandidateIds.", nameof(candidates));
                }

                if (snapshot[previous].Action == candidate.Action)
                {
                    throw new ArgumentException("Local candidates must not duplicate a GameActionSpec.", nameof(candidates));
                }
            }
        }

        Array.Sort(snapshot, CandidateComparer.Instance);
        _rankedCandidates = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<LocalDecisionCandidate> RankedCandidates => _rankedCandidates;
    public int Count => _rankedCandidates.Count;

    public bool Equals(LocalDecisionCandidateSet? other)
    {
        return other is not null && RankedCandidates.SequenceEqual(other.RankedCandidates);
    }

    public override bool Equals(object? obj) => Equals(obj as LocalDecisionCandidateSet);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (LocalDecisionCandidate candidate in RankedCandidates)
        {
            hash.Add(candidate);
        }

        return hash.ToHashCode();
    }

    private static void EnsureCorrelated(ActorCognitionView view, LocalDecisionCandidate candidate)
    {
        GameActionSpec action = candidate.Action;
        if (action.ActorId != view.ActorId ||
            view.CurrentStep.Target is null ||
            action.Binding.ContractRef.TargetRef != view.CurrentStep.Target)
        {
            throw new ArgumentException("Local candidate must belong to the view actor and current Step target.", nameof(candidate));
        }

        NpcKnownOpportunityState opportunities = view.Knowledge.KnownOpportunities;
        bool matches = action.Arguments switch
        {
            DamageActionArguments => MatchesDamage(opportunities, action.Binding),
            ConsumptionActionArguments consumption => MatchesConsumption(opportunities, action.Binding, consumption),
            PickupActionArguments pickup => MatchesPickup(opportunities, action.Binding, pickup),
            _ => false
        };
        if (!matches)
        {
            throw new ArgumentException("Local candidate must match one actor-known opportunity in the same action family.", nameof(candidate));
        }
    }

    private static bool MatchesDamage(NpcKnownOpportunityState opportunities, InteractionBinding binding)
    {
        return opportunities.TryResolveDamage(binding.ContractRef, out KnownDamageOpportunity? opportunity) &&
            opportunity is not null && MatchesBinding(binding, opportunity.ObservedVersion, opportunity.BelievedRequirement);
    }

    private static bool MatchesConsumption(
        NpcKnownOpportunityState opportunities,
        InteractionBinding binding,
        ConsumptionActionArguments arguments)
    {
        return opportunities.TryResolveConsumption(binding.ContractRef, out KnownConsumptionOpportunity? opportunity) &&
            opportunity is not null &&
            MatchesBinding(binding, opportunity.ObservedVersion, opportunity.BelievedRequirement) &&
            arguments.SourceItemTypeId == opportunity.SourceItemTypeId;
    }

    private static bool MatchesPickup(
        NpcKnownOpportunityState opportunities,
        InteractionBinding binding,
        PickupActionArguments arguments)
    {
        return opportunities.TryResolvePickup(binding.ContractRef, out KnownPickupOpportunity? opportunity) &&
            opportunity is not null &&
            MatchesBinding(binding, opportunity.ObservedVersion, opportunity.BelievedRequirement) &&
            arguments.WorldDropId.Equals(opportunity.WorldDropId);
    }

    private static bool MatchesBinding(
        InteractionBinding binding,
        long observedVersion,
        KnownCapabilityRequirement requirement)
    {
        return binding.ExpectedVersion.Value == observedVersion &&
            binding.Capability == requirement.CapabilityIdentity;
    }

    private sealed class CandidateComparer : IComparer<LocalDecisionCandidate>
    {
        public static CandidateComparer Instance { get; } = new();

        public int Compare(LocalDecisionCandidate? left, LocalDecisionCandidate? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return 1;
            if (right is null) return -1;
            int score = right.Score.Value.CompareTo(left.Score.Value);
            return score != 0
                ? score
                : StringComparer.Ordinal.Compare(left.CandidateId.Value, right.CandidateId.Value);
        }
    }
}
