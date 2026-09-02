using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Alice.Authority;
using Alice.Commitments;
using Alice.World;

namespace Alice.Cognition;

public sealed record AuthorityTargetAffectedNodeBinding
{
    public AuthorityTargetAffectedNodeBinding(TargetRef targetRef, AffectedNode affectedNode)
    {
        ArgumentNullException.ThrowIfNull(targetRef);
        ArgumentNullException.ThrowIfNull(affectedNode);
        TargetRef = targetRef;
        AffectedNode = affectedNode;
    }

    public TargetRef TargetRef { get; }
    public AffectedNode AffectedNode { get; }
}

/// <summary>Frozen typed mapping from Authority contract targets to shared dependency nodes.</summary>
public sealed class AuthorityTargetAffectedNodeIndex
{
    private readonly Dictionary<TargetRef, AffectedNode> _nodeByTarget;

    private AuthorityTargetAffectedNodeIndex(
        string bindingSetId,
        string contentHash,
        Dictionary<TargetRef, AffectedNode> nodeByTarget)
    {
        BindingSetId = bindingSetId;
        ContentHash = contentHash;
        _nodeByTarget = nodeByTarget;
    }

    public string BindingSetId { get; }
    public string ContentHash { get; }

    public static AuthorityTargetAffectedNodeIndex Create(
        string bindingSetId,
        IEnumerable<AuthorityTargetAffectedNodeBinding> bindings)
    {
        DependencyContractIdentity.Validate(bindingSetId, nameof(bindingSetId));
        ArgumentNullException.ThrowIfNull(bindings);
        var result = new Dictionary<TargetRef, AffectedNode>();
        foreach (AuthorityTargetAffectedNodeBinding? binding in bindings)
        {
            if (binding is null || !result.TryAdd(binding.TargetRef, binding.AffectedNode))
            {
                throw new ArgumentException("Target bindings must be non-null and unique by TargetRef.", nameof(bindings));
            }
        }

        return new AuthorityTargetAffectedNodeIndex(bindingSetId, Hash(bindingSetId, result), result);
    }

    internal AffectedNode Resolve(TargetRef targetRef)
    {
        return _nodeByTarget.TryGetValue(targetRef, out AffectedNode? node)
            ? node
            : throw new KeyNotFoundException("Authority TargetRef has no frozen affected-node binding: " + targetRef.Value);
    }

