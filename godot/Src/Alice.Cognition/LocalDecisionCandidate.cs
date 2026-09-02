using Alice.Interaction;
using Alice.Identity;

namespace Alice.Cognition;

/// <summary>Exact normalized local score supplied by a later concrete scorer.</summary>
public readonly record struct NormalizedLocalScore
{
    public NormalizedLocalScore(decimal value)
    {
        if (value < 0m || value > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Value = value;
    }

    public decimal Value { get; }
}

/// <summary>Caller-owned deterministic reference for one bounded local candidate.</summary>
public sealed record LocalCandidateId
{
    public LocalCandidateId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Local candidate identifier must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

/// <summary>One already feasible typed local action and its explicit normalized score.</summary>
public sealed record LocalDecisionCandidate
{
    public LocalDecisionCandidate(LocalCandidateId candidateId, GameActionSpec action, NormalizedLocalScore score)
    {
        ArgumentNullException.ThrowIfNull(candidateId);
        ArgumentNullException.ThrowIfNull(action);
        CandidateId = candidateId;
        Action = action;
        Score = score;
    }

    public LocalCandidateId CandidateId { get; }
    public GameActionSpec Action { get; }
    public NormalizedLocalScore Score { get; }
}
