using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Capabilities;
using Alice.Navigation;

namespace Alice.Validation;

/// <summary>Trusted exact-current actor evidence used only at the Consumption validation boundary.</summary>
public sealed class ConsumptionValidationContext
{
    private readonly ReadOnlyCollection<CapabilityContribution> _intrinsicContributions;
    private readonly ReadOnlyCollection<TemporaryCapabilityModifier> _temporaryModifiers;

    public ConsumptionValidationContext(ActorId actorId, WorldPosition actorPosition, IEnumerable<CapabilityContribution> intrinsicContributions, IEnumerable<TemporaryCapabilityModifier> temporaryModifiers)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(intrinsicContributions);
        ArgumentNullException.ThrowIfNull(temporaryModifiers);
        WorldPositionValidation.Validate(actorPosition, nameof(actorPosition), "World position must be finite.");

        CapabilityContribution[] contributions = intrinsicContributions.ToArray();
        TemporaryCapabilityModifier[] modifiers = temporaryModifiers.ToArray();
        EnsureEntries(contributions);
        EnsureEntries(modifiers);
        ActorId = actorId;
        ActorPosition = actorPosition;
        _intrinsicContributions = Array.AsReadOnly(contributions);
        _temporaryModifiers = Array.AsReadOnly(modifiers);
    }

    public ActorId ActorId { get; }
    public WorldPosition ActorPosition { get; }
    public IReadOnlyList<CapabilityContribution> IntrinsicContributions => _intrinsicContributions;
    public IReadOnlyList<TemporaryCapabilityModifier> TemporaryModifiers => _temporaryModifiers;

    private static void EnsureEntries<T>(IEnumerable<T> entries) where T : class
    {
        foreach (T entry in entries) ArgumentNullException.ThrowIfNull(entry);
    }
}
