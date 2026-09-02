using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Capabilities;
using Alice.Damage;
using Alice.Navigation;

namespace Alice.Validation;

/// <summary>Trusted current actor-side evidence supplied only at the damage validation boundary.</summary>
public sealed class DamageValidationContext
{
    private readonly ReadOnlyCollection<CapabilityContribution> _intrinsicContributions;
    private readonly ReadOnlyCollection<TemporaryCapabilityModifier> _temporaryModifiers;

    public DamageValidationContext(
        SharedActorState actorState,
        WorldPosition actorPosition,
        IEnumerable<CapabilityContribution> intrinsicContributions,
        IEnumerable<TemporaryCapabilityModifier> temporaryModifiers,
        DamageContribution? intrinsicDamageContribution)
    {
        ArgumentNullException.ThrowIfNull(actorState);
        ArgumentNullException.ThrowIfNull(intrinsicContributions);
        ArgumentNullException.ThrowIfNull(temporaryModifiers);
        WorldPositionValidation.Validate(actorPosition, nameof(actorPosition), "World position must be finite.");

        CapabilityContribution[] contributions = intrinsicContributions.ToArray();
        TemporaryCapabilityModifier[] modifiers = temporaryModifiers.ToArray();
        EnsureEntries(contributions);
        EnsureEntries(modifiers);
        Array.Sort(contributions, CapabilityContributionComparer.Instance);
        Array.Sort(modifiers, TemporaryCapabilityModifierComparer.Instance);

        ActorState = actorState;
        ActorPosition = actorPosition;
        _intrinsicContributions = Array.AsReadOnly(contributions);
        _temporaryModifiers = Array.AsReadOnly(modifiers);
        IntrinsicDamageContribution = intrinsicDamageContribution;
    }

    public SharedActorState ActorState { get; }
    public WorldPosition ActorPosition { get; }
    public IReadOnlyList<CapabilityContribution> IntrinsicContributions => _intrinsicContributions;
    public IReadOnlyList<TemporaryCapabilityModifier> TemporaryModifiers => _temporaryModifiers;
    public DamageContribution? IntrinsicDamageContribution { get; }

    private static void EnsureEntries<T>(IEnumerable<T> entries) where T : class
    {
        foreach (T entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
        }
    }

    private sealed class CapabilityContributionComparer : IComparer<CapabilityContribution>
    {
        public static CapabilityContributionComparer Instance { get; } = new();

        public int Compare(CapabilityContribution? left, CapabilityContribution? right)
        {
            return StringComparer.Ordinal.Compare(left?.CapabilityIdentity.Value, right?.CapabilityIdentity.Value);
        }
    }

    private sealed class TemporaryCapabilityModifierComparer : IComparer<TemporaryCapabilityModifier>
    {
        public static TemporaryCapabilityModifierComparer Instance { get; } = new();

        public int Compare(TemporaryCapabilityModifier? left, TemporaryCapabilityModifier? right)
        {
            return StringComparer.Ordinal.Compare(left?.CapabilityIdentity.Value, right?.CapabilityIdentity.Value);
        }
    }
}
