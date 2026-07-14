using CodeIndexer.Core.Nodes;
using CodeIndexer.Storage;
using Xunit;

namespace CodeIndexer.Tests.Storage;

public class BinaryIndexStoreTests : IDisposable
{
    private readonly string _tempDir;

    public BinaryIndexStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "codeindex-storage-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    private static CodeNode SampleNode(string name = "Foo") => new()
    {
        Id = "abc123",
        Name = name,
        ScopeChain = new[] { "App", name },
        ScopeSeparator = ".",
        QualifiedName = $"App.{name}",
        Kind = NodeKind.Class,
        Location = new NodeLocation { FilePath = "App.cs", StartLine = 1, EndLine = 10 },
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
        Edges = new[] { new NodeEdge { Kind = EdgeKind.Contains, TargetNodeId = "parent-id" } },
        SkippedRelationships = new[] { "call to 'Save' skipped: 2 ambiguous candidates (a @ A.cs, b @ B.cs)" },
    };

    [Fact]
    public void WriteThenRead_RoundTripsAllFields()
    {
        var store = new BinaryIndexStore();
        var filePath = Path.Combine(_tempDir, "index.bin");
        var node = SampleNode();

        store.Write(filePath, new[] { node });
        var result = store.Read(filePath);

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
    public void Read_MissingFile_ReturnsNotFound()
    {
        var store = new BinaryIndexStore();

        var result = store.Read(Path.Combine(_tempDir, "missing.bin"));

        Assert.Equal(IndexReadStatus.NotFound, result.Status);
    }

    [Fact]
    public void Read_BadMagicHeader_ReturnsCorrupted()
    {
        var filePath = Path.Combine(_tempDir, "garbage.bin");
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var result = new BinaryIndexStore().Read(filePath);

        Assert.Equal(IndexReadStatus.Corrupted, result.Status);
    }

    [Fact]
    public void Read_VersionMismatch_IsDetectedExplicitly()
    {
        var filePath = Path.Combine(_tempDir, "oldversion.bin");
        using (var stream = new FileStream(filePath, FileMode.Create))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes(BinaryIndexFormat.MagicHeader));
            writer.Write(999);
            writer.Write(0);
        }

        var result = new BinaryIndexStore().Read(filePath);

        Assert.Equal(IndexReadStatus.VersionMismatch, result.Status);
    }

    [Fact]
    public void Read_TruncatedFile_ReturnsCorruptedNotException()
    {
        var filePath = Path.Combine(_tempDir, "truncated.bin");
        var store = new BinaryIndexStore();
        store.Write(filePath, new[] { SampleNode() });

        var fullBytes = File.ReadAllBytes(filePath);
        File.WriteAllBytes(filePath, fullBytes[..(fullBytes.Length / 2)]);

        var result = store.Read(filePath);

        Assert.Equal(IndexReadStatus.Corrupted, result.Status);
    }

    [Fact]
    public void Write_CreatesParentDirectoryIfMissing()
    {
        var filePath = Path.Combine(_tempDir, "nested", "sub", "index.bin");
        var store = new BinaryIndexStore();

        store.Write(filePath, new[] { SampleNode() });

        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public void Write_OverwritesExistingFileAtomically()
    {
        var filePath = Path.Combine(_tempDir, "index.bin");
        var store = new BinaryIndexStore();

        store.Write(filePath, new[] { SampleNode("First") });
        store.Write(filePath, new[] { SampleNode("Second") });

        var result = store.Read(filePath);
        var node = Assert.Single(result.Nodes);
        Assert.Equal("Second", node.Name);

        var leftoverTempFiles = Directory.GetFiles(_tempDir, "*.tmp");
        Assert.Empty(leftoverTempFiles);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
