using CodeIndexer.Core.Nodes;
using CodeIndexer.Core.Parsing;
using CodeIndexer.Indexing.Discovery;
using CodeIndexer.Indexing.Sessions;
using CodeIndexer.Storage;

namespace CodeIndexer.Indexing;

/// <summary>
/// Drives a full re-index of a session: discover files, dispatch each to the
/// parser that owns its extension, and persist the resulting nodes. Talks to
/// parsers only through <see cref="ICodeParser"/> — it has no idea which
/// languages are registered.
/// </summary>
public sealed class IndexOrchestrator
{
    private readonly IReadOnlyList<ICodeParser> _parsers;
    private readonly FileDiscoverer _fileDiscoverer;
    private readonly BinaryIndexStore _indexStore;

    public IndexOrchestrator(IReadOnlyList<ICodeParser> parsers)
    {
        _parsers = parsers;
        _fileDiscoverer = new FileDiscoverer();
        _indexStore = new BinaryIndexStore();
    }

    public async Task<IndexRunResult> RunFullIndexAsync(Session session, CancellationToken cancellationToken)
    {
        var extensionToParser = new Dictionary<string, ICodeParser>(StringComparer.OrdinalIgnoreCase);
        foreach (var parser in _parsers)
        {
            foreach (var extension in parser.SupportedExtensions)
            {
                extensionToParser[extension] = parser;
            }
        }

        var discoveryOptions = new FileDiscoveryOptions { IncludeExtensions = extensionToParser.Keys.ToArray() };
        var files = _fileDiscoverer.Discover(session.RootPath, discoveryOptions);

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

        _indexStore.Write(session.IndexFilePath, nodes);

        return new IndexRunResult
        {
            FilesDiscovered = files.Count,
            NodesIndexed = nodes.Count,
            SkippedFiles = skipped,
        };
    }
}
