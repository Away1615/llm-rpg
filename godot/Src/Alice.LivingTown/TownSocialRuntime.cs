using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Alice.Activities;
using Alice.Actors;
using Alice.Commitments;
using Alice.Interaction;
using Alice.Items;
using Alice.Memory;
using Alice.Navigation;
using Alice.Npc;
using Alice.ProductRuntime;

namespace Alice.LivingTown;

public sealed record TownSocialBondConfiguration
{
    [JsonRequired, JsonPropertyName("first_actor_id")] public string FirstActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("second_actor_id")] public string SecondActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("stage")] public string Stage { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("source_event_id")] public string SourceEventId { get; init; } = string.Empty;
}

public sealed record TownTransferObligationConfiguration
{
    [JsonRequired, JsonPropertyName("commitment_id")] public string CommitmentId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("debtor_actor_id")] public string DebtorActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("creditor_actor_id")] public string CreditorActorId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("asset_id")] public string AssetId { get; init; } = string.Empty;
    [JsonRequired, JsonPropertyName("amount")] public long Amount { get; init; }
    [JsonRequired, JsonPropertyName("due_at_ticks")] public long DueAtTicks { get; init; }
    [JsonRequired, JsonPropertyName("source_event_id")] public string SourceEventId { get; init; } = string.Empty;
}

public sealed record TownSocialConfigurationDocument
{
    [JsonRequired, JsonPropertyName("bonds")] public TownSocialBondConfiguration[] Bonds { get; init; } = [];
    [JsonRequired, JsonPropertyName("transfer_obligations")] public TownTransferObligationConfiguration[] TransferObligations { get; init; } = [];
}

public static class TownSocialConfigurationValidator
{
    public static void Validate(
        TownSocialConfigurationDocument configuration,
        TownWorldConfigurationDocument world,
        TownPopulationManifest population)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(population);
        if (configuration.Bonds.Length > 8 || configuration.TransferObligations.Length > 2)
            throw new InvalidDataException("Initial social configuration must remain compact.");

        HashSet<string> actors = population.Actors.Select(value => value.Identity.ActorId)
            .Append(world.Player.ActorId).ToHashSet(StringComparer.Ordinal);
        HashSet<string> historyEvents = world.History.HistoryEvents.Select(value => value.EventId)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> assets = world.Gameplay.Regions.Select(value => value.OutputAssetId)
            .Concat(world.Gameplay.Containers.SelectMany(value => value.Balances.Select(balance => balance.AssetId)))
            .Concat(world.Player.FungibleAssets.Select(value => value.AssetId))
            .Concat(population.Actors.SelectMany(value => value.Inventory.Stacks.Select(stack => stack.ItemTypeId)))
            .Concat(population.Actors.SelectMany(value => value.Currency.Select(currency => currency.CurrencyId)))
            .ToHashSet(StringComparer.Ordinal);
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownSocialBondConfiguration bond in configuration.Bonds)
        {
            string pair = PairKey(bond.FirstActorId, bond.SecondActorId);
            if (!actors.Contains(bond.FirstActorId) || !actors.Contains(bond.SecondActorId)
                || bond.FirstActorId == bond.SecondActorId || !pairs.Add(pair)
                || !Enum.TryParse(bond.Stage, false, out TownSocialBondStage stage)
                || stage == TownSocialBondStage.Stranger
                || !historyEvents.Contains(bond.SourceEventId))
                throw new InvalidDataException("Initial social bond references are invalid.");
        }

        var commitments = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownTransferObligationConfiguration obligation in configuration.TransferObligations)
        {
            if (string.IsNullOrWhiteSpace(obligation.CommitmentId) || !commitments.Add(obligation.CommitmentId)
                || !actors.Contains(obligation.DebtorActorId) || !actors.Contains(obligation.CreditorActorId)
                || obligation.DebtorActorId == obligation.CreditorActorId
                || !assets.Contains(obligation.AssetId) || obligation.Amount <= 0 || obligation.DueAtTicks < 0
                || !historyEvents.Contains(obligation.SourceEventId))
                throw new InvalidDataException("Initial transfer obligation references are invalid.");
        }
    }

    internal static string PairKey(string first, string second) =>
        StringComparer.Ordinal.Compare(first, second) < 0 ? $"{first}/{second}" : $"{second}/{first}";
}

public enum TownRelationshipEventKind
{
    FulfilledCommitment,
    BrokenCommitment,
    Help,
    Harm,
    SharedActivity
}

