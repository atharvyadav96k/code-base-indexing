using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Search.Structure;

/// <summary>
/// One entry in the namespace/module outline — the codebase organized by
/// scope chain rather than by folder. A segment with no matching emitted node
/// (e.g. a passthrough namespace prefix) has null <see cref="Kind"/>/<see cref="NodeId"/>.
/// </summary>
public sealed record ScopeOutlineNode
{
    public required string Name { get; init; }

    public required string QualifiedName { get; init; }

    public NodeKind? Kind { get; init; }

    public string? NodeId { get; init; }

    public IReadOnlyList<ScopeOutlineNode> Children { get; init; } = Array.Empty<ScopeOutlineNode>();
}
