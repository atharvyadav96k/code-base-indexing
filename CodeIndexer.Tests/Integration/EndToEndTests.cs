using CodeIndexer.Core.Nodes;
using CodeIndexer.Core.Parsing;
using CodeIndexer.Indexing;
using CodeIndexer.Indexing.Manifest;
using CodeIndexer.Indexing.Sessions;
using CodeIndexer.Parsing.CSharp;
using CodeIndexer.Search;
using CodeIndexer.Search.Relationships;
using CodeIndexer.Search.Structure;
using CodeIndexer.Storage;
using Xunit;

namespace CodeIndexer.Tests.Integration;

/// <summary>
/// Exercises the whole v1 loop for real: write source files to disk, run the
/// actual orchestrator (discovery + Roslyn parsing + JSON per-file storage),
/// then drive search, retrieval, and structure views against what got
/// persisted. No component is mocked — this is what the CLI does end to end.
/// </summary>
public class EndToEndTests : IDisposable
{
    private readonly string _root;
    private readonly IReadOnlyList<ICodeParser> _parsers = new ICodeParser[] { new CSharpParser() };
    private readonly IIndexStore _indexStore = new JsonIndexStore();

    public EndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "codeindex-e2e-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    private void WriteSource(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private IReadOnlyList<CodeNode> ReadAllPersistedNodes(Session session)
    {
        var manifest = FileManifestStore.Read(session.ManifestFilePath);
        var readResult = _indexStore.ReadFiles(session.MarkerDirectoryPath, manifest.FileHashes.Keys.ToArray());
        Assert.True(readResult.Success, readResult.Detail);
        return readResult.Nodes;
    }

    private async Task<(Session Session, IReadOnlyList<CodeNode> Nodes, IndexRunResult Result)> IndexAsync()
    {
        var sessionManager = new SessionManager(new SessionRegistry(Path.Combine(_root, "registry.json")));
        var session = sessionManager.EnsureSession(_root);

        var orchestrator = new IndexOrchestrator(_parsers, _indexStore);
        var result = await orchestrator.RunFullIndexAsync(session, CancellationToken.None);

        return (session, ReadAllPersistedNodes(session), result);
    }

    private async Task<(Session Session, IReadOnlyList<CodeNode> Nodes, IncrementalIndexResult Result)> UpdateAsync()
    {
        var sessionManager = new SessionManager(new SessionRegistry(Path.Combine(_root, "registry.json")));
        var session = sessionManager.EnsureSession(_root);

        var orchestrator = new IndexOrchestrator(_parsers, _indexStore);
        var result = await orchestrator.RunIncrementalIndexAsync(session, CancellationToken.None);

        return (session, ReadAllPersistedNodes(session), result);
    }

    [Fact]
    public async Task FullPipeline_IndexSearchRetrieve_WorksEndToEnd()
    {
        WriteSource("src/UserService.cs", """
            namespace SampleApp.Services;

            /// <summary>Looks up users.</summary>
            public class UserService
            {
                public async Task<string> GetUserAsync(int id)
                {
                    return await Task.FromResult("user-" + id);
                }
            }
            """);

        var (_, nodes, result) = await IndexAsync();

        Assert.Equal(1, result.FilesDiscovered);
        Assert.Empty(result.SkippedFiles);

        var hits = new NodeSearchEngine().Search(nodes, new SearchQuery { NamePattern = "GetUser" });
        var hit = Assert.Single(hits);
        Assert.Equal("SampleApp.Services.UserService.GetUserAsync", hit.QualifiedName);

        var classNode = Assert.Single(nodes, n => n.Kind == NodeKind.Class);
        Assert.Equal("Looks up users.", classNode.Summary.DocComment);

        var code = new NodeRetriever().GetCode(nodes, hit.Id);
        Assert.True(code.Found);
        Assert.Contains("Task.FromResult", code.Body);
        Assert.Equal(nodes.First(n => n.Id == hit.Id).ContentHash, code.ContentHash);
    }

    [Fact]
    public async Task FullPipeline_RespectsExcludedDirectoriesAndGitignore()
    {
        WriteSource("src/App.cs", "namespace App; public class Real {}");
        WriteSource("bin/Debug/Generated.cs", "namespace App; public class ShouldBeSkipped {}");
        WriteSource("vendor/Third.cs", "namespace App; public class AlsoSkipped {}");
        WriteSource(".gitignore", "vendor/\n");

        var (_, nodes, result) = await IndexAsync();

        Assert.Equal(1, result.FilesDiscovered);
        Assert.Contains(nodes, n => n.Name == "Real");
        Assert.DoesNotContain(nodes, n => n.Name == "ShouldBeSkipped");
        Assert.DoesNotContain(nodes, n => n.Name == "AlsoSkipped");
    }

