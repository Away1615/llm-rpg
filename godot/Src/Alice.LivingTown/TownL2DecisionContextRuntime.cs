using System.Collections.ObjectModel;
using System.Text.Json;
using Alice.Activities;
using Alice.Actors;
using Alice.Commitments;
using Alice.Interaction;
using Alice.Memory;
using Alice.Npc;
using Alice.ProductRuntime;

namespace Alice.LivingTown;

public sealed record TownL2DecisionProblem
{
    private readonly ReadOnlyCollection<ActorId> _involvedActors;
    private readonly ReadOnlyCollection<TownL2CurrentEvidence> _currentEvidence;

    public TownL2DecisionProblem(
        string decisionId,
        string kind,
        string subjectRef,
        string? targetId,
        IEnumerable<ActorId> involvedActors,
        IEnumerable<TownL2CurrentEvidence>? currentEvidence = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);
        ArgumentNullException.ThrowIfNull(involvedActors);
        ActorId[] actors = involvedActors.Distinct().OrderBy(value => value.Value, StringComparer.Ordinal).ToArray();
        TownL2CurrentEvidence[] evidence = currentEvidence?.ToArray() ?? [];
        if (evidence.Any(value => value is null))
            throw new ArgumentException("Current evidence entries must be non-null.", nameof(currentEvidence));
        DecisionId = decisionId;
        Kind = kind;
        SubjectRef = subjectRef;
        TargetId = targetId;
        _involvedActors = Array.AsReadOnly(actors);
        _currentEvidence = Array.AsReadOnly(evidence);
    }

    public string DecisionId { get; }
    public string Kind { get; }
    public string SubjectRef { get; }
    public string? TargetId { get; }
    public IReadOnlyList<ActorId> InvolvedActors => _involvedActors;
    public IReadOnlyList<TownL2CurrentEvidence> CurrentEvidence => _currentEvidence;
}

public sealed record TownL2AllowedProposal(
    string ProposalId,
    ActorExecutionMode ExecutionMode,
    ProductActionFamily ActionFamily,
    string TargetRef);

public sealed record TownL2CurrentEvidence(
    string EvidenceId,
    string SourceEventId,
    string ActorVisibleText);

public sealed class TownL2DecisionPreparationTrace
{
    internal TownL2DecisionPreparationTrace(
        DecisionMemoryCandidateSet candidateSet,
        IEnumerable<string> orderedMemoryIds,
        IEnumerable<string> coveredSourceIds,
        IEnumerable<string> currentEvidenceIds)
    {
        CandidateSetId = candidateSet.CandidateSetId.Value;
        OrderedMemoryIds = Snapshot(orderedMemoryIds);
        CoveredSourceIds = Snapshot(coveredSourceIds);
        CurrentEvidenceIds = Snapshot(currentEvidenceIds);
    }

    public string CandidateSetId { get; }
    public IReadOnlyList<string> OrderedMemoryIds { get; }
    public IReadOnlyList<string> CoveredSourceIds { get; }
    public IReadOnlyList<string> CurrentEvidenceIds { get; }

    private static IReadOnlyList<string> Snapshot(IEnumerable<string> values) =>
        new ReadOnlyCollection<string>(values.ToArray());
}

public abstract record TownL2DecisionPreparationOutcome
{
    private protected TownL2DecisionPreparationOutcome()
    {
    }
}

public sealed record TownL2NoActorVisibleMemory(ActorId ActorId) : TownL2DecisionPreparationOutcome;

public sealed record TownL2PreparedDecision : TownL2DecisionPreparationOutcome
{
    private readonly byte[] _sharedModelVisibleBytes;

    internal TownL2PreparedDecision(
        ActorId actorId,
        string? targetId,
        DecisionMemoryCandidateSet candidateSet,
        IEnumerable<TownL2AllowedProposal> allowedProposals,
        IEnumerable<TownL2CurrentEvidence> currentEvidence,
        TownL2DecisionPreparationTrace trace,
        byte[] sharedModelVisibleBytes)
    {
        ActorId = actorId;
        TargetId = targetId;
        CandidateSet = candidateSet;
        AllowedProposals = new ReadOnlyCollection<TownL2AllowedProposal>(allowedProposals.ToArray());
        CurrentEvidence = new ReadOnlyCollection<TownL2CurrentEvidence>(currentEvidence.ToArray());
        Trace = trace;
        _sharedModelVisibleBytes = sharedModelVisibleBytes.ToArray();
    }

