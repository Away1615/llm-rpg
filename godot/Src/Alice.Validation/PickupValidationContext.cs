using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Capabilities;
using Alice.Navigation;

namespace Alice.Validation;

public sealed class PickupValidationContext
{
    private readonly ReadOnlyCollection<CapabilityContribution> _intrinsicContributions;
    private readonly ReadOnlyCollection<TemporaryCapabilityModifier> _temporaryModifiers;

    public PickupValidationContext(ActorId actorId, WorldPosition actorPosition, IEnumerable<CapabilityContribution> intrinsicContributions, IEnumerable<TemporaryCapabilityModifier> temporaryModifiers)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(intrinsicContributions);
        ArgumentNullException.ThrowIfNull(temporaryModifiers);
        if (!WorldPositionValidation.IsFinite(actorPosition))
        {
            throw new ArgumentOutOfRangeException(nameof(actorPosition));
        }

        _intrinsicContributions = Array.AsReadOnly(intrinsicContributions.ToArray());
        _temporaryModifiers = Array.AsReadOnly(temporaryModifiers.ToArray());
        foreach (CapabilityContribution contribution in _intrinsicContributions) ArgumentNullException.ThrowIfNull(contribution);
        foreach (TemporaryCapabilityModifier modifier in _temporaryModifiers) ArgumentNullException.ThrowIfNull(modifier);
        ActorId = actorId;
        ActorPosition = actorPosition;
    }

    public ActorId ActorId { get; }
    public WorldPosition ActorPosition { get; }
    public IReadOnlyList<CapabilityContribution> IntrinsicContributions => _intrinsicContributions;
    public IReadOnlyList<TemporaryCapabilityModifier> TemporaryModifiers => _temporaryModifiers;
}
