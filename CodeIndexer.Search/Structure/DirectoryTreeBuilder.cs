namespace CodeIndexer.Search.Structure;

/// <summary>Builds a folder/file tree view from a flat list of discovered file paths.</summary>
public static class DirectoryTreeBuilder
{
    public static DirectoryTreeNode Build(string rootPath, IReadOnlyList<string> absoluteFilePaths)
    {
        var root = new MutableDir();

        foreach (var absolutePath in absoluteFilePaths)
        {
            var relative = Path.GetRelativePath(rootPath, absolutePath).Replace('\\', '/');
            var segments = relative.Split('/');

            var current = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                current = current.Directories.TryGetValue(segments[i], out var next)
                    ? next
                    : current.Directories[segments[i]] = new MutableDir();
            }

            current.Files.Add(segments[^1]);
        }

        return ToNode(root, name: Path.GetFileName(Path.GetFullPath(rootPath).TrimEnd('/', '\\')), relativePath: string.Empty);
    }

    private static DirectoryTreeNode ToNode(MutableDir dir, string name, string relativePath)
    {
        var childDirs = dir.Directories
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => ToNode(kv.Value, kv.Key, Combine(relativePath, kv.Key)));

        var childFiles = dir.Files
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => new DirectoryTreeNode { Name = f, RelativePath = Combine(relativePath, f), IsDirectory = false });

        return new DirectoryTreeNode
        {
            Name = name,
            RelativePath = relativePath,
            IsDirectory = true,
            Children = childDirs.Concat(childFiles).ToArray(),
        };
    }

    private static string Combine(string relativePath, string segment) =>
        string.IsNullOrEmpty(relativePath) ? segment : $"{relativePath}/{segment}";

    private sealed class MutableDir
    {
        public Dictionary<string, MutableDir> Directories { get; } = new();

        public List<string> Files { get; } = new();
    }
}
