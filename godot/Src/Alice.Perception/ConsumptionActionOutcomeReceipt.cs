using Alice.Activities;
using Alice.Interaction;

namespace Alice.Perception;

public enum ConsumptionActionOutcome { Committed, Rejected }

public enum ConsumptionActionPerceivedFailure { InteractionUnavailable, InteractionChanged, OutOfRange, MethodUnavailable, SourceUnavailable, AttemptFailed }

/// <summary>Immutable actor-visible result of one settled Consumption attempt.</summary>
public sealed class ConsumptionActionOutcomeReceipt : IEquatable<ConsumptionActionOutcomeReceipt>
{
    public ConsumptionActionOutcomeReceipt(GameActionSpec attemptedAction, GameActionId gameActionId, ConsumptionActionOutcome outcome, ConsumptionActionPerceivedFailure? perceivedFailure, SimTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(attemptedAction);
        ArgumentNullException.ThrowIfNull(gameActionId);
        if (!Enum.IsDefined(outcome) || perceivedFailure is not null && !Enum.IsDefined(perceivedFailure.Value)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if (outcome == ConsumptionActionOutcome.Committed && perceivedFailure is not null || outcome == ConsumptionActionOutcome.Rejected && perceivedFailure is null) throw new ArgumentException("Outcome facts form an inconsistent actor-visible receipt.");

        AttemptedAction = attemptedAction;
        GameActionId = gameActionId;
        Outcome = outcome;
        PerceivedFailure = perceivedFailure;
        ObservedAt = observedAt;
    }

    public GameActionSpec AttemptedAction { get; }
    public GameActionId GameActionId { get; }
    public ConsumptionActionOutcome Outcome { get; }
    public ConsumptionActionPerceivedFailure? PerceivedFailure { get; }
    public SimTime ObservedAt { get; }

    public bool Equals(ConsumptionActionOutcomeReceipt? other) => other is not null && AttemptedAction == other.AttemptedAction && GameActionId == other.GameActionId && Outcome == other.Outcome && PerceivedFailure == other.PerceivedFailure && ObservedAt == other.ObservedAt;
    public override bool Equals(object? obj) => Equals(obj as ConsumptionActionOutcomeReceipt);
    public override int GetHashCode() => HashCode.Combine(AttemptedAction, GameActionId, Outcome, PerceivedFailure, ObservedAt);
}
