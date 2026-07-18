using CodeIndexer.Core.Nodes;
using CodeIndexer.Indexing.Relationships;
using Xunit;

namespace CodeIndexer.Tests.Indexing;

public class RelationshipResolverTests
{
    private static CodeNode MakeNode(
        string name,
        string qualifiedName,
        NodeKind kind,
        string body = "",
        string filePath = "App.cs",
        NodeMetadata? metadata = null,
        IReadOnlyList<ParameterInfo>? parameters = null,
        string? returnType = null)
    {
        return new CodeNode
        {
            Id = filePath + "|" + qualifiedName + "|" + kind,
            Name = name,
            ScopeChain = qualifiedName.Split('.'),
            ScopeSeparator = ".",
            QualifiedName = qualifiedName,
            Kind = kind,
            Location = new NodeLocation { FilePath = filePath, StartLine = 1, EndLine = 5 },
            Summary = new NodeSummary
            {
                Name = name,
                Signature = name,
                Parameters = parameters ?? Array.Empty<ParameterInfo>(),
                ReturnType = returnType,
                LineCount = 5,
            },
            Body = body,
            Metadata = metadata ?? new NodeMetadata(),
            ContentHash = ContentHasher.Hash(body),
        };
    }

    [Fact]
    public void Resolve_DuplicateNodeIds_DoesNotThrow()
    {
        // Real large repos can produce duplicate node IDs (e.g. a file discovered
        // twice via a symlink/junction). Resolve must degrade gracefully, not crash.
        var node = MakeNode("Foo", "App.Foo", NodeKind.Class);
        var duplicate = MakeNode("Foo", "App.Foo", NodeKind.Class);

        var resolved = RelationshipResolver.Resolve(new[] { node, duplicate });

        Assert.Equal(2, resolved.Count);
    }

    [Fact]
    public void Resolve_ParentGetsContainsEdgeToChild()
    {
        var parent = MakeNode("Foo", "App.Foo", NodeKind.Class);
        var child = MakeNode("Bar", "App.Foo.Bar", NodeKind.Method);

        var resolved = RelationshipResolver.Resolve(new[] { parent, child });

        var resolvedParent = resolved.Single(n => n.Id == parent.Id);
        Assert.Contains(resolvedParent.Edges, e => e.Kind == EdgeKind.Contains && e.TargetNodeId == child.Id);
    }

    [Fact]
    public void Resolve_SameNamespaceReopenedAcrossFiles_AttributesContainmentToSameFileOnly()
    {
        // Two files both declare "namespace App.Services;" — a common, valid pattern.
        // Each produces its own Namespace node sharing the same QualifiedName.
        var nsInFileA = MakeNode("Services", "App.Services", NodeKind.Namespace, filePath: "A.cs");
        var classInFileA = MakeNode("Foo", "App.Services.Foo", NodeKind.Class, filePath: "A.cs");
        var nsInFileB = MakeNode("Services", "App.Services", NodeKind.Namespace, filePath: "B.cs");
        var classInFileB = MakeNode("Bar", "App.Services.Bar", NodeKind.Class, filePath: "B.cs");

        var resolved = RelationshipResolver.Resolve(new[] { nsInFileA, classInFileA, nsInFileB, classInFileB });

        var resolvedNsA = resolved.Single(n => n.Id == nsInFileA.Id);
        var resolvedNsB = resolved.Single(n => n.Id == nsInFileB.Id);

        Assert.Contains(resolvedNsA.Edges, e => e.TargetNodeId == classInFileA.Id);
        Assert.DoesNotContain(resolvedNsA.Edges, e => e.TargetNodeId == classInFileB.Id);

        Assert.Contains(resolvedNsB.Edges, e => e.TargetNodeId == classInFileB.Id);
        Assert.DoesNotContain(resolvedNsB.Edges, e => e.TargetNodeId == classInFileA.Id);
    }

    [Fact]
    public void Resolve_UnambiguousInterfaceImplementation_AddsImplementsEdge()
    {
        var iface = MakeNode("IShape", "App.IShape", NodeKind.Interface);
        var metadata = new NodeMetadata().WithBaseTypes(new[] { "IShape" });
        var cls = MakeNode("Circle", "App.Circle", NodeKind.Class, metadata: metadata);

        var resolved = RelationshipResolver.Resolve(new[] { iface, cls });

        var resolvedCls = resolved.Single(n => n.Id == cls.Id);
        Assert.Contains(resolvedCls.Edges, e => e.Kind == EdgeKind.Implements && e.TargetNodeId == iface.Id);
    }

