using Alice.Activities;
using Alice.Actors;
using Alice.Identity;
using Alice.Interaction;

namespace Alice.Authority;

/// <summary>Closed audit provenance for an accepted persistent Authority commit.</summary>
public abstract record CommitOrigin
{
    private protected CommitOrigin() { }

    public sealed record ActorAction : CommitOrigin
    {
        public ActorAction(ActorId actorId, GameActionId gameActionId)
        {
            ArgumentNullException.ThrowIfNull(actorId);
            ArgumentNullException.ThrowIfNull(gameActionId);
            ActorId = actorId;
            GameActionId = gameActionId;
        }

        public ActorId ActorId { get; }
        public GameActionId GameActionId { get; }
    }

    public sealed record WorldRule : CommitOrigin
    {
        public WorldRule(RuleId ruleId, EvaluationId evaluationId, SimTime simTime)
        {
            ArgumentNullException.ThrowIfNull(ruleId);
            ArgumentNullException.ThrowIfNull(evaluationId);
            RuleId = ruleId;
            EvaluationId = evaluationId;
            SimTime = simTime;
        }

        public RuleId RuleId { get; }
        public EvaluationId EvaluationId { get; }
        public SimTime SimTime { get; }
    }
}

public sealed record RuleId
{
    public RuleId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Rule identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public sealed record EvaluationId
{
    public EvaluationId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Evaluation identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}
