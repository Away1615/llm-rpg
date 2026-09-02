using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace Alice.Cognition;

public readonly record struct PressureId
{
    public PressureId(string value)
    {
        DependencyContractIdentity.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public readonly record struct PressureProfileId
{
    public PressureProfileId(string value)
    {
        DependencyContractIdentity.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

/// <summary>Versioned opaque deterministic pressure state; its semantics belong to the external evaluator profile.</summary>
public sealed class PressureState
{
    private readonly byte[] _canonicalBytes;

    public PressureState(
        PressureId pressureId,
        PressureProfileId profileId,
        long profileVersion,
        string evaluatorContentHash,
        ReadOnlySpan<byte> canonicalBytes)
    {
        if (profileVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(profileVersion));
        }

        if (canonicalBytes.IsEmpty)
        {
            throw new ArgumentException("Canonical pressure-state bytes cannot be empty.", nameof(canonicalBytes));
        }

        PressureId = pressureId;
        ProfileId = profileId;
        ProfileVersion = profileVersion;
        EvaluatorContentHash = L2PlanningContextCanonicalJson.ValidateSha256(
            evaluatorContentHash,
            nameof(evaluatorContentHash));
        _canonicalBytes = canonicalBytes.ToArray();
        StateHash = Convert.ToHexString(SHA256.HashData(_canonicalBytes)).ToLowerInvariant();
    }

    public PressureId PressureId { get; }
    public PressureProfileId ProfileId { get; }
    public long ProfileVersion { get; }
    public string EvaluatorContentHash { get; }
    public string StateHash { get; }
    public ReadOnlyMemory<byte> CanonicalBytes => _canonicalBytes;
}

/// <summary>One post-Authority-commit source projection. Pressure refs are structurally unavailable here.</summary>
public sealed class PressureSourceCommit
{
    private readonly ReadOnlyCollection<AffectedNode> _affectedNodes;

    public PressureSourceCommit(
        string sourceCommitId,
        IEnumerable<AffectedNode> affectedNodes)
    {
        DependencyContractIdentity.Validate(sourceCommitId, nameof(sourceCommitId));
        ArgumentNullException.ThrowIfNull(affectedNodes);
        AffectedNode[] snapshot = affectedNodes.ToArray();
        if (snapshot.Any(node => node is null))
        {
            throw new ArgumentException("Pressure source nodes cannot contain null.", nameof(affectedNodes));
        }

        if (snapshot.Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException("Pressure source nodes cannot contain duplicates.", nameof(affectedNodes));
        }

        SourceCommitId = sourceCommitId;
        _affectedNodes = Array.AsReadOnly(snapshot
            .OrderBy(PressureAffectedNodeOrder.Key, StringComparer.Ordinal)
            .ToArray());
    }

    public string SourceCommitId { get; }
    public IReadOnlyList<AffectedNode> AffectedNodes => _affectedNodes;
}

public sealed record PressureDependency
{
    public PressureDependency(AffectedNode affectedNode, PressureId pressureId)
    {
        ArgumentNullException.ThrowIfNull(affectedNode);
        AffectedNode = affectedNode;
        PressureId = pressureId;
    }

    public AffectedNode AffectedNode { get; }
    public PressureId PressureId { get; }
}

/// <summary>A dedicated shared-world reverse index, separate from the EventCentric treatment index.</summary>
public sealed class PressureDependencyIndex
{
    private readonly Dictionary<AffectedNode, PressureId[]> _pressureIdsByAffectedNode;

    private PressureDependencyIndex(
        string version,
        string contentHash,
        Dictionary<AffectedNode, PressureId[]> pressureIdsByAffectedNode)
    {
        Version = version;
        ContentHash = contentHash;
        _pressureIdsByAffectedNode = pressureIdsByAffectedNode;
    }

    public string Version { get; }
    public string ContentHash { get; }

    public static PressureDependencyIndex Create(
        string version,
        IEnumerable<PressureDependency> dependencies)
    {
        DependencyContractIdentity.Validate(version, nameof(version));
        ArgumentNullException.ThrowIfNull(dependencies);
        var unique = new HashSet<PressureDependency>();
        var grouped = new Dictionary<AffectedNode, List<PressureId>>();
        foreach (PressureDependency? dependency in dependencies)
        {
            if (dependency is null)
            {
                throw new ArgumentException("Pressure dependencies cannot contain null.", nameof(dependencies));
            }

            if (!unique.Add(dependency))
            {
                throw new ArgumentException("Pressure dependencies cannot contain duplicates.", nameof(dependencies));
            }

            if (!grouped.TryGetValue(dependency.AffectedNode, out List<PressureId>? ids))
            {
                ids = [];
                grouped.Add(dependency.AffectedNode, ids);
            }

            ids.Add(dependency.PressureId);
        }

        var snapshot = new Dictionary<AffectedNode, PressureId[]>(grouped.Count);
        foreach (KeyValuePair<AffectedNode, List<PressureId>> item in grouped)
        {
            snapshot.Add(
                item.Key,
                item.Value.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray());
        }

        return new PressureDependencyIndex(version, HashDependencies(version, unique), snapshot);
    }

    internal IReadOnlyList<PressureId> FindAffected(PressureSourceCommit sourceCommit)
    {
        var ids = new HashSet<PressureId>();
        foreach (AffectedNode node in sourceCommit.AffectedNodes)
        {
            if (_pressureIdsByAffectedNode.TryGetValue(node, out PressureId[]? matching))
            {
                ids.UnionWith(matching);
            }
        }

        return Array.AsReadOnly(ids.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray());
    }

    private static string HashDependencies(string version, IEnumerable<PressureDependency> dependencies)
    {
        PressureDependency[] ordered = dependencies
            .OrderBy(dependency => PressureAffectedNodeOrder.Key(dependency.AffectedNode), StringComparer.Ordinal)
            .ThenBy(dependency => dependency.PressureId.Value, StringComparer.Ordinal)
            .ToArray();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", "pressure-dependency-index-v1");
            writer.WriteString("index_version", version);
            writer.WritePropertyName("dependencies");
            writer.WriteStartArray();
            foreach (PressureDependency dependency in ordered)
            {
                writer.WriteStartObject();
                writer.WriteString("affected_node", PressureAffectedNodeOrder.Key(dependency.AffectedNode));
                writer.WriteString("pressure_id", dependency.PressureId.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
}

public sealed class PressureEvaluation
{
    private readonly ReadOnlyCollection<AffectedNode> _changedAffectedNodes;

    public PressureEvaluation(
        PressureState nextState,
        IEnumerable<AffectedNode> changedAffectedNodes)
    {
        ArgumentNullException.ThrowIfNull(nextState);
        ArgumentNullException.ThrowIfNull(changedAffectedNodes);
        AffectedNode[] nodes = changedAffectedNodes.ToArray();
        if (nodes.Any(node => node is null))
        {
            throw new ArgumentException("Pressure output nodes cannot contain null.", nameof(changedAffectedNodes));
        }

        if (nodes.Distinct().Count() != nodes.Length)
        {
            throw new ArgumentException("Pressure output nodes cannot contain duplicates.", nameof(changedAffectedNodes));
        }

        NextState = nextState;
        _changedAffectedNodes = Array.AsReadOnly(nodes
            .OrderBy(PressureAffectedNodeOrder.Key, StringComparer.Ordinal)
            .ToArray());
    }

    public PressureState NextState { get; }
    public IReadOnlyList<AffectedNode> ChangedAffectedNodes => _changedAffectedNodes;
}

public interface IPressureEvaluator
{
    PressureId PressureId { get; }
    PressureProfileId ProfileId { get; }
    long ProfileVersion { get; }
    string EvaluatorContentHash { get; }
    PressureEvaluation Evaluate(PressureState currentState, PressureSourceCommit sourceCommit);
}

public enum PressureSourceCompositionOutcome
{
    Evaluated,
    AlreadyProcessed
}

/// <summary>Committed shared-world evidence for one successful pressure-state replacement.</summary>
public sealed record PressureStateChangeReceipt
{
    public PressureStateChangeReceipt(
        string sourceCommitId,
        PressureState previousState,
        PressureState currentState)
    {
        DependencyContractIdentity.Validate(sourceCommitId, nameof(sourceCommitId));
        ArgumentNullException.ThrowIfNull(previousState);
        ArgumentNullException.ThrowIfNull(currentState);
        if (previousState.PressureId != currentState.PressureId
            || previousState.ProfileId != currentState.ProfileId
            || previousState.ProfileVersion != currentState.ProfileVersion
            || !StringComparer.Ordinal.Equals(
                previousState.EvaluatorContentHash,
                currentState.EvaluatorContentHash)
            || previousState.CanonicalBytes.Span.SequenceEqual(currentState.CanonicalBytes.Span))
        {
            throw new ArgumentException("A pressure-state change receipt requires one changed profile-identical state.");
        }

        SourceCommitId = sourceCommitId;
        PreviousState = previousState;
        CurrentState = currentState;
    }

    public string SourceCommitId { get; }
    public PressureState PreviousState { get; }
    public PressureState CurrentState { get; }
    public PressureId PressureId => CurrentState.PressureId;
}

public sealed class PressureSourceCompositionResult
{
    private readonly ReadOnlyCollection<PressureId> _evaluatedPressureIds;
    private readonly ReadOnlyCollection<PressureStateChangeReceipt> _stateChangeReceipts;
    private readonly ReadOnlyCollection<AffectedNodeFact> _changedPressureFacts;

    internal PressureSourceCompositionResult(
        PressureSourceCompositionOutcome outcome,
        IEnumerable<PressureId> evaluatedPressureIds,
        IEnumerable<PressureStateChangeReceipt> stateChangeReceipts,
        IEnumerable<AffectedNodeFact> changedPressureFacts)
    {
        Outcome = outcome;
        _evaluatedPressureIds = Array.AsReadOnly(evaluatedPressureIds.ToArray());
        _stateChangeReceipts = Array.AsReadOnly(stateChangeReceipts.ToArray());
        _changedPressureFacts = Array.AsReadOnly(changedPressureFacts.ToArray());
    }

    public PressureSourceCompositionOutcome Outcome { get; }
    public IReadOnlyList<PressureId> EvaluatedPressureIds => _evaluatedPressureIds;
    public IReadOnlyList<PressureStateChangeReceipt> StateChangeReceipts => _stateChangeReceipts;
    public IReadOnlyList<AffectedNodeFact> ChangedPressureFacts => _changedPressureFacts;
}

/// <summary>
/// Evaluates dedicated shared-world pressures once per Authority source commit, before the caller starts
/// the next discovery epoch. Evaluation is atomic and ordered by PressureId.
/// </summary>
public sealed class PressureWorldRuntime
{
    private readonly object _sync = new();
    private readonly PressureDependencyIndex _index;
    private readonly Dictionary<PressureId, PressureState> _states;
    private readonly Dictionary<PressureId, PressureState> _initialStates;
    private readonly Dictionary<PressureId, IPressureEvaluator> _evaluators;
    private readonly Dictionary<string, AffectedNode[]> _processedSources = new(StringComparer.Ordinal);

    public PressureWorldRuntime(
        string evaluatorHostVersion,
        PressureDependencyIndex index,
        IEnumerable<PressureState> initialStates,
        IEnumerable<IPressureEvaluator> evaluators)
    {
        DependencyContractIdentity.Validate(evaluatorHostVersion, nameof(evaluatorHostVersion));
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(initialStates);
        ArgumentNullException.ThrowIfNull(evaluators);
        _index = index;
        _states = UniqueByPressureId(initialStates, nameof(initialStates));
        _initialStates = new Dictionary<PressureId, PressureState>(_states);
        _evaluators = UniqueByPressureId(evaluators, nameof(evaluators));
        if (!_states.Keys.ToHashSet().SetEquals(_evaluators.Keys))
        {
            throw new ArgumentException("Initial pressure states and evaluators must have identical PressureIds.");
        }

        foreach (PressureId id in _states.Keys)
        {
            ValidateEvaluatorIdentity(_states[id], _evaluators[id]);
        }

        EvaluatorHostVersion = evaluatorHostVersion;
    }

    public string EvaluatorHostVersion { get; }
    public string DependencyIndexVersion => _index.Version;
    public string DependencyIndexContentHash => _index.ContentHash;
    public long IndexLookupCount { get; private set; }
    public long EvaluationCount { get; private set; }
    public long StateChangeCount { get; private set; }

    public FormalRq1PressureManifest CreateManifest()
    {
        lock (_sync)
        {
            var profiles = _initialStates.Values
                .GroupBy(state => state.ProfileId)
                .Select(CreateProfileManifestEntry)
                .ToArray();
            var states = _initialStates.Values
                .Select(state => new FormalRq1PressureStateManifestEntry(
                    state.PressureId,
                    state.ProfileId,
                    state.ProfileVersion,
                    state.EvaluatorContentHash,
                    state.StateHash))
                .ToArray();
            return new FormalRq1PressureManifest(
                EvaluatorHostVersion,
                DependencyIndexVersion,
                DependencyIndexContentHash,
                profiles,
                states);
        }
    }

    public PressureState GetState(PressureId pressureId)
    {
        lock (_sync)
        {
            return _states.TryGetValue(pressureId, out PressureState? state)
                ? state
                : throw new KeyNotFoundException("Unknown PressureId: " + pressureId.Value);
        }
    }

    public PressureSourceCompositionResult AfterAuthorityCommit(PressureSourceCommit sourceCommit)
    {
        ArgumentNullException.ThrowIfNull(sourceCommit);
        lock (_sync)
        {
            if (_processedSources.TryGetValue(sourceCommit.SourceCommitId, out AffectedNode[]? priorNodes))
            {
                if (!priorNodes.SequenceEqual(sourceCommit.AffectedNodes))
                {
                    throw new InvalidOperationException(
                        "A processed pressure source identity cannot be replayed with different affected nodes.");
                }

                return new PressureSourceCompositionResult(
                    PressureSourceCompositionOutcome.AlreadyProcessed,
                    Array.Empty<PressureId>(),
                    Array.Empty<PressureStateChangeReceipt>(),
                    Array.Empty<AffectedNodeFact>());
            }

            IReadOnlyList<PressureId> affectedIds = _index.FindAffected(sourceCommit);
            IndexLookupCount = checked(IndexLookupCount + sourceCommit.AffectedNodes.Count);
            var evaluations = new List<PressureEvaluation>(affectedIds.Count);
            foreach (PressureId id in affectedIds)
            {
                if (!_states.TryGetValue(id, out PressureState? current)
                    || !_evaluators.TryGetValue(id, out IPressureEvaluator? evaluator))
                {
                    throw new InvalidOperationException("The pressure index references an uncompiled PressureId: " + id.Value);
                }

                PressureEvaluation evaluation = evaluator.Evaluate(current, sourceCommit)
                    ?? throw new InvalidOperationException("A pressure evaluator returned null: " + id.Value);
                ValidateEvaluation(current, evaluator, evaluation);
                evaluations.Add(evaluation);
            }

            var changedFacts = new List<AffectedNodeFact>();
            var changedStates = new List<PressureState>();
            var changeReceipts = new List<PressureStateChangeReceipt>();
            for (int index = 0; index < affectedIds.Count; index++)
            {
                PressureId id = affectedIds[index];
                PressureEvaluation evaluation = evaluations[index];
                PressureState current = _states[id];
                bool changed = !current.CanonicalBytes.Span.SequenceEqual(evaluation.NextState.CanonicalBytes.Span);
                if (!changed)
                {
                    continue;
                }

                changedStates.Add(evaluation.NextState);
                changeReceipts.Add(new PressureStateChangeReceipt(
                    sourceCommit.SourceCommitId,
                    current,
                    evaluation.NextState));
                foreach (AffectedNode node in evaluation.ChangedAffectedNodes)
                {
                    changedFacts.Add(new AffectedNodeFact(DependencySourceKind.Pressure, id.Value, node));
                }
            }

            long nextEvaluationCount = checked(EvaluationCount + affectedIds.Count);
            long nextStateChangeCount = checked(StateChangeCount + changedStates.Count);
            foreach (PressureState changedState in changedStates)
            {
                _states[changedState.PressureId] = changedState;
            }

            EvaluationCount = nextEvaluationCount;
            StateChangeCount = nextStateChangeCount;
            _processedSources.Add(sourceCommit.SourceCommitId, sourceCommit.AffectedNodes.ToArray());
            return new PressureSourceCompositionResult(
                PressureSourceCompositionOutcome.Evaluated,
                affectedIds,
                changeReceipts,
                changedFacts);
        }
    }

    private static Dictionary<PressureId, PressureState> UniqueByPressureId(
        IEnumerable<PressureState> states,
        string parameterName)
    {
        var result = new Dictionary<PressureId, PressureState>();
        foreach (PressureState? state in states)
        {
            if (state is null || !result.TryAdd(state.PressureId, state))
            {
                throw new ArgumentException("Pressure states must be non-null and unique by PressureId.", parameterName);
            }
        }

        return result;
    }

    private static FormalRq1PressureProfileManifestEntry CreateProfileManifestEntry(
        IGrouping<PressureProfileId, PressureState> group)
    {
        PressureState first = group.First();
        if (group.Any(state => state.ProfileVersion != first.ProfileVersion
            || !StringComparer.Ordinal.Equals(state.EvaluatorContentHash, first.EvaluatorContentHash)))
        {
            throw new InvalidOperationException("One PressureProfileId cannot identify multiple evaluator profiles.");
        }

        return new FormalRq1PressureProfileManifestEntry(
            first.ProfileId,
            first.ProfileVersion,
            first.EvaluatorContentHash);
    }

    private static Dictionary<PressureId, IPressureEvaluator> UniqueByPressureId(
        IEnumerable<IPressureEvaluator> evaluators,
        string parameterName)
    {
        var result = new Dictionary<PressureId, IPressureEvaluator>();
        foreach (IPressureEvaluator? evaluator in evaluators)
        {
            if (evaluator is null || !result.TryAdd(evaluator.PressureId, evaluator))
            {
                throw new ArgumentException("Pressure evaluators must be non-null and unique by PressureId.", parameterName);
            }
        }

        return result;
    }

    private static void ValidateEvaluatorIdentity(PressureState state, IPressureEvaluator evaluator)
    {
        if (state.PressureId != evaluator.PressureId
            || state.ProfileId != evaluator.ProfileId
            || state.ProfileVersion != evaluator.ProfileVersion
            || !StringComparer.Ordinal.Equals(state.EvaluatorContentHash, evaluator.EvaluatorContentHash))
        {
            throw new ArgumentException("Pressure state and evaluator profile identity must match exactly.");
        }
    }

    private static void ValidateEvaluation(
        PressureState current,
        IPressureEvaluator evaluator,
        PressureEvaluation evaluation)
    {
        ValidateEvaluatorIdentity(evaluation.NextState, evaluator);
        bool changed = !current.CanonicalBytes.Span.SequenceEqual(evaluation.NextState.CanonicalBytes.Span);
        if (!changed && evaluation.ChangedAffectedNodes.Count != 0)
        {
            throw new InvalidOperationException("An unchanged pressure state cannot emit changed source facts.");
        }
    }
}

internal static class PressureAffectedNodeOrder
{
    public static string Key(AffectedNode node)
    {
        if (node.PlaceRef is { } place) return "place/" + place.Value;
        if (node.ResourceRef is { } resource) return "resource/" + resource.Value;
        if (node.CommitmentId is { } commitment) return "commitment/" + commitment.Value;
        if (node.ActorId is { } actor) return "actor/" + actor.Value;
        return "duty/" + node.DutyRef!.Value.Value;
    }
}
