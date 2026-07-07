using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Search.Structure;

/// <summary>Groups nodes by their containing file, for "what's in this file" browsing.</summary>
public static class FileOutlineBuilder
{
    public static IReadOnlyList<FileOutline> Build(IReadOnlyList<CodeNode> nodes)
    {
        return nodes
            .GroupBy(n => n.Location.FilePath)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FileOutline
            {
                FilePath = g.Key,
                Nodes = g
                    .OrderBy(n => n.Location.StartLine)
                    .Select(n => NodeSearchHit.FromNode(n, score: 0))
                    .ToArray(),
            })
            .ToArray();
    }

    public static FileOutline? ForFile(IReadOnlyList<CodeNode> nodes, string filePath)
    {
        var matching = nodes.Where(n => string.Equals(n.Location.FilePath, filePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matching.Length == 0)
        {
            return null;
        }

        return new FileOutline
        {
            FilePath = filePath,
            Nodes = matching.OrderBy(n => n.Location.StartLine).Select(n => NodeSearchHit.FromNode(n, score: 0)).ToArray(),
        };
    }
}
