using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Alice.Activities;
using Alice.Actors;
using Alice.Interaction;
using Alice.Memory;
using Alice.ModelRuntime;
using Alice.Navigation;
using Alice.ProductRuntime;
using Alice.Social;

namespace Alice.LivingTown;

public enum TownRq1ActivationMode
{
    AgentCentric,
    EventCentric
}

public enum TownRq2MemoryMode
{
    Verbatim,
    Summary
}

public sealed record TownL2PolicySnapshot(
    TownRq1ActivationMode Rq1Activation,
    TownRq2MemoryMode Rq2Memory,
    long ActivatedAtTick);

public sealed record TownL2PolicyDurableState(
    TownL2PolicySnapshot Active,
    bool MixedPolicyDemo);

public sealed record TownL2AdmissionCandidate(ActorId ActorId, TownL2DecisionProblem Problem);

/// <summary>Demo policy owner. Pending UI choices become active only after a settled tick.</summary>
public sealed class TownL2PolicyRuntime
{
    private TownL2PolicySnapshot _active = new(
        TownRq1ActivationMode.AgentCentric,
        TownRq2MemoryMode.Verbatim,
        0);
    private TownRq1ActivationMode? _pendingRq1;
    private TownRq2MemoryMode? _pendingRq2;

    public TownL2PolicySnapshot Active => _active;
    public TownRq1ActivationMode PendingRq1 => _pendingRq1 ?? _active.Rq1Activation;
    public TownRq2MemoryMode PendingRq2 => _pendingRq2 ?? _active.Rq2Memory;
    public bool MixedPolicyDemo { get; private set; }

    public TownL2PolicyDurableState CaptureDurableState() => new(_active, MixedPolicyDemo);

    public void RestoreDurableState(TownL2PolicyDurableState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _active = state.Active;
        MixedPolicyDemo = state.MixedPolicyDemo;
        _pendingRq1 = null;
        _pendingRq2 = null;
    }

    public void SelectRq1(TownRq1ActivationMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _pendingRq1 = mode;
    }

    public void SelectRq2(TownRq2MemoryMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _pendingRq2 = mode;
    }

    public bool SettleTick(SimTime settledAt)
    {
        TownRq1ActivationMode rq1 = _pendingRq1 ?? _active.Rq1Activation;
        TownRq2MemoryMode rq2 = _pendingRq2 ?? _active.Rq2Memory;
        _pendingRq1 = null;
        _pendingRq2 = null;
        if (rq1 == _active.Rq1Activation && rq2 == _active.Rq2Memory) return false;
        _active = new TownL2PolicySnapshot(rq1, rq2, settledAt.Ticks);
        MixedPolicyDemo = true;
        return true;
    }

    public IReadOnlyList<TownL2AdmissionCandidate> OrderForAdmission(
        IEnumerable<TownL2AdmissionCandidate> candidates)
        => OrderForAdmission(candidates, _active.Rq1Activation);

    public static IReadOnlyList<TownL2AdmissionCandidate> OrderForAdmission(
        IEnumerable<TownL2AdmissionCandidate> candidates,
        TownRq1ActivationMode mode)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        TownL2AdmissionCandidate[] values = candidates.ToArray();
        foreach (TownL2AdmissionCandidate value in values) ArgumentNullException.ThrowIfNull(value);
        IOrderedEnumerable<TownL2AdmissionCandidate> ordered = mode switch
        {
            TownRq1ActivationMode.AgentCentric => values
                .OrderBy(value => value.ActorId.Value, StringComparer.Ordinal)
                .ThenBy(value => value.Problem.DecisionId, StringComparer.Ordinal),
            TownRq1ActivationMode.EventCentric => values
                .OrderBy(value => value.Problem.SubjectRef, StringComparer.Ordinal)
                .ThenBy(value => value.Problem.DecisionId, StringComparer.Ordinal)
                .ThenBy(value => value.ActorId.Value, StringComparer.Ordinal),
            _ => throw new ArgumentOutOfRangeException()
        };
        return new ReadOnlyCollection<TownL2AdmissionCandidate>(ordered.ToArray());
    }
}

/// <summary>Non-formal in-memory cache for complete-set Demo summaries.</summary>
public sealed class TownDemoSummaryCache
{
    private readonly Dictionary<string, FrozenSummaryArtifact> _artifacts = new(StringComparer.Ordinal);

    public void Register(FrozenSummaryArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        _artifacts[artifact.CandidateSet.CandidateSetId.Value] = artifact;
    }

    public FrozenSummaryArtifact? Find(DecisionMemoryCandidateSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (!_artifacts.TryGetValue(set.CandidateSetId.Value, out FrozenSummaryArtifact? artifact)) return null;
        return artifact.CandidateSet.GetCanonicalBytes().AsSpan().SequenceEqual(set.GetCanonicalBytes())
            ? artifact
            : null;
    }
}

public sealed class TownApproximateTokenCounter : IMemoryPacketTokenCounter
{
    public int CountTokens(ReadOnlySpan<byte> modelVisibleBytes) =>
        checked((modelVisibleBytes.Length + 3) / 4);
}

