using System.Collections.ObjectModel;
using Alice.Actors;

namespace Alice.Cognition;

/// <summary>An immutable reverse index over one explicitly supplied current edge snapshot.</summary>
public sealed class DependencyIndex
{
    private readonly Dictionary<AffectedNode, DependencyEdge[]> _edgesByAffectedNode;

    private DependencyIndex(Dictionary<AffectedNode, DependencyEdge[]> edgesByAffectedNode)
    {
        _edgesByAffectedNode = edgesByAffectedNode;
    }

    public static DependencyIndex Create(IEnumerable<DependencyEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);

        var uniqueEdges = new HashSet<DependencyEdge>();
        var groupedEdges = new Dictionary<AffectedNode, List<DependencyEdge>>();
        foreach (DependencyEdge? edge in edges)
        {
            if (edge is null)
            {
                throw new ArgumentException("A dependency snapshot cannot contain null edges.", nameof(edges));
            }

            if (!uniqueEdges.Add(edge))
            {
                throw new ArgumentException("A dependency snapshot cannot contain duplicate edge assertions.", nameof(edges));
            }

            if (!groupedEdges.TryGetValue(edge.AffectedNode, out List<DependencyEdge>? matchingEdges))
            {
                matchingEdges = [];
                groupedEdges.Add(edge.AffectedNode, matchingEdges);
            }

            matchingEdges.Add(edge);
        }

        var snapshot = new Dictionary<AffectedNode, DependencyEdge[]>(groupedEdges.Count);
        foreach (KeyValuePair<AffectedNode, List<DependencyEdge>> item in groupedEdges)
        {
            snapshot.Add(item.Key, item.Value.ToArray());
        }

        return new DependencyIndex(snapshot);
    }

    public IReadOnlyList<EventCentricDiscoverySeed> Discover(AffectedNodeFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (!_edgesByAffectedNode.TryGetValue(fact.AffectedNode, out DependencyEdge[]? matchingEdges))
        {
            return Array.Empty<EventCentricDiscoverySeed>();
        }

        var bestBandByActor = new Dictionary<ActorId, EventCentricRankBand>();
        foreach (DependencyEdge edge in matchingEdges)
        {
            EventCentricRankBand rankBand = DependencyEdgeRankBandMapping.GetRankBand(edge.Kind);
            if (!bestBandByActor.TryGetValue(edge.ActorId, out EventCentricRankBand currentBest) || rankBand < currentBest)
            {
                bestBandByActor[edge.ActorId] = rankBand;
            }
        }

        var seeds = new List<EventCentricDiscoverySeed>(bestBandByActor.Count);
        foreach (KeyValuePair<ActorId, EventCentricRankBand> item in bestBandByActor)
        {
            seeds.Add(new EventCentricDiscoverySeed(
                fact.SourceKind,
                fact.SourceId,
                item.Key,
                item.Value));
        }

        seeds.Sort(CompareSeeds);
        return new ReadOnlyCollection<EventCentricDiscoverySeed>(seeds.ToArray());
    }

    private static int CompareSeeds(EventCentricDiscoverySeed left, EventCentricDiscoverySeed right)
    {
        int bandComparison = left.RankBand.CompareTo(right.RankBand);
        return bandComparison != 0
            ? bandComparison
            : StringComparer.Ordinal.Compare(left.ActorId.Value, right.ActorId.Value);
    }
}
