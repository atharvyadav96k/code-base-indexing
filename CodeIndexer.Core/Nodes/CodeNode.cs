namespace CodeIndexer.Core.Nodes;

/// <summary>
/// The single shape every language parser must emit. This is the contract that
/// keeps the shared core language-agnostic: nothing above the parser layer may
/// know which language produced a given node.
/// </summary>
public sealed record CodeNode
{
    /// <summary>Stable unique identifier, consistent across re-parses of unchanged code.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// The node's scope chain (e.g. ["MyApp", "Services", "UserService", "GetUser"]),
    /// from outermost to innermost. This is how namespaces/modules are handled
    /// generically rather than as a per-language special case.
    /// </summary>
    public required IReadOnlyList<string> ScopeChain { get; init; }

    /// <summary>The separator this node's language uses to join <see cref="ScopeChain"/> (e.g. "." for C#).</summary>
    public required string ScopeSeparator { get; init; }

    /// <summary><see cref="ScopeChain"/> joined by <see cref="ScopeSeparator"/>. Computed via <see cref="Scope.ScopeNameBuilder"/>.</summary>
    public required string QualifiedName { get; init; }

    public required NodeKind Kind { get; init; }

    public required NodeLocation Location { get; init; }

    /// <summary>Cheap projection for browsing search results.</summary>
    public required NodeSummary Summary { get; init; }

    /// <summary>Full source text of the node. Expensive — callers should fetch this only on demand.</summary>
    public required string Body { get; init; }

    public required NodeMetadata Metadata { get; init; }

    /// <summary>Content hash of <see cref="Body"/>, used for staleness detection on retrieval.</summary>
    public required string ContentHash { get; init; }

    /// <summary>Relationships to other nodes. Empty until Phase 7 populates them.</summary>
    public IReadOnlyList<NodeEdge> Edges { get; init; } = Array.Empty<NodeEdge>();
}
