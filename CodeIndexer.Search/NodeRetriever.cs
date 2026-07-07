using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Search;

/// <summary>The "get node code" half of the search → retrieve loop: fetch the expensive full body by ID.</summary>
public sealed class NodeRetriever
{
    public NodeCodeResult GetCode(IReadOnlyList<CodeNode> nodes, string nodeId)
    {
        foreach (var node in nodes)
        {
            if (node.Id == nodeId)
            {
                return NodeCodeResult.Ok(node);
            }
        }

        return NodeCodeResult.NotFound;
    }
}
