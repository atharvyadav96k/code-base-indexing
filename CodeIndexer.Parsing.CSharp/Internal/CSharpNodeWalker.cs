using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeIndexer.Core.Nodes;
using CodeIndexer.Core.Scope;

namespace CodeIndexer.Parsing.CSharp.Internal;

/// <summary>
/// Walks a C# syntax tree, emitting shared <see cref="CodeNode"/>s for every
/// construct the shared model understands. Purely syntax-driven — no semantic
/// model/cross-file resolution is needed to produce correct per-file nodes.
/// </summary>
internal sealed class CSharpNodeWalker : CSharpSyntaxWalker
{
    private const string ScopeSeparator = ".";

    private readonly string _filePath;
    private readonly SyntaxTree _tree;
    private readonly Stack<string> _scope = new();
    private readonly List<CodeNode> _nodes = new();

    public CSharpNodeWalker(string filePath, SyntaxTree tree)
        : base(SyntaxWalkerDepth.Node)
    {
        _filePath = filePath;
        _tree = tree;
    }

    public IReadOnlyList<CodeNode> Nodes => _nodes;

    public override void VisitCompilationUnit(CompilationUnitSyntax node)
    {
        foreach (var usingDirective in node.Usings)
        {
            EmitImport(usingDirective);
        }

        base.VisitCompilationUnit(node);
    }

    public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
    {
        foreach (var usingDirective in node.Usings)
        {
            EmitImport(usingDirective);
        }

        VisitWithScope(node.Name.ToString(), () => base.VisitFileScopedNamespaceDeclaration(node), emitNamespaceNode: node);
    }

    public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        foreach (var usingDirective in node.Usings)
        {
            EmitImport(usingDirective);
        }

        VisitWithScope(node.Name.ToString(), () => base.VisitNamespaceDeclaration(node), emitNamespaceNode: node);
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        EmitTypeAndRecurse(node, NodeKind.Class, "class", () => base.VisitClassDeclaration(node));
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        EmitTypeAndRecurse(node, NodeKind.Interface, "interface", () => base.VisitInterfaceDeclaration(node));
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        EmitTypeAndRecurse(node, NodeKind.Struct, "struct", () => base.VisitStructDeclaration(node));
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        var isTopLevel = _scope.Count == 0;
        var signature = SignatureBuilder.ForEnum(node);
        var metadata = MetadataBuilder.Build(node.Modifiers, isTopLevel, node.AttributeLists);
        Emit(NodeKind.Enum, node.Identifier.Text, node, signature, Array.Empty<ParameterInfo>(), null, metadata);
        // Enum members are values, not modeled as separate nodes in v1 — skip descending further.
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var parameters = node.ParameterList.Parameters
            .Select(p => new ParameterInfo { Name = p.Identifier.Text, Type = p.Type?.ToString() ?? "var" })
            .ToArray();

        var signature = SignatureBuilder.ForMethod(node);
        var metadata = MetadataBuilder.Build(node.Modifiers, isTopLevelType: false, node.AttributeLists);
        Emit(NodeKind.Method, node.Identifier.Text, node, signature, parameters, node.ReturnType.ToString(), metadata);
        // Do not descend into method bodies — local functions/lambdas are not modeled as nodes in v1.
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        var signature = SignatureBuilder.ForProperty(node);
        var metadata = MetadataBuilder.Build(node.Modifiers, isTopLevelType: false, node.AttributeLists);
        Emit(NodeKind.Property, node.Identifier.Text, node, signature, Array.Empty<ParameterInfo>(), node.Type.ToString(), metadata);
    }

    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        var isConst = node.Modifiers.Any(SyntaxKind.ConstKeyword);
        var metadata = MetadataBuilder.Build(node.Modifiers, isTopLevelType: false, node.AttributeLists);

        foreach (var variable in node.Declaration.Variables)
        {
            var signature = SignatureBuilder.ForField(node, variable);
            Emit(isConst ? NodeKind.Constant : NodeKind.Field, variable.Identifier.Text, node, signature, Array.Empty<ParameterInfo>(), node.Declaration.Type.ToString(), metadata);
        }
    }

    private void VisitWithScope(string name, Action visitChildren, SyntaxNode? emitNamespaceNode)
    {
        if (emitNamespaceNode is not null)
        {
            var signature = "namespace " + ScopeNameBuilder.Build(_scope.Reverse().Append(name).ToArray(), ScopeSeparator);
            Emit(NodeKind.Namespace, name, emitNamespaceNode, signature, Array.Empty<ParameterInfo>(), null, new NodeMetadata { IsPublic = true });
        }

        _scope.Push(name);
        visitChildren();
        _scope.Pop();
    }

    private void EmitTypeAndRecurse(TypeDeclarationSyntax node, NodeKind kind, string keyword, Action visitChildren)
    {
        var isTopLevel = _scope.Count == 0;
        var signature = SignatureBuilder.ForType(node, keyword);
        var metadata = MetadataBuilder.Build(node.Modifiers, isTopLevel, node.AttributeLists);
        Emit(kind, node.Identifier.Text, node, signature, Array.Empty<ParameterInfo>(), null, metadata);

        _scope.Push(node.Identifier.Text);
        visitChildren();
        _scope.Pop();
    }

    private void EmitImport(UsingDirectiveSyntax node)
    {
        var name = node.Name?.ToString() ?? node.NamespaceOrType.ToString();
        var signature = node.ToString();
        Emit(NodeKind.Import, name, node, signature, Array.Empty<ParameterInfo>(), null, new NodeMetadata());
    }

    private void Emit(
        NodeKind kind,
        string name,
        SyntaxNode declarationSyntax,
        string signature,
        IReadOnlyList<ParameterInfo> parameters,
        string? returnType,
        NodeMetadata metadata)
    {
        var scopeChain = _scope.Reverse().Append(name).ToArray();
        var qualifiedName = ScopeNameBuilder.Build(scopeChain, ScopeSeparator);

        var lineSpan = _tree.GetLineSpan(declarationSyntax.Span);
        var startLine = lineSpan.StartLinePosition.Line + 1;
        var endLine = lineSpan.EndLinePosition.Line + 1;

        var body = declarationSyntax.ToString();
        var docComment = DocCommentExtractor.Extract(declarationSyntax);

        var summary = new NodeSummary
        {
            Name = name,
            Signature = signature,
            Parameters = parameters,
            ReturnType = returnType,
            DocComment = docComment,
            LineCount = endLine - startLine + 1,
        };

        var id = NodeIdFactory.Create(_filePath, qualifiedName, kind, signature);

        _nodes.Add(new CodeNode
        {
            Id = id,
            Name = name,
            ScopeChain = scopeChain,
            ScopeSeparator = ScopeSeparator,
            QualifiedName = qualifiedName,
            Kind = kind,
            Location = new NodeLocation { FilePath = _filePath, StartLine = startLine, EndLine = endLine },
            Summary = summary,
            Body = body,
            Metadata = metadata,
            ContentHash = ContentHasher.Hash(body),
        });
    }
}
