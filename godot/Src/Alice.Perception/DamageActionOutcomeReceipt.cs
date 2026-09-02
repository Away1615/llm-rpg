using Alice.Activities;
using Alice.Interaction;

namespace Alice.Perception;

public enum DamageActionOutcome
{
    Committed,
    Rejected
}

public enum DamageActionPerceivedFailure
{
    TargetUnavailable,
    InteractionChanged,
    OutOfRange,
    MethodUnavailable,
    AttemptFailed
}

/// <summary>Immutable actor-visible result of one settled Damage action.</summary>
public sealed class DamageActionOutcomeReceipt : IEquatable<DamageActionOutcomeReceipt>
{
    public DamageActionOutcomeReceipt(GameActionSpec attemptedAction, DamageActionOutcome outcome, DamageActionPerceivedFailure? perceivedFailure, SimTime observedAt, bool targetBecameTerminal)
    {
        ArgumentNullException.ThrowIfNull(attemptedAction);
        if (attemptedAction.Arguments is not DamageActionArguments)
        {
            throw new ArgumentException("Damage outcome receipts require Damage action arguments.", nameof(attemptedAction));
        }
        if (!Enum.IsDefined(outcome) || perceivedFailure is not null && !Enum.IsDefined(perceivedFailure.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        if (outcome == DamageActionOutcome.Committed && perceivedFailure is not null || outcome == DamageActionOutcome.Rejected && perceivedFailure is null || outcome == DamageActionOutcome.Rejected && targetBecameTerminal)
        {
            throw new ArgumentException("Outcome facts form an inconsistent actor-visible receipt.");
        }

        AttemptedAction = attemptedAction;
        Outcome = outcome;
        PerceivedFailure = perceivedFailure;
        ObservedAt = observedAt;
        TargetBecameTerminal = targetBecameTerminal;
    }

    public GameActionSpec AttemptedAction { get; }
    public DamageActionOutcome Outcome { get; }
    public DamageActionPerceivedFailure? PerceivedFailure { get; }
    public SimTime ObservedAt { get; }
    public bool TargetBecameTerminal { get; }

    public bool Equals(DamageActionOutcomeReceipt? other)
    {
        return other is not null && AttemptedAction == other.AttemptedAction && Outcome == other.Outcome && PerceivedFailure == other.PerceivedFailure && ObservedAt == other.ObservedAt && TargetBecameTerminal == other.TargetBecameTerminal;
    }

    public override bool Equals(object? obj) => Equals(obj as DamageActionOutcomeReceipt);

    public override int GetHashCode() => HashCode.Combine(AttemptedAction, Outcome, PerceivedFailure, ObservedAt, TargetBecameTerminal);
}