public enum TownSocialEffectKind
{
    Neutral,
    Support,
    Harm,
    Promise,
    Breach,
    Threat,
    Apology,
    SharedInterest
}

public enum TownSocialCognitionRoute
{
    L0,
    L1,
    L2
}

public sealed record TownSocialAppraisalResult(
    bool Applied,
    string SourceEventId,
    TownSocialEffectKind Effect,
    TownSocialCognitionRoute Route,
    CurrentEmotionState Emotion,
    TownDirectedRelationshipSnapshot Relationship);

public enum TownSocialBondStage
{
    Stranger,
    Acquaintance,
    Friend,
    Partner,
    Spouse
}

public sealed record TownDirectedRelationshipSnapshot(
    ActorId SubjectActorId,
    ActorId OtherActorId,
    double Familiarity,
    double Trust,
    double Affection,
    double Respect,
    double Fear,
    double Grievance);

public sealed record TownSocialBondSnapshot(
    ActorId FirstActorId,
    ActorId SecondActorId,
    TownSocialBondStage Stage,
    string? SourceEventId);

public sealed record TownSocialEventContext(
    string LocationId,
    WorldPosition Position,
    string SpatialLayer,
    IReadOnlyList<TownHistoryActorPresence> Presences);

public sealed record TownRelationshipProjectionResult(
    bool Applied,
    TownDirectedRelationshipSnapshot Relationship,
    CanonicalHistoryEventRecord SourceEvent);

public sealed record TownSocialBondTransitionResult(
    bool Accepted,
    string? Reason,
    TownSocialBondSnapshot? Bond,
    CanonicalHistoryEventRecord? SourceEvent);

public sealed record TownObligationTransitionResult(
    bool Accepted,
    string? Reason,
    Commitment Commitment,
    CanonicalHistoryEventRecord? SourceEvent,
    ActorExecutionReceipt? TransferReceipt);

public sealed record TownCommitmentDurableState(
    string CommitmentId,
    string DebtorActorId,
    string CreditorActorId,
    string AssetId,
    long Amount,
    long DeadlineTicks,
    CommitmentStatus Status,
    string? SourceEventId);

public sealed record TownSocialDurableState(
    IReadOnlyList<TownDirectedRelationshipSnapshot> Relationships,
    IReadOnlyList<TownSocialBondSnapshot> Bonds,
    IReadOnlyList<TownCommitmentDurableState> Commitments,
    IReadOnlyList<string> AppliedRelationshipEventIds);

/// <summary>One Authority for directed appraisals, oral pair bonds and minimum fungible transfer obligations.</summary>
public sealed class TownSocialRuntime
{
    private readonly TownHistoryRuntime _history;
    private readonly RegionSocialGameplayRuntime _gameplay;
    private readonly HashSet<string> _actorIds;
    private readonly Dictionary<(string Subject, string Other), TownDirectedRelationshipSnapshot> _relationships = [];
    private readonly Dictionary<string, TownSocialBondSnapshot> _bonds = new(StringComparer.Ordinal);
    private readonly Dictionary<CommitmentId, Commitment> _commitments = [];
    private readonly HashSet<string> _appliedRelationshipEvents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TownSocialBondTransitionResult> _bondTransitionEvents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TownObligationTransitionResult> _obligationTransitionEvents = new(StringComparer.Ordinal);

    private TownSocialRuntime(
        TownSocialConfigurationDocument configuration,
        TownPopulationManifest population,
        ActorId playerActorId,
        TownHistoryRuntime history,
        RegionSocialGameplayRuntime gameplay)
    {
        _history = history;
        _gameplay = gameplay;
        _actorIds = population.Actors.Select(value => value.Identity.ActorId)
            .Append(playerActorId.Value).ToHashSet(StringComparer.Ordinal);
        foreach (TownNpcConfiguration actor in population.Actors)
        {
            foreach (TownRelationshipConfiguration relationship in actor.Relationships)
            {
                var subject = new ActorId(actor.Identity.ActorId);
                var other = new ActorId(relationship.OtherActorId);
                _relationships.Add((subject.Value, other.Value), new TownDirectedRelationshipSnapshot(
                    subject, other, relationship.Familiarity, relationship.Trust, relationship.Affection,
                    relationship.Respect, relationship.Fear, relationship.Grievance));
            }
        }
        foreach (TownSocialBondConfiguration bond in configuration.Bonds)
        {
            TownSocialBondSnapshot snapshot = CreateBondSnapshot(
                new ActorId(bond.FirstActorId),
                new ActorId(bond.SecondActorId),
                Enum.Parse<TownSocialBondStage>(bond.Stage, false),
                bond.SourceEventId);
            _bonds.Add(PairKey(snapshot.FirstActorId, snapshot.SecondActorId), snapshot);
        }
        foreach (TownTransferObligationConfiguration obligation in configuration.TransferObligations)
        {
            _ = AddTransferObligation(
                new CommitmentId(obligation.CommitmentId),
                new ActorId(obligation.DebtorActorId),
                new ActorId(obligation.CreditorActorId),
                obligation.AssetId,
                obligation.Amount,
                new SimTime(obligation.DueAtTicks),
                obligation.SourceEventId);
        }
    }

