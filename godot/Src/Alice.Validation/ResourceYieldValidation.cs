using Alice.Actors;
using Alice.Capabilities;
using Alice.Interaction;
using Alice.Navigation;

namespace Alice.Validation;

public sealed record ResourceYieldValidationContext
{
    public ResourceYieldValidationContext(
        ActorId actorId,
        WorldPosition actorPosition,
        IEnumerable<CapabilityContribution> intrinsicContributions,
        IEnumerable<TemporaryCapabilityModifier> temporaryModifiers)
    {
        ActorIdentity.ValidateActorId(actorId);
        WorldPositionValidation.Validate(actorPosition, nameof(actorPosition));
        ArgumentNullException.ThrowIfNull(intrinsicContributions);
        ArgumentNullException.ThrowIfNull(temporaryModifiers);
        ActorId = actorId;
        ActorPosition = actorPosition;
        IntrinsicContributions = intrinsicContributions.ToArray();
        TemporaryModifiers = temporaryModifiers.ToArray();
    }

    public ActorId ActorId { get; }
    public WorldPosition ActorPosition { get; }
    public IReadOnlyList<CapabilityContribution> IntrinsicContributions { get; }
    public IReadOnlyList<TemporaryCapabilityModifier> TemporaryModifiers { get; }
}

internal enum ResourceYieldValidationFailure
{
    ActionKindMismatch,
    ActorContextMismatch,
    ContractUnavailable,
    StaleContractVersion,
    SourceUnavailable,
    InvalidSpatialEvidence,
    OutOfRange,
    BindingCapabilityMismatch,
    InstrumentNotAllowed,
    InsufficientCapability,
    CheckedNumericOverflow,
    DuplicateGameActionId,
    CommitConstructionFailed
}

public sealed class ResourceYieldValidationResult
{
    private ResourceYieldValidationResult(ResourceYieldValidationFailure? failure) => Failure = failure;

    public bool IsValid => Failure is null;
    internal ResourceYieldValidationFailure? Failure { get; }
    internal static ResourceYieldValidationResult Accepted() => new(null);
    internal static ResourceYieldValidationResult Rejected(ResourceYieldValidationFailure failure) => new(failure);
}

/// <summary>Pure validation for one exact-current non-damage resource collection request.</summary>
public static class ResourceYieldActionValidator
{
    public static ResourceYieldValidationResult Validate(
        GameActionSpec action,
        ResourceYieldValidationContext context,
        ResourceYieldContract? contract,
        bool sourceAvailable,
        WorldPosition sourcePosition)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(context);
        if (action.Arguments is not ResourceYieldActionArguments)
            return ResourceYieldValidationResult.Rejected(ResourceYieldValidationFailure.ActionKindMismatch);
        if (action.ActorId != context.ActorId)
            return ResourceYieldValidationResult.Rejected(ResourceYieldValidationFailure.ActorContextMismatch);
        if (contract is null || action.Binding.ContractRef != contract.ContractRef)
            return ResourceYieldValidationResult.Rejected(ResourceYieldValidationFailure.ContractUnavailable);
        if (action.Binding.ExpectedVersion.Value != contract.Version)
            return ResourceYieldValidationResult.Rejected(ResourceYieldValidationFailure.StaleContractVersion);
        if (!sourceAvailable)
            return ResourceYieldValidationResult.Rejected(ResourceYieldValidationFailure.SourceUnavailable);
        if (!WorldPositionValidation.IsFinite(sourcePosition)
            || !WorldPositionValidation.IsFinite(context.ActorPosition))
            return ResourceYieldValidationResult.Rejected(ResourceYieldValidationFailure.InvalidSpatialEvidence);
        if (!IsInRange(context.ActorPosition, sourcePosition, contract.InteractionRange))
            return ResourceYieldValidationResult.Rejected(ResourceYieldValidationFailure.OutOfRange);
        if (action.Binding.Capability != contract.Requirement.CapabilityIdentity)
            return ResourceYieldValidationResult.Rejected(ResourceYieldValidationFailure.BindingCapabilityMismatch);
        if (action.Binding.InstrumentRef is not null)
            return ResourceYieldValidationResult.Rejected(ResourceYieldValidationFailure.InstrumentNotAllowed);

        try
        {
            EffectiveCapabilities capabilities = EffectiveCapabilities.Resolve(
                context.IntrinsicContributions,
                null,
                context.TemporaryModifiers);
            return capabilities.GetValue(contract.Requirement.CapabilityIdentity)
                < contract.Requirement.MinimumValue
                ? ResourceYieldValidationResult.Rejected(ResourceYieldValidationFailure.InsufficientCapability)
                : ResourceYieldValidationResult.Accepted();
        }
        catch (OverflowException)
        {
            return ResourceYieldValidationResult.Rejected(ResourceYieldValidationFailure.CheckedNumericOverflow);
        }
    }

    private static bool IsInRange(WorldPosition left, WorldPosition right, InteractionRange range)
    {
        double horizontal = left.X - right.X;
        double vertical = left.Y - right.Y;
        return Math.Sqrt((horizontal * horizontal) + (vertical * vertical)) <= range.Value;
    }
}
