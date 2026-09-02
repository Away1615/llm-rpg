using Alice.Activities;
using Alice.Actors;
using Alice.Social;

namespace Alice.Commitments;

public readonly record struct CommitmentId(string Value);
public readonly record struct ResourceRef(string Value);
public readonly record struct CommitmentDestinationRef(string Value);

public sealed record CommitmentSourceRef
{
    public CommitmentSourceRef(ConversationSessionId sessionId, SemanticDialogueActId sourceActId)
    {
        CommitmentIdentity.Validate(sessionId.Value, nameof(sessionId));
        CommitmentIdentity.Validate(sourceActId.Value, nameof(sourceActId));
        SessionId = sessionId;
        SourceActId = sourceActId;
    }

    public CommitmentSourceRef(string canonicalEventId)
    {
        CommitmentIdentity.Validate(canonicalEventId, nameof(canonicalEventId));
        CanonicalEventId = canonicalEventId;
    }

    public ConversationSessionId? SessionId { get; }
    public SemanticDialogueActId? SourceActId { get; }
    public string? CanonicalEventId { get; }
}

public abstract record CommitmentTerm
{
    internal CommitmentTerm()
    {
    }
}

public sealed record ResourceTransferTerm : CommitmentTerm
{
    public ResourceTransferTerm(ResourceRef resourceRef, int amount, CommitmentDestinationRef destinationRef)
    {
        CommitmentIdentity.Validate(resourceRef.Value, nameof(resourceRef));
        CommitmentIdentity.Validate(destinationRef.Value, nameof(destinationRef));
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        ResourceRef = resourceRef;
        Amount = amount;
        DestinationRef = destinationRef;
    }

    public ResourceRef ResourceRef { get; }
    public int Amount { get; }
    public CommitmentDestinationRef DestinationRef { get; }
}

/// <summary>One fungible Coin or resource transfer; debtor and creditor remain on the Commitment envelope.</summary>
public sealed record CoinOrResourceTransferTerm : CommitmentTerm
{
    public CoinOrResourceTransferTerm(ResourceRef assetRef, long amount)
    {
        CommitmentIdentity.Validate(assetRef.Value, nameof(assetRef));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        AssetRef = assetRef;
        Amount = amount;
    }

    public ResourceRef AssetRef { get; }
    public long Amount { get; }
}

public sealed record PresenceWindowTerm : CommitmentTerm
{
    public PresenceWindowTerm(GatheringRef gatheringRef, int expectedGatheringRevision)
    {
        CommitmentIdentity.Validate(gatheringRef.Value, nameof(gatheringRef));
        if (expectedGatheringRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedGatheringRevision));
        }

        GatheringRef = gatheringRef;
        ExpectedGatheringRevision = expectedGatheringRevision;
    }

    public GatheringRef GatheringRef { get; }
    public int ExpectedGatheringRevision { get; }
}

public enum CommitmentStatus
{
    Active,
    Overdue,
    Fulfilled,
    Cancelled
}

/// <summary>Immutable Authority-owned obligation with exactly one typed term.</summary>
public sealed record Commitment
{
    public Commitment(
        CommitmentId commitmentId,
        ActorId debtor,
        ActorId creditor,
        CommitmentTerm term,
        SimTime deadline,
        CommitmentStatus status,
        CommitmentSourceRef sourceRef)
    {
        CommitmentIdentity.Validate(commitmentId.Value, nameof(commitmentId));
        CommitmentIdentity.ValidateActor(debtor, nameof(debtor));
        CommitmentIdentity.ValidateActor(creditor, nameof(creditor));
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(sourceRef);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        CommitmentId = commitmentId;
        Debtor = debtor;
        Creditor = creditor;
        Term = term;
        Deadline = deadline;
        Status = status;
        SourceRef = sourceRef;
    }

    public CommitmentId CommitmentId { get; }
    public ActorId Debtor { get; }
    public ActorId Creditor { get; }
    public CommitmentTerm Term { get; }
    public SimTime Deadline { get; }
    public CommitmentStatus Status { get; }
    public CommitmentSourceRef SourceRef { get; }

    internal Commitment AsFulfilled()
    {
        if (Status is not (CommitmentStatus.Active or CommitmentStatus.Overdue))
        {
            throw new InvalidOperationException("Only an active or overdue Commitment can be fulfilled.");
        }

        return WithStatus(CommitmentStatus.Fulfilled);
    }

    internal Commitment AsCancelled()
    {
        if (Status is not (CommitmentStatus.Active or CommitmentStatus.Overdue))
            throw new InvalidOperationException("Only an active or overdue Commitment can be cancelled.");
        return WithStatus(CommitmentStatus.Cancelled);
    }

    internal Commitment AsOverdue()
    {
        if (Status != CommitmentStatus.Active)
            throw new InvalidOperationException("Only an active Commitment can become overdue.");
        return WithStatus(CommitmentStatus.Overdue);
    }

    private Commitment WithStatus(CommitmentStatus status) => new(
        CommitmentId,
        Debtor,
        Creditor,
        Term,
        Deadline,
        status,
        SourceRef);
}

internal static class CommitmentIdentity
{
    public static void Validate(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identity values must be non-empty.", parameterName);
        }
    }

    public static void ValidateActor(ActorId actorId, string parameterName) => Validate(actorId.Value, parameterName);
}
