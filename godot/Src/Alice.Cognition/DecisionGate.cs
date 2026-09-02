namespace Alice.Cognition;

/// <summary>Pure fixed-threshold ScoreGap route for an already validated local candidate set.</summary>
public static class DecisionGate
{
    public const int MAX_L1_CANDIDATES = 4;
    public const decimal L1_SCORE_GAP_THRESHOLD = 0.10m;

    public static DecisionGateDecision Evaluate(LocalDecisionCandidateSet candidateSet)
    {
        ArgumentNullException.ThrowIfNull(candidateSet);
        IReadOnlyList<LocalDecisionCandidate> ranked = candidateSet.RankedCandidates;
        if (ranked.Count == 0)
        {
            return new DecisionGateDecision(
                LocalDecisionRoute.NeedsBlockerPolicy,
                ranked,
                null,
                null,
                null,
                null);
        }

        LocalDecisionCandidate top1 = ranked[0];
        if (ranked.Count == 1)
        {
            return new DecisionGateDecision(
                LocalDecisionRoute.L0,
                ranked,
                top1,
                top1,
                null,
                null);
        }

        LocalDecisionCandidate top2 = ranked[1];
        var scoreGap = new NormalizedLocalScore(top1.Score.Value - top2.Score.Value);
        LocalDecisionRoute route = scoreGap.Value > L1_SCORE_GAP_THRESHOLD
            ? LocalDecisionRoute.L0
            : LocalDecisionRoute.L1;
        return new DecisionGateDecision(
            route,
            ranked,
            route == LocalDecisionRoute.L0 ? top1 : null,
            top1,
            top2,
            scoreGap);
    }
}
