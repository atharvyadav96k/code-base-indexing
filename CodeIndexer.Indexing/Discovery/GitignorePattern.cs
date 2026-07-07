using System.Text.RegularExpressions;

namespace CodeIndexer.Indexing.Discovery;

/// <summary>
/// One compiled rule from a .gitignore file, scoped to the directory that
/// contained it (relative to the session root, using '/' separators, "" for root).
/// </summary>
internal sealed class GitignorePattern
{
    public bool IsNegation { get; }

    public bool DirectoryOnly { get; }

    private readonly Regex _regex;

    private GitignorePattern(Regex regex, bool isNegation, bool directoryOnly)
    {
        _regex = regex;
        IsNegation = isNegation;
        DirectoryOnly = directoryOnly;
    }

    public bool Matches(string relativePath) => _regex.IsMatch(relativePath);

    /// <summary>Parses one non-comment, non-blank line of a .gitignore file.</summary>
    public static GitignorePattern? Parse(string rawLine, string baseDirectory)
    {
        var line = rawLine;
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
        {
            return null;
        }

        var isNegation = line.StartsWith('!');
        if (isNegation)
        {
            line = line[1..];
        }

        var directoryOnly = line.EndsWith('/');
        if (directoryOnly)
        {
            line = line[..^1];
        }

        var anchored = line.Contains('/');
        if (line.StartsWith('/'))
        {
            line = line[1..];
        }

        var prefix = string.IsNullOrEmpty(baseDirectory) ? string.Empty : baseDirectory + "/";
        var regexBody = GlobToRegex(line);

        // Unanchored patterns (no '/' in the body) may match at any depth under baseDirectory.
        var pattern = anchored
            ? $"^{Regex.Escape(prefix)}{regexBody}(/.*)?$"
            : $"^{Regex.Escape(prefix)}(.*/)?{regexBody}(/.*)?$";

        var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        return new GitignorePattern(regex, isNegation, directoryOnly);
    }

    private static string GlobToRegex(string glob)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                sb.Append(".*");
                i++;
            }
            else if (c == '*')
            {
                sb.Append("[^/]*");
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }

        return sb.ToString();
    }
}
