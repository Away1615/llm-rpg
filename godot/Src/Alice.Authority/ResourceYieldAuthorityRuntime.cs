using Alice.Actors;
using Alice.Interaction;
using Alice.Navigation;
using Alice.Validation;
using Alice.World;

namespace Alice.Authority;

/// <summary>Immutable audit fact for a resource source transition and its claimant WorldDrop.</summary>
public sealed record ResourceYieldCommitReceipt
{
    public ResourceYieldCommitReceipt(
        CommitOrigin.ActorAction origin,
        ContractRef contractRef,
        long contractVersion,
        WorldDrop worldDrop)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(contractRef);
        ArgumentNullException.ThrowIfNull(worldDrop);
        if (contractVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contractVersion));
        }

        Origin = origin;
        ContractRef = contractRef;
        ContractVersion = contractVersion;
        WorldDrop = worldDrop;
    }

    public CommitOrigin.ActorAction Origin { get; }
    public ActorId ActorId => Origin.ActorId;
    public ContractRef ContractRef { get; }
    public long ContractVersion { get; }
    public WorldDrop WorldDrop { get; }
}

public sealed class ResourceYieldCommitResult
{
    internal ResourceYieldCommitResult(ResourceYieldCommitReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        Receipt = receipt;
    }

    internal ResourceYieldCommitResult(ResourceYieldValidationFailure failure)
    {
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        Failure = failure;
    }

    public ResourceYieldCommitReceipt? Receipt { get; }
    public bool IsCommitted => Receipt is not null;
    internal ResourceYieldValidationFailure? Failure { get; }
}

/// <summary>Sole synchronous owner of one non-damage source and its not-yet-picked WorldDrop.</summary>
public sealed class ResourceYieldAuthorityRuntime
{
    private readonly HashSet<GameActionId> _committedGameActionIds = [];
    private bool _sourceAvailable;
    private WorldDrop? _worldDrop;

    public ResourceYieldAuthorityRuntime(
        TargetRef targetRef,
        ResourceYieldContract currentContract,
        WorldPosition sourcePosition,
        WorldDropId worldDropId,
        bool sourceAvailable = true,
        WorldDrop? currentWorldDrop = null)
    {
        ArgumentNullException.ThrowIfNull(targetRef);
        ArgumentNullException.ThrowIfNull(currentContract);
        ArgumentNullException.ThrowIfNull(worldDropId);
        WorldPositionValidation.Validate(sourcePosition, nameof(sourcePosition));
        if (targetRef != currentContract.ContractRef.TargetRef)
        {
            throw new ArgumentException("Authority target must match its ResourceYield contract.", nameof(targetRef));
        }

        if (sourceAvailable && currentWorldDrop is not null)
        {
            throw new ArgumentException("An available source cannot already own a drop.");
        }

        if (currentWorldDrop is not null
            && (!currentWorldDrop.DropId.Equals(worldDropId)
                || currentWorldDrop.Position != sourcePosition
                || currentWorldDrop.Version != currentContract.WorldDropVersion
                || !currentWorldDrop.Items.SequenceEqual(currentContract.Yields)))
        {
            throw new ArgumentException("Restored ResourceYield drop does not exact-match its Authority contract.", nameof(currentWorldDrop));
        }

        TargetRef = targetRef;
        CurrentContract = currentContract;
        SourcePosition = sourcePosition;
        WorldDropId = worldDropId;
        _sourceAvailable = sourceAvailable;
        _worldDrop = currentWorldDrop;
    }

    public TargetRef TargetRef { get; }
    public ResourceYieldContract CurrentContract { get; }
    public WorldPosition SourcePosition { get; }
    public WorldDropId WorldDropId { get; }
    public bool SourceAvailable => _sourceAvailable;
    public WorldDrop? WorldDrop => _worldDrop;

    public ResourceYieldCommitResult TryCommit(
        GameActionSpec action,
        GameActionId gameActionId,
        ResourceYieldValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(gameActionId);
        ArgumentNullException.ThrowIfNull(context);
        if (_committedGameActionIds.Contains(gameActionId))
        {
            return new ResourceYieldCommitResult(ResourceYieldValidationFailure.DuplicateGameActionId);
        }

        ResourceYieldValidationResult validation = ResourceYieldActionValidator.Validate(
            action,
            context,
            CurrentContract,
            _sourceAvailable,
            SourcePosition);
        if (!validation.IsValid)
        {
            return new ResourceYieldCommitResult(validation.Failure!.Value);
        }

        try
        {
            var drop = new WorldDrop(
                WorldDropId,
                CurrentContract.Yields,
                SourcePosition,
                action.ActorId,
                CurrentContract.WorldDropVersion);
            var receipt = new ResourceYieldCommitReceipt(
                new CommitOrigin.ActorAction(action.ActorId, gameActionId),
                CurrentContract.ContractRef,
                CurrentContract.Version,
                drop);
            _sourceAvailable = false;
            _worldDrop = drop;
            _committedGameActionIds.Add(gameActionId);
            return new ResourceYieldCommitResult(receipt);
        }
        catch (ArgumentException)
        {
            return new ResourceYieldCommitResult(ResourceYieldValidationFailure.CommitConstructionFailed);
        }
        catch (OverflowException)
        {
            return new ResourceYieldCommitResult(ResourceYieldValidationFailure.CommitConstructionFailed);
        }
    }

    public WorldDrop TransferWorldDrop()
    {
        WorldDrop drop = _worldDrop
            ?? throw new InvalidOperationException("ResourceYield Authority has no WorldDrop to transfer.");
        _worldDrop = null;
        return drop;
    }
}
