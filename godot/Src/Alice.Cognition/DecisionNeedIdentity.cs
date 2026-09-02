using Alice.Actors;
using Alice.Npc;

namespace Alice.Cognition;

public sealed record DecisionNeedId
{
    public DecisionNeedId(string value)
    {
        DecisionNeedIdentityValidation.ValidateSha256(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public sealed record DecisionNeedFingerprint
{
    public DecisionNeedFingerprint(string value)
    {
        DecisionNeedIdentityValidation.ValidateSha256(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public sealed record DecisionProblemDescriptorHash
{
    public DecisionProblemDescriptorHash(string value)
    {
        DecisionNeedIdentityValidation.ValidateSha256(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public sealed record DecisionNeedWorldRevision
{
    public DecisionNeedWorldRevision(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Value = value;
    }

    public long Value { get; }
}

public sealed record DecisionNeedKind
{
    public DecisionNeedKind(string value)
    {
        DecisionNeedIdentityValidation.ValidateCanonicalToken(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public sealed record DecisionProblemCode
{
    public DecisionProblemCode(string value)
    {
        DecisionNeedIdentityValidation.ValidateCanonicalToken(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

internal static class DecisionNeedIdentityValidation
{
    public static void ValidateActorAndStepCorrelation(
        ActorId actorId,
        PlanId? planId,
        PlanStepId? planStepId,
        DecisionProblemDescriptor problemDescriptor)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(problemDescriptor);
        if (planStepId is not null && planId is null)
        {
            throw new ArgumentException("A current Plan Step identity requires a Plan identity.", nameof(planId));
        }

        if (problemDescriptor is CurrentStepDecisionProblemDescriptor currentStep &&
            (currentStep.ActorId != actorId || planId is null || planStepId != currentStep.PlanStepId))
        {
            throw new ArgumentException(
                "A current-step problem must match the Need actor and current Plan Step identity.",
                nameof(problemDescriptor));
        }

        if (problemDescriptor is PlanlessStrategicDecisionProblemDescriptor planlessStrategic &&
            (planlessStrategic.ActorId != actorId || planId is not null || planStepId is not null))
        {
            throw new ArgumentException(
                "A planless strategic problem must match the Need actor and have null Plan identities.",
                nameof(problemDescriptor));
        }
    }

    public static void ValidateCanonicalToken(string? value, string parameterName)
    {
        if (value is null || value.Length is < 1 or > 64 || value[0] < 'a' || value[0] > 'z')
        {
            throw new ArgumentException("Value must be a canonical lower-ASCII token.", parameterName);
        }

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            bool valid = character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_';
            if (!valid)
            {
                throw new ArgumentException("Value must be a canonical lower-ASCII token.", parameterName);
            }
        }
    }

    public static void ValidateSha256(string? value, string parameterName)
    {
        if (value is null || value.Length != 64)
        {
            throw new ArgumentException("Value must be a lowercase SHA-256 hexadecimal string.", parameterName);
        }

        foreach (char character in value)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                throw new ArgumentException("Value must be a lowercase SHA-256 hexadecimal string.", parameterName);
            }
        }
    }
}