    public IReadOnlyList<Commitment> Commitments => new ReadOnlyCollection<Commitment>(
        _commitments.Values.OrderBy(value => value.CommitmentId.Value, StringComparer.Ordinal).ToArray());

    public static TownSocialRuntime Create(
        TownSocialConfigurationDocument configuration,
        TownPopulationManifest population,
        ActorId playerActorId,
        TownHistoryRuntime history,
        RegionSocialGameplayRuntime gameplay)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(gameplay);
        return new TownSocialRuntime(configuration, population, playerActorId, history, gameplay);
    }

    public TownDirectedRelationshipSnapshot GetRelationship(ActorId subject, ActorId other)
    {
        ValidatePair(subject, other);
        return _relationships.TryGetValue((subject.Value, other.Value), out TownDirectedRelationshipSnapshot? value)
            ? value
            : new TownDirectedRelationshipSnapshot(subject, other, 0, 0, 0, 0, 0, 0);
    }

    public TownSocialBondSnapshot GetBond(ActorId first, ActorId second)
    {
        ValidatePair(first, second);
        string key = PairKey(first, second);
        return _bonds.TryGetValue(key, out TownSocialBondSnapshot? value)
            ? value
            : CreateBondSnapshot(first, second, TownSocialBondStage.Stranger, null);
    }

    public Commitment GetCommitment(CommitmentId commitmentId) => _commitments.TryGetValue(commitmentId, out Commitment? value)
        ? value
        : throw new KeyNotFoundException($"Unknown transfer obligation '{commitmentId.Value}'.");

    public CanonicalHistoryEventRecord GetSourceEvent(string sourceEventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEventId);
        return _history.Events.SingleOrDefault(value =>
            StringComparer.Ordinal.Equals(value.SourceId.Value, sourceEventId))
            ?? throw new KeyNotFoundException($"Unknown social source event '{sourceEventId}'.");
    }

    public TownSocialDurableState CaptureDurableState()
    {
        TownDirectedRelationshipSnapshot[] relationships = _relationships.Values
            .OrderBy(value => value.SubjectActorId.Value, StringComparer.Ordinal)
            .ThenBy(value => value.OtherActorId.Value, StringComparer.Ordinal).ToArray();
        TownSocialBondSnapshot[] bonds = _bonds.Values
            .OrderBy(value => value.FirstActorId.Value, StringComparer.Ordinal)
            .ThenBy(value => value.SecondActorId.Value, StringComparer.Ordinal).ToArray();
        TownCommitmentDurableState[] commitments = _commitments.Values
            .OrderBy(value => value.CommitmentId.Value, StringComparer.Ordinal)
            .Select(value =>
            {
                var term = (CoinOrResourceTransferTerm)value.Term;
                return new TownCommitmentDurableState(
                    value.CommitmentId.Value,
                    value.Debtor.Value,
                    value.Creditor.Value,
                    term.AssetRef.Value,
                    term.Amount,
                    value.Deadline.Ticks,
                    value.Status,
                    value.SourceRef.CanonicalEventId);
            }).ToArray();
        return new TownSocialDurableState(
            relationships,
            bonds,
            commitments,
            _appliedRelationshipEvents.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    public void RestoreDurableState(TownSocialDurableState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _relationships.Clear();
        foreach (TownDirectedRelationshipSnapshot relationship in state.Relationships)
        {
            ValidatePair(relationship.SubjectActorId, relationship.OtherActorId);
            _relationships.Add(
                (relationship.SubjectActorId.Value, relationship.OtherActorId.Value),
                relationship);
        }
        _bonds.Clear();
        foreach (TownSocialBondSnapshot bond in state.Bonds)
        {
            ValidatePair(bond.FirstActorId, bond.SecondActorId);
            _bonds.Add(PairKey(bond.FirstActorId, bond.SecondActorId), bond);
        }
        _commitments.Clear();
        foreach (TownCommitmentDurableState saved in state.Commitments)
        {
            var commitment = new Commitment(
                new CommitmentId(saved.CommitmentId),
                new ActorId(saved.DebtorActorId),
                new ActorId(saved.CreditorActorId),
                new CoinOrResourceTransferTerm(new ResourceRef(saved.AssetId), saved.Amount),
                new SimTime(saved.DeadlineTicks),
                saved.Status,
                new CommitmentSourceRef(saved.SourceEventId
                    ?? throw new InvalidDataException("Saved transfer obligation lacks its canonical source.")));
            _commitments.Add(commitment.CommitmentId, commitment);
        }
        _appliedRelationshipEvents.Clear();
        foreach (string eventId in state.AppliedRelationshipEventIds)
            _appliedRelationshipEvents.Add(eventId);
        _bondTransitionEvents.Clear();
        _obligationTransitionEvents.Clear();
    }

    public Commitment AddTransferObligation(
        CommitmentId commitmentId,
        ActorId debtor,
        ActorId creditor,
        string assetId,
        long amount,
        SimTime dueAt,
        string sourceEventId)
    {
        ValidatePair(debtor, creditor);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEventId);
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (_commitments.ContainsKey(commitmentId)) throw new InvalidOperationException("Transfer obligation identity is already used.");
        if (!_history.Events.Any(value => value.SourceId.Value == sourceEventId))
            throw new InvalidOperationException("Transfer obligation source must resolve to canonical history.");
        var commitment = new Commitment(
            commitmentId,
            debtor,
            creditor,
            new CoinOrResourceTransferTerm(new ResourceRef(assetId), amount),
            dueAt,
            CommitmentStatus.Active,
            new CommitmentSourceRef(sourceEventId));
        _commitments.Add(commitmentId, commitment);
        return commitment;
    }

    public TownRelationshipProjectionResult ApplyRelationshipEvent(
        string sourceEventId,
        TownRelationshipEventKind kind,
        ActorId subject,
        ActorId other,
        bool successful,
        SimTime now,
        TownSocialEventContext context)
    {
        ValidatePair(subject, other);
        ValidateContext(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEventId);
        string applicationId = $"{sourceEventId}|{subject.Value}|{other.Value}";
        if (_appliedRelationshipEvents.Contains(applicationId))
        {
            CanonicalHistoryEventRecord existing = _history.Events.Single(value => value.SourceId.Value == sourceEventId);
            return new TownRelationshipProjectionResult(false, GetRelationship(subject, other), existing);
        }

        string token = RelationshipEventToken(kind, successful);
        TownHistoryProjectionResult projection = _history.RecordRuntimeEvent(
            sourceEventId,
            $"social/{token}",
            now.Ticks,
            context.LocationId,
            context.Position,
            context.SpatialLayer,
            [subject, other],
            $"{subject.Value} appraised {other.Value} after {token}.",
            [new CanonicalHistorySourceReference("relationship", $"{subject.Value}/{other.Value}")],
            context.Presences);
        TownDirectedRelationshipSnapshot current = ApplyRelationshipDelta(sourceEventId, kind, subject, other, successful);
        return new TownRelationshipProjectionResult(true, current, projection.Event);
    }

    public TownRelationshipProjectionResult ApplyRelationshipEffect(
        string sourceEventId,
        TownRelationshipEventKind kind,
        ActorId subject,
        ActorId other,
        bool successful,
        double scale)
    {
        ValidatePair(subject, other);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEventId);
        if (!double.IsFinite(scale) || scale <= 0 || scale > 1)
            throw new ArgumentOutOfRangeException(nameof(scale));
        CanonicalHistoryEventRecord source = _history.Events.SingleOrDefault(value =>
            StringComparer.Ordinal.Equals(value.SourceId.Value, sourceEventId))
            ?? throw new InvalidOperationException("Relationship effect source must resolve to canonical history.");
        string applicationId = $"{sourceEventId}|{subject.Value}|{other.Value}";
        bool applied = !_appliedRelationshipEvents.Contains(applicationId);
        TownDirectedRelationshipSnapshot relationship = ApplyRelationshipDelta(
            sourceEventId, kind, subject, other, successful, scale);
        return new TownRelationshipProjectionResult(applied, relationship, source);
    }

    public TownSocialBondTransitionResult ProposeBondTransition(
        string sourceEventId,
        ActorId first,
        ActorId second,
        TownSocialBondStage proposedStage,
        bool firstAccepted,
        bool secondAccepted,
        SimTime now,
        TownSocialEventContext context)
    {
        ValidatePair(first, second);
        ValidateContext(context);
        if (_bondTransitionEvents.TryGetValue(sourceEventId, out TownSocialBondTransitionResult? replay)) return replay;
        TownSocialBondSnapshot current = GetBond(first, second);
        if (!firstAccepted || !secondAccepted)
            return new TownSocialBondTransitionResult(false, "mutual oral acceptance required", current, null);
        if ((int)proposedStage != (int)current.Stage + 1)
            return new TownSocialBondTransitionResult(false, "bond stages advance exactly one step", current, null);

        TownHistoryProjectionResult projection = RecordBondEvent(
            sourceEventId, current, proposedStage, now, context, "bond-transition");
        TownSocialBondSnapshot updated = current with { Stage = proposedStage, SourceEventId = sourceEventId };
        _bonds[PairKey(first, second)] = updated;
        var result = new TownSocialBondTransitionResult(true, null, updated, projection.Event);
        _bondTransitionEvents.Add(sourceEventId, result);
        return result;
    }

    public TownSocialBondTransitionResult Separate(
        string sourceEventId,
        ActorId first,
        ActorId second,
        bool firstAccepted,
        bool secondAccepted,
        SimTime now,
        TownSocialEventContext context)
    {
        ValidatePair(first, second);
        ValidateContext(context);
        if (_bondTransitionEvents.TryGetValue(sourceEventId, out TownSocialBondTransitionResult? replay)) return replay;
        TownSocialBondSnapshot current = GetBond(first, second);
        if (!firstAccepted || !secondAccepted)
            return new TownSocialBondTransitionResult(false, "mutual oral acceptance required", current, null);
        if (current.Stage is not (TownSocialBondStage.Partner or TownSocialBondStage.Spouse))
            return new TownSocialBondTransitionResult(false, "only partners or spouses can separate", current, null);

        TownHistoryProjectionResult projection = RecordBondEvent(
            sourceEventId, current, TownSocialBondStage.Friend, now, context, "separation");
        TownSocialBondSnapshot updated = current with { Stage = TownSocialBondStage.Friend, SourceEventId = sourceEventId };
        _bonds[PairKey(first, second)] = updated;
        var result = new TownSocialBondTransitionResult(true, null, updated, projection.Event);
        _bondTransitionEvents.Add(sourceEventId, result);
        return result;
    }

    public TownObligationTransitionResult FulfillObligation(
        CommitmentId commitmentId,
        string sourceEventId,
        SimTime now,
        TownSocialEventContext context,
        AutonomousNpcCognitionRoute cognitionRoute = AutonomousNpcCognitionRoute.None)
    {
        ValidateContext(context);
        if (_obligationTransitionEvents.TryGetValue(sourceEventId, out TownObligationTransitionResult? replay)) return replay;
        Commitment current = GetCommitment(commitmentId);
        if (current.Status is not (CommitmentStatus.Active or CommitmentStatus.Overdue))
            return RejectedObligation(current, "only active or overdue obligations can be fulfilled");
        if (_history.ContainsSource(sourceEventId))
            return RejectedObligation(current, "canonical transition identity is already used");
        var term = (CoinOrResourceTransferTerm)current.Term;
        GameActionSpec transfer = _gameplay.CreateAssetTransfer(
            current.Debtor,
            AssetContainerOwnerKind.Actor,
            current.Debtor.Value,
            AssetContainerOwnerKind.Actor,
            current.Creditor.Value,
            term.AssetRef.Value,
            term.Amount);
        GameplayValidationResult validation = _gameplay.Validate(transfer, now);
        if (!validation.Available) return RejectedObligation(current, validation.Reason ?? "asset transfer unavailable");
        var request = new ActorExecutionRequest(
            new ActorExecutionId($"commitment/{commitmentId.Value}/{sourceEventId}"),
            current.Debtor,
            ActorExecutionMode.Interact,
            new InteractExecutionPayload(current.Debtor, transfer),
            now,
            cognitionRoute);
        ActorExecutionReceipt receipt = ActorExecutionPipeline.Dispatch(request, _gameplay.CreateExecutor(current.Debtor));
        if (receipt.Outcome != ActorExecutionOutcome.Completed)
            return RejectedObligation(current, receipt.Evidence, receipt);

        Commitment updated = current.AsFulfilled();
        CanonicalHistoryEventRecord source = RecordObligationEvent(
            sourceEventId, current, updated.Status, now, context, receipt.ExecutionId.Value);
        _commitments[commitmentId] = updated;
        _ = ApplyRelationshipDelta(sourceEventId, TownRelationshipEventKind.FulfilledCommitment,
            current.Creditor, current.Debtor, true);
        var result = new TownObligationTransitionResult(true, null, updated, source, receipt);
        _obligationTransitionEvents.Add(sourceEventId, result);
        return result;
    }

    public TownObligationTransitionResult CancelObligation(
        CommitmentId commitmentId,
        string sourceEventId,
        SimTime now,
        TownSocialEventContext context)
    {
        ValidateContext(context);
        if (_obligationTransitionEvents.TryGetValue(sourceEventId, out TownObligationTransitionResult? replay)) return replay;
        Commitment current = GetCommitment(commitmentId);
        if (current.Status is not (CommitmentStatus.Active or CommitmentStatus.Overdue))
            return RejectedObligation(current, "only active or overdue obligations can be cancelled");
        Commitment updated = current.AsCancelled();
        CanonicalHistoryEventRecord source = RecordObligationEvent(sourceEventId, current, updated.Status, now, context, null);
        _commitments[commitmentId] = updated;
        _ = ApplyRelationshipDelta(sourceEventId, TownRelationshipEventKind.BrokenCommitment,
            current.Creditor, current.Debtor, true);
        var result = new TownObligationTransitionResult(true, null, updated, source, null);
        _obligationTransitionEvents.Add(sourceEventId, result);
        return result;
    }

    public TownObligationTransitionResult MarkOverdue(
        CommitmentId commitmentId,
        string sourceEventId,
        SimTime now,
        TownSocialEventContext context)
    {
        ValidateContext(context);
        if (_obligationTransitionEvents.TryGetValue(sourceEventId, out TownObligationTransitionResult? replay)) return replay;
        Commitment current = GetCommitment(commitmentId);
        if (current.Status != CommitmentStatus.Active || now.Ticks <= current.Deadline.Ticks)
            return RejectedObligation(current, "only past-due active obligations can become overdue");
        Commitment updated = current.AsOverdue();
        CanonicalHistoryEventRecord source = RecordObligationEvent(sourceEventId, current, updated.Status, now, context, null);
        _commitments[commitmentId] = updated;
        _ = ApplyRelationshipDelta(sourceEventId, TownRelationshipEventKind.BrokenCommitment,
            current.Creditor, current.Debtor, true);
        var result = new TownObligationTransitionResult(true, null, updated, source, null);
        _obligationTransitionEvents.Add(sourceEventId, result);
        return result;
    }

    public IReadOnlyList<TownObligationTransitionResult> Advance(
        SimTime now,
        Func<ActorId, TownSocialEventContext> contextForActor)
    {
        ArgumentNullException.ThrowIfNull(contextForActor);
        Commitment[] due = _commitments.Values.Where(value =>
            value.Status == CommitmentStatus.Active && now.Ticks > value.Deadline.Ticks).ToArray();
        var results = new List<TownObligationTransitionResult>();
        foreach (Commitment commitment in due)
        {
            results.Add(MarkOverdue(
                commitment.CommitmentId,
                $"social/commitment/{commitment.CommitmentId.Value}/overdue/{commitment.Deadline.Ticks}",
                now,
                contextForActor(commitment.Debtor)));
        }
        return new ReadOnlyCollection<TownObligationTransitionResult>(results);
    }

    private TownDirectedRelationshipSnapshot ApplyRelationshipDelta(
        string sourceEventId,
        TownRelationshipEventKind kind,
        ActorId subject,
        ActorId other,
        bool successful,
        double scale = 1)
    {
        string applicationId = $"{sourceEventId}|{subject.Value}|{other.Value}";
        TownDirectedRelationshipSnapshot current = GetRelationship(subject, other);
        if (!_appliedRelationshipEvents.Add(applicationId)) return current;
        (double familiarity, double trust, double affection, double respect, double fear, double grievance) = kind switch
        {
            TownRelationshipEventKind.FulfilledCommitment => (0d, 0.10, 0d, 0.08, 0d, 0d),
            TownRelationshipEventKind.BrokenCommitment => (0d, -0.12, 0d, -0.08, 0d, 0.12),
            TownRelationshipEventKind.Help => (0d, 0.10, 0.08, 0d, 0d, 0d),
            TownRelationshipEventKind.Harm => (0d, -0.12, -0.12, 0d, 0.12, 0.15),
            TownRelationshipEventKind.SharedActivity when successful => (0.08, 0d, 0.03, 0d, 0d, 0d),
            TownRelationshipEventKind.SharedActivity => (0.08, 0d, 0d, 0d, 0d, 0d),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        familiarity *= scale;
        trust *= scale;
        affection *= scale;
        respect *= scale;
        fear *= scale;
        grievance *= scale;
        var updated = current with
        {
            Familiarity = Clamp(current.Familiarity + familiarity),
            Trust = Clamp(current.Trust + trust),
            Affection = Clamp(current.Affection + affection),
            Respect = Clamp(current.Respect + respect),
            Fear = Clamp(current.Fear + fear),
            Grievance = Clamp(current.Grievance + grievance)
        };
        _relationships[(subject.Value, other.Value)] = updated;
        return updated;
    }

    private TownHistoryProjectionResult RecordBondEvent(
        string sourceEventId,
        TownSocialBondSnapshot current,
        TownSocialBondStage next,
        SimTime now,
        TownSocialEventContext context,
        string kind) => _history.RecordRuntimeEvent(
        sourceEventId,
        $"social/{kind}",
        now.Ticks,
        context.LocationId,
        context.Position,
        context.SpatialLayer,
        [current.FirstActorId, current.SecondActorId],
        $"{current.FirstActorId.Value} and {current.SecondActorId.Value} orally changed their bond from {current.Stage} to {next}.",
        [new CanonicalHistorySourceReference("bond", PairKey(current.FirstActorId, current.SecondActorId))],
        context.Presences);

    private CanonicalHistoryEventRecord RecordObligationEvent(
        string sourceEventId,
        Commitment current,
        CommitmentStatus next,
        SimTime now,
        TownSocialEventContext context,
        string? executionId)
    {
        var references = new List<CanonicalHistorySourceReference>
        {
            new("commitment", current.CommitmentId.Value),
            new("commitment-source", current.SourceRef.CanonicalEventId ?? "dialogue")
        };
        if (executionId is not null) references.Add(new CanonicalHistorySourceReference("receipt", executionId));
        return _history.RecordRuntimeEvent(
            sourceEventId,
            $"social/commitment/{next}",
            now.Ticks,
            context.LocationId,
            context.Position,
            context.SpatialLayer,
            [current.Debtor, current.Creditor],
            $"Transfer obligation {current.CommitmentId.Value} changed from {current.Status} to {next}.",
            references,
            context.Presences).Event;
    }

    private static TownSocialBondSnapshot CreateBondSnapshot(
        ActorId first,
        ActorId second,
        TownSocialBondStage stage,
        string? sourceEventId) => StringComparer.Ordinal.Compare(first.Value, second.Value) < 0
        ? new TownSocialBondSnapshot(first, second, stage, sourceEventId)
        : new TownSocialBondSnapshot(second, first, stage, sourceEventId);

    private static string PairKey(ActorId first, ActorId second) =>
        TownSocialConfigurationValidator.PairKey(first.Value, second.Value);

    private static string RelationshipEventToken(TownRelationshipEventKind kind, bool successful) => kind switch
    {
        TownRelationshipEventKind.FulfilledCommitment => "commitment-fulfilled",
        TownRelationshipEventKind.BrokenCommitment => "commitment-broken",
        TownRelationshipEventKind.Help => "help",
        TownRelationshipEventKind.Harm => "harm",
        TownRelationshipEventKind.SharedActivity when successful => "shared-activity-success",
        TownRelationshipEventKind.SharedActivity => "shared-activity",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static TownObligationTransitionResult RejectedObligation(
        Commitment commitment,
        string reason,
        ActorExecutionReceipt? receipt = null) => new(false, reason, commitment, null, receipt);

    private void ValidatePair(ActorId first, ActorId second)
    {
        if (!_actorIds.Contains(first.Value) || !_actorIds.Contains(second.Value) || first == second)
            throw new ArgumentException("Social state requires two distinct Actors in this Town world.");
    }

    private static void ValidateContext(TownSocialEventContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.LocationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.SpatialLayer);
        ArgumentNullException.ThrowIfNull(context.Presences);
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);
}