    [Fact]
    public void Resolve_UnambiguousClassExtension_AddsInheritsEdge()
    {
        var baseClass = MakeNode("Animal", "App.Animal", NodeKind.Class);
        var metadata = new NodeMetadata().WithBaseTypes(new[] { "Animal" });
        var derived = MakeNode("Dog", "App.Dog", NodeKind.Class, metadata: metadata);

        var resolved = RelationshipResolver.Resolve(new[] { baseClass, derived });

        var resolvedDerived = resolved.Single(n => n.Id == derived.Id);
        Assert.Contains(resolvedDerived.Edges, e => e.Kind == EdgeKind.Inherits && e.TargetNodeId == baseClass.Id);
    }

    [Fact]
    public void Resolve_AmbiguousBaseTypeName_SkipsEdgeRatherThanGuessing()
    {
        var first = MakeNode("Handler", "App.Feature1.Handler", NodeKind.Class);
        var second = MakeNode("Handler", "App.Feature2.Handler", NodeKind.Class);
        var metadata = new NodeMetadata().WithBaseTypes(new[] { "Handler" });
        var derived = MakeNode("MyHandler", "App.MyHandler", NodeKind.Class, metadata: metadata);

        var resolved = RelationshipResolver.Resolve(new[] { first, second, derived });

        var resolvedDerived = resolved.Single(n => n.Id == derived.Id);
        Assert.DoesNotContain(resolvedDerived.Edges, e => e.Kind is EdgeKind.Inherits or EdgeKind.Implements);
        Assert.Contains(resolvedDerived.SkippedRelationships, note => note.Contains("'Handler'"));
    }

    [Fact]
    public void Resolve_UnresolvableExternalBaseType_ProducesNoEdgeAndNoSkipNote()
    {
        // Zero candidates (unresolved/external) is expected and distinct from
        // ambiguity (>1 candidates) — it must stay silent, not produce a note.
        var metadata = new NodeMetadata().WithBaseTypes(new[] { "System.IDisposable" });
        var cls = MakeNode("Resource", "App.Resource", NodeKind.Class, metadata: metadata);

        var resolved = RelationshipResolver.Resolve(new[] { cls });

        var resolvedCls = resolved.Single();
        Assert.Empty(resolvedCls.Edges);
        Assert.Empty(resolvedCls.SkippedRelationships);
    }

    [Fact]
    public void Resolve_UnambiguousMethodCall_AddsCallsEdge()
    {
        var callee = MakeNode("Helper", "App.Helper", NodeKind.Method);
        var caller = MakeNode("DoWork", "App.DoWork", NodeKind.Method, body: "public void DoWork() { Helper(); }");

        var resolved = RelationshipResolver.Resolve(new[] { callee, caller });

        var resolvedCaller = resolved.Single(n => n.Id == caller.Id);
        Assert.Contains(resolvedCaller.Edges, e => e.Kind == EdgeKind.Calls && e.TargetNodeId == callee.Id);
    }

    [Fact]
    public void Resolve_AmbiguousCallTarget_SkipsEdge()
    {
        var callee1 = MakeNode("Save", "App.RepoA.Save", NodeKind.Method);
        var callee2 = MakeNode("Save", "App.RepoB.Save", NodeKind.Method);
        var caller = MakeNode("Commit", "App.Commit", NodeKind.Method, body: "public void Commit() { Save(); }");

        var resolved = RelationshipResolver.Resolve(new[] { callee1, callee2, caller });

        var resolvedCaller = resolved.Single(n => n.Id == caller.Id);
        Assert.DoesNotContain(resolvedCaller.Edges, e => e.Kind == EdgeKind.Calls);
        Assert.Contains(resolvedCaller.SkippedRelationships, note => note.Contains("'Save'"));
    }

