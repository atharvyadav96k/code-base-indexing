using CodeIndexer.Core.Nodes;
using CodeIndexer.Search.Relationships;
using Xunit;

namespace CodeIndexer.Tests.Search;

public class ReferenceFinderTests
{
    private static CodeNode MakeNode(string id, string name, NodeKind kind, IReadOnlyList<NodeEdge>? edges = null)
    {
        return new CodeNode
        {
            Id = id,
            Name = name,
            ScopeChain = new[] { name },
            ScopeSeparator = ".",
            QualifiedName = name,
            Kind = kind,
            Location = new NodeLocation { FilePath = "App.cs", StartLine = 1, EndLine = 5 },
            Summary = new NodeSummary { Name = name, Signature = name, Parameters = Array.Empty<ParameterInfo>(), LineCount = 5 },
            Body = name,
            Metadata = new NodeMetadata(),
            ContentHash = ContentHasher.Hash(name),
            Edges = edges ?? Array.Empty<NodeEdge>(),
        };
    }

    [Fact]
    public void FindReferences_ReturnsEveryNodeThatPointsAtTarget()
    {
        var target = MakeNode("target", "Target", NodeKind.Method);
        var caller = MakeNode("caller", "Caller", NodeKind.Method, new[] { new NodeEdge { Kind = EdgeKind.Calls, TargetNodeId = "target" } });
        var subclass = MakeNode("sub", "Sub", NodeKind.Class, new[] { new NodeEdge { Kind = EdgeKind.Inherits, TargetNodeId = "target" } });
        var unrelated = MakeNode("other", "Other", NodeKind.Method);

        var hits = new ReferenceFinder().FindReferences(new[] { target, caller, subclass, unrelated }, "target");

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.Source.Id == "caller" && h.Kind == EdgeKind.Calls);
        Assert.Contains(hits, h => h.Source.Id == "sub" && h.Kind == EdgeKind.Inherits);
    }

    [Fact]
    public void GetCallers_FiltersToCallsEdgesOnly()
    {
        var target = MakeNode("target", "Target", NodeKind.Method);
        var caller = MakeNode("caller", "Caller", NodeKind.Method, new[] { new NodeEdge { Kind = EdgeKind.Calls, TargetNodeId = "target" } });
        var subclass = MakeNode("sub", "Sub", NodeKind.Class, new[] { new NodeEdge { Kind = EdgeKind.Inherits, TargetNodeId = "target" } });

        var callers = new ReferenceFinder().GetCallers(new[] { target, caller, subclass }, "target");

        var result = Assert.Single(callers);
        Assert.Equal("caller", result.Id);
    }

    [Fact]
    public void GetCallees_ResolvesOutgoingCallEdgesToNodes()
    {
        var callee = MakeNode("callee", "Callee", NodeKind.Method);
        var caller = MakeNode("caller", "Caller", NodeKind.Method, new[] { new NodeEdge { Kind = EdgeKind.Calls, TargetNodeId = "callee" } });

        var callees = new ReferenceFinder().GetCallees(new[] { callee, caller }, "caller");

        var result = Assert.Single(callees);
        Assert.Equal("callee", result.Id);
    }

    [Fact]
    public void GetCallees_DuplicateNodeIdsInProject_DoesNotThrow()
    {
        // Real large repos can produce duplicate node IDs (e.g. a file discovered
        // twice via a symlink). GetCallees must degrade gracefully, not crash.
        var callee = MakeNode("callee", "Callee", NodeKind.Method);
        var duplicateOfCallee = MakeNode("callee", "Callee", NodeKind.Method);
        var caller = MakeNode("caller", "Caller", NodeKind.Method, new[] { new NodeEdge { Kind = EdgeKind.Calls, TargetNodeId = "callee" } });

        var callees = new ReferenceFinder().GetCallees(new[] { callee, duplicateOfCallee, caller }, "caller");

        Assert.Single(callees);
    }

    [Fact]
    public void GetCallees_UnknownSourceId_ReturnsEmpty()
    {
        var callees = new ReferenceFinder().GetCallees(Array.Empty<CodeNode>(), "missing");

        Assert.Empty(callees);
    }

    [Fact]
    public void GetSubtypes_FiltersToInheritsAndImplementsEdges()
    {
        var target = MakeNode("target", "Target", NodeKind.Interface);
        var implementer = MakeNode("impl", "Impl", NodeKind.Class, new[] { new NodeEdge { Kind = EdgeKind.Implements, TargetNodeId = "target" } });
        var caller = MakeNode("caller", "Caller", NodeKind.Method, new[] { new NodeEdge { Kind = EdgeKind.Calls, TargetNodeId = "target" } });

        var subtypes = new ReferenceFinder().GetSubtypes(new[] { target, implementer, caller }, "target");

        var result = Assert.Single(subtypes);
        Assert.Equal("impl", result.Id);
    }

    [Fact]
    public void GetChildren_ResolvesContainsEdgesToDirectMembers()
    {
        var method = MakeNode("method", "DoWork", NodeKind.Method);
        var field = MakeNode("field", "_state", NodeKind.Field);
        var container = MakeNode("class", "Worker", NodeKind.Class, new[]
        {
            new NodeEdge { Kind = EdgeKind.Contains, TargetNodeId = "method" },
            new NodeEdge { Kind = EdgeKind.Contains, TargetNodeId = "field" },
        });
        var unrelated = MakeNode("other", "Other", NodeKind.Method);

        var children = new ReferenceFinder().GetChildren(new[] { method, field, container, unrelated }, "class");

        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.Id == "method");
        Assert.Contains(children, c => c.Id == "field");
    }

    [Fact]
    public void GetChildren_UnknownContainerId_ReturnsEmpty()
    {
        var children = new ReferenceFinder().GetChildren(Array.Empty<CodeNode>(), "missing");

        Assert.Empty(children);
    }

    [Fact]
    public void GetUsages_FiltersToReferencesEdgesOnly()
    {
        var target = MakeNode("AuthService", "AuthService", NodeKind.Class);
        var ctor = MakeNode("ctor", "AuthController.ctor", NodeKind.Method, new[] { new NodeEdge { Kind = EdgeKind.References, TargetNodeId = "target" } });
        var caller = MakeNode("caller", "Caller", NodeKind.Method, new[] { new NodeEdge { Kind = EdgeKind.Calls, TargetNodeId = "target" } });

        var usages = new ReferenceFinder().GetUsages(new[] { target, ctor, caller }, "target");

        var result = Assert.Single(usages);
        Assert.Equal("ctor", result.Id);
    }
}