    [Fact]
    public async Task FullPipeline_SyntaxErrorFile_IsSkippedButOthersStillIndexed()
    {
        WriteSource("src/Good.cs", "namespace App; public class Good {}");
        WriteSource("src/Bad.cs", "public class {{{ totally broken");

        var (_, nodes, result) = await IndexAsync();

        Assert.Equal(2, result.FilesDiscovered);
        Assert.Single(result.SkippedFiles);
        Assert.Contains("Bad.cs", result.SkippedFiles[0]);
        Assert.Contains(nodes, n => n.Name == "Good");
    }

    [Fact]
    public async Task FullPipeline_ReIndex_OverwritesPreviousResultsAtomically()
    {
        WriteSource("src/App.cs", "namespace App; public class First {}");
        await IndexAsync();

        File.WriteAllText(Path.Combine(_root, "src/App.cs"), "namespace App; public class Second {}");
        var (_, nodes, _) = await IndexAsync();

        Assert.DoesNotContain(nodes, n => n.Name == "First");
        Assert.Contains(nodes, n => n.Name == "Second");
    }

    [Fact]
    public async Task FullPipeline_SessionResolvesFromNestedChildDirectory()
    {
        WriteSource("src/deep/nested/App.cs", "namespace App; public class Foo {}");
        var (session, _, _) = await IndexAsync();

        var sessionManager = new SessionManager(new SessionRegistry(Path.Combine(_root, "registry.json")));
        var childDir = Path.Combine(_root, "src", "deep", "nested");

        var resolution = sessionManager.TryResolve(childDir);

        Assert.True(resolution.Found);
        Assert.Equal(session.RootPath, resolution.Session!.RootPath);
    }

    [Fact]
    public async Task FullPipeline_StructureViews_ReflectIndexedNodes()
    {
        WriteSource("src/Services/UserService.cs", """
            namespace SampleApp.Services;
            public class UserService
            {
                public void GetUser() {}
            }
            """);
        WriteSource("src/Models/User.cs", """
            namespace SampleApp.Models;
            public class User
            {
                public string Name;
            }
            """);

        var (session, nodes, _) = await IndexAsync();

        var files = nodes.Select(n => n.Location.FilePath).Distinct().ToArray();

        var tree = DirectoryTreeBuilder.Build(session.RootPath, files);
        var servicesDir = Assert.Single(tree.Children, c => c.Name == "src");
        Assert.Contains(servicesDir.Children, c => c.Name == "Services");
        Assert.Contains(servicesDir.Children, c => c.Name == "Models");

        var fileOutlines = FileOutlineBuilder.Build(nodes);
        Assert.Equal(2, fileOutlines.Count);
        Assert.Contains(fileOutlines, o => o.Nodes.Any(n => n.Name == "UserService"));

        var scopeOutline = ScopeOutlineBuilder.Build(nodes);
        var sampleApp = Assert.Single(scopeOutline, n => n.Name == "SampleApp");
        Assert.Contains(sampleApp.Children, c => c.Name == "Services");
        Assert.Contains(sampleApp.Children, c => c.Name == "Models");

        var located = FileLocator.Locate(files, "UserService");
        Assert.Single(located);
    }

    [Fact]
    public async Task FullPipeline_RelationshipEdges_SurviveParseResolveAndBinaryRoundTrip()
    {
        WriteSource("src/IGreeter.cs", """
            namespace SampleApp;
            public interface IGreeter
            {
                string Greet(string name);
            }
            """);
        WriteSource("src/Greeter.cs", """
            namespace SampleApp;
            public class Greeter : IGreeter
            {
                public string Greet(string name)
                {
                    return Format(name);
                }

                private string Format(string name)
                {
                    return "Hello, " + name;
                }
            }
            """);

        var (_, nodes, _) = await IndexAsync();

        var greeterClass = Assert.Single(nodes, n => n.Kind == NodeKind.Class && n.Name == "Greeter");
        var iGreeterInterface = Assert.Single(nodes, n => n.Kind == NodeKind.Interface && n.Name == "IGreeter");
        var greetMethod = Assert.Single(nodes, n => n.QualifiedName == "SampleApp.Greeter.Greet");
        var formatMethod = Assert.Single(nodes, n => n.QualifiedName == "SampleApp.Greeter.Format");

        // Implements edge survived Roslyn parsing -> RelationshipResolver -> JSON shard write -> JSON shard read.
        Assert.Contains(greeterClass.Edges, e => e.Kind == EdgeKind.Implements && e.TargetNodeId == iGreeterInterface.Id);

        // Call graph edge: Greet() calls Format().
        Assert.Contains(greetMethod.Edges, e => e.Kind == EdgeKind.Calls && e.TargetNodeId == formatMethod.Id);

        // Containment edge: the class contains both of its methods.
        Assert.Contains(greeterClass.Edges, e => e.Kind == EdgeKind.Contains && e.TargetNodeId == greetMethod.Id);
        Assert.Contains(greeterClass.Edges, e => e.Kind == EdgeKind.Contains && e.TargetNodeId == formatMethod.Id);

        // Phase 8 reference lookup, driven off the same persisted edges.
        var finder = new ReferenceFinder();
        var implementers = finder.GetSubtypes(nodes, iGreeterInterface.Id);
        Assert.Contains(implementers, n => n.Id == greeterClass.Id);

        var callersOfFormat = finder.GetCallers(nodes, formatMethod.Id);
        Assert.Contains(callersOfFormat, n => n.Id == greetMethod.Id);

        var calleesOfGreet = finder.GetCallees(nodes, greetMethod.Id);
        Assert.Contains(calleesOfGreet, n => n.Id == formatMethod.Id);
    }

