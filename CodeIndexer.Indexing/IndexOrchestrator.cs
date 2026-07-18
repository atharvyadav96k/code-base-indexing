using CodeIndexer.Core.Nodes;
using CodeIndexer.Core.Parsing;
using CodeIndexer.Indexing.Discovery;
using CodeIndexer.Indexing.Manifest;
using CodeIndexer.Indexing.Relationships;
using CodeIndexer.Indexing.Sessions;
using CodeIndexer.Storage;

namespace CodeIndexer.Indexing;

/// <summary>
/// Drives indexing of a session: discover files, dispatch each to the parser
/// that owns its extension, and persist the resulting nodes. Talks to parsers
/// only through <see cref="ICodeParser"/> — it has no idea which languages are
/// registered. Supports both a full re-index (the source of truth) and a
/// file-granularity incremental update on top of it.
/// </summary>
public sealed class IndexOrchestrator
{
    private readonly IReadOnlyList<ICodeParser> _parsers;
    private readonly FileDiscoverer _fileDiscoverer;
    private readonly IIndexStore _indexStore;

    public IndexOrchestrator(IReadOnlyList<ICodeParser> parsers, IIndexStore indexStore)
    {
        _parsers = parsers;
        _fileDiscoverer = new FileDiscoverer();
        _indexStore = indexStore;
    }

    public async Task<IndexRunResult> RunFullIndexAsync(Session session, CancellationToken cancellationToken)
    {
        var (resolvedNodes, skipped, files) = await RunFullBuildAsync(session, cancellationToken);

        return new IndexRunResult
        {
            FilesDiscovered = files.Count,
            NodesIndexed = resolvedNodes.Count,
            SkippedFiles = skipped,
        };
    }

    /// <summary>
    /// Re-parses only files whose content changed since the last index/update,
    /// carries forward nodes from unchanged files (loaded from just their own
    /// shards, not the whole graph), and drops shards for files that no longer
    /// exist. Only shards for Added/Changed files — plus any Unchanged file
    /// whose resolved outbound edges actually moved — are rewritten. Falls back
    /// to a full index if there's no usable prior manifest to diff against
    /// (e.g. the first run, or an index written before this schema).
    /// </summary>
    public async Task<IncrementalIndexResult> RunIncrementalIndexAsync(Session session, CancellationToken cancellationToken)
    {
        var oldManifest = FileManifestStore.Read(session.ManifestFilePath);

        if (oldManifest.FileHashes.Count == 0)
        {
            var (resolvedAll, allSkipped, files) = await RunFullBuildAsync(session, cancellationToken);

            return new IncrementalIndexResult
            {
                FilesAdded = files.Count,
                FilesChanged = 0,
                FilesRemoved = 0,
                FilesUnchanged = 0,
                NodesIndexed = resolvedAll.Count,
                SkippedFiles = allSkipped,
                FellBackToFullIndex = true,
            };
        }

        var extensionToParser = BuildExtensionMap();
        var discoveredFiles = DiscoverFiles(session, extensionToParser);
        var changeSet = FileChangeDetector.Detect(discoveredFiles, oldManifest);

        var carriedForwardRead = _indexStore.ReadFiles(session.MarkerDirectoryPath, changeSet.Unchanged);
        var carriedForwardNodes = carriedForwardRead.Nodes;

        var filesToParse = changeSet.Added.Concat(changeSet.Changed).ToArray();
        var (newNodes, skipped) = await ParseFilesAsync(filesToParse, extensionToParser, cancellationToken);

        var mergedNodes = carriedForwardNodes.Concat(newNodes).ToArray();
        var resolvedNodes = RelationshipResolver.Resolve(mergedNodes);
        var resolvedByFile = resolvedNodes.ToLookup(n => n.Location.FilePath);

        foreach (var file in filesToParse)
        {
            _indexStore.WriteFile(session.MarkerDirectoryPath, file, resolvedByFile[file].ToArray());
        }

        foreach (var file in changeSet.Removed)
        {
            _indexStore.DeleteFile(session.MarkerDirectoryPath, file);
        }

        var beforeByFile = carriedForwardNodes.ToLookup(n => n.Location.FilePath);
        foreach (var file in changeSet.Unchanged)
        {
            if (!EdgeSetsEqual(beforeByFile[file], resolvedByFile[file]))
            {
                _indexStore.WriteFile(session.MarkerDirectoryPath, file, resolvedByFile[file].ToArray());
            }
        }

        _indexStore.WriteSearchIndex(session.MarkerDirectoryPath, resolvedNodes);

        var newManifest = new FileManifest { IndexedAtUtc = DateTimeOffset.UtcNow, FileHashes = changeSet.CurrentHashes };
        FileManifestStore.Write(session.ManifestFilePath, newManifest);

        return new IncrementalIndexResult
        {
            FilesAdded = changeSet.Added.Count,
            FilesChanged = changeSet.Changed.Count,
            FilesRemoved = changeSet.Removed.Count,
            FilesUnchanged = changeSet.Unchanged.Count,
            NodesIndexed = resolvedNodes.Count,
            SkippedFiles = skipped,
            FellBackToFullIndex = false,
        };
    }