/// <summary>Deterministic actor-local appraisal shared by dialogue and other visible social sources.</summary>
public sealed class TownSocialAppraisalRuntime
{
    private readonly TownSocialRuntime _social;
    private readonly LivingTownPopulationRuntime _population;
    private readonly HashSet<string> _applied = new(StringComparer.Ordinal);

    public TownSocialAppraisalRuntime(TownSocialRuntime social, LivingTownPopulationRuntime population)
    {
        _social = social ?? throw new ArgumentNullException(nameof(social));
        _population = population ?? throw new ArgumentNullException(nameof(population));
    }

    public TownSocialAppraisalResult Apply(
        string sourceEventId,
        ActorId subject,
        ActorId other,
        TownSocialEffectKind effect,
        double intensity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEventId);
        if (!Enum.IsDefined(effect)) throw new ArgumentOutOfRangeException(nameof(effect));
        if (!double.IsFinite(intensity) || intensity < 0 || intensity > 1)
            throw new ArgumentOutOfRangeException(nameof(intensity));
        CanonicalHistoryEventRecord source = _social.GetSourceEvent(sourceEventId);
        LivingTownNpcStateOwner state = _population.GetNpc(subject).State;
        string applicationId = $"{sourceEventId}|{subject.Value}|{other.Value}|{effect}";
        TownDirectedRelationshipSnapshot relationship = _social.GetRelationship(subject, other);
        if (!_applied.Add(applicationId))
            return new TownSocialAppraisalResult(
                false, sourceEventId, effect, Route(effect, intensity), state.CurrentEmotion, relationship);