    private static string Hash(string bindingSetId, IReadOnlyDictionary<TargetRef, AffectedNode> bindings)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", "authority-target-affected-node-index-v1");
            writer.WriteString("binding_set_id", bindingSetId);
            writer.WritePropertyName("bindings");
            writer.WriteStartArray();
            foreach (KeyValuePair<TargetRef, AffectedNode> binding in bindings
                .OrderBy(item => item.Key.Value, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("target_ref", binding.Key.Value);
                writer.WriteString("affected_node", PressureAffectedNodeOrder.Key(binding.Value));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
}

/// <summary>One committed Authority source projected once for Event discovery and shared Pressure evaluation.</summary>
public sealed class AuthorityCommitProjection
{
    private readonly ReadOnlyCollection<AffectedNodeFact> _eventFacts;

    internal AuthorityCommitProjection(string sourceCommitId, IEnumerable<AffectedNode> affectedNodes)
    {
        DependencyContractIdentity.Validate(sourceCommitId, nameof(sourceCommitId));
        ArgumentNullException.ThrowIfNull(affectedNodes);
        AffectedNode[] nodes = affectedNodes.ToArray();
        if (nodes.Length == 0 || nodes.Any(IsNullNode) || nodes.Distinct().Count() != nodes.Length)
        {
            throw new ArgumentException(
                "An Authority commit projection requires non-empty unique affected nodes.",
                nameof(affectedNodes));
        }

        Array.Sort(nodes, AffectedNodeComparer.Instance);
        SourceCommitId = sourceCommitId;
        PressureSourceCommit = new PressureSourceCommit(sourceCommitId, nodes);
        var facts = new AffectedNodeFact[nodes.Length];
        for (int index = 0; index < nodes.Length; index++)
        {
            facts[index] = new AffectedNodeFact(
                DependencySourceKind.Event,
                sourceCommitId,
                nodes[index]);
        }

        _eventFacts = Array.AsReadOnly(facts);
    }

    public string SourceCommitId { get; }
    public PressureSourceCommit PressureSourceCommit { get; }
    public IReadOnlyList<AffectedNodeFact> EventFacts => _eventFacts;

    private static bool IsNullNode(AffectedNode node)
    {
        return node is null;
    }

    private sealed class AffectedNodeComparer : IComparer<AffectedNode>
    {
        public static AffectedNodeComparer Instance { get; } = new();

        public int Compare(AffectedNode? left, AffectedNode? right)
        {
            return StringComparer.Ordinal.Compare(
                PressureAffectedNodeOrder.Key(left!),
                PressureAffectedNodeOrder.Key(right!));
        }
    }
}

/// <summary>Closed projector for every Authority commit receipt currently present in the runtime.</summary>
public sealed class AuthorityCommitAffectedNodeProjector
{
    private const string PresenceNamespace = "presence_commit_v1";
    private const string DamageNamespace = "damage_commit_v1";
    private const string ConsumptionNamespace = "consumption_commit_v1";
    private const string PickupNamespace = "pickup_commit_v1";
    private const string WorldDropResourceNamespace = "world_drop";
    private readonly AuthorityTargetAffectedNodeIndex _targetIndex;

    public AuthorityCommitAffectedNodeProjector(AuthorityTargetAffectedNodeIndex targetIndex)
    {
        ArgumentNullException.ThrowIfNull(targetIndex);
        _targetIndex = targetIndex;
    }

    public string BindingContentHash => _targetIndex.ContentHash;

    public AuthorityCommitProjection Project(PresenceCommitReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new AuthorityCommitProjection(
            SourceId(PresenceNamespace, receipt.SourceActivityId.Value),
            [AffectedNode.FromCommitment(receipt.CommitmentId)]);
    }

    public AuthorityCommitProjection Project(DamageCommitReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var nodes = new List<AffectedNode> { _targetIndex.Resolve(receipt.ContractRef.TargetRef) };
        bool instrumentChanged = receipt.PreviousInstrumentDurability != receipt.CurrentInstrumentDurability
            || receipt.PreviousInstrumentVersion != receipt.CurrentInstrumentVersion;
        if (instrumentChanged)
        {
            nodes.Add(AffectedNode.FromActor(receipt.ActorId));
        }
        if (receipt.WorldDrop is not null)
        {
            nodes.Add(WorldDropNode(receipt.WorldDrop.DropId.Value));
        }

        return new AuthorityCommitProjection(
            SourceId(DamageNamespace, receipt.Origin.GameActionId.Value),
            nodes);
    }

    public AuthorityCommitProjection Project(ConsumptionCommitReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new AuthorityCommitProjection(
            SourceId(ConsumptionNamespace, receipt.Origin.GameActionId.Value),
            [AffectedNode.FromActor(receipt.ActorId)]);
    }

    public AuthorityCommitProjection Project(PickupCommitReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new AuthorityCommitProjection(
            SourceId(PickupNamespace, receipt.Origin.GameActionId.Value),
            [AffectedNode.FromActor(receipt.ActorId), WorldDropNode(receipt.WorldDropId.Value)]);
    }

    private static AffectedNode WorldDropNode(string worldDropId)
    {
        return AffectedNode.FromResource(new ResourceRef(
            string.Concat(WorldDropResourceNamespace, "/", worldDropId)));
    }

    private static string SourceId(string sourceNamespace, string identity)
    {
        return string.Concat(sourceNamespace, "/", identity);
    }
}

public enum AuthorityPressureCompositionOutcome
{
    Composed,
    AlreadyProcessed
}

/// <summary>Closed post-commit batch made visible only after shared Pressure evaluation has committed.</summary>
public sealed class AuthorityPressureDiscoveryBatch
{
    private readonly ReadOnlyCollection<AffectedNodeFact> _discoveryFacts;

    internal AuthorityPressureDiscoveryBatch(
        AuthorityPressureCompositionOutcome outcome,
        AuthorityCommitProjection projection,
        PressureSourceCompositionResult pressureResult,
        IEnumerable<AffectedNodeFact> discoveryFacts)
    {
        Outcome = outcome;
        Projection = projection;
        PressureResult = pressureResult;
        _discoveryFacts = Array.AsReadOnly(discoveryFacts.ToArray());
    }

    public AuthorityPressureCompositionOutcome Outcome { get; }
    public AuthorityCommitProjection Projection { get; }
    public PressureSourceCompositionResult PressureResult { get; }
    public IReadOnlyList<AffectedNodeFact> DiscoveryFacts => _discoveryFacts;
}

/// <summary>Enforces Authority commit → Pressure commit → next discovery-epoch visibility.</summary>
public sealed class AuthorityPressureEventCompositionRuntime
{
    private readonly object _sync = new();
    private readonly PressureWorldRuntime _pressureRuntime;

    public AuthorityPressureEventCompositionRuntime(PressureWorldRuntime pressureRuntime)
    {
        ArgumentNullException.ThrowIfNull(pressureRuntime);
        _pressureRuntime = pressureRuntime;
    }

    public PressureWorldRuntime PressureRuntime => _pressureRuntime;

    public AuthorityPressureDiscoveryBatch AfterAuthorityCommit(AuthorityCommitProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        lock (_sync)
        {
            PressureSourceCompositionResult pressureResult =
                _pressureRuntime.AfterAuthorityCommit(projection.PressureSourceCommit);
            if (pressureResult.Outcome == PressureSourceCompositionOutcome.AlreadyProcessed)
            {
                return new AuthorityPressureDiscoveryBatch(
                    AuthorityPressureCompositionOutcome.AlreadyProcessed,
                    projection,
                    pressureResult,
                    Array.Empty<AffectedNodeFact>());
            }

            var facts = new List<AffectedNodeFact>(
                projection.EventFacts.Count + pressureResult.ChangedPressureFacts.Count);
            facts.AddRange(projection.EventFacts);
            facts.AddRange(pressureResult.ChangedPressureFacts);
            return new AuthorityPressureDiscoveryBatch(
                AuthorityPressureCompositionOutcome.Composed,
                projection,
                pressureResult,
                facts);
        }
    }
}
