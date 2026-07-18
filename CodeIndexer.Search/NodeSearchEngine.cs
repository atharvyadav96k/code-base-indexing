using CodeIndexer.Core.Nodes;
using CodeIndexer.Search.Internal;
using CodeIndexer.Storage.Json;

namespace CodeIndexer.Search;

/// <summary>
/// Filters and ranks nodes for the search half of the search → retrieve loop.
/// Operates over whatever set of nodes the caller loaded from storage — this
/// class has no I/O of its own.
/// </summary>
public sealed class NodeSearchEngine
{
    public IReadOnlyList<NodeSearchHit> Search(IReadOnlyList<CodeNode> nodes, SearchQuery query)
    {
        var scored = new List<(CodeNode Node, int Score)>();

        foreach (var node in nodes)
        {
            if (!PassesFilters(node, query))
            {
                continue;
            }

            var score = query.NamePattern is null ? 1 : NameMatcher.Score(node.Name, query.NamePattern);
            if (score <= 0)
            {
                continue;
            }

            scored.Add((node, score));
        }

        var ranked = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Node.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => NodeSearchHit.FromNode(s.Node, s.Score));

        return (query.MaxResults is { } max ? ranked.Take(max) : ranked).ToArray();
    }

    /// <summary>
    /// Ranks search-index.json entries directly — the fast path for the
    /// 'search' command, which never needs to load any file's full shard.
    /// </summary>
    public IReadOnlyList<FileEntryDto> SearchEntries(IReadOnlyList<FileEntryDto> entries, string? namePattern, IReadOnlyCollection<NodeKind>? kinds, int? maxResults)
    {
        var scored = new List<(FileEntryDto Entry, int Score)>();

        foreach (var entry in entries)
        {
            if (kinds is { Count: > 0 } && !kinds.Contains((NodeKind)entry.Kind))
            {
                continue;
            }

            var score = namePattern is null ? 1 : NameMatcher.Score(entry.Name, namePattern);
            if (score <= 0)
            {
                continue;
            }

            scored.Add((entry, score));
        }

        var ranked = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.Entry);

        return (maxResults is { } max ? ranked.Take(max) : ranked).ToArray();
    }

    private static bool PassesFilters(CodeNode node, SearchQuery query)
    {
        if (query.Kinds is { Count: > 0 } kinds && !kinds.Contains(node.Kind))
        {
            return false;
        }

        if (query.QualifiedNamePrefix is { } qnPrefix &&
            !node.QualifiedName.StartsWith(qnPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.DirectoryPrefix is { } dirPrefix)
        {
            var normalizedFile = node.Location.FilePath.Replace('\\', '/');
            var normalizedPrefix = dirPrefix.Replace('\\', '/');
            if (!normalizedFile.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (query.IsPublic is { } isPublic && node.Metadata.IsPublic != isPublic)
        {
            return false;
        }

        if (query.IsStatic is { } isStatic && node.Metadata.IsStatic != isStatic)
        {
            return false;
        }

        if (query.IsAsync is { } isAsync && node.Metadata.IsAsync != isAsync)
        {
            return false;
        }

        if (query.IsTest is { } isTest && node.Metadata.IsTest != isTest)
        {
            return false;
        }

        return true;
    }
}