public sealed class TownL2RequestTrace
{
    internal TownL2RequestTrace(TownL2PolicySnapshot policy, TownL2PreparedDecision prepared)
    {
        Rq1Activation = policy.Rq1Activation;
        Rq2Memory = policy.Rq2Memory;
        PolicyActivatedAtTick = policy.ActivatedAtTick;
        CandidateSetId = prepared.CandidateSet.CandidateSetId.Value;
        OrderedMemoryIds = new ReadOnlyCollection<string>(
            prepared.CandidateSet.RankedSlices.Select(value => value.MemoryId.Value).ToArray());
    }

    public TownRq1ActivationMode Rq1Activation { get; }
    public TownRq2MemoryMode Rq2Memory { get; }
    public long PolicyActivatedAtTick { get; }
    public string CandidateSetId { get; }
    public IReadOnlyList<string> OrderedMemoryIds { get; }
}

public sealed class TownL2ProductRequest
{
    private readonly byte[] _modelVisibleBytes;

    internal TownL2ProductRequest(
        TownL2PreparedDecision prepared,
        TownL2PolicySnapshot policy,
        MemoryPacket packet,
        RemotePlannerRequest remoteRequest,
        byte[] modelVisibleBytes)
    {
        Prepared = prepared;
        Policy = policy;
        Packet = packet;
        RemoteRequest = remoteRequest;
        Trace = new TownL2RequestTrace(policy, prepared);
        _modelVisibleBytes = modelVisibleBytes.ToArray();
    }

    public TownL2PreparedDecision Prepared { get; }
    public TownL2PolicySnapshot Policy { get; }
    public MemoryPacket Packet { get; }
    public RemotePlannerRequest RemoteRequest { get; }
    public TownL2RequestTrace Trace { get; }
    public byte[] GetSharedModelVisibleBytes() => Prepared.GetSharedModelVisibleBytes();
    public byte[] GetModelVisibleBytes() => _modelVisibleBytes.ToArray();
}

public abstract record TownL2RequestPreparationOutcome
{
    private protected TownL2RequestPreparationOutcome() { }
}

public sealed record TownL2RequestReady(TownL2ProductRequest Request) : TownL2RequestPreparationOutcome;
public sealed record TownL2SummaryPending(
    DecisionMemoryCandidateSetId CandidateSetId,
    TownL2PolicySnapshot Policy) : TownL2RequestPreparationOutcome;
public sealed record TownL2PreparationUnavailable(string Reason) : TownL2RequestPreparationOutcome;

public abstract record TownL2MemoryPacketPreparation
{
    private protected TownL2MemoryPacketPreparation() { }
}

public sealed record TownL2MemoryPacketReady(
    TownL2PolicySnapshot Policy,
    MemoryPacket Packet,
    byte[] ModelVisibleBytes) : TownL2MemoryPacketPreparation;
public sealed record TownL2MemoryPacketSummaryPending(
    DecisionMemoryCandidateSetId CandidateSetId,
    TownL2PolicySnapshot Policy) : TownL2MemoryPacketPreparation;
public sealed record TownL2MemoryPacketUnavailable(string Reason) : TownL2MemoryPacketPreparation;

public abstract record TownL2ProposalSettlementOutcome
{
    private protected TownL2ProposalSettlementOutcome() { }
}

public sealed record TownL2ProposalSettled(ActorExecutionReceipt Receipt) : TownL2ProposalSettlementOutcome;
public sealed record TownL2ProposalTravelRequired(
    TownGameplayActionOffer Offer,
    string CatalogueTargetId,
    string TravelPlaceId) : TownL2ProposalSettlementOutcome;
public sealed record TownL2ProposalRejected(string Reason) : TownL2ProposalSettlementOutcome;

public abstract record TownL2InvocationOutcome
{
    private protected TownL2InvocationOutcome() { }
}

public sealed record TownL2InvocationNotReady(TownL2RequestPreparationOutcome Preparation) : TownL2InvocationOutcome;
public sealed record TownL2ProviderUnavailable(
    ModelClientExecutionMode Mode,
    ModelClientUnavailableReason Reason) : TownL2InvocationOutcome;
public sealed record TownL2ProviderRejected(RemotePlannerDecision Decision) : TownL2InvocationOutcome;
public sealed record TownL2InvocationSettled(
    TownL2ProductRequest Request,
    ModelClientExecutionEvidence Evidence,
    ActorExecutionReceipt Receipt) : TownL2InvocationOutcome;
public sealed record TownL2InvocationTravelRequired(
    TownL2ProductRequest Request,
    ModelClientExecutionEvidence Evidence,
    TownGameplayActionOffer Offer,
    string CatalogueTargetId,
    string TravelPlaceId) : TownL2InvocationOutcome;

public sealed record TownL2PacketDebugSnapshot(
    TownRq2MemoryMode Mode,
    string CandidateSetId,
    int CandidateCount,
    int RepresentedCount,
    int IncludedCount,
    int TruncatedCount,
    int ConsumedTokens,
    int UnspentTokens,
    IReadOnlyList<string> IncludedMemoryIds,
    TownL2PacketModeDebugSnapshot Verbatim,
    TownL2PacketModeDebugSnapshot Summary);

public sealed record TownL2PacketModeDebugSnapshot(
    TownRq2MemoryMode Mode,
    string Status,
    int IncludedCount,
    int ConsumedTokens,
    string ModelVisiblePreview);

