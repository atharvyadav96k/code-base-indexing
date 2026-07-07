namespace CodeIndexer.Core.Nodes;

/// <summary>
/// A directed relationship from the owning node to another node, identified by
/// the target's stable ID.
/// </summary>
public sealed record NodeEdge
{
    public required EdgeKind Kind { get; init; }

    public required string TargetNodeId { get; init; }
}
