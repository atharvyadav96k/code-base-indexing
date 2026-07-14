using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Search.Relationships;

/// <summary>
/// The Phase 8 "find references" query: given a node, find everywhere it is
/// referenced by walking every other node's outgoing edges and keeping the
/// ones that point at it — the reverse of the edges <see cref="RelationshipResolver"/>
/// (in Indexing) already resolved at index time.
/// </summary>
public sealed class ReferenceFinder
{
    /// <summary>Every node that references <paramref name="targetNodeId"/>, via any edge kind.</summary>
    public IReadOnlyList<ReferenceHit> FindReferences(IReadOnlyList<CodeNode> allNodes, string targetNodeId)
    {
        var hits = new List<ReferenceHit>();

        foreach (var node in allNodes)
        {
            foreach (var edge in node.Edges)
            {
                if (edge.TargetNodeId == targetNodeId)
                {
                    hits.Add(new ReferenceHit { Source = node, Kind = edge.Kind });
                }
            }
        }

        return hits;
    }

    /// <summary>Nodes that call <paramref name="targetNodeId"/> — <see cref="FindReferences"/> filtered to Calls edges.</summary>
    public IReadOnlyList<CodeNode> GetCallers(IReadOnlyList<CodeNode> allNodes, string targetNodeId) =>
        FindReferences(allNodes, targetNodeId)
            .Where(hit => hit.Kind == EdgeKind.Calls)
            .Select(hit => hit.Source)
            .ToArray();

    /// <summary>Nodes that <paramref name="sourceNodeId"/> itself calls — its own outgoing Calls edges, resolved.</summary>
    public IReadOnlyList<CodeNode> GetCallees(IReadOnlyList<CodeNode> allNodes, string sourceNodeId)
    {
        var source = allNodes.FirstOrDefault(n => n.Id == sourceNodeId);
        if (source is null)
        {
            return Array.Empty<CodeNode>();
        }

        var byId = allNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        return source.Edges
            .Where(e => e.Kind == EdgeKind.Calls)
            .Select(e => byId.GetValueOrDefault(e.TargetNodeId))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToArray();
    }

    /// <summary>Direct members (methods, fields, nested types, ...) declared inside <paramref name="containerNodeId"/> — its resolved outgoing Contains edges.</summary>
    public IReadOnlyList<CodeNode> GetChildren(IReadOnlyList<CodeNode> allNodes, string containerNodeId)
    {
        var container = allNodes.FirstOrDefault(n => n.Id == containerNodeId);
        if (container is null)
        {
            return Array.Empty<CodeNode>();
        }

        var byId = allNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        return container.Edges
            .Where(e => e.Kind == EdgeKind.Contains)
            .Select(e => byId.GetValueOrDefault(e.TargetNodeId))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToArray();
    }

    /// <summary>Types that inherit from or implement <paramref name="targetNodeId"/>.</summary>
    public IReadOnlyList<CodeNode> GetSubtypes(IReadOnlyList<CodeNode> allNodes, string targetNodeId) =>
        FindReferences(allNodes, targetNodeId)
            .Where(hit => hit.Kind is EdgeKind.Inherits or EdgeKind.Implements)
            .Select(hit => hit.Source)
            .ToArray();

    /// <summary>
    /// Parameters, fields, and properties typed as <paramref name="targetNodeId"/>
    /// — chiefly where a class is constructor-injected or held as a dependency,
    /// as opposed to called or subclassed.
    /// </summary>
    public IReadOnlyList<CodeNode> GetUsages(IReadOnlyList<CodeNode> allNodes, string targetNodeId) =>
        FindReferences(allNodes, targetNodeId)
            .Where(hit => hit.Kind == EdgeKind.References)
            .Select(hit => hit.Source)
            .ToArray();
}
