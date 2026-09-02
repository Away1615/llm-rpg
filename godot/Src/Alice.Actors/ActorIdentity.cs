using Alice.Identity;

namespace Alice.Actors;

/// <summary>An immutable actor name whose supplied non-blank value is preserved.</summary>
public sealed record ActorName
{
    public ActorName(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Actor name must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

/// <summary>An immutable whole-years actor age.</summary>
public readonly record struct ActorAge
{
    public ActorAge(int wholeYears)
    {
        if (wholeYears < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wholeYears));
        }

        WholeYears = wholeYears;
    }

    public int WholeYears { get; }
}

/// <summary>Controller-neutral identity facts for one Actor.</summary>
public sealed record ActorIdentity
{
    public ActorIdentity(ActorId actorId, ActorName name, ActorAge age)
    {
        ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(name);

        ActorId = actorId;
        Name = name;
        Age = age;
    }

    public ActorId ActorId { get; }
    public ActorName Name { get; }
    public ActorAge Age { get; }

    internal static void ValidateActorId(ActorId actorId)
    {
        NonEmptyIdentityValue.Validate(actorId.Value, nameof(actorId), "Actor identity must be non-empty.");
    }
}
