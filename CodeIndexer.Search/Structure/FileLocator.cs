namespace CodeIndexer.Search.Structure;

/// <summary>Finds files by name or path fragment among a known set of file paths.</summary>
public static class FileLocator
{
    public static IReadOnlyList<string> Locate(IReadOnlyList<string> filePaths, string fragment)
    {
        var normalizedFragment = fragment.Replace('\\', '/');

        return filePaths
            .Select(path => (Path: path, Normalized: path.Replace('\\', '/')))
            .Where(f => f.Normalized.Contains(normalizedFragment, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => string.Equals(System.IO.Path.GetFileName(f.Normalized), fragment, StringComparison.OrdinalIgnoreCase))
            .ThenBy(f => f.Normalized.Length)
            .Select(f => f.Path)
            .ToArray();
    }
}