/// <summary>Product L2 packet, live-provider and Validator/Authority bridge.</summary>
public sealed class TownL2DecisionRuntime
{
    private static readonly MemoryPacketTokenizerVersion DemoTokenizer = new("town_demo_utf8_approx");
    private readonly TownL2DecisionContextRuntime _context;
    private readonly LivingTownPopulationRuntime _population;
    private readonly RegionSocialGameplayRuntime _gameplay;
    private readonly IModelClient<RemotePlannerResponse> _client;
    private readonly ProviderQueueConfiguration _queue;
    private readonly IMemoryPacketTokenCounter _counter;
    private long _requestSequence;
    private long _executionSequence;
    private int _inFlightCount;

    public TownL2DecisionRuntime(
        TownL2DecisionContextRuntime context,
        LivingTownPopulationRuntime population,
        RegionSocialGameplayRuntime gameplay,
        IModelClient<RemotePlannerResponse> client,
        ProviderQueueConfiguration queue,
        IMemoryPacketTokenCounter? counter = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _population = population ?? throw new ArgumentNullException(nameof(population));
        _gameplay = gameplay ?? throw new ArgumentNullException(nameof(gameplay));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _counter = counter ?? new TownApproximateTokenCounter();
    }

    public TownL2PolicyRuntime Policy { get; } = new();
    public TownDemoSummaryCache Summaries { get; } = new();
    public bool EnableDebugSummaryFallback { get; set; }
    public TownL2PacketDebugSnapshot? LastPacketDebug { get; private set; }
    public bool HasInFlightWork => Volatile.Read(ref _inFlightCount) != 0;

    public TownL2RequestPreparationOutcome Prepare(
        ActorId actorId,
        TownL2DecisionProblem problem,
        SimTime now)
    {
        TownL2DecisionPreparationOutcome baseOutcome = _context.Prepare(actorId, problem, now);
        if (baseOutcome is not TownL2PreparedDecision prepared)
            return new TownL2PreparationUnavailable("No actor-visible memory is available.");
        if (prepared.AllowedProposals.Count == 0)
            return new TownL2PreparationUnavailable("No currently allowed gameplay proposal is available.");
        TownL2MemoryPacketPreparation packetPreparation = PrepareMemoryPacket(prepared);
        if (packetPreparation is TownL2MemoryPacketSummaryPending pending)
            return new TownL2SummaryPending(pending.CandidateSetId, pending.Policy);
        if (packetPreparation is TownL2MemoryPacketUnavailable unavailable)
            return new TownL2PreparationUnavailable(unavailable.Reason);
        TownL2MemoryPacketReady readyPacket = (TownL2MemoryPacketReady)packetPreparation;
        var requestId = new RemotePlannerRequestId(
            $"town-l2-{actorId.Value}-{checked(Interlocked.Increment(ref _requestSequence))}");
        RemotePlannerRequest remote = RemotePlannerRequest.CreateTownProposal(
            requestId,
            actorId,
            problem.DecisionId,
            prepared.CandidateSet.CandidateSetId,
            readyPacket.ModelVisibleBytes,
            prepared.AllowedProposals.Select(value => value.ProposalId));
        return new TownL2RequestReady(new TownL2ProductRequest(
            prepared, readyPacket.Policy, readyPacket.Packet, remote, readyPacket.ModelVisibleBytes));
    }

    public TownL2MemoryPacketPreparation PrepareMemoryPacket(TownL2PreparedDecision prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        TownL2PolicySnapshot policy = Policy.Active;
        int sharedTokens = _counter.CountTokens(prepared.GetSharedModelVisibleBytes());
        int memoryCeilingValue = _queue.MaxContextTokens - sharedTokens;
        if (memoryCeilingValue <= 0)
            return new TownL2MemoryPacketUnavailable("The shared decision envelope exceeds the context ceiling.");
        var ceiling = new MemoryPacketTokenCeiling(memoryCeilingValue);
        MemoryPacketBuildOutcome packetOutcome;
        if (policy.Rq2Memory == TownRq2MemoryMode.Verbatim)
        {
            packetOutcome = MemoryPacketBuilders.BuildVerbatim(
                prepared.CandidateSet, _counter, ceiling, DemoTokenizer);
        }
        else
        {
            FrozenSummaryArtifact? artifact = Summaries.Find(prepared.CandidateSet);
            if (artifact is null && EnableDebugSummaryFallback)
            {
                artifact = CreateDebugSummary(prepared.CandidateSet);
                Summaries.Register(artifact);
            }
            if (artifact is null)
                return new TownL2MemoryPacketSummaryPending(prepared.CandidateSet.CandidateSetId, policy);
            packetOutcome = MemoryPacketBuilders.BuildSummary(
                prepared.CandidateSet, artifact, _counter, ceiling, DemoTokenizer);
        }
        if (packetOutcome is not MemoryPacketBuildSuccess success)
            return new TownL2MemoryPacketUnavailable(packetOutcome.GetType().Name);
        TownL2PacketModeDebugSnapshot verbatimDebug = BuildPacketDebug(
            TownRq2MemoryMode.Verbatim, prepared.CandidateSet, ceiling, null);
        TownL2PacketModeDebugSnapshot summaryDebug = BuildPacketDebug(
            TownRq2MemoryMode.Summary,
            prepared.CandidateSet,
            ceiling,
            Summaries.Find(prepared.CandidateSet));
        LastPacketDebug = new TownL2PacketDebugSnapshot(
            policy.Rq2Memory,
            prepared.CandidateSet.CandidateSetId.Value,
            prepared.CandidateSet.RankedSlices.Count,
            policy.Rq2Memory == TownRq2MemoryMode.Summary
                ? Math.Min(6, prepared.CandidateSet.RankedSlices.Count)
                : success.Packet.IncludedMemoryIds.Count,
            success.Packet.IncludedMemoryIds.Count,
            success.Packet.TruncatedMemoryIds.Count,
            success.Packet.ConsumedTokens,
            success.Packet.UnspentTokens,
            new ReadOnlyCollection<string>(success.Packet.IncludedMemoryIds
                .Select(value => value.Value)
                .ToArray()),
            verbatimDebug,
            summaryDebug);
        byte[] modelVisible = SerializeModelVisible(
            prepared.GetSharedModelVisibleBytes(),
            success.Packet.GetModelVisibleBytes());
        return new TownL2MemoryPacketReady(policy, success.Packet, modelVisible);
    }

