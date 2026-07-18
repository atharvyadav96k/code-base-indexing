using CodeIndexer.Core.Nodes;
using CodeIndexer.Core.Parsing;
using CodeIndexer.Indexing;
using CodeIndexer.Indexing.Manifest;
using CodeIndexer.Indexing.Sessions;
using CodeIndexer.Parsing.CSharp;
using CodeIndexer.Storage;
using CodeIndexer.Storage.Json;
using Xunit;

namespace CodeIndexer.Tests.Indexing;

/// <summary>
/// Verifies the incremental update's core promise: only changed/added/removed
/// files' shards get rewritten on disk, unchanged files are left alone, and
/// search-index.json is regenerated to match the current full node set.
/// </summary>
public class IndexOrchestratorShardTests : IDisposable
{
    private readonly string _root;
    private readonly IReadOnlyList<ICodeParser> _parsers = new ICodeParser[] { new CSharpParser() };
    private readonly SpyIndexStore _store = new(new JsonIndexStore());

    public IndexOrchestratorShardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "codeindex-shardtest-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    private void WriteSource(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private Session EnsureSession()
    {
        var sessionManager = new SessionManager(new SessionRegistry(Path.Combine(_root, "registry.json")));
        return sessionManager.EnsureSession(_root);
    }

    [Fact]
    public async Task IncrementalUpdate_OnlyRewritesChangedFileShard()
    {
        WriteSource("A.cs", "namespace App; public class A {}");
        WriteSource("B.cs", "namespace App; public class B {}");
        WriteSource("C.cs", "namespace App; public class C {}");

        var session = EnsureSession();
        var orchestrator = new IndexOrchestrator(_parsers, _store);
        await orchestrator.RunFullIndexAsync(session, CancellationToken.None);

        _store.WriteFileCallsByPath.Clear();

        File.WriteAllText(Path.Combine(_root, "B.cs"), "namespace App; public class B2 {}");
        var result = await orchestrator.RunIncrementalIndexAsync(session, CancellationToken.None);

        Assert.False(result.FellBackToFullIndex);
        var writtenPaths = _store.WriteFileCallsByPath.Keys.Select(Path.GetFileName).ToArray();
        Assert.Contains("B.cs", writtenPaths);
        Assert.DoesNotContain("A.cs", writtenPaths);
        Assert.DoesNotContain("C.cs", writtenPaths);
    }

    [Fact]
    public async Task IncrementalUpdate_DeletedFile_RemovesShardFolderFromDisk()
    {
        WriteSource("Keep.cs", "namespace App; public class Keep {}");
        WriteSource("Gone.cs", "namespace App; public class Gone {}");

        var session = EnsureSession();
        var orchestrator = new IndexOrchestrator(_parsers, _store);
        await orchestrator.RunFullIndexAsync(session, CancellationToken.None);

        var goneFilePath = Path.Combine(_root, "Gone.cs");
        var goneShardDir = JsonIndexStore.GetShardDirectory(session.MarkerDirectoryPath, goneFilePath);
        Assert.True(Directory.Exists(goneShardDir));

        File.Delete(goneFilePath);
        var result = await orchestrator.RunIncrementalIndexAsync(session, CancellationToken.None);

        Assert.Equal(1, result.FilesRemoved);
        Assert.False(Directory.Exists(goneShardDir));
    }

    [Fact]
    public async Task IncrementalUpdate_RegeneratesSearchIndexWithCurrentNodes()
    {
        WriteSource("A.cs", "namespace App; public class A {}");

        var session = EnsureSession();
        var orchestrator = new IndexOrchestrator(_parsers, _store);
        await orchestrator.RunFullIndexAsync(session, CancellationToken.None);

        WriteSource("New.cs", "namespace App; public class Brand {}");
        await orchestrator.RunIncrementalIndexAsync(session, CancellationToken.None);

        var searchIndex = _store.ReadSearchIndex(session.MarkerDirectoryPath);
        Assert.True(searchIndex.Success);
        Assert.Contains(searchIndex.Entries, e => e.Name == "A");
        Assert.Contains(searchIndex.Entries, e => e.Name == "Brand");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Wraps a real <see cref="IIndexStore"/>, counting <see cref="WriteFile"/> calls per file so tests can assert which shards actually got touched.</summary>
    private sealed class SpyIndexStore : IIndexStore
    {
        private readonly IIndexStore _inner;

        public SpyIndexStore(IIndexStore inner) => _inner = inner;

        public Dictionary<string, int> WriteFileCallsByPath { get; } = new();

        public void ResetAll(string indexRootPath) => _inner.ResetAll(indexRootPath);

        public void WriteFile(string indexRootPath, string absoluteFilePath, IReadOnlyList<CodeNode> nodes)
        {
            WriteFileCallsByPath[absoluteFilePath] = WriteFileCallsByPath.GetValueOrDefault(absoluteFilePath) + 1;
            _inner.WriteFile(indexRootPath, absoluteFilePath, nodes);
        }

        public void DeleteFile(string indexRootPath, string absoluteFilePath) => _inner.DeleteFile(indexRootPath, absoluteFilePath);

        public void WriteSearchIndex(string indexRootPath, IReadOnlyList<CodeNode> allCurrentNodes) => _inner.WriteSearchIndex(indexRootPath, allCurrentNodes);

        public IndexReadResult ReadFile(string indexRootPath, string absoluteFilePath) => _inner.ReadFile(indexRootPath, absoluteFilePath);

        public IndexReadResult ReadFiles(string indexRootPath, IReadOnlyList<string> absoluteFilePaths) => _inner.ReadFiles(indexRootPath, absoluteFilePaths);

        public IReadOnlyList<RelationDto> ReadRelations(string indexRootPath, IReadOnlyList<string> absoluteFilePaths) => _inner.ReadRelations(indexRootPath, absoluteFilePaths);

        public SearchIndexReadResult ReadSearchIndex(string indexRootPath) => _inner.ReadSearchIndex(indexRootPath);
    }
}
