using Alice.Authority;

namespace Alice.Cognition;

/// <summary>Projects one committed attendance arrival into its directly changed indexed node.</summary>
public sealed class PresenceCommitAffectedNodeFactProjector
{
    public AffectedNodeFact Project(PresenceCommitReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new AffectedNodeFact(
            DependencySourceKind.Event,
            string.Concat("presence_commit_v1/", receipt.SourceActivityId.Value),
            AffectedNode.FromCommitment(receipt.CommitmentId));
    }
}
