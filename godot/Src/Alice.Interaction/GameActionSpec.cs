using Alice.Actors;
using Alice.Damage;

namespace Alice.Interaction;

public abstract record GameActionArguments
{
    private protected GameActionArguments()
    {
    }
}

/// <summary>Typed requested damage family; current source magnitude is Validator-owned truth.</summary>
public sealed record DamageActionArguments : GameActionArguments
{
    public DamageActionArguments(DamageType damageType)
    {
        if (!Enum.IsDefined(damageType))
        {
            throw new ArgumentOutOfRangeException(nameof(damageType));
        }

        DamageType = damageType;
    }

    public DamageType DamageType { get; }
}

/// <summary>Shared equipment family; null clears the Actor's hand.</summary>
public sealed record EquipmentChangeActionArguments : GameActionArguments
{
    public EquipmentChangeActionArguments(HandItemRef? handItemRef)
    {
        HandItemRef = handItemRef;
    }

    public HandItemRef? HandItemRef { get; }
}

public sealed record RegionOperationActionArguments(
    string RegionId,
    int? ExpectedInstrumentVersion) : GameActionArguments;

public sealed record CraftActionArguments(string RecipeId) : GameActionArguments;

public sealed record ServiceExchangeActionArguments(
    string ServiceId,
    string ProviderActorId,
    long ExpectedProviderContainerRevision,
    string? TargetItemInstanceId) : GameActionArguments;

public sealed record AssetTransferActionArguments(
    string SourceKind,
    string SourceId,
    string DestinationKind,
    string DestinationId,
    string AssetId,
    long Quantity) : GameActionArguments;

public sealed record ListedExchangeActionArguments(string ListingId) : GameActionArguments;

public sealed record PlaceStateChangeActionArguments(string PlaceId, bool Closed) : GameActionArguments;

/// <summary>Uses one configured sleep facility through the shared interaction pipeline.</summary>
public sealed record RestActionArguments(string FacilityId) : GameActionArguments;

/// <summary>A typed action handoff. Target identity exists only within Binding.ContractRef.</summary>
public sealed record GameActionSpec
{
    public GameActionSpec(
        ActorId actorId,
        InteractionBinding binding,
        GameActionArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(arguments);
        ActorId = actorId;
        Binding = binding;
        Arguments = arguments;
    }

    public ActorId ActorId { get; }
    public InteractionBinding Binding { get; }
    public GameActionArguments Arguments { get; }
}