    private TownL2PacketModeDebugSnapshot BuildPacketDebug(
        TownRq2MemoryMode mode,
        DecisionMemoryCandidateSet candidateSet,
        MemoryPacketTokenCeiling ceiling,
        FrozenSummaryArtifact? summary)
    {
        if (mode == TownRq2MemoryMode.Summary && summary is null && EnableDebugSummaryFallback)
        {
            summary = CreateDebugSummary(candidateSet);
            Summaries.Register(summary);
        }
        if (mode == TownRq2MemoryMode.Summary && summary is null)
            return new TownL2PacketModeDebugSnapshot(mode, "frozen summary unavailable", 0, 0, string.Empty);
        MemoryPacketBuildOutcome outcome = mode == TownRq2MemoryMode.Verbatim
            ? MemoryPacketBuilders.BuildVerbatim(candidateSet, _counter, ceiling, DemoTokenizer)
            : MemoryPacketBuilders.BuildSummary(candidateSet, summary!, _counter, ceiling, DemoTokenizer);
        if (outcome is not MemoryPacketBuildSuccess built)
            return new TownL2PacketModeDebugSnapshot(mode, outcome.GetType().Name, 0, 0, string.Empty);
        return new TownL2PacketModeDebugSnapshot(
            mode,
            mode == TownRq2MemoryMode.Summary ? "Demo-only extractive preview" : "real packet preview",
            built.Packet.IncludedMemoryIds.Count,
            built.Packet.ConsumedTokens,
            CompactDebugSource(built.Packet.GetModelVisibleBytes()));
    }

    private static FrozenSummaryArtifact CreateDebugSummary(DecisionMemoryCandidateSet candidateSet)
    {
        FrozenSummaryClaim[] claims = candidateSet.RankedSlices
            .Take(6)
            .Select((slice, index) => new FrozenSummaryClaim(
                index,
                Encoding.UTF8.GetBytes(
                    $"DEBUG extract {index + 1}/{candidateSet.RankedSlices.Count}: "
                    + CompactDebugSource(slice.GetCanonicalSourceBytes())),
                slice.EvidenceStatus,
                slice.SourceIds,
                [],
                []))
            .ToArray();
        return FrozenSummaryArtifact.Create(
            candidateSet,
            new FrozenSummaryProfileVersion("town_debug_preview"),
            new FrozenSummaryArtifactVersion("debug_preview"),
            claims);
    }

    private static string CompactDebugSource(byte[] bytes)
    {
        string value = Encoding.UTF8.GetString(bytes)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return value.Length <= 280 ? value : $"{value[..280]}…";
    }

    public async ValueTask<TownL2InvocationOutcome> InvokeAsync(
        ActorId actorId,
        TownL2DecisionProblem problem,
        SimTime now,
        CancellationToken cancellationToken,
        Func<SimTime>? settlementTimeSource = null)
    {
        TownL2RequestPreparationOutcome preparation = Prepare(actorId, problem, now);
        if (preparation is not TownL2RequestReady ready)
            return new TownL2InvocationNotReady(preparation);
        Interlocked.Increment(ref _inFlightCount);
        try
        {
            ModelClientResult<RemotePlannerResponse> result = await _client.InvokeAsync(
                ready.Request.RemoteRequest, cancellationToken);
            if (result.Status == ModelClientResultStatus.Unavailable)
                return new TownL2ProviderUnavailable(result.Mode, result.UnavailableReason!.Value);
            RemotePlannerResponse response = result.Output!;
            if (response.Binding.RequestId != ready.Request.RemoteRequest.Binding.RequestId
                || response.Decision is not RemotePlannerSelectProposal selected)
                return new TownL2ProviderRejected(response.Decision);
            SimTime settledAt = settlementTimeSource?.Invoke() ?? now;
            TownL2ProposalSettlementOutcome settlement = SettleProposal(
                ready.Request, selected.ProposalId, settledAt);
            return settlement switch
            {
                TownL2ProposalSettled settled =>
                    new TownL2InvocationSettled(ready.Request, result.ExecutionEvidence!, settled.Receipt),
                TownL2ProposalTravelRequired travel =>
                    new TownL2InvocationTravelRequired(
                        ready.Request, result.ExecutionEvidence!, travel.Offer,
                        travel.CatalogueTargetId, travel.TravelPlaceId),
                _ => new TownL2ProviderRejected(new RemotePlannerFailure(RemotePlannerFailureKind.InvalidArguments))
            };
        }
        finally
        {
            Interlocked.Decrement(ref _inFlightCount);
        }
    }

