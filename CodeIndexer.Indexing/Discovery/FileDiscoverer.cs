namespace CodeIndexer.Indexing.Discovery;

/// <summary>
/// Enumerates source files under a session root, filtered by extension and by
/// exclusion rules (always-skip directory names, plus .gitignore).
/// </summary>
public sealed class FileDiscoverer
{
    public IReadOnlyList<string> Discover(string rootPath, FileDiscoveryOptions options)
    {
        var root = Path.GetFullPath(rootPath);
        var gitignore = options.RespectGitignore ? GitignoreMatcher.Load(root) : null;
        var results = new List<string>();

        Walk(root, root, options, gitignore, results);

        return results;
    }

    private static void Walk(
        string root,
        string currentDirectory,
        FileDiscoveryOptions options,
        GitignoreMatcher? gitignore,
        List<string> results)
    {
        foreach (var directory in Directory.EnumerateDirectories(currentDirectory))
        {
            var name = Path.GetFileName(directory);
            if (options.ExcludedDirectoryNames.Contains(name))
            {
                continue;
            }

            if (gitignore is not null && gitignore.IsIgnored(RelativePath(root, directory), isDirectory: true))
            {
                continue;
            }

            Walk(root, directory, options, gitignore, results);
        }

        foreach (var file in Directory.EnumerateFiles(currentDirectory))
        {
            var extension = Path.GetExtension(file);
            if (!options.IncludeExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (gitignore is not null && gitignore.IsIgnored(RelativePath(root, file), isDirectory: false))
            {
                continue;
            }

            results.Add(file);
        }
    }

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
