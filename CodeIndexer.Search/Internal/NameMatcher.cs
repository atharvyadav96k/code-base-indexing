namespace CodeIndexer.Search.Internal;

/// <summary>
/// Scores how well a candidate name matches a search pattern. Higher is
/// better; 0 means no match at all. Exact beats prefix beats substring beats
/// fuzzy subsequence, so ranking falls out of sorting by score.
/// </summary>
internal static class NameMatcher
{
    public static int Score(string candidateName, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return 1;
        }

        if (string.Equals(candidateName, pattern, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (candidateName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        if (candidateName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
        {
            return 50;
        }

        return IsSubsequence(candidateName, pattern) ? 20 : 0;
    }

    private static bool IsSubsequence(string candidateName, string pattern)
    {
        var pIndex = 0;
        foreach (var c in candidateName)
        {
            if (pIndex < pattern.Length && char.ToLowerInvariant(c) == char.ToLowerInvariant(pattern[pIndex]))
            {
                pIndex++;
            }
        }

        return pIndex == pattern.Length;
    }
}