    public ActorId ActorId { get; }
    public string? TargetId { get; }
    public DecisionMemoryCandidateSet CandidateSet { get; }
    public IReadOnlyList<TownL2AllowedProposal> AllowedProposals { get; }
    public IReadOnlyList<TownL2CurrentEvidence> CurrentEvidence { get; }
    public TownL2DecisionPreparationTrace Trace { get; }
    public byte[] GetSharedModelVisibleBytes() => _sharedModelVisibleBytes.ToArray();
}

/// <summary>Prepares one strategy-neutral, actor-visible Living Town L2 decision input.</summary>
public sealed class TownL2DecisionContextRuntime
{
    public const int CandidateLimit = 8;
    private static readonly DecisionMemoryKind MemoryKind = new("town_experience");
    private static readonly DecisionMemoryProjectorVersion Projector = new("town_l2_actor_visible");
    private readonly TownWorldConfiguration _world;
    private readonly TownSocialRuntime _social;
    private readonly RegionSocialGameplayRuntime _gameplay;

    private TownL2DecisionContextRuntime(
        TownWorldConfiguration world,
        LivingTownPopulationRuntime population,
        TownHistoryRuntime history,
        TownSocialRuntime social,
        RegionSocialGameplayRuntime gameplay)
    {
        _world = world;
        Population = population;
        History = history;
        _social = social;
        _gameplay = gameplay;
    }

    public LivingTownPopulationRuntime Population { get; }
    public TownHistoryRuntime History { get; }