    /// <summary>Full parse + resolve + shard rewrite, shared by a top-level full index and the incremental-update fallback path.</summary>
    private async Task<(IReadOnlyList<CodeNode> ResolvedNodes, List<string> Skipped, IReadOnlyList<string> Files)> RunFullBuildAsync(Session session, CancellationToken cancellationToken)
    {
        var extensionToParser = BuildExtensionMap();
        var files = DiscoverFiles(session, extensionToParser);

        var (nodes, skipped) = await ParseFilesAsync(files, extensionToParser, cancellationToken);
        var resolvedNodes = RelationshipResolver.Resolve(nodes);

        _indexStore.ResetAll(session.MarkerDirectoryPath);
        foreach (var group in resolvedNodes.GroupBy(n => n.Location.FilePath))
        {
            _indexStore.WriteFile(session.MarkerDirectoryPath, group.Key, group.ToArray());
        }

        _indexStore.WriteSearchIndex(session.MarkerDirectoryPath, resolvedNodes);
        WriteManifest(session, files);

        return (resolvedNodes, skipped, files);
    }

    /// <summary>Whether two node sets' outbound edges are identical, ignoring order — used to skip rewriting an unchanged file's shard when its edges genuinely haven't moved.</summary>
    private static bool EdgeSetsEqual(IEnumerable<CodeNode> before, IEnumerable<CodeNode> after)
    {
        var beforeEdges = before.SelectMany(n => n.Edges.Select(e => (n.Id, e.Kind, e.TargetNodeId))).ToHashSet();
        var afterEdges = after.SelectMany(n => n.Edges.Select(e => (n.Id, e.Kind, e.TargetNodeId))).ToHashSet();
        return beforeEdges.SetEquals(afterEdges);
    }

    /// <summary>Reports drift (added/changed/removed files) against the stored manifest without writing anything.</summary>
    public FileChangeSet DetectDrift(Session session)
    {
        var extensionToParser = BuildExtensionMap();
        var files = DiscoverFiles(session, extensionToParser);
        var manifest = FileManifestStore.Read(session.ManifestFilePath);
        return FileChangeDetector.Detect(files, manifest);
    }

    private Dictionary<string, ICodeParser> BuildExtensionMap()
    {
        var extensionToParser = new Dictionary<string, ICodeParser>(StringComparer.OrdinalIgnoreCase);
        foreach (var parser in _parsers)
        {
            foreach (var extension in parser.SupportedExtensions)
            {
                extensionToParser[extension] = parser;
            }
        }

        return extensionToParser;
    }

    private IReadOnlyList<string> DiscoverFiles(Session session, Dictionary<string, ICodeParser> extensionToParser)
    {
        var discoveryOptions = new FileDiscoveryOptions { IncludeExtensions = extensionToParser.Keys.ToArray() };
        return _fileDiscoverer.Discover(session.RootPath, discoveryOptions);
    }

    private void WriteManifest(Session session, IReadOnlyList<string> files)
    {
        var hashes = files.ToDictionary(f => f, f => ContentHasher.Hash(File.ReadAllText(f)));
        FileManifestStore.Write(session.ManifestFilePath, new FileManifest { IndexedAtUtc = DateTimeOffset.UtcNow, FileHashes = hashes });
    }

    private async Task<(List<CodeNode> Nodes, List<string> Skipped)> ParseFilesAsync(
        IReadOnlyList<string> files,
        Dictionary<string, ICodeParser> extensionToParser,
        CancellationToken cancellationToken)
    {
        var nodes = new List<CodeNode>();
        var skipped = new List<string>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parser = extensionToParser[Path.GetExtension(file)];
            var sourceText = await File.ReadAllTextAsync(file, cancellationToken);
            var result = await parser.ParseFileAsync(file, sourceText, cancellationToken);

            if (result.Success)
            {
                nodes.AddRange(result.Nodes);
            }
            else
            {
                skipped.Add($"{file}: {result.ErrorMessage}");
            }
        }

        return (nodes, skipped);
    }
}
