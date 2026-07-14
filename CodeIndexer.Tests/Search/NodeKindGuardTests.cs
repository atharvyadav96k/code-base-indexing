using CodeIndexer.Core.Nodes;
using CodeIndexer.Search.Relationships;
using Xunit;

namespace CodeIndexer.Tests.Search;

public class NodeKindGuardTests
{
    private static CodeNode MakeNode(string id, NodeKind kind) => new()
    {
        Id = id,
        Name = "Foo",
        ScopeChain = new[] { "Foo" },
        ScopeSeparator = ".",
        QualifiedName = "Foo",
        Kind = kind,
        Location = new NodeLocation { FilePath = "App.cs", StartLine = 1, EndLine = 5 },
        Summary = new NodeSummary { Name = "Foo", Signature = "Foo", Parameters = Array.Empty<ParameterInfo>(), LineCount = 5 },
        Body = "Foo",
        Metadata = new NodeMetadata(),
        ContentHash = ContentHasher.Hash("Foo"),
    };

    [Fact]
    public void Validate_KindInAllowedList_ReturnsNull()
    {
        var node = MakeNode("id", NodeKind.Method);

        var error = NodeKindGuard.Validate(node, "callers", NodeKind.Method);

        Assert.Null(error);
    }

    [Fact]
    public void Validate_KindNotInAllowedList_ReturnsExplanatoryMessage()
    {
        var node = MakeNode("id", NodeKind.Class);

        var error = NodeKindGuard.Validate(node, "callers", NodeKind.Method);

        Assert.NotNull(error);
        Assert.Contains("callers", error);
        Assert.Contains("Method", error);
        Assert.Contains("Class", error);
        Assert.Contains("id", error);
    }
}