    public static TownL2DecisionContextRuntime Create(
        TownWorldConfiguration world,
        LivingTownPopulationRuntime population,
        TownHistoryRuntime history,
        TownSocialRuntime social,
        RegionSocialGameplayRuntime gameplay)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(social);
        ArgumentNullException.ThrowIfNull(gameplay);
        return new TownL2DecisionContextRuntime(world, population, history, social, gameplay);
    }

    public TownL2DecisionPreparationOutcome Prepare(
        ActorId actorId,
        TownL2DecisionProblem problem,
        SimTime now)
    {
        ArgumentNullException.ThrowIfNull(problem);
        LivingTownNpcRuntime npc = Population.GetNpc(actorId);
        LivingTownMemorySeed[] memories = npc.State.Memory.Snapshot()
            .OrderBy(value => value.MemoryId, StringComparer.Ordinal).ToArray();
        if (memories.Length == 0) return new TownL2NoActorVisibleMemory(actorId);

        CueSet cues = BuildCues(npc, problem);
        CurrentEvidenceValue[] evidence = BuildCurrentEvidence(actorId, problem, now, cues).ToArray();
        AddEvidenceCues(cues, evidence);
        TownL2AllowedProposal[] proposals = BuildAllowedProposals(actorId, problem.TargetId, now).ToArray();
        CandidateValue[] candidates = ProjectCandidates(actorId, memories, cues, evidence).ToArray();
        CandidateValue[] ranked = candidates.OrderByDescending(value => value.DirectMatch)
            .ThenByDescending(value => value.MatchCount)
            .ThenByDescending(value => value.CausalEvidenceMatch)
            .ThenByDescending(value => value.Memory.OccurredAtTicks)
            .ThenBy(value => value.Memory.MemoryId, StringComparer.Ordinal)
            .Take(CandidateLimit)
            .ToArray();
        if (ranked.Length == 0) return new TownL2NoActorVisibleMemory(actorId);

        DecisionMemoryCandidateSet candidateSet = DecisionMemoryCandidateSet.Create(ranked.Select(value => value.Slice));
        TownL2CurrentEvidence[] visibleEvidence = evidence.Select(value => value.Visible).ToArray();
        byte[] envelope = SerializeEnvelope(npc, problem, proposals, visibleEvidence);
        var trace = new TownL2DecisionPreparationTrace(
            candidateSet,
            candidateSet.RankedSlices.Select(value => value.MemoryId.Value),
            candidateSet.SourceIds.Select(value => value.Value),
            visibleEvidence.Select(value => value.EvidenceId));
        return new TownL2PreparedDecision(actorId, problem.TargetId, candidateSet, proposals, visibleEvidence, trace, envelope);
    }

    private CueSet BuildCues(LivingTownNpcRuntime npc, TownL2DecisionProblem problem)
    {
        var cues = new CueSet();
        cues.Direct.Add(problem.SubjectRef);
        cues.All.Add(problem.SubjectRef);
        cues.All.Add($"actor/{npc.ActorId.Value}");
        LivingTownNpcProfile profile = npc.State.Profile;
        Add(cues.All, "settlement", profile.SettlementId);
        Add(cues.All, "household", profile.HouseholdId);
        Add(cues.All, "occupation", profile.OccupationId);
        Add(cues.All, "place", profile.Residence?.Value);
        Add(cues.All, "place", profile.Workplace?.Value);
        foreach (ActorId involved in problem.InvolvedActors)
        {
            string actorRef = $"actor/{involved.Value}";
            cues.Direct.Add(actorRef);
            cues.All.Add(actorRef);
            string relationship = TownSocialConfigurationValidator.PairKey(npc.ActorId.Value, involved.Value);
            cues.Direct.Add($"relationship/{relationship}");
            cues.All.Add($"relationship/{relationship}");
        }
        if (problem.TargetId is not null) AddTargetCues(cues, problem.TargetId);
        foreach (Commitment commitment in _social.Commitments)
        {
            if (commitment.Debtor != npc.ActorId && commitment.Creditor != npc.ActorId) continue;
            string commitmentRef = $"commitment/{commitment.CommitmentId.Value}";
            cues.All.Add(commitmentRef);
            if (StringComparer.Ordinal.Equals(problem.SubjectRef, commitmentRef)) cues.Direct.Add(commitmentRef);
            if (commitment.Term is CoinOrResourceTransferTerm transfer)
                cues.All.Add($"asset/{transfer.AssetRef.Value}");
        }
        return cues;
    }

    private void AddTargetCues(CueSet cues, string targetId)
    {
        string[] values =
        [
            $"resource/{targetId}", $"place/{targetId}", $"shop/{targetId}",
            $"road/{targetId}", $"bottleneck/{targetId}"
        ];
        foreach (string value in values)
        {
            cues.Direct.Add(value);
            cues.All.Add(value);
        }
        TownGameplayShopConfiguration? shop = _world.Gameplay.Shops.FirstOrDefault(value => value.PlaceId == targetId);
        if (shop is not null)
        {
            cues.Direct.Add($"shop/{shop.ShopId}");
            cues.All.Add($"shop/{shop.ShopId}");
        }
    }

    private IEnumerable<TownL2AllowedProposal> BuildAllowedProposals(
        ActorId actorId,
        string? targetId,
        SimTime now)
    {
        if (targetId is null) return [];
        return _gameplay.GetDecisionActionOffers(actorId, targetId, now)
            .Select(value => new TownL2AllowedProposal(
                value.EntryId,
                ActorExecutionMode.Interact,
                ResolveFamily(value.Selection.Arguments),
                value.Selection.Binding.ContractRef.TargetRef.Value))
            .OrderBy(value => value.ProposalId, StringComparer.Ordinal)
            .ToArray();
    }

    private IEnumerable<CurrentEvidenceValue> BuildCurrentEvidence(
        ActorId actorId,
        TownL2DecisionProblem problem,
        SimTime now,
        CueSet cues)
    {
        var result = new List<CurrentEvidenceValue>();
        foreach (TownL2CurrentEvidence item in problem.CurrentEvidence)
        {
            result.Add(new CurrentEvidenceValue(
                item,
                [problem.SubjectRef, $"event/{item.SourceEventId}", $"evidence/{item.EvidenceId}"]));
        }
        _ = actorId;
        _ = now;
        foreach (Commitment commitment in _social.Commitments)
        {
            string commitmentRef = $"commitment/{commitment.CommitmentId.Value}";
            if (!StringComparer.Ordinal.Equals(problem.SubjectRef, commitmentRef)) continue;
            string evidenceId = $"current/commitment/{commitment.CommitmentId.Value}/{commitment.Status}";
            string sourceId = commitment.SourceRef.CanonicalEventId ?? evidenceId;
            result.Add(new CurrentEvidenceValue(
                new TownL2CurrentEvidence(
                    evidenceId,
                    sourceId,
                    $"Transfer obligation {commitment.CommitmentId.Value} is {commitment.Status}."),
                [commitmentRef, $"event/{sourceId}"]));
        }
        return result;
    }

    private static void AddEvidenceCues(CueSet cues, IEnumerable<CurrentEvidenceValue> evidence)
    {
        foreach (CurrentEvidenceValue value in evidence)
        {
            foreach (string reference in value.ReferenceIds)
            {
                cues.Direct.Add(reference);
                cues.All.Add(reference);
            }
        }
    }

    private IEnumerable<CandidateValue> ProjectCandidates(
        ActorId actorId,
        IReadOnlyList<LivingTownMemorySeed> memories,
        CueSet cues,
        IReadOnlyList<CurrentEvidenceValue> evidence)
    {
        for (int index = 0; index < memories.Count; index++)
        {
            LivingTownMemorySeed memory = memories[index];
            CanonicalHistoryEventRecord source = History.Events.SingleOrDefault(value =>
                value.SourceId.Value == memory.SourceEventId.Value)
                ?? throw new InvalidOperationException($"Memory source '{memory.SourceEventId.Value}' is absent.");
            CanonicalHistoryExperience experience = source.Experiences.SingleOrDefault(value => value.ActorId == actorId)
                ?? throw new InvalidOperationException("Actor-local memory lacks an admitted Experience.");
            CanonicalHistoryActorVisibleFact fact = source.ActorVisibleFacts.SingleOrDefault(value => value.ActorId == actorId)
                ?? throw new InvalidOperationException("Actor-local memory lacks an actor-visible fact.");
            if (!StringComparer.Ordinal.Equals(memory.ActorVisibleText, fact.Text))
                throw new InvalidOperationException("Actor-local memory does not match its visible canonical fact.");

            HashSet<string> references = CandidateReferences(source);
            int directMatches = references.Intersect(cues.Direct, StringComparer.Ordinal).Count();
            int matches = references.Intersect(cues.All, StringComparer.Ordinal).Count();
            bool causal = evidence.Any(value => value.ReferenceIds.Intersect(references, StringComparer.Ordinal).Any());
            byte[] actorVisibleBytes = SerializeActorVisibleSource(source, experience, fact);
            DecisionMemorySlice slice = DecisionMemorySlice.Create(
                actorId,
                MemoryKind,
                new SimTime(memory.OccurredAtTicks),
                Projector,
                index,
                DecisionMemoryEvidenceStatus.Current,
                [new DecisionMemorySourceId(source.SourceId.Value)],
                [],
                [],
                actorVisibleBytes);
            yield return new CandidateValue(memory, slice, directMatches > 0, matches, causal);
        }
    }

    private static HashSet<string> CandidateReferences(CanonicalHistoryEventRecord source)
    {
        var references = new HashSet<string>(StringComparer.Ordinal)
        {
            $"event/{source.SourceId.Value}",
            $"event-kind/{source.EventKind}",
            $"place/{source.LocationId}"
        };
        foreach (CanonicalHistorySourceReference reference in source.SourceReferences)
            references.Add($"{reference.Kind}/{reference.Value}");
        foreach (CanonicalHistoryExperience experience in source.Experiences)
            references.Add($"actor/{experience.ActorId.Value}");
        return references;
    }

    private static byte[] SerializeActorVisibleSource(
        CanonicalHistoryEventRecord source,
        CanonicalHistoryExperience experience,
        CanonicalHistoryActorVisibleFact fact)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("source_event_id", source.SourceId.Value);
            writer.WriteNumber("occurred_at_ticks", source.OccurredAtTicks);
            writer.WriteString("experience_role", experience.Role.ToString());
            writer.WriteString("actor_visible_fact", fact.Text);
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static byte[] SerializeEnvelope(
        LivingTownNpcRuntime npc,
        TownL2DecisionProblem problem,
        IReadOnlyList<TownL2AllowedProposal> proposals,
        IReadOnlyList<TownL2CurrentEvidence> evidence)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("actor");
            writer.WriteStartObject();
            writer.WriteString("actor_id", npc.ActorId.Value);
            writer.WriteString("name", npc.State.Profile.DisplayName);
            writer.WriteEndObject();
            WritePersonality(writer, npc.State.NpcState.Personality);
            writer.WritePropertyName("current_emotion");
            writer.WriteStartObject();
            writer.WriteString("kind", npc.State.CurrentEmotion.Kind.ToString());
            writer.WriteNumber("valence", npc.State.CurrentEmotion.Valence);
            writer.WriteNumber("intensity", npc.State.CurrentEmotion.Intensity);
            if (npc.State.CurrentEmotion.SourceEventId is null) writer.WriteNull("source_event_id");
            else writer.WriteString("source_event_id", npc.State.CurrentEmotion.SourceEventId.Value);
            writer.WriteEndObject();
            writer.WritePropertyName("current_problem");
            writer.WriteStartObject();
            writer.WriteString("decision_id", problem.DecisionId);
            writer.WriteString("kind", problem.Kind);
            writer.WriteString("subject_ref", problem.SubjectRef);
            if (problem.TargetId is null) writer.WriteNull("target_id");
            else writer.WriteString("target_id", problem.TargetId);
            writer.WriteEndObject();
            writer.WritePropertyName("allowed_proposals");
            writer.WriteStartArray();
            foreach (TownL2AllowedProposal proposal in proposals)
            {
                writer.WriteStartObject();
                writer.WriteString("proposal_id", proposal.ProposalId);
                writer.WriteString("execution_mode", proposal.ExecutionMode.ToString());
                writer.WriteString("action_family", proposal.ActionFamily.ToString());
                writer.WriteString("target_ref", proposal.TargetRef);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("current_evidence");
            writer.WriteStartArray();
            foreach (TownL2CurrentEvidence item in evidence)
            {
                writer.WriteStartObject();
                writer.WriteString("evidence_id", item.EvidenceId);
                writer.WriteString("source_event_id", item.SourceEventId);
                writer.WriteString("text", item.ActorVisibleText);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static void WritePersonality(Utf8JsonWriter writer, NpcPersonalityState personality)
    {
        writer.WritePropertyName("personality");
        writer.WriteStartObject();
        writer.WritePropertyName("cognitive_functions");
        writer.WriteStartObject();
        writer.WriteNumber("se", personality.CognitiveStyle.Se);
        writer.WriteNumber("si", personality.CognitiveStyle.Si);
        writer.WriteNumber("ne", personality.CognitiveStyle.Ne);
        writer.WriteNumber("ni", personality.CognitiveStyle.Ni);
        writer.WriteNumber("te", personality.CognitiveStyle.Te);
        writer.WriteNumber("ti", personality.CognitiveStyle.Ti);
        writer.WriteNumber("fe", personality.CognitiveStyle.Fe);
        writer.WriteNumber("fi", personality.CognitiveStyle.Fi);
        writer.WriteEndObject();
        writer.WritePropertyName("traits");
        writer.WriteStartArray();
        foreach (PersonalityTagId trait in personality.Traits) writer.WriteStringValue(trait.Value);
        writer.WriteEndArray();
        writer.WritePropertyName("weighted_values");
        writer.WriteStartArray();
        foreach (WeightedPersonalityValue value in personality.Values)
        {
            writer.WriteStartObject();
            writer.WriteString("value_id", value.ValueIdentity.Value);
            writer.WriteNumber("weight", value.Weight);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static ProductActionFamily ResolveFamily(GameActionArguments arguments) => arguments switch
    {
        RegionOperationActionArguments => ProductActionFamily.RegionOperation,
        CraftActionArguments => ProductActionFamily.Craft,
        ListedExchangeActionArguments => ProductActionFamily.ListedExchange,
        ServiceExchangeActionArguments => ProductActionFamily.ServiceExchange,
        AssetTransferActionArguments => ProductActionFamily.AssetTransfer,
        PlaceStateChangeActionArguments => ProductActionFamily.PlaceStateChange,
        EquipmentChangeActionArguments => ProductActionFamily.EquipmentChange,
        ConsumptionActionArguments => ProductActionFamily.Consumption,
        RestActionArguments => ProductActionFamily.Rest,
        _ => throw new InvalidOperationException("Gameplay offer is outside the bounded product action families.")
    };

    private static void Add(HashSet<string> destination, string kind, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) destination.Add($"{kind}/{value}");
    }

    private sealed class CueSet
    {
        public HashSet<string> Direct { get; } = new(StringComparer.Ordinal);
        public HashSet<string> All { get; } = new(StringComparer.Ordinal);
    }

    private sealed record CurrentEvidenceValue(TownL2CurrentEvidence Visible, IReadOnlyList<string> ReferenceIds);

    private sealed record CandidateValue(
        LivingTownMemorySeed Memory,
        DecisionMemorySlice Slice,
        bool DirectMatch,
        int MatchCount,
        bool CausalEvidenceMatch);
}
