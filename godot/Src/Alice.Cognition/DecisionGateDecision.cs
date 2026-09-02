using System.Collections.ObjectModel;

namespace Alice.Cognition;

public enum LocalDecisionRoute
{
    NeedsBlockerPolicy,
    L0,
    L1
}

/// <summary>Bounded deterministic routing evidence without execution or model output.</summary>
public sealed class DecisionGateDecision : IEquatable<DecisionGateDecision>
{
    private readonly ReadOnlyCollection<LocalDecisionCandidate> _rankedCandidates;

    internal DecisionGateDecision(
        LocalDecisionRoute route,
        IEnumerable<LocalDecisionCandidate> rankedCandidates,
        LocalDecisionCandidate? selectedCandidate,
        LocalDecisionCandidate? top1Fallback,
        LocalDecisionCandidate? top2,
        NormalizedLocalScore? scoreGap)
    {
        if (!Enum.IsDefined(route))
        {
            throw new ArgumentOutOfRangeException(nameof(route));
        }

        ArgumentNullException.ThrowIfNull(rankedCandidates);
        LocalDecisionCandidate[] snapshot = rankedCandidates.ToArray();
        ValidateShape(route, snapshot, selectedCandidate, top1Fallback, top2, scoreGap);
        Route = route;
        _rankedCandidates = Array.AsReadOnly(snapshot);
        SelectedCandidate = selectedCandidate;
        Top1Fallback = top1Fallback;
        Top2 = top2;
        ScoreGap = scoreGap;
    }

    public LocalDecisionRoute Route { get; }
    public IReadOnlyList<LocalDecisionCandidate> RankedCandidates => _rankedCandidates;
    public int CandidateCount => _rankedCandidates.Count;
    public LocalDecisionCandidate? SelectedCandidate { get; }
    public LocalDecisionCandidate? Top1Fallback { get; }
    public LocalDecisionCandidate? Top2 { get; }
    public NormalizedLocalScore? ScoreGap { get; }

    public bool Equals(DecisionGateDecision? other)
    {
        return other is not null &&
            Route == other.Route &&
            RankedCandidates.SequenceEqual(other.RankedCandidates) &&
            SelectedCandidate == other.SelectedCandidate &&
            Top1Fallback == other.Top1Fallback &&
            Top2 == other.Top2 &&
            ScoreGap == other.ScoreGap;
    }

    public override bool Equals(object? obj) => Equals(obj as DecisionGateDecision);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Route);
        foreach (LocalDecisionCandidate candidate in RankedCandidates)
        {
            hash.Add(candidate);
        }

        hash.Add(SelectedCandidate);
        hash.Add(Top1Fallback);
        hash.Add(Top2);
        hash.Add(ScoreGap);
        return hash.ToHashCode();
    }

    private static void ValidateShape(
        LocalDecisionRoute route,
        IReadOnlyList<LocalDecisionCandidate> rankedCandidates,
        LocalDecisionCandidate? selectedCandidate,
        LocalDecisionCandidate? top1Fallback,
        LocalDecisionCandidate? top2,
        NormalizedLocalScore? scoreGap)
    {
        if (rankedCandidates.Count == 0)
        {
            if (route != LocalDecisionRoute.NeedsBlockerPolicy || selectedCandidate is not null ||
                top1Fallback is not null || top2 is not null || scoreGap is not null)
            {
                throw new ArgumentException("An empty decision must be only a blocker-policy handoff.");
            }

            return;
        }

        if (route == LocalDecisionRoute.NeedsBlockerPolicy || top1Fallback != rankedCandidates[0])
        {
            throw new ArgumentException("A non-empty decision must expose its canonical top1 fallback.");
        }

        if (route == LocalDecisionRoute.L0 && selectedCandidate != top1Fallback ||
            route == LocalDecisionRoute.L1 && selectedCandidate is not null)
        {
            throw new ArgumentException("Only L0 selects the canonical top1 candidate.");
        }

        if (rankedCandidates.Count == 1)
        {
            if (route != LocalDecisionRoute.L0 || top2 is not null || scoreGap is not null)
            {
                throw new ArgumentException("A one-candidate decision must route L0 without synthetic gap evidence.");
            }

            return;
        }

        if (top2 != rankedCandidates[1] || scoreGap is null)
        {
            throw new ArgumentException("A multi-candidate decision must expose top2 and exact ScoreGap evidence.");
        }
    }
}
