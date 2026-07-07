using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Search;

/// <summary>
/// A search request. All filters are optional and combine with AND semantics;
/// <see cref="NamePattern"/> (when present) also drives ranking.
/// </summary>
public sealed record SearchQuery
{
    /// <summary>Exact, prefix, substring, or fuzzy-subsequence match against the node's simple name.</summary>
    public string? NamePattern { get; init; }

    public IReadOnlyCollection<NodeKind>? Kinds { get; init; }

    /// <summary>Only include nodes whose qualified name starts with this (e.g. a namespace).</summary>
    public string? QualifiedNamePrefix { get; init; }

    /// <summary>Only include nodes whose file path is under this directory.</summary>
    public string? DirectoryPrefix { get; init; }

    public bool? IsPublic { get; init; }

    public bool? IsStatic { get; init; }

    public bool? IsAsync { get; init; }

    public bool? IsTest { get; init; }

    /// <summary>Maximum number of ranked results to return. Null means unlimited.</summary>
    public int? MaxResults { get; init; }
}