    public TownL2ProposalSettlementOutcome SettleProposal(
        TownL2ProductRequest request,
        string proposalId,
        SimTime now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        TownL2AllowedProposal? admitted = request.Prepared.AllowedProposals.SingleOrDefault(value =>
            StringComparer.Ordinal.Equals(value.ProposalId, proposalId));
        if (admitted is null) return new TownL2ProposalRejected("Proposal is outside the captured catalogue.");
        string catalogueTargetId = request.Prepared.TargetId ?? admitted.TargetRef;
        TownGameplayActionOffer? offer = _gameplay.GetDecisionActionOffers(
                request.Prepared.ActorId,
                catalogueTargetId,
                now)
            .SingleOrDefault(value => StringComparer.Ordinal.Equals(value.EntryId, proposalId));
        if (offer is null || !offer.Validation.Available)
            return new TownL2ProposalRejected("Proposal is no longer available.");
        var action = new GameActionSpec(
            request.Prepared.ActorId,
            offer.Selection.Binding,
            offer.Selection.Arguments);
        string? travelPlaceId = _gameplay.GetTravelPlaceId(offer.Selection.Binding)
            ?? request.Prepared.TargetId;
        if (travelPlaceId is not null
            && !_gameplay.IsInInteractionRange(
                _population.GetNpc(request.Prepared.ActorId).State.Position,
                action,
                travelPlaceId))
            return new TownL2ProposalTravelRequired(offer, catalogueTargetId, travelPlaceId);
        var execution = new ActorExecutionRequest(
            new ActorExecutionId($"town-l2/{request.Prepared.ActorId.Value}/{checked(Interlocked.Increment(ref _executionSequence))}"),
            request.Prepared.ActorId,
            ActorExecutionMode.Interact,
            new InteractExecutionPayload(request.Prepared.ActorId, action),
            now,
            AutonomousNpcCognitionRoute.L2);
        ActorExecutionReceipt receipt = ActorExecutionPipeline.Dispatch(
            execution,
            _gameplay.CreateExecutor(request.Prepared.ActorId));
        return receipt.Outcome == ActorExecutionOutcome.Completed
            ? new TownL2ProposalSettled(receipt)
            : new TownL2ProposalRejected(receipt.Evidence);
    }

    internal static byte[] SerializeModelVisible(byte[] shared, byte[] packet)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("shared_context");
            writer.WriteRawValue(shared, skipInputValidation: false);
            writer.WritePropertyName("memory_packet");
            writer.WriteRawValue(packet, skipInputValidation: false);
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

}

public sealed record TownL2DialogueRequest(
    TownL2PreparedDecision Prepared,
    TownL2MemoryPacketReady Memory,
    RemotePlannerRequest RemoteRequest,
    ConversationSession Session,
    DialogueResponseOpportunity Opportunity,
    CanonicalHistoryEventRecord SourceEvent);

public abstract record TownL2DialogueInvocationOutcome
{
    private protected TownL2DialogueInvocationOutcome() { }
}

public sealed record TownL2DialogueNotReady(string Reason) : TownL2DialogueInvocationOutcome;
public sealed record TownL2DialogueProviderUnavailable(
    ModelClientExecutionMode Mode,
    ModelClientUnavailableReason Reason) : TownL2DialogueInvocationOutcome;
public sealed record TownL2DialogueProviderRejected(RemotePlannerDecision Decision) : TownL2DialogueInvocationOutcome;
public sealed record TownL2DialogueSettled(
    TownL2DialogueRequest Request,
    ModelClientExecutionEvidence Evidence,
    SemanticDialogueTurn ReplyTurn,
    CanonicalHistoryEventRecord ReplyEvent,
    TownSocialAppraisalResult IncomingAppraisal,
    TownSocialAppraisalResult? ReplyAppraisal) : TownL2DialogueInvocationOutcome;

public sealed record TownLocalDialogueSettled(
    SemanticDialogueTurn ReplyTurn,
    CanonicalHistoryEventRecord ReplyEvent,
    TownSocialAppraisalResult IncomingAppraisal,
    TownSocialAppraisalResult? ReplyAppraisal);

public delegate void TownL2DialogueTrace(
    ActorId actorId,
    string stage,
    string evidence,
    bool accepted);

