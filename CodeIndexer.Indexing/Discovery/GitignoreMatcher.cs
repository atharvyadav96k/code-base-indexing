namespace CodeIndexer.Indexing.Discovery;

/// <summary>
/// Loads every .gitignore under a root (root-to-leaf order, so more specific
/// rules are evaluated after general ones, matching real git precedence) and
/// answers whether a given relative path is ignored.
/// </summary>
public sealed class GitignoreMatcher
{
    private readonly List<GitignorePattern> _patterns;

    private GitignoreMatcher(List<GitignorePattern> patterns)
    {
        _patterns = patterns;
    }

    public static GitignoreMatcher Load(string rootPath)
    {
        var patterns = new List<GitignorePattern>();

        foreach (var gitignoreFile in EnumerateGitignoreFiles(rootPath))
        {
            var baseDirectory = Path.GetDirectoryName(gitignoreFile) is { } dir
                ? ToRelative(rootPath, dir)
                : string.Empty;

            foreach (var line in File.ReadAllLines(gitignoreFile))
            {
                var pattern = GitignorePattern.Parse(line.Trim(), baseDirectory);
                if (pattern is not null)
                {
                    patterns.Add(pattern);
                }
            }
        }

        return new GitignoreMatcher(patterns);
    }

    /// <summary>True if <paramref name="relativePath"/> (root-relative, '/'-separated) should be ignored.</summary>
    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        var normalized = relativePath.Replace('\\', '/');
        var ignored = false;

        foreach (var pattern in _patterns)
        {
            if (pattern.DirectoryOnly && !isDirectory)
            {
                continue;
            }

            if (pattern.Matches(normalized))
            {
                ignored = !pattern.IsNegation;
            }
        }

        return ignored;
    }

    private static IEnumerable<string> EnumerateGitignoreFiles(string rootPath)
    {
        var rootGitignore = Path.Combine(rootPath, ".gitignore");
        if (File.Exists(rootGitignore))
        {
            yield return rootGitignore;
        }

        foreach (var file in Directory.EnumerateFiles(rootPath, ".gitignore", SearchOption.AllDirectories))
        {
            if (file == rootGitignore)
            {
                continue;
            }

            yield return file;
        }
    }

    private static string ToRelative(string rootPath, string directory)
    {
        var relative = Path.GetRelativePath(rootPath, directory);
        return relative == "." ? string.Empty : relative.Replace('\\', '/');
    }
}
