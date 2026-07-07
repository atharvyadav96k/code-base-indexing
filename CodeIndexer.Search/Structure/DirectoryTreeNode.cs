namespace CodeIndexer.Search.Structure;

/// <summary>One entry in a directory tree view — either a folder with children or a leaf file.</summary>
public sealed record DirectoryTreeNode
{
    public required string Name { get; init; }

    /// <summary>Path relative to the session root, '/'-separated.</summary>
    public required string RelativePath { get; init; }

    public required bool IsDirectory { get; init; }

    public IReadOnlyList<DirectoryTreeNode> Children { get; init; } = Array.Empty<DirectoryTreeNode>();
}
