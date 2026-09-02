using System.Collections.ObjectModel;

namespace Alice.Cognition;

public enum LocalReasonerResolutionKind
{
    ModelSelected,
    Deferred,
    EscalationRequested,
    Failed
}

public enum LocalReasonerFailureReason
{
    UnknownCandidate,
    InvocationFailed,
    InvalidStructuredOutput,
    InvalidEscalationEvidence
}

/// <summary>Host-validated local choice, defer, escalation suggestion, or failure; never a world mutation.</summary>
public sealed class LocalReasonerResolution
{
    private readonly ReadOnlyCollection<string> _evidenceRefs;

    private LocalReasonerResolution(
        LocalReasonerResolutionKind kind,
        LocalDecisionCandidate? selectedCandidate,
        string? reasonCode,
        IEnumerable<string>? evidenceRefs,
        LocalReasonerFailureReason? failureReason)
    {
        Kind = kind;
        SelectedCandidate = selectedCandidate;
        ReasonCode = reasonCode;
        _evidenceRefs = Array.AsReadOnly(evidenceRefs?.ToArray() ?? []);
        FailureReason = failureReason;
    }

    public LocalReasonerResolutionKind Kind { get; }
    public LocalDecisionCandidate? SelectedCandidate { get; }
    public string? ReasonCode { get; }
    public IReadOnlyList<string> EvidenceRefs => _evidenceRefs;
    public LocalReasonerFailureReason? FailureReason { get; }

    internal static LocalReasonerResolution Selected(LocalDecisionCandidate candidate) =>
        new(LocalReasonerResolutionKind.ModelSelected, candidate, null, null, null);

    internal static LocalReasonerResolution Deferred(string reasonCode) =>
        new(LocalReasonerResolutionKind.Deferred, null, reasonCode, null, null);

    internal static LocalReasonerResolution Escalation(string reasonCode, IEnumerable<string> evidenceRefs) =>
        new(LocalReasonerResolutionKind.EscalationRequested, null, reasonCode, evidenceRefs, null);

    internal static LocalReasonerResolution Failed(LocalReasonerFailureReason reason) =>
        new(LocalReasonerResolutionKind.Failed, null, null, null, reason);
}
