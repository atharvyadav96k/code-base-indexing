namespace CodeIndexer.Core.Nodes;

/// <summary>
/// Boolean/flag-style facts about a node that filters and search commonly key on.
/// <see cref="Extra"/> lets a parser attach language-specific flags without
/// changing this shared contract.
/// </summary>
public sealed record NodeMetadata
{
    public bool IsPublic { get; init; }

    public bool IsPrivate { get; init; }

    public bool IsProtected { get; init; }

    public bool IsInternal { get; init; }

    public bool IsStatic { get; init; }

    public bool IsAsync { get; init; }

    public bool IsAbstract { get; init; }

    public bool IsVirtual { get; init; }

    public bool IsOverride { get; init; }

    public bool IsTest { get; init; }

    /// <summary>Escape hatch for language-specific flags that don't warrant a shared field.</summary>
    public IReadOnlyDictionary<string, string> Extra { get; init; } = new Dictionary<string, string>();
}