    [Fact]
    public void Resolve_CallAmbiguousBetweenInterfaceAndItsImplementation_ResolvesToBoth()
    {
        var iface = MakeNode("Bar", "App.IFoo.Bar", NodeKind.Method, filePath: "IFoo.cs");
        var ifaceType = MakeNode("IFoo", "App.IFoo", NodeKind.Interface, filePath: "IFoo.cs");
        var metadata = new NodeMetadata().WithBaseTypes(new[] { "IFoo" });
        var implType = MakeNode("Foo", "App.Foo", NodeKind.Class, metadata: metadata, filePath: "Foo.cs");
        var impl = MakeNode("Bar", "App.Foo.Bar", NodeKind.Method, filePath: "Foo.cs");
        var caller = MakeNode("Commit", "App.Commit", NodeKind.Method, body: "public void Commit() { Bar(); }", filePath: "Caller.cs");

        var resolved = RelationshipResolver.Resolve(new[] { ifaceType, iface, implType, impl, caller });

        var resolvedCaller = resolved.Single(n => n.Id == caller.Id);
        Assert.Contains(resolvedCaller.Edges, e => e.Kind == EdgeKind.Calls && e.TargetNodeId == iface.Id);
        Assert.Contains(resolvedCaller.Edges, e => e.Kind == EdgeKind.Calls && e.TargetNodeId == impl.Id);
        Assert.DoesNotContain(resolvedCaller.SkippedRelationships, note => note.Contains("'Bar'"));
    }

    [Fact]
    public void Resolve_CallAmbiguousBetweenBaseVirtualMethodAndOverride_ResolvesToBoth()
    {
        var baseType = MakeNode("Animal", "App.Animal", NodeKind.Class, filePath: "Animal.cs");
        var baseMethod = MakeNode("Speak", "App.Animal.Speak", NodeKind.Method, filePath: "Animal.cs");
        var metadata = new NodeMetadata().WithBaseTypes(new[] { "Animal" });
        var derivedType = MakeNode("Dog", "App.Dog", NodeKind.Class, metadata: metadata, filePath: "Dog.cs");
        var derivedMethod = MakeNode("Speak", "App.Dog.Speak", NodeKind.Method, filePath: "Dog.cs");
        var caller = MakeNode("Commit", "App.Commit", NodeKind.Method, body: "public void Commit() { Speak(); }", filePath: "Caller.cs");

        var resolved = RelationshipResolver.Resolve(new[] { baseType, baseMethod, derivedType, derivedMethod, caller });

        var resolvedCaller = resolved.Single(n => n.Id == caller.Id);
        Assert.Contains(resolvedCaller.Edges, e => e.Kind == EdgeKind.Calls && e.TargetNodeId == baseMethod.Id);
        Assert.Contains(resolvedCaller.Edges, e => e.Kind == EdgeKind.Calls && e.TargetNodeId == derivedMethod.Id);
        Assert.DoesNotContain(resolvedCaller.SkippedRelationships, note => note.Contains("'Speak'"));
    }

    [Fact]
    public void Resolve_CallAmbiguousAcrossMultipleImplementersOfSameInterface_ResolvesToAll()
    {
        var iface = MakeNode("Bar", "App.IFoo.Bar", NodeKind.Method, filePath: "IFoo.cs");
        var ifaceType = MakeNode("IFoo", "App.IFoo", NodeKind.Interface, filePath: "IFoo.cs");
        var metadata = new NodeMetadata().WithBaseTypes(new[] { "IFoo" });
        var implTypeA = MakeNode("FooA", "App.FooA", NodeKind.Class, metadata: metadata, filePath: "FooA.cs");
        var implA = MakeNode("Bar", "App.FooA.Bar", NodeKind.Method, filePath: "FooA.cs");
        var implTypeB = MakeNode("FooB", "App.FooB", NodeKind.Class, metadata: metadata, filePath: "FooB.cs");
        var implB = MakeNode("Bar", "App.FooB.Bar", NodeKind.Method, filePath: "FooB.cs");
        var caller = MakeNode("Commit", "App.Commit", NodeKind.Method, body: "public void Commit() { Bar(); }", filePath: "Caller.cs");

        var resolved = RelationshipResolver.Resolve(new[] { ifaceType, iface, implTypeA, implA, implTypeB, implB, caller });

        var resolvedCaller = resolved.Single(n => n.Id == caller.Id);
        Assert.Contains(resolvedCaller.Edges, e => e.Kind == EdgeKind.Calls && e.TargetNodeId == iface.Id);
        Assert.Contains(resolvedCaller.Edges, e => e.Kind == EdgeKind.Calls && e.TargetNodeId == implA.Id);
        Assert.Contains(resolvedCaller.Edges, e => e.Kind == EdgeKind.Calls && e.TargetNodeId == implB.Id);
        Assert.DoesNotContain(resolvedCaller.SkippedRelationships, note => note.Contains("'Bar'"));
    }

