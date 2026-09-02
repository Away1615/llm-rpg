using System.Collections.ObjectModel;

namespace Alice.Cognition;

public abstract record LocalReasonerDecision
{
    private protected LocalReasonerDecision() { }
}

public sealed record LocalReasonerChoice : LocalReasonerDecision
{
    public LocalReasonerChoice(LocalCandidateId nextAction)
    {
        ArgumentNullException.ThrowIfNull(nextAction);
        NextAction = nextAction;
    }

    public LocalCandidateId NextAction { get; }
}

public sealed record LocalReasonerDefer : LocalReasonerDecision
{
    public LocalReasonerDefer(string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ReasonCode = reasonCode;
    }

    public string ReasonCode { get; }
}

public sealed record LocalReasonerEscalationRequest : LocalReasonerDecision
{
    private readonly ReadOnlyCollection<string> _evidenceRefs;

    public LocalReasonerEscalationRequest(string reasonCode, IEnumerable<string> evidenceRefs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ArgumentNullException.ThrowIfNull(evidenceRefs);
        string[] refs = evidenceRefs.ToArray();
        if (refs.Length == 0 || refs.Any(string.IsNullOrWhiteSpace) || refs.Distinct(StringComparer.Ordinal).Count() != refs.Length)
            throw new ArgumentException("Escalation evidence references must be non-empty and distinct.", nameof(evidenceRefs));
        ReasonCode = reasonCode;
        _evidenceRefs = Array.AsReadOnly(refs);
    }

    public string ReasonCode { get; }
    public IReadOnlyList<string> EvidenceRefs => _evidenceRefs;
}

public enum LocalReasonerCallFailureKind
{
    InvocationFailed,
    InvalidStructuredOutput
}

public abstract record LocalReasonerCallAttempt
{
    private protected LocalReasonerCallAttempt() { }
}

public sealed record LocalReasonerChoiceProduced : LocalReasonerCallAttempt
{
    public LocalReasonerChoiceProduced(LocalReasonerChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        Choice = choice;
    }

    public LocalReasonerChoice Choice { get; }
}

public sealed record LocalReasonerDeferProduced : LocalReasonerCallAttempt
{
    public LocalReasonerDeferProduced(LocalReasonerDefer decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        Decision = decision;
    }

    public LocalReasonerDefer Decision { get; }
}

public sealed record LocalReasonerEscalationRequested : LocalReasonerCallAttempt
{
    public LocalReasonerEscalationRequested(LocalReasonerEscalationRequest decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        Decision = decision;
    }

    public LocalReasonerEscalationRequest Decision { get; }
}

public sealed record LocalReasonerCallFailed : LocalReasonerCallAttempt
{
    public LocalReasonerCallFailed(LocalReasonerCallFailureKind failureKind)
    {
        if (!Enum.IsDefined(failureKind)) throw new ArgumentOutOfRangeException(nameof(failureKind));
        FailureKind = failureKind;
    }

    public LocalReasonerCallFailureKind FailureKind { get; }
}
