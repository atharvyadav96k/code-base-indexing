using CodeIndexer.Core.Nodes;
using CodeIndexer.Storage;
using Xunit;

namespace CodeIndexer.Tests.Storage;

public class JsonIndexStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _indexRoot;

    public JsonIndexStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "codeindex-jsonstorage-" + Guid.NewGuid());
        _indexRoot = Path.Combine(_root, ".codeindex");
        Directory.CreateDirectory(_indexRoot);
    }

    private string SourceFile(string relativePath) => Path.Combine(_root, relativePath);

    private static CodeNode SampleNode(string filePath, string name = "Foo", IReadOnlyList<NodeEdge>? edges = null) => new()
    {
        Id = $"id-{name}",
        Name = name,
        ScopeChain = new[] { "App", name },
        ScopeSeparator = ".",
        QualifiedName = $"App.{name}",
        Kind = NodeKind.Class,
        Location = new NodeLocation { FilePath = filePath, StartLine = 1, EndLine = 10 },
        Summary = new NodeSummary
        {
            Name = name,
            Signature = $"public class {name}",
            Parameters = Array.Empty<ParameterInfo>(),
            ReturnType = null,
            DocComment = "A sample class.",
            LineCount = 10,
        },
        Body = $"public class {name} {{ }}",
        Metadata = new NodeMetadata { IsPublic = true, Extra = new Dictionary<string, string> { ["lang"] = "csharp" } },
        ContentHash = ContentHasher.Hash($"public class {name} {{ }}"),
        Edges = edges ?? Array.Empty<NodeEdge>(),
        SkippedRelationships = new[] { "call to 'Save' skipped: 2 ambiguous candidates (a @ A.cs, b @ B.cs)" },
    };

    [Fact]
    public void WriteFileThenReadFile_RoundTripsAllFields()
    {
        var store = new JsonIndexStore();
        var filePath = SourceFile("App.cs");
        var node = SampleNode(filePath, edges: new[] { new NodeEdge { Kind = EdgeKind.Contains, TargetNodeId = "parent-id" } });

        store.WriteFile(_indexRoot, filePath, new[] { node });
        var result = store.ReadFile(_indexRoot, filePath);

        Assert.True(result.Success);
        var read = Assert.Single(result.Nodes);
        Assert.Equal(node.Id, read.Id);
        Assert.Equal(node.QualifiedName, read.QualifiedName);
        Assert.Equal(node.ScopeChain, read.ScopeChain);
        Assert.Equal(node.Summary.DocComment, read.Summary.DocComment);
        Assert.Equal(node.Metadata.Extra["lang"], read.Metadata.Extra["lang"]);
        Assert.Single(read.Edges);
        Assert.Equal(node.Edges[0].TargetNodeId, read.Edges[0].TargetNodeId);
        Assert.Equal(node.ContentHash, read.ContentHash);
        Assert.Equal(node.SkippedRelationships, read.SkippedRelationships);
    }

    [Fact]
    public void ReadFile_MissingShard_ReturnsNotFound()
    {
        var store = new JsonIndexStore();

        var result = store.ReadFile(_indexRoot, SourceFile("Missing.cs"));

        Assert.Equal(IndexReadStatus.NotFound, result.Status);
    }

    [Fact]
    public void ReadFile_MalformedIndexJson_ReturnsCorruptedNotException()
    {
        var store = new JsonIndexStore();
        var filePath = SourceFile("App.cs");
        var shardDir = JsonIndexStore.GetShardDirectory(_indexRoot, filePath);
        Directory.CreateDirectory(shardDir);
        File.WriteAllText(Path.Combine(shardDir, "index.json"), "{ not valid json");

        var result = store.ReadFile(_indexRoot, filePath);

        Assert.Equal(IndexReadStatus.Corrupted, result.Status);
    }

    [Fact]
    public void WriteFile_OverwritesExistingShardAtomically()
    {
        var store = new JsonIndexStore();
        var filePath = SourceFile("App.cs");

        store.WriteFile(_indexRoot, filePath, new[] { SampleNode(filePath, "First") });
        store.WriteFile(_indexRoot, filePath, new[] { SampleNode(filePath, "Second") });

        var result = store.ReadFile(_indexRoot, filePath);
        var node = Assert.Single(result.Nodes);
        Assert.Equal("Second", node.Name);

        var shardDir = JsonIndexStore.GetShardDirectory(_indexRoot, filePath);
        var leftoverTempFiles = Directory.GetFiles(shardDir, "*.tmp");
        Assert.Empty(leftoverTempFiles);
    }

    [Fact]
    public void DeleteFile_RemovesShardFolderAndEmptyParentDirectories()
    {
        var store = new JsonIndexStore();
        var filePath = SourceFile(Path.Combine("src", "nested", "App.cs"));

        store.WriteFile(_indexRoot, filePath, new[] { SampleNode(filePath) });
        store.DeleteFile(_indexRoot, filePath);

        var result = store.ReadFile(_indexRoot, filePath);
        Assert.Equal(IndexReadStatus.NotFound, result.Status);

        var indexedFilesRoot = Path.Combine(_indexRoot, "indexed-files");
        Assert.False(Directory.Exists(Path.Combine(indexedFilesRoot, "src")));
    }

    [Fact]
    public void CrossFileEdges_AreRecordedOnlyOnSourceFilesRelationsJson()
    {
        var store = new JsonIndexStore();
        var fileA = SourceFile("A.cs");
        var fileB = SourceFile("B.cs");

        var nodeB = SampleNode(fileB, "B");
        var nodeA = SampleNode(fileA, "A", edges: new[] { new NodeEdge { Kind = EdgeKind.Calls, TargetNodeId = nodeB.Id } });

        store.WriteFile(_indexRoot, fileA, new[] { nodeA });
        store.WriteFile(_indexRoot, fileB, new[] { nodeB });

        var relationsA = store.ReadRelations(_indexRoot, new[] { fileA });
        var relationsB = store.ReadRelations(_indexRoot, new[] { fileB });

        Assert.Contains(relationsA, r => r.SourceNodeId == nodeA.Id && r.TargetNodeId == nodeB.Id);
        Assert.DoesNotContain(relationsB, r => r.TargetNodeId == nodeB.Id && r.SourceNodeId == nodeA.Id);

        var allRelations = store.ReadRelations(_indexRoot, new[] { fileA, fileB });
        Assert.Contains(allRelations, r => r.TargetNodeId == nodeB.Id);
    }

    [Fact]
    public void WriteSearchIndexThenRead_RoundTripsEntries()
    {
        var store = new JsonIndexStore();
        var filePath = SourceFile("App.cs");
        var node = SampleNode(filePath);

        store.WriteSearchIndex(_indexRoot, new[] { node });
        var result = store.ReadSearchIndex(_indexRoot);

        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(node.Id, entry.Id);
        Assert.Equal(node.QualifiedName, entry.QualifiedName);
        Assert.Equal(node.Location.FilePath, entry.FilePath);
    }

    [Fact]
    public void ReadFiles_ConcatenatesAcrossMultipleShards()
    {
        var store = new JsonIndexStore();
        var fileA = SourceFile("A.cs");
        var fileB = SourceFile("B.cs");

        store.WriteFile(_indexRoot, fileA, new[] { SampleNode(fileA, "A") });
        store.WriteFile(_indexRoot, fileB, new[] { SampleNode(fileB, "B") });

        var result = store.ReadFiles(_indexRoot, new[] { fileA, fileB });

        Assert.True(result.Success);
        Assert.Equal(2, result.Nodes.Count);
        Assert.Contains(result.Nodes, n => n.Name == "A");
        Assert.Contains(result.Nodes, n => n.Name == "B");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
