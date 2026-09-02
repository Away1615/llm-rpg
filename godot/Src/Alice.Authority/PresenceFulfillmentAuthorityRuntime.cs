using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Commitments;
using Alice.Social;
using Alice.Validation;

namespace Alice.Authority;

/// <summary>Bounded public outcome of one attendance-arrival settlement attempt.</summary>
public sealed class PresenceFulfillmentResult
{
    internal PresenceFulfillmentResult(Commitment commitment, PresenceCommitReceipt receipt)
    {
        Commitment = commitment;
        Receipt = receipt;
    }

    internal PresenceFulfillmentResult(PresenceFulfillmentFailure failure)
    {
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        Failure = failure;
    }

    public bool IsCommitted => Receipt is not null;
    public PresenceFulfillmentFailure? Failure { get; }
    public Commitment? Commitment { get; }
    public PresenceCommitReceipt? Receipt { get; }
}

/// <summary>Synchronous Authority settlement for attendance Commitments using exact Travel evidence.</summary>
public sealed class PresenceFulfillmentAuthorityRuntime
{
    private readonly InvitationAcceptanceAuthorityRuntime _commitmentAuthority;
    private readonly ReadOnlyCollection<GatheringArrivalBinding> _arrivalBindings;
    private readonly List<PresenceCommitReceipt> _receipts = [];

    public PresenceFulfillmentAuthorityRuntime(
        InvitationAcceptanceAuthorityRuntime commitmentAuthority,
        IEnumerable<GatheringArrivalBinding> arrivalBindings)
    {
        ArgumentNullException.ThrowIfNull(commitmentAuthority);
        ArgumentNullException.ThrowIfNull(arrivalBindings);
        GatheringArrivalBinding[] bindingSnapshot = arrivalBindings.ToArray();
        foreach (GatheringArrivalBinding binding in bindingSnapshot)
        {
            ArgumentNullException.ThrowIfNull(binding);
        }

        _commitmentAuthority = commitmentAuthority;
        _arrivalBindings = Array.AsReadOnly(bindingSnapshot);
    }

    public IReadOnlyList<PresenceCommitReceipt> Receipts => Array.AsReadOnly(_receipts.ToArray());

    public PresenceFulfillmentResult TryFulfill(
        CommitmentId commitmentId,
        ActivityRuntime sourceActivity,
        TravelActivityResult travelResult)
    {
        ArgumentNullException.ThrowIfNull(sourceActivity);
        ArgumentNullException.ThrowIfNull(travelResult);

        Commitment? current = _commitmentAuthority.FindCommitment(commitmentId);
        ScheduledGathering? gathering = current?.Term is PresenceWindowTerm term
            ? _commitmentAuthority.FindCurrentGathering(term.GatheringRef)
            : null;
        PresenceFulfillmentValidationResult validation = PresenceFulfillmentValidator.Validate(
            current,
            sourceActivity,
            travelResult,
            gathering,
            _arrivalBindings);
        if (!validation.IsValid)
        {
            return new PresenceFulfillmentResult(validation.Failure!.Value);
        }

        Commitment fulfilled = current!.AsFulfilled();
        GatheringArrivalBinding binding = validation.ArrivalBinding!;
        var receipt = new PresenceCommitReceipt(
            fulfilled.CommitmentId,
            fulfilled.Debtor,
            validation.Term!.GatheringRef,
            validation.Term.ExpectedGatheringRevision,
            gathering!.PlaceRef,
            binding.TargetRef,
            travelResult.ActivityId,
            sourceActivity.LastProgressTime,
            current.Status,
            fulfilled.Status);

        _commitmentAuthority.ReplaceCommitment(current, fulfilled);
        _receipts.Add(receipt);
        return new PresenceFulfillmentResult(fulfilled, receipt);
    }
}