        double sensitivity = 0.75 + 0.25 * Math.Max(
            state.NpcState.Personality.CognitiveStyle.Fe,
            state.NpcState.Personality.CognitiveStyle.Fi);
        double effectiveIntensity = Math.Clamp(intensity * sensitivity, 0, 1);
        CurrentEmotionState emotion = ProjectEmotion(effect, effectiveIntensity, sourceEventId, state.CurrentEmotion);
        if (effect != TownSocialEffectKind.Neutral)
        {
            state.ApplyEmotion(emotion);
            _ = state.Memory.ApplyEmotion(
                new SourceEventId(sourceEventId),
                new MemoryEmotion(
                    emotion.Kind,
                    emotion.Valence,
                    emotion.Intensity,
                    new SourceEventId(sourceEventId),
                    source.OccurredAtTicks));
        }

        TownRelationshipEventKind? relationshipKind = effect switch
        {
            TownSocialEffectKind.Support => TownRelationshipEventKind.Help,
            TownSocialEffectKind.Harm or TownSocialEffectKind.Threat => TownRelationshipEventKind.Harm,
            TownSocialEffectKind.Breach => TownRelationshipEventKind.BrokenCommitment,
            TownSocialEffectKind.SharedInterest => TownRelationshipEventKind.SharedActivity,
            _ => null
        };
        if (relationshipKind is TownRelationshipEventKind kind)
        {
            relationship = _social.ApplyRelationshipEffect(
                sourceEventId,
                kind,
                subject,
                other,
                effect != TownSocialEffectKind.Breach,
                0.5 + effectiveIntensity * 0.5).Relationship;
        }
        return new TownSocialAppraisalResult(
            true, sourceEventId, effect, Route(effect, intensity), state.CurrentEmotion, relationship);
    }

    private static TownSocialCognitionRoute Route(TownSocialEffectKind effect, double intensity) => effect switch
    {
        TownSocialEffectKind.Neutral when intensity < 0.5 => TownSocialCognitionRoute.L0,
        TownSocialEffectKind.Support or TownSocialEffectKind.Apology or TownSocialEffectKind.SharedInterest
            when intensity < 0.7 => TownSocialCognitionRoute.L1,
        _ => TownSocialCognitionRoute.L2
    };

    private static CurrentEmotionState ProjectEmotion(
        TownSocialEffectKind effect,
        double intensity,
        string sourceEventId,
        CurrentEmotionState current)
    {
        if (effect == TownSocialEffectKind.Neutral) return current;
        (LivingTownEmotionKind kind, double valence) = effect switch
        {
            TownSocialEffectKind.Support => (LivingTownEmotionKind.Trust, 0.7),
            TownSocialEffectKind.Harm => (LivingTownEmotionKind.Anger, -0.8),
            TownSocialEffectKind.Promise => (LivingTownEmotionKind.Trust, 0.4),
            TownSocialEffectKind.Breach => (LivingTownEmotionKind.Anger, -0.9),
            TownSocialEffectKind.Threat => (LivingTownEmotionKind.Fear, -0.9),
            TownSocialEffectKind.Apology => (LivingTownEmotionKind.Surprise, 0.2),
            TownSocialEffectKind.SharedInterest => (LivingTownEmotionKind.Joy, 0.6),
            _ => throw new ArgumentOutOfRangeException(nameof(effect))
        };
        return new CurrentEmotionState(
            kind,
            valence * intensity,
            intensity,
            new SourceEventId(sourceEventId));
    }
}
