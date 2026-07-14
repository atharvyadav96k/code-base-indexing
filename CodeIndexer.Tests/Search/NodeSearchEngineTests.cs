using CodeIndexer.Core.Nodes;
using CodeIndexer.Search;
using Xunit;

namespace CodeIndexer.Tests.Search;

public class NodeSearchEngineTests
{
    private static CodeNode MakeNode(
        string name,
        NodeKind kind = NodeKind.Method,
        string qualifiedName = "",
        string filePath = "App/File.cs",
        bool isPublic = true,
        bool isTest = false,
        bool isAsync = false)
    {
        qualifiedName = string.IsNullOrEmpty(qualifiedName) ? name : qualifiedName;
        return new CodeNode
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            ScopeChain = qualifiedName.Split('.'),
            ScopeSeparator = ".",
            QualifiedName = qualifiedName,
            Kind = kind,
            Location = new NodeLocation { FilePath = filePath, StartLine = 1, EndLine = 5 },
            Summary = new NodeSummary { Name = name, Signature = name, Parameters = Array.Empty<ParameterInfo>(), LineCount = 5 },
            Body = name,
            Metadata = new NodeMetadata { IsPublic = isPublic, IsTest = isTest, IsAsync = isAsync },
            ContentHash = ContentHasher.Hash(name),
        };
    }

    [Fact]
    public void Search_ExactMatch_RanksAboveSubstringMatch()
    {
        var nodes = new[] { MakeNode("GetUser"), MakeNode("TryGetUserById") };
        var hits = new NodeSearchEngine().Search(nodes, new SearchQuery { NamePattern = "GetUser" });

        Assert.Equal(2, hits.Count);
        Assert.Equal("GetUser", hits[0].Name);
        Assert.True(hits[0].Score > hits[1].Score);
    }

    [Fact]
    public void Search_NoPattern_ReturnsAllMatchingFilters()
    {
        var nodes = new[] { MakeNode("A", NodeKind.Class), MakeNode("B", NodeKind.Method) };
        var hits = new NodeSearchEngine().Search(nodes, new SearchQuery { Kinds = new[] { NodeKind.Class } });

        Assert.Single(hits);
        Assert.Equal("A", hits[0].Name);
    }

    [Fact]
    public void Search_FiltersByQualifiedNamePrefix()
    {
        var nodes = new[]
        {
            MakeNode("Foo", qualifiedName: "App.Services.Foo"),
            MakeNode("Bar", qualifiedName: "App.Models.Bar"),
        };

        var hits = new NodeSearchEngine().Search(nodes, new SearchQuery { QualifiedNamePrefix = "App.Services" });

        Assert.Single(hits);
        Assert.Equal("Foo", hits[0].Name);
    }

    [Fact]
    public void Search_FiltersByMetadataFlags()
    {
        var nodes = new[]
        {
            MakeNode("PublicOne", isPublic: true),
            MakeNode("PrivateOne", isPublic: false),
        };

        var hits = new NodeSearchEngine().Search(nodes, new SearchQuery { IsPublic = true });

        Assert.Single(hits);
        Assert.Equal("PublicOne", hits[0].Name);
    }

    [Fact]
    public void Search_HitsExcludeBody()
    {
        var nodes = new[] { MakeNode("Foo") };

        var hits = new NodeSearchEngine().Search(nodes, new SearchQuery());

        // NodeSearchHit has no Body property at all — compile-time guarantee it can't leak.
        Assert.Equal("Foo", hits[0].Name);
    }

    [Fact]
    public void Search_FuzzySubsequenceMatch_FindsResultButRanksLow()
    {
        var nodes = new[] { MakeNode("GetUserById"), MakeNode("Unrelated") };

        var hits = new NodeSearchEngine().Search(nodes, new SearchQuery { NamePattern = "gtusrid" });

        Assert.Single(hits);
        Assert.Equal("GetUserById", hits[0].Name);
    }

    [Fact]
    public void Search_MaxResults_LimitsOutput()
    {
        var nodes = Enumerable.Range(0, 5).Select(i => MakeNode($"Item{i}")).ToArray();

        var hits = new NodeSearchEngine().Search(nodes, new SearchQuery { MaxResults = 2 });

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void Search_DefaultKindSetExcludingImportAndField_HidesReferenceNoise()
    {
        // Mirrors the CLI's default 'search' behavior: everything except Import/Field.
        var defaultKinds = Enum.GetValues<NodeKind>().Where(k => k is not (NodeKind.Import or NodeKind.Field)).ToArray();
        var nodes = new[]
        {
            MakeNode("Auth", NodeKind.Class),
            MakeNode("Auth", NodeKind.Import),
            MakeNode("Auth", NodeKind.Field),
        };

        var hits = new NodeSearchEngine().Search(nodes, new SearchQuery { NamePattern = "Auth", Kinds = defaultKinds });

        var hit = Assert.Single(hits);
        Assert.Equal(NodeKind.Class, hit.Kind);
    }

    [Fact]
    public void Search_NoKindsFilter_IncludesImportAndField()
    {
        var nodes = new[]
        {
            MakeNode("Auth", NodeKind.Class),
            MakeNode("Auth", NodeKind.Import),
            MakeNode("Auth", NodeKind.Field),
        };

        var hits = new NodeSearchEngine().Search(nodes, new SearchQuery { NamePattern = "Auth" });

        Assert.Equal(3, hits.Count);
    }

    [Fact]
    public void GetCode_KnownId_ReturnsBodyAndHash()
    {
        var node = MakeNode("Foo");
        var retriever = new NodeRetriever();

        var result = retriever.GetCode(new[] { node }, node.Id);

        Assert.True(result.Found);
        Assert.Equal(node.Body, result.Body);
        Assert.Equal(node.ContentHash, result.ContentHash);
    }

    [Fact]
    public void GetCode_UnknownId_ReturnsNotFound()
    {
        var retriever = new NodeRetriever();

        var result = retriever.GetCode(Array.Empty<CodeNode>(), "missing-id");

        Assert.False(result.Found);
    }
}
