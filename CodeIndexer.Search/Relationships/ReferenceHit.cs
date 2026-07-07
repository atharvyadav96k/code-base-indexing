using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Search.Relationships;

/// <summary>One place a node is referenced from, and how (call, inheritance, containment, import).</summary>
public sealed record ReferenceHit
{
    public required CodeNode Source { get; init; }

    public required EdgeKind Kind { get; init; }
}