    [Fact]
    public async Task FullPipeline_IncrementalUpdate_NoPriorIndex_FallsBackToFullIndex()
    {
        WriteSource("src/App.cs", "namespace App; public class Foo {}");

        var (_, nodes, result) = await UpdateAsync();

        Assert.True(result.FellBackToFullIndex);
        Assert.Contains(nodes, n => n.Name == "Foo");
    }

    [Fact]
    public async Task FullPipeline_IncrementalUpdate_UnchangedFiles_AreCarriedForwardNotReparsed()
    {
        WriteSource("src/App.cs", "namespace App; public class Foo {}");
        await IndexAsync();

        var (_, nodes, result) = await UpdateAsync();

        Assert.False(result.FellBackToFullIndex);
        Assert.Equal(1, result.FilesUnchanged);
        Assert.Equal(0, result.FilesChanged);
        Assert.Equal(0, result.FilesAdded);
        Assert.Contains(nodes, n => n.Name == "Foo");
    }

    [Fact]
    public async Task FullPipeline_IncrementalUpdate_ChangedFile_IsReparsedAndReplaced()
    {
        WriteSource("src/App.cs", "namespace App; public class Foo {}");
        await IndexAsync();

        File.WriteAllText(Path.Combine(_root, "src/App.cs"), "namespace App; public class Bar {}");
        var (_, nodes, result) = await UpdateAsync();

        Assert.Equal(1, result.FilesChanged);
        Assert.DoesNotContain(nodes, n => n.Name == "Foo");
        Assert.Contains(nodes, n => n.Name == "Bar");
    }

    [Fact]
    public async Task FullPipeline_IncrementalUpdate_NewFile_IsParsedAndAdded()
    {
        WriteSource("src/App.cs", "namespace App; public class Foo {}");
        await IndexAsync();

        WriteSource("src/New.cs", "namespace App; public class NewOne {}");
        var (_, nodes, result) = await UpdateAsync();

        Assert.Equal(1, result.FilesAdded);
        Assert.Contains(nodes, n => n.Name == "Foo");
        Assert.Contains(nodes, n => n.Name == "NewOne");
    }

    [Fact]
    public async Task FullPipeline_IncrementalUpdate_DeletedFile_DropsItsNodes()
    {
        WriteSource("src/App.cs", "namespace App; public class Foo {}");
        WriteSource("src/Gone.cs", "namespace App; public class Gone {}");
        await IndexAsync();

        File.Delete(Path.Combine(_root, "src/Gone.cs"));
        var (_, nodes, result) = await UpdateAsync();

        Assert.Equal(1, result.FilesRemoved);
        Assert.DoesNotContain(nodes, n => n.Name == "Gone");
        Assert.Contains(nodes, n => n.Name == "Foo");
    }

    [Fact]
    public async Task FullPipeline_IncrementalUpdate_RelationshipEdgesStillResolveAcrossCarriedForwardAndReparsedFiles()
    {
        WriteSource("src/IGreeter.cs", "namespace App; public interface IGreeter { string Greet(); }");
        WriteSource("src/Greeter.cs", "namespace App; public class Greeter : IGreeter { public string Greet() => \"hi\"; }");
        await IndexAsync();

        // Only IGreeter.cs changes; Greeter.cs is carried forward unparsed, but its
        // Implements edge must still resolve against the (possibly new-ID) interface node.
        File.WriteAllText(Path.Combine(_root, "src/IGreeter.cs"), "namespace App; public interface IGreeter { string Greet(); string Extra(); }");
        var (_, nodes, _) = await UpdateAsync();

        var iGreeter = Assert.Single(nodes, n => n.Kind == NodeKind.Interface);
        var greeter = Assert.Single(nodes, n => n.Kind == NodeKind.Class);
        Assert.Contains(greeter.Edges, e => e.Kind == EdgeKind.Implements && e.TargetNodeId == iGreeter.Id);
    }

    [Fact]
    public async Task FullPipeline_Verify_ReportsDriftThenCleanAfterUpdate()
    {
        WriteSource("src/App.cs", "namespace App; public class Foo {}");
        var (session, _, _) = await IndexAsync();

        File.WriteAllText(Path.Combine(_root, "src/App.cs"), "namespace App; public class Bar {}");

        var orchestrator = new IndexOrchestrator(_parsers, _indexStore);
        var driftBefore = orchestrator.DetectDrift(session);
        Assert.False(driftBefore.IsClean);
        Assert.Single(driftBefore.Changed);

        await orchestrator.RunIncrementalIndexAsync(session, CancellationToken.None);

        var driftAfter = orchestrator.DetectDrift(session);
        Assert.True(driftAfter.IsClean);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
