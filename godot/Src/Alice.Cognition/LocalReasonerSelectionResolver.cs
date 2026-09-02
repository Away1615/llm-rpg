namespace Alice.Cognition;

/// <summary>Validates one typed local-model decision against the current actor-visible candidate set.</summary>
public static class LocalReasonerSelectionResolver
{
    private static readonly HashSet<string> EscalationReasons = new(StringComparer.Ordinal)
    {
        "no_feasible_local_action",
        "goal_or_plan_change",
        "commitment_or_debt",
        "major_relationship",
        "medical_or_body_deadline",
        "repeated_visible_failure"
    };

    public static LocalReasonerResolution Resolve(
        ActorCognitionView view,
        DecisionGateDecision decision,
        LocalReasonerContext context,
        LocalReasonerCallAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(attempt);
        LocalReasonerContext expectedContext = LocalReasonerContextBuilder.Build(view, decision);
        if (!expectedContext.Equals(context))
            throw new ArgumentException("Local reasoner context does not match the revalidated current view and Gate decision.", nameof(context));

        if (attempt is LocalReasonerChoiceProduced produced)
        {
            LocalDecisionCandidate? selected = FindCandidate(decision.RankedCandidates, produced.Choice.NextAction);
            return selected is null
                ? LocalReasonerResolution.Failed(LocalReasonerFailureReason.UnknownCandidate)
                : LocalReasonerResolution.Selected(selected);
        }

        if (attempt is LocalReasonerDeferProduced deferred)
            return LocalReasonerResolution.Deferred(deferred.Decision.ReasonCode);

        if (attempt is LocalReasonerEscalationRequested escalation)
        {
            bool validReason = EscalationReasons.Contains(escalation.Decision.ReasonCode);
            bool visibleEvidence = escalation.Decision.EvidenceRefs.All(reference =>
                decision.RankedCandidates.Any(candidate =>
                    StringComparer.Ordinal.Equals(candidate.CandidateId.Value, reference)));
            return validReason && visibleEvidence
                ? LocalReasonerResolution.Escalation(
                    escalation.Decision.ReasonCode,
                    escalation.Decision.EvidenceRefs)
                : LocalReasonerResolution.Failed(LocalReasonerFailureReason.InvalidEscalationEvidence);
        }

        if (attempt is LocalReasonerCallFailed failed)
        {
            LocalReasonerFailureReason reason = failed.FailureKind switch
            {
                LocalReasonerCallFailureKind.InvocationFailed => LocalReasonerFailureReason.InvocationFailed,
                LocalReasonerCallFailureKind.InvalidStructuredOutput => LocalReasonerFailureReason.InvalidStructuredOutput,
                _ => throw new ArgumentOutOfRangeException(nameof(attempt))
            };
            return LocalReasonerResolution.Failed(reason);
        }

        throw new ArgumentException("Local reasoner call attempt is outside the closed domain.", nameof(attempt));
    }

    private static LocalDecisionCandidate? FindCandidate(
        IReadOnlyList<LocalDecisionCandidate> candidates,
        LocalCandidateId candidateId)
    {
        for (int index = 0; index < candidates.Count; index++)
            if (candidates[index].CandidateId == candidateId) return candidates[index];
        return null;
    }
}