/// <summary>Real-provider L2 dialogue over the same actor-visible RQ2 memory path as town decisions.</summary>
public sealed class TownL2DialogueRuntime
{
    private readonly TownL2DecisionContextRuntime _context;
    private readonly TownL2DecisionRuntime _decisions;
    private readonly IModelClient<RemotePlannerResponse> _client;
    private readonly ConversationRuntime _conversations;
    private readonly DialogueSurfaceLedger _surface;
    private readonly TownHistoryRuntime _history;
    private readonly TownSocialAppraisalRuntime _appraisals;
    private readonly LivingTownPopulationRuntime _population;
    private readonly TownPlayerStateOwner _player;
    private long _requestSequence;
    private int _inFlightCount;

    public TownL2DialogueRuntime(
        TownL2DecisionContextRuntime context,
        TownL2DecisionRuntime decisions,
        IModelClient<RemotePlannerResponse> client,
        ConversationRuntime conversations,
        DialogueSurfaceLedger surface,
        TownHistoryRuntime history,
        TownSocialAppraisalRuntime appraisals,
        LivingTownPopulationRuntime population,
        TownPlayerStateOwner player)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _appraisals = appraisals ?? throw new ArgumentNullException(nameof(appraisals));
        _population = population ?? throw new ArgumentNullException(nameof(population));
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    public bool HasInFlightWork => Volatile.Read(ref _inFlightCount) != 0;
    public TownL2PolicyRuntime Policy => _decisions.Policy;

    public TownLocalDialogueSettled SettleLocal(
        ConversationSession session,
        SemanticDialogueTurn sourceTurn,
        string actorVisibleText,
        TownL1DialogueRouteResponse response,
        SimTime now)
    {
        DialogueResponseOpportunity opportunity = session.PendingResponseOpportunities.Single(value =>
            value.SourceActId == sourceTurn.Act.ActId);
        CanonicalHistoryEventRecord sourceEvent = ProjectDialogueEvent(session, sourceTurn, actorVisibleText, now);
        ActorId responder = opportunity.Recipient;
        SemanticDialogueActKind kind = Enum.Parse<SemanticDialogueActKind>(response.ReplyKind!, false);
        TownSocialEffectKind incomingEffect = Enum.Parse<TownSocialEffectKind>(response.IncomingEffect!, false);
        TownSocialEffectKind replyEffect = Enum.Parse<TownSocialEffectKind>(response.ReplyEffect!, false);
        var reply = new SemanticDialogueAct(
            new SemanticDialogueActId($"{sourceTurn.Act.ActId.Value}-local-reply-{responder.Value}"),
            kind,
            responder,
            [sourceTurn.Act.Speaker],
            sourceTurn.Act.TopicRef,
            [],
            null,
            DialogueResponseExpectation.None);
        ConversationReplyResult replyResult = _conversations.Reply(session, opportunity, reply, now);
        SemanticDialogueTurn replyTurn = replyResult.Reply.RecordedTurn
            ?? throw new InvalidOperationException("Local dialogue reply was not committed.");
        _surface.RecordNpcTurn(
            response.ReplyText!, session, replyTurn, now, DialogueSurfaceRoute.L1);
        CanonicalHistoryEventRecord replyEvent = ProjectDialogueEvent(session, replyTurn, response.ReplyText!, now);
        TownSocialAppraisalResult incoming = _appraisals.Apply(
            sourceEvent.SourceId.Value, responder, sourceTurn.Act.Speaker, incomingEffect, response.Intensity);
        TownSocialAppraisalResult? outgoing = _population.Npcs.Any(value => value.ActorId == sourceTurn.Act.Speaker)
            ? _appraisals.Apply(replyEvent.SourceId.Value, sourceTurn.Act.Speaker, responder, replyEffect, response.Intensity)
            : null;
        return new TownLocalDialogueSettled(replyTurn, replyEvent, incoming, outgoing);
    }

