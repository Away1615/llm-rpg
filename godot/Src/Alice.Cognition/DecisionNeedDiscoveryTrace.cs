using System.Collections.ObjectModel;

namespace Alice.Cognition;

public enum DecisionNeedDiscoveryRoute
{
    HostRuntime,
    AgentCentric,
    EventCentric,
    MandatoryResponse
}

public sealed record DecisionNeedDiscoverySourceId
{
    public DecisionNeedDiscoverySourceId(string value)
    {
        Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }

    private static void Validate(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Discovery source identity must be non-empty.", parameterName);
        }
    }
}

public sealed record DecisionNeedDiscoveryNodeId
{
    public DecisionNeedDiscoveryNodeId(string value)
    {
        Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }

    private static void Validate(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Discovery node identity must be non-empty.", parameterName);
        }
    }
}

public sealed class DecisionNeedDiscoveryTrace
{
    private readonly ReadOnlyCollection<DecisionNeedDiscoveryNodeId> _nodeIds;

    public DecisionNeedDiscoveryTrace(
        DecisionNeedDiscoveryRoute route,
        DecisionNeedDiscoverySourceId sourceId,
        IEnumerable<DecisionNeedDiscoveryNodeId> nodeIds)
    {
        if (!Enum.IsDefined(route))
        {
            throw new ArgumentOutOfRangeException(nameof(route));
        }

        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(nodeIds);
        DecisionNeedDiscoveryNodeId[] snapshot = nodeIds.ToArray();
        foreach (DecisionNeedDiscoveryNodeId nodeId in snapshot)
        {
            ArgumentNullException.ThrowIfNull(nodeId);
        }

        Array.Sort(snapshot, DecisionNeedDiscoveryNodeComparer.Instance);
        for (int index = 1; index < snapshot.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(snapshot[index - 1].Value, snapshot[index].Value))
            {
                throw new ArgumentException("Discovery node identities must be unique.", nameof(nodeIds));
            }
        }

        Route = route;
        SourceId = sourceId;
        _nodeIds = Array.AsReadOnly(snapshot);
    }

    public DecisionNeedDiscoveryRoute Route { get; }
    public DecisionNeedDiscoverySourceId SourceId { get; }
    public IReadOnlyList<DecisionNeedDiscoveryNodeId> NodeIds => _nodeIds;

    private sealed class DecisionNeedDiscoveryNodeComparer : IComparer<DecisionNeedDiscoveryNodeId>
    {
        public static DecisionNeedDiscoveryNodeComparer Instance { get; } = new();

        public int Compare(DecisionNeedDiscoveryNodeId? left, DecisionNeedDiscoveryNodeId? right)
        {
            return StringComparer.Ordinal.Compare(left?.Value, right?.Value);
        }
    }
}