    [Fact]
    public void Resolve_ControlFlowKeywordsAndSelfCalls_AreNotTreatedAsCalls()
    {
        var caller = MakeNode(
            "Recurse",
            "App.Recurse",
            NodeKind.Method,
            body: "public void Recurse() { if (true) { Recurse(); } for (int i = 0; i < 1; i++) {} }");

        var resolved = RelationshipResolver.Resolve(new[] { caller });

        var resolvedCaller = resolved.Single();
        Assert.Empty(resolvedCaller.Edges);
    }

    [Fact]
    public void Resolve_ImportMatchingNamespace_AddsImportsEdge()
    {
        var ns = MakeNode("Services", "App.Services", NodeKind.Namespace);
        var import = MakeNode("App.Services", "App.Services", NodeKind.Import, filePath: "Other.cs");

        var resolved = RelationshipResolver.Resolve(new[] { ns, import });

        var resolvedImport = resolved.Single(n => n.Id == import.Id);
        Assert.Contains(resolvedImport.Edges, e => e.Kind == EdgeKind.Imports && e.TargetNodeId == ns.Id);
    }

    [Fact]
    public void Resolve_ImportWithNoMatchingNamespace_ProducesNoEdge()
    {
        var import = MakeNode("react", "react", NodeKind.Import);

        var resolved = RelationshipResolver.Resolve(new[] { import });

        Assert.Empty(resolved.Single().Edges);
    }

    [Fact]
    public void Resolve_ConstructorParameterTypingAnotherClass_AddsReferencesEdge()
    {
        // The pattern this exists for: constructor(private authService: AuthService)
        var authService = MakeNode("AuthService", "App.AuthService", NodeKind.Class);
        var ctor = MakeNode(
            "constructor",
            "App.AuthController.constructor",
            NodeKind.Method,
            parameters: new[] { new ParameterInfo { Name = "authService", Type = "AuthService" } });

        var resolved = RelationshipResolver.Resolve(new[] { authService, ctor });

        var resolvedCtor = resolved.Single(n => n.Id == ctor.Id);
        Assert.Contains(resolvedCtor.Edges, e => e.Kind == EdgeKind.References && e.TargetNodeId == authService.Id);
    }

    [Fact]
    public void Resolve_FieldTypedAsAnotherClass_AddsReferencesEdge()
    {
        var repository = MakeNode("IOrderRepository", "App.IOrderRepository", NodeKind.Interface);
        var field = MakeNode("_repository", "App.OrderService._repository", NodeKind.Field, returnType: "IOrderRepository");

        var resolved = RelationshipResolver.Resolve(new[] { repository, field });

        var resolvedField = resolved.Single(n => n.Id == field.Id);
        Assert.Contains(resolvedField.Edges, e => e.Kind == EdgeKind.References && e.TargetNodeId == repository.Id);
    }

    [Fact]
    public void Resolve_TypeWrappedInGeneric_UnwrapsOneLevelToFindReference()
    {
        // Task<AuthService> / Promise<AuthService>-shaped return types.
        var authService = MakeNode("AuthService", "App.AuthService", NodeKind.Class);
        var method = MakeNode("GetAuth", "App.Foo.GetAuth", NodeKind.Method, returnType: "Task<AuthService>");

        var resolved = RelationshipResolver.Resolve(new[] { authService, method });

        var resolvedMethod = resolved.Single(n => n.Id == method.Id);
        Assert.Contains(resolvedMethod.Edges, e => e.Kind == EdgeKind.References && e.TargetNodeId == authService.Id);
    }

    [Fact]
    public void Resolve_AmbiguousTypeName_SkipsReferenceEdge()
    {
        var first = MakeNode("Handler", "App.Feature1.Handler", NodeKind.Class);
        var second = MakeNode("Handler", "App.Feature2.Handler", NodeKind.Class);
        var method = MakeNode(
            "Process",
            "App.Foo.Process",
            NodeKind.Method,
            parameters: new[] { new ParameterInfo { Name = "handler", Type = "Handler" } });

        var resolved = RelationshipResolver.Resolve(new[] { first, second, method });

        var resolvedMethod = resolved.Single(n => n.Id == method.Id);
        Assert.DoesNotContain(resolvedMethod.Edges, e => e.Kind == EdgeKind.References);
        Assert.Contains(resolvedMethod.SkippedRelationships, note => note.Contains("'Handler'"));
    }
}
