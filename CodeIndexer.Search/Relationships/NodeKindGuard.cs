using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Search.Relationships;

/// <summary>
/// Validates that a node is a kind a given relationship query actually makes
/// sense for (e.g. "callers" only means something for a Method) — without this,
/// querying the wrong kind of node silently returns an empty result
/// indistinguishable from "no callers found".
/// </summary>
public static class NodeKindGuard
{
    /// <summary>
    /// Returns null when <paramref name="node"/>'s kind is one of <paramref name="allowedKinds"/>;
    /// otherwise an explanatory message naming the actual kind found.
    /// </summary>
    public static string? Validate(CodeNode node, string commandName, params NodeKind[] allowedKinds)
    {
        if (allowedKinds.Contains(node.Kind))
        {
            return null;
        }

        var allowedList = string.Join("/", allowedKinds);
        return $"{commandName} is only valid for {allowedList} nodes; {node.Id} is a {node.Kind}";
    }
}