    public void AbandonPendingResponse(ConversationSession session, SemanticDialogueTurn sourceTurn)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sourceTurn);
        DialogueResponseOpportunity opportunity = session.PendingResponseOpportunities.Single(value =>
            value.SourceActId == sourceTurn.Act.ActId);
        _conversations.AbandonResponse(session, opportunity);
    }

    public async ValueTask<TownL2DialogueInvocationOutcome> InvokeAsync(
        ConversationSession session,
        SemanticDialogueTurn sourceTurn,
        string actorVisibleText,
        SimTime now,
        CancellationToken cancellationToken,
        TownL2DialogueTrace? trace = null,
        string responseLanguage = "English")
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sourceTurn);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorVisibleText);
        DialogueResponseOpportunity opportunity = session.PendingResponseOpportunities.SingleOrDefault(value =>
            value.SourceActId == sourceTurn.Act.ActId)
            ?? throw new ArgumentException("The dialogue turn has no pending response opportunity.", nameof(sourceTurn));
        CanonicalHistoryEventRecord sourceEvent = ProjectDialogueEvent(session, sourceTurn, actorVisibleText, now);
        ActorId responder = opportunity.Recipient;
        LivingTownCurrentActivity currentActivity = _population.GetNpc(responder).GetCurrentActivity();
        string currentActivityText = currentActivity.ActivityRef is null
            ? $"Current activity: {currentActivity.Kind}."
            : $"Current activity: {currentActivity.Kind} at {currentActivity.ActivityRef}.";
        trace?.Invoke(responder, "queued", $"response need for {sourceEvent.SourceId.Value}", true);
        var problem = new TownL2DecisionProblem(
            $"dialogue/{session.SessionId.Value}/{sourceTurn.Sequence}",
            "social_dialogue_response",
            $"event/{sourceEvent.SourceId.Value}",
            null,
            [sourceTurn.Act.Speaker],
            [
                new TownL2CurrentEvidence(
                    $"current/dialogue/{sourceTurn.Act.ActId.Value}",
                    sourceEvent.SourceId.Value,
                    actorVisibleText),
                new TownL2CurrentEvidence(
                    $"current/activity/{responder.Value}",
                    $"runtime/activity/{responder.Value}/{now.Ticks}",
                    currentActivityText)
            ]);
        TownL2DecisionPreparationOutcome preparation = _context.Prepare(responder, problem, now);
        if (preparation is not TownL2PreparedDecision prepared)
        {
            trace?.Invoke(responder, "preparation-unavailable", "no actor-visible memory", false);
            return Abandon(session, opportunity, "No actor-visible memory is available for the dialogue response.");
        }
        TownL2MemoryPacketPreparation packetPreparation = _decisions.PrepareMemoryPacket(prepared);
        if (packetPreparation is TownL2MemoryPacketSummaryPending pending)
        {
            trace?.Invoke(responder, "preparation-unavailable", $"summary pending for {pending.CandidateSetId.Value}", false);
            return Abandon(session, opportunity, $"SummaryPending:{pending.CandidateSetId.Value}");
        }
        if (packetPreparation is TownL2MemoryPacketUnavailable unavailable)
        {
            trace?.Invoke(responder, "preparation-unavailable", unavailable.Reason, false);
            return Abandon(session, opportunity, unavailable.Reason);
        }
        TownL2MemoryPacketReady memory = (TownL2MemoryPacketReady)packetPreparation;
        trace?.Invoke(
            responder,
            "context-built",
            $"{prepared.CandidateSet.RankedSlices.Count} memories via {memory.Policy.Rq2Memory}",
            true);
        var requestId = new RemotePlannerRequestId(
            $"town-dialogue-{responder.Value}-{checked(Interlocked.Increment(ref _requestSequence))}");
        RemotePlannerRequest remote = RemotePlannerRequest.CreateTownDialogue(
            requestId,
            responder,
            problem.DecisionId,
            session.SessionId.Value,
            opportunity.OpportunityId.Value,
            sourceEvent.SourceId.Value,
            prepared.CandidateSet.CandidateSetId,
            memory.ModelVisibleBytes,
            responseLanguage);
        var request = new TownL2DialogueRequest(
            prepared, memory, remote, session, opportunity, sourceEvent);

        Interlocked.Increment(ref _inFlightCount);
        try
        {
            trace?.Invoke(responder, "provider-call-started", "calling configured live remote Provider", true);
            ModelClientResult<RemotePlannerResponse> result = await _client.InvokeAsync(remote, cancellationToken);
            if (result.Status == ModelClientResultStatus.Unavailable)
            {
                _conversations.AbandonResponse(session, opportunity);
                trace?.Invoke(responder, "provider-unavailable", $"{result.Mode}: {result.UnavailableReason}", false);
                return new TownL2DialogueProviderUnavailable(result.Mode, result.UnavailableReason!.Value);
            }
            RemotePlannerResponse response = result.Output!;
            if (response.Binding.RequestId != remote.Binding.RequestId
                || response.Decision is not RemotePlannerDialogueResponse dialogue)
            {
                _conversations.AbandonResponse(session, opportunity);
                trace?.Invoke(responder, "provider-rejected", response.Decision.GetType().Name, false);
                return new TownL2DialogueProviderRejected(response.Decision);
            }
            trace?.Invoke(
                responder,
                "validated",
                $"{dialogue.ReplyKind}/{dialogue.IncomingEffect}->{dialogue.ReplyEffect}/{dialogue.Intensity:0.00}",
                true);
            var reply = new SemanticDialogueAct(
                new SemanticDialogueActId($"{sourceTurn.Act.ActId.Value}-l2-reply-{responder.Value}"),
                MapReplyKind(dialogue.ReplyKind),
                responder,
                [sourceTurn.Act.Speaker],
                sourceTurn.Act.TopicRef,
                [],
                null,
                DialogueResponseExpectation.None);
            ConversationReplyResult replyResult = _conversations.Reply(session, opportunity, reply, now);
            SemanticDialogueTurn replyTurn = replyResult.Reply.RecordedTurn
                ?? throw new InvalidOperationException("Validated L2 dialogue reply was not committed.");
            _surface.RecordNpcTurn(
                dialogue.ReplyText, session, replyTurn, now, DialogueSurfaceRoute.L2);
            CanonicalHistoryEventRecord replyEvent = ProjectDialogueEvent(
                session, replyTurn, dialogue.ReplyText, now);
            TownSocialAppraisalResult incomingAppraisal = _appraisals.Apply(
                sourceEvent.SourceId.Value,
                responder,
                sourceTurn.Act.Speaker,
                MapEffect(dialogue.IncomingEffect),
                dialogue.Intensity);
            TownSocialAppraisalResult? replyAppraisal = _population.Npcs.Any(value =>
                value.ActorId == sourceTurn.Act.Speaker)
                ? _appraisals.Apply(
                    replyEvent.SourceId.Value,
                    sourceTurn.Act.Speaker,
                    responder,
                    MapEffect(dialogue.ReplyEffect),
                    dialogue.Intensity)
                : null;
            trace?.Invoke(
                responder,
                "committed",
                $"reply {replyTurn.Act.ActId.Value}; emotion {incomingAppraisal.Emotion.Kind}; social-impact {incomingAppraisal.Route}",
                true);
            return new TownL2DialogueSettled(
                request, result.ExecutionEvidence!, replyTurn, replyEvent, incomingAppraisal, replyAppraisal);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlightCount);
        }
    }

    private TownL2DialogueNotReady Abandon(
        ConversationSession session,
        DialogueResponseOpportunity opportunity,
        string reason)
    {
        _conversations.AbandonResponse(session, opportunity);
        return new TownL2DialogueNotReady(reason);
    }

    private CanonicalHistoryEventRecord ProjectDialogueEvent(
        ConversationSession session,
        SemanticDialogueTurn turn,
        string surfaceText,
        SimTime now)
    {
        WorldPosition position = ResolvePosition(turn.Act.Speaker);
        return _history.RecordRuntimeEvent(
            $"history/dialogue/{turn.Act.ActId.Value}",
            $"dialogue/{turn.Act.Kind}",
            now.Ticks,
            position,
            "outdoor",
            session.Participants,
            $"{turn.Act.Speaker.Value} said: {surfaceText}",
            [
                new CanonicalHistorySourceReference("dialogue-session", session.SessionId.Value),
                new CanonicalHistorySourceReference("dialogue-act", turn.Act.ActId.Value)
            ],
            CreatePresences()).Event;
    }

    private WorldPosition ResolvePosition(ActorId actorId) => actorId == _player.ActorId
        ? _player.ConfirmedPosition
        : _population.GetNpc(actorId).State.Position;

    private TownHistoryActorPresence[] CreatePresences()
    {
        var values = new List<TownHistoryActorPresence>
        {
            new(_player.ActorId, _player.ConfirmedPosition, "outdoor", null)
        };
        foreach (LivingTownNpcRuntime npc in _population.Npcs)
            values.Add(new TownHistoryActorPresence(npc.ActorId, npc.State.Position, "outdoor", npc.State.Memory));
        return values.ToArray();
    }

    private static SemanticDialogueActKind MapReplyKind(RemoteDialogueReplyKind kind) => kind switch
    {
        RemoteDialogueReplyKind.Ask => SemanticDialogueActKind.Ask,
        RemoteDialogueReplyKind.Inform => SemanticDialogueActKind.Inform,
        RemoteDialogueReplyKind.Clarify => SemanticDialogueActKind.Clarify,
        RemoteDialogueReplyKind.Request => SemanticDialogueActKind.Request,
        RemoteDialogueReplyKind.Offer => SemanticDialogueActKind.Offer,
        RemoteDialogueReplyKind.Recommend => SemanticDialogueActKind.Recommend,
        RemoteDialogueReplyKind.Accept => SemanticDialogueActKind.Accept,
        RemoteDialogueReplyKind.Decline => SemanticDialogueActKind.Decline,
        RemoteDialogueReplyKind.CounterOffer => SemanticDialogueActKind.CounterOffer,
        RemoteDialogueReplyKind.Warn => SemanticDialogueActKind.Warn,
        RemoteDialogueReplyKind.Apologize => SemanticDialogueActKind.Apologize,
        RemoteDialogueReplyKind.Thank => SemanticDialogueActKind.Thank,
        RemoteDialogueReplyKind.Complain => SemanticDialogueActKind.Complain,
        RemoteDialogueReplyKind.Tease => SemanticDialogueActKind.Tease,
        RemoteDialogueReplyKind.Comfort => SemanticDialogueActKind.Comfort,
        RemoteDialogueReplyKind.Congratulate => SemanticDialogueActKind.Congratulate,
        RemoteDialogueReplyKind.CasualComment => SemanticDialogueActKind.CasualComment,
        RemoteDialogueReplyKind.ShareNews => SemanticDialogueActKind.ShareNews,
        RemoteDialogueReplyKind.ShareGossip => SemanticDialogueActKind.ShareGossip,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static TownSocialEffectKind MapEffect(RemoteDialogueSocialEffectKind effect) => effect switch
    {
        RemoteDialogueSocialEffectKind.Neutral => TownSocialEffectKind.Neutral,
        RemoteDialogueSocialEffectKind.Support => TownSocialEffectKind.Support,
        RemoteDialogueSocialEffectKind.Harm => TownSocialEffectKind.Harm,
        RemoteDialogueSocialEffectKind.Promise => TownSocialEffectKind.Promise,
        RemoteDialogueSocialEffectKind.Breach => TownSocialEffectKind.Breach,
        RemoteDialogueSocialEffectKind.Threat => TownSocialEffectKind.Threat,
        RemoteDialogueSocialEffectKind.Apology => TownSocialEffectKind.Apology,
        RemoteDialogueSocialEffectKind.SharedInterest => TownSocialEffectKind.SharedInterest,
        _ => throw new ArgumentOutOfRangeException(nameof(effect))
    };
}
