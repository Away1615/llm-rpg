using System.Collections.ObjectModel;
using Alice.Identity;
using Alice.Actors;

namespace Alice.Npc;



/// <summary>Read-only personality prior consumed by later deterministic context code.</summary>
public interface IPersonalityPriorView
{
    CognitiveFunctionProfile CognitiveStyle { get; }
    IReadOnlyList<PersonalityTagId> Traits { get; }
    IReadOnlyList<WeightedPersonalityValue> Values { get; }
}

/// <summary>Immutable NPC-only personality prior state.</summary>
public sealed class NpcPersonalityState : IPersonalityPriorView, IEquatable<NpcPersonalityState>
{
    private readonly ReadOnlyCollection<PersonalityTagId> _traits;
    private readonly ReadOnlyCollection<WeightedPersonalityValue> _values;

    public NpcPersonalityState(
        CognitiveFunctionProfile cognitiveStyle,
        IEnumerable<PersonalityTagId> traits,
        IEnumerable<WeightedPersonalityValue> values)
    {
        ArgumentNullException.ThrowIfNull(cognitiveStyle);
        ArgumentNullException.ThrowIfNull(traits);
        ArgumentNullException.ThrowIfNull(values);

        PersonalityTagId[] traitSnapshot = traits.ToArray();
        if (traitSnapshot.Length < 2 || traitSnapshot.Length > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(traits), "Personality requires two through four traits.");
        }

        ValidateTraits(traitSnapshot);
        WeightedPersonalityValue[] valueSnapshot = values.ToArray();
        ValidateValues(valueSnapshot);
        Array.Sort(traitSnapshot, PersonalityTagComparer.Instance);
        Array.Sort(valueSnapshot, PersonalityValueComparer.Instance);

        CognitiveStyle = cognitiveStyle;
        _traits = Array.AsReadOnly(traitSnapshot);
        _values = Array.AsReadOnly(valueSnapshot);
    }

    public CognitiveFunctionProfile CognitiveStyle { get; }
    public IReadOnlyList<PersonalityTagId> Traits => _traits;
    public IReadOnlyList<WeightedPersonalityValue> Values => _values;

    public bool Equals(NpcPersonalityState? other)
    {
        return other is not null &&
            CognitiveStyle == other.CognitiveStyle &&
            Traits.SequenceEqual(other.Traits) &&
            Values.SequenceEqual(other.Values);
    }

    public override bool Equals(object? obj) => Equals(obj as NpcPersonalityState);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CognitiveStyle);
        foreach (PersonalityTagId trait in Traits)
        {
            hash.Add(trait);
        }

        foreach (WeightedPersonalityValue value in Values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    private static void ValidateTraits(IEnumerable<PersonalityTagId> traits)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (PersonalityTagId trait in traits)
        {
            ArgumentNullException.ThrowIfNull(trait);
            if (!identities.Add(trait.Value))
            {
                throw new ArgumentException("Personality traits must be unique.", nameof(traits));
            }
        }
    }

    private static void ValidateValues(IEnumerable<WeightedPersonalityValue> values)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (WeightedPersonalityValue value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!identities.Add(value.ValueIdentity.Value))
            {
                throw new ArgumentException("Personality values must be unique.", nameof(values));
            }
        }
    }

    private sealed class PersonalityTagComparer : IComparer<PersonalityTagId>
    {
        public static PersonalityTagComparer Instance { get; } = new();
        public int Compare(PersonalityTagId? left, PersonalityTagId? right) => StringComparer.Ordinal.Compare(left?.Value, right?.Value);
    }

    private sealed class PersonalityValueComparer : IComparer<WeightedPersonalityValue>
    {
        public static PersonalityValueComparer Instance { get; } = new();
        public int Compare(WeightedPersonalityValue? left, WeightedPersonalityValue? right) => StringComparer.Ordinal.Compare(left?.ValueIdentity.Value, right?.ValueIdentity.Value);
    }
}


/// <summary>Eight independently normalized cognitive-style values.</summary>
public sealed record CognitiveFunctionProfile
{
    public CognitiveFunctionProfile(double se, double si, double ne, double ni, double te, double ti, double fe, double fi)
    {
        WeightedPersonalityValue.ValidateNormalized(se, nameof(se));
        WeightedPersonalityValue.ValidateNormalized(si, nameof(si));
        WeightedPersonalityValue.ValidateNormalized(ne, nameof(ne));
        WeightedPersonalityValue.ValidateNormalized(ni, nameof(ni));
        WeightedPersonalityValue.ValidateNormalized(te, nameof(te));
        WeightedPersonalityValue.ValidateNormalized(ti, nameof(ti));
        WeightedPersonalityValue.ValidateNormalized(fe, nameof(fe));
        WeightedPersonalityValue.ValidateNormalized(fi, nameof(fi));

        Se = se;
        Si = si;
        Ne = ne;
        Ni = ni;
        Te = te;
        Ti = ti;
        Fe = fe;
        Fi = fi;
    }

