using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Search.Structure;

/// <summary>
/// Builds a tree keyed purely by scope chain segments (split on each node's own
/// separator), independent of file layout — lets an AI browse "by namespace"
/// rather than "by folder".
/// </summary>
public static class ScopeOutlineBuilder
{
    public static IReadOnlyList<ScopeOutlineNode> Build(IReadOnlyList<CodeNode> nodes)
    {
        var root = new MutableNode();

        foreach (var node in nodes)
        {
            var segments = node.ScopeChain.SelectMany(s => s.Split(node.ScopeSeparator)).ToArray();

            var current = root;
            var pathSoFar = new List<string>();
            foreach (var segment in segments)
            {
                pathSoFar.Add(segment);
                if (!current.Children.TryGetValue(segment, out var child))
                {
                    child = new MutableNode { QualifiedName = string.Join(node.ScopeSeparator, pathSoFar) };
                    current.Children[segment] = child;
                }

                current = child;
            }

            current.Kind = node.Kind;
            current.NodeId = node.Id;
        }

        return root.Children
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => ToOutlineNode(kv.Key, kv.Value))
            .ToArray();
    }

    private static ScopeOutlineNode ToOutlineNode(string name, MutableNode node) => new()
    {
        Name = name,
        QualifiedName = node.QualifiedName,
        Kind = node.Kind,
        NodeId = node.NodeId,
        Children = node.Children
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => ToOutlineNode(kv.Key, kv.Value))
            .ToArray(),
    };

    private sealed class MutableNode
    {
        public string QualifiedName { get; set; } = string.Empty;

        public NodeKind? Kind { get; set; }

        public string? NodeId { get; set; }

        public Dictionary<string, MutableNode> Children { get; } = new();
    }
}
