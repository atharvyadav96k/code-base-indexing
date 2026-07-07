namespace CodeIndexer.Indexing.Discovery;

/// <summary>
/// Configuration for which files a discovery pass should return.
/// </summary>
public sealed record FileDiscoveryOptions
{
    /// <summary>File extensions to include, leading dot included (e.g. ".cs"). Case-insensitive.</summary>
    public required IReadOnlyCollection<string> IncludeExtensions { get; init; }

    /// <summary>Directory names to always skip, regardless of .gitignore (e.g. "bin", "obj", "node_modules", ".git").</summary>
    public IReadOnlyCollection<string> ExcludedDirectoryNames { get; init; } = new[]
    {
        "bin", "obj", "node_modules", ".git", ".codeindex", "dist", "build", ".vs", ".idea",
    };

    /// <summary>Whether to honor .gitignore files found under the root.</summary>
    public bool RespectGitignore { get; init; } = true;
}