    public double Se { get; }
    public double Si { get; }
    public double Ne { get; }
    public double Ni { get; }
    public double Te { get; }
    public double Ti { get; }
    public double Fe { get; }
    public double Fi { get; }
}



/// <summary>Open typed identity for one readable personality tag.</summary>
public sealed record PersonalityTagId
{
    public PersonalityTagId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Personality tag identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

/// <summary>Open typed identity for one personality value.</summary>
public sealed record ValueIdentity
{
    public ValueIdentity(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Value identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

/// <summary>One finite normalized personality value weight.</summary>
public sealed record WeightedPersonalityValue
{
    public WeightedPersonalityValue(ValueIdentity valueIdentity, double weight)
    {
        ArgumentNullException.ThrowIfNull(valueIdentity);
        ValidateNormalized(weight, nameof(weight));
        ValueIdentity = valueIdentity;
        Weight = weight;
    }

    public ValueIdentity ValueIdentity { get; }
    public double Weight { get; }

    internal static void ValidateNormalized(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}



/// <summary>One immutable actor-local appraisal of another actor.</summary>
public sealed record NpcRelationshipAppraisal
{
    public NpcRelationshipAppraisal(
        ActorId otherActorId,
        double familiarity,
        double trust,
        double affection,
        double respect,
        double fear,
        double grievance)
    {
        ActorIdentity.ValidateActorId(otherActorId);
        ValidateNormalized(familiarity, nameof(familiarity));
        ValidateNormalized(trust, nameof(trust));
        ValidateNormalized(affection, nameof(affection));
        ValidateNormalized(respect, nameof(respect));
        ValidateNormalized(fear, nameof(fear));
        ValidateNormalized(grievance, nameof(grievance));

        OtherActorId = otherActorId;
        Familiarity = familiarity;
        Trust = trust;
        Affection = affection;
        Respect = respect;
        Fear = fear;
        Grievance = grievance;
    }

    public ActorId OtherActorId { get; }
    public double Familiarity { get; }
    public double Trust { get; }
    public double Affection { get; }
    public double Respect { get; }
    public double Fear { get; }
    public double Grievance { get; }

    private static void ValidateNormalized(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

/// <summary>Immutable current social appraisals owned by exactly one NPC.</summary>
public sealed class NpcSocialState : IEquatable<NpcSocialState>
{
    private readonly ReadOnlyCollection<NpcRelationshipAppraisal> _appraisals;

    public NpcSocialState(ActorId actorId, IEnumerable<NpcRelationshipAppraisal> appraisals)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(appraisals);

        NpcRelationshipAppraisal[] snapshot = appraisals.ToArray();
        var identities = new HashSet<ActorId>();
        foreach (NpcRelationshipAppraisal appraisal in snapshot)
        {
            ArgumentNullException.ThrowIfNull(appraisal);
            if (appraisal.OtherActorId == actorId)
            {
                throw new ArgumentException("An NPC cannot hold a relationship appraisal of itself.", nameof(appraisals));
            }

            if (!identities.Add(appraisal.OtherActorId))
            {
                throw new ArgumentException("Relationship appraisals must have distinct other-actor identities.", nameof(appraisals));
            }
        }

        Array.Sort(snapshot, AppraisalComparer.Instance);
        ActorId = actorId;
        _appraisals = Array.AsReadOnly(snapshot);
    }

    public ActorId ActorId { get; }
    public IReadOnlyList<NpcRelationshipAppraisal> Appraisals => _appraisals;

    public NpcRelationshipAppraisal? FindAppraisal(ActorId otherActorId)
    {
        ActorIdentity.ValidateActorId(otherActorId);
        return Appraisals.SingleOrDefault(appraisal => appraisal.OtherActorId == otherActorId);
    }

    public bool Equals(NpcSocialState? other) =>
        other is not null && ActorId == other.ActorId && Appraisals.SequenceEqual(other.Appraisals);

    public override bool Equals(object? obj) => Equals(obj as NpcSocialState);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ActorId);
        foreach (NpcRelationshipAppraisal appraisal in Appraisals)
        {
            hash.Add(appraisal);
        }

        return hash.ToHashCode();
    }

    private sealed class AppraisalComparer : IComparer<NpcRelationshipAppraisal>
    {
        public static AppraisalComparer Instance { get; } = new();

        public int Compare(NpcRelationshipAppraisal? left, NpcRelationshipAppraisal? right) =>
            StringComparer.Ordinal.Compare(left?.OtherActorId.Value, right?.OtherActorId.Value);
    }
}
