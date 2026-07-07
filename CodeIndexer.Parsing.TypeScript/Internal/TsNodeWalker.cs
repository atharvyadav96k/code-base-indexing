using CodeIndexer.Core.Nodes;
using CodeIndexer.Core.Scope;
using Zu.TypeScript.TsTypes;

namespace CodeIndexer.Parsing.TypeScript.Internal;

/// <summary>
/// Walks a TS AST (from TypeScriptAST), emitting shared <see cref="CodeNode"/>s.
/// Purely syntax-driven, like the C# and JS walkers: no cross-file resolution,
/// and it deliberately does not descend into function/method bodies — only
/// declarations that shape the browsable scope tree are modeled.
/// </summary>
internal sealed class TsNodeWalker
{
    private const string ScopeSeparator = ".";

    private readonly string _filePath;
    private readonly string _sourceText;
    private readonly LineIndex _lineIndex;
    private readonly Stack<string> _scope = new();
    private readonly List<CodeNode> _nodes = new();

    public TsNodeWalker(string filePath, string sourceText)
    {
        _filePath = filePath;
        _sourceText = sourceText;
        _lineIndex = new LineIndex(sourceText);
    }

    public IReadOnlyList<CodeNode> Nodes => _nodes;

    public void Walk(Node root) => WalkChildren(root.Children);

    private void WalkChildren(IEnumerable<Node> children)
    {
        foreach (var child in children)
        {
            WalkNode(child);
        }
    }

    private void WalkNode(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ClassDeclaration:
                EmitTypeAndRecurse((ClassDeclaration)node, NodeKind.Class, "class");
                break;
            case SyntaxKind.InterfaceDeclaration:
                EmitTypeAndRecurse((InterfaceDeclaration)node, NodeKind.Interface, "interface");
                break;
            case SyntaxKind.EnumDeclaration:
                EmitEnum((EnumDeclaration)node);
                break;
            case SyntaxKind.ModuleDeclaration:
                EmitNamespaceAndRecurse((ModuleDeclaration)node);
                break;
            case SyntaxKind.TypeAliasDeclaration:
                EmitTypeAlias((TypeAliasDeclaration)node);
                break;
            case SyntaxKind.FunctionDeclaration:
                EmitFunction((FunctionDeclaration)node);
                break;
            case SyntaxKind.MethodDeclaration:
                EmitMethodLike(node, node.IdentifierStr, ((MethodDeclaration)node).Parameters.Cast<Node>(), ((MethodDeclaration)node).Type, prefix: string.Empty);
                break;
            case SyntaxKind.Constructor:
                EmitMethodLike(node, "constructor", ((ConstructorDeclaration)node).Parameters.Cast<Node>(), ((ConstructorDeclaration)node).Type, prefix: string.Empty);
                break;
            case SyntaxKind.GetAccessor:
                EmitMethodLike(node, node.IdentifierStr, ((GetAccessorDeclaration)node).Parameters.Cast<Node>(), ((GetAccessorDeclaration)node).Type, prefix: "get ");
                break;
            case SyntaxKind.SetAccessor:
                EmitMethodLike(node, node.IdentifierStr, ((SetAccessorDeclaration)node).Parameters.Cast<Node>(), ((SetAccessorDeclaration)node).Type, prefix: "set ");
                break;
            case SyntaxKind.PropertyDeclaration:
                EmitProperty((PropertyDeclaration)node);
                break;
            case SyntaxKind.PropertySignature:
                EmitInterfaceProperty(node);
                break;
            case SyntaxKind.MethodSignature:
                EmitInterfaceMethod(node);
                break;
            case SyntaxKind.VariableStatement:
                EmitVariableStatement((VariableStatement)node);
                break;
            case SyntaxKind.ImportDeclaration:
                EmitImport((ImportDeclaration)node);
                break;
            default:
                WalkChildren(node.Children);
                break;
        }
    }

    private void EmitTypeAndRecurse(Node node, NodeKind kind, string keyword)
    {
        var name = string.IsNullOrEmpty(node.IdentifierStr) ? "default" : node.IdentifierStr;
        var baseTypeNames = BaseTypeNamesOf(node).ToArray();
        var heritage = HeritageOf(node);
        var signature = $"{keyword} {name}{heritage}";

        var metadata = MetadataOf(node, isTopLevel: true).WithBaseTypes(baseTypeNames);
        Emit(kind, name, node, signature, Array.Empty<ParameterInfo>(), null, metadata);

        _scope.Push(name);
        WalkChildren(MembersOf(node));
        _scope.Pop();
    }

    private static IEnumerable<Node> MembersOf(Node node) => node switch
    {
        ClassDeclaration cls => cls.Members.Cast<Node>(),
        InterfaceDeclaration iface => iface.Members.Cast<Node>(),
        _ => Enumerable.Empty<Node>(),
    };

    private static IEnumerable<HeritageClause> HeritageClausesOf(Node node) => node switch
    {
        ClassDeclaration cls => cls.HeritageClauses?.Cast<HeritageClause>() ?? Enumerable.Empty<HeritageClause>(),
        InterfaceDeclaration iface => iface.HeritageClauses?.Cast<HeritageClause>() ?? Enumerable.Empty<HeritageClause>(),
        _ => Enumerable.Empty<HeritageClause>(),
    };

    private IEnumerable<string> BaseTypeNamesOf(Node node) =>
        HeritageClausesOf(node).SelectMany(clause => clause.Types.Select(t => t.GetText(_sourceText)));

    private string HeritageOf(Node node)
    {
        var parts = HeritageClausesOf(node).Select(clause =>
        {
            var keyword = clause.Token == SyntaxKind.ExtendsKeyword ? "extends" : "implements";
            var types = string.Join(", ", clause.Types.Select(t => t.GetText(_sourceText)));
            return $"{keyword} {types}";
        });

        var joined = string.Join(" ", parts);
        return joined.Length == 0 ? string.Empty : " " + joined;
    }

    private void EmitEnum(EnumDeclaration node)
    {
        var name = node.IdentifierStr;
        var signature = $"enum {name}";
        Emit(NodeKind.Enum, name, node, signature, Array.Empty<ParameterInfo>(), null, MetadataOf(node, isTopLevel: true));
    }

    private void EmitNamespaceAndRecurse(ModuleDeclaration node)
    {
        var name = node.IdentifierStr;
        var signature = $"namespace {name}";
        Emit(NodeKind.Namespace, name, node, signature, Array.Empty<ParameterInfo>(), null, MetadataOf(node, isTopLevel: true));

        _scope.Push(name);
        WalkChildren(node.Body?.Children ?? Enumerable.Empty<Node>());
        _scope.Pop();
    }

    private void EmitTypeAlias(TypeAliasDeclaration node)
    {
        var name = node.IdentifierStr;
        var typeText = node.Type?.GetText(_sourceText) ?? "unknown";
        var signature = $"type {name} = {typeText}";

        // No dedicated "type alias" kind in the shared taxonomy — a named type
        // shape is closest in spirit to an interface, so it's mapped there.
        Emit(NodeKind.Interface, name, node, signature, Array.Empty<ParameterInfo>(), null, MetadataOf(node, isTopLevel: true));
    }

    private void EmitFunction(FunctionDeclaration node)
    {
        var name = string.IsNullOrEmpty(node.IdentifierStr) ? "default" : node.IdentifierStr;
        var parameters = ParamRenderer.RenderAll(node.Parameters, _sourceText);
        var paramList = string.Join(", ", parameters.Select(p => $"{p.Name}: {p.Type}"));
        var returnType = node.Type?.GetText(_sourceText);
        var metadata = MetadataOf(node, isTopLevel: true, extraGenerator: node.AsteriskToken is not null);
        var asyncText = metadata.IsAsync ? "async " : string.Empty;
        var generatorMark = node.AsteriskToken is not null ? "*" : string.Empty;
        var signature = $"{asyncText}function{generatorMark} {name}({paramList}){(returnType is null ? string.Empty : $": {returnType}")}";

        Emit(NodeKind.Method, name, node, signature, parameters, returnType, metadata);
    }

    private void EmitMethodLike(Node node, string name, IEnumerable<Node> parameterNodes, ITypeNode? returnTypeNode, string prefix)
    {
        var parameters = ParamRenderer.RenderAll(parameterNodes, _sourceText);
        var paramList = string.Join(", ", parameters.Select(p => $"{p.Name}: {p.Type}"));
        var returnType = returnTypeNode?.GetText(_sourceText);
        var metadata = MetadataOf(node, isTopLevel: false);
        var staticText = metadata.IsStatic ? "static " : string.Empty;
        var asyncText = metadata.IsAsync ? "async " : string.Empty;
        var signature = $"{staticText}{asyncText}{prefix}{name}({paramList}){(returnType is null ? string.Empty : $": {returnType}")}";

        Emit(NodeKind.Method, name, node, signature, parameters, returnType, metadata);
    }

    private void EmitProperty(PropertyDeclaration node)
    {
        var name = node.IdentifierStr;

        if (node.Initializer is { Kind: SyntaxKind.ArrowFunction or SyntaxKind.FunctionExpression } initializer)
        {
            var parameters = ParamRenderer.RenderAll(ParametersOf(initializer), _sourceText);
            var paramList = string.Join(", ", parameters.Select(p => $"{p.Name}: {p.Type}"));
            var metadata = MetadataOf(node, isTopLevel: false, extraGenerator: initializer is FunctionExpression { AsteriskToken: not null });
            var staticText = metadata.IsStatic ? "static " : string.Empty;
            var asyncText = metadata.IsAsync ? "async " : string.Empty;
            var signature = $"{staticText}{name} = {asyncText}({paramList}) => ...";

            Emit(NodeKind.Method, name, node, signature, parameters, null, metadata);
            return;
        }

        var typeText = node.Type?.GetText(_sourceText);
        var metadataField = MetadataOf(node, isTopLevel: false);
        var staticTextField = metadataField.IsStatic ? "static " : string.Empty;
        var signatureField = $"{staticTextField}{name}{(typeText is null ? string.Empty : $": {typeText}")}";

        Emit(NodeKind.Field, name, node, signatureField, Array.Empty<ParameterInfo>(), typeText, metadataField);
    }

    private static IEnumerable<Node> ParametersOf(IExpression functionLike) => functionLike switch
    {
        ArrowFunction arrow => arrow.Parameters.Cast<Node>(),
        FunctionExpression func => func.Parameters.Cast<Node>(),
        _ => Enumerable.Empty<Node>(),
    };

    private void EmitInterfaceProperty(Node node)
    {
        var name = node.IdentifierStr;
        var typeText = (node as PropertySignature)?.Type?.GetText(_sourceText);
        var signature = $"{name}{(typeText is null ? string.Empty : $": {typeText}")}";

        Emit(NodeKind.Field, name, node, signature, Array.Empty<ParameterInfo>(), typeText, new NodeMetadata { IsPublic = true });
    }

    private void EmitInterfaceMethod(Node node)
    {
        var name = node.IdentifierStr;
        var signature = (node as MethodSignature)?.GetText(_sourceText) ?? name;

        Emit(NodeKind.Method, name, node, signature, Array.Empty<ParameterInfo>(), null, new NodeMetadata { IsPublic = true });
    }

    private void EmitVariableStatement(VariableStatement node)
    {
        var keyword = node.DeclarationList.Flags switch
        {
            var f when f.HasFlag(Zu.TypeScript.TsTypes.NodeFlags.Const) => "const",
            var f when f.HasFlag(Zu.TypeScript.TsTypes.NodeFlags.Let) => "let",
            _ => "var",
        };

        var isExported = node.ModifierFlagsCache.HasFlag(ModifierFlags.Export);

        foreach (var declNode in node.DeclarationList.Declarations)
        {
            var decl = (VariableDeclaration)declNode;
            var name = decl.IdentifierStr;

            if (decl.Initializer is { Kind: SyntaxKind.ArrowFunction or SyntaxKind.FunctionExpression } initializer)
            {
                var parameters = ParamRenderer.RenderAll(ParametersOf(initializer), _sourceText);
                var paramList = string.Join(", ", parameters.Select(p => $"{p.Name}: {p.Type}"));
                var isAsync = initializer.ModifierFlagsCache.HasFlag(ModifierFlags.Async);
                var asyncText = isAsync ? "async " : string.Empty;
                var signature = $"{keyword} {asyncText}{name} = ({paramList}) => ...";

                var metadata = new NodeMetadata { IsAsync = isAsync, IsPublic = isExported, IsPrivate = !isExported };
                Emit(NodeKind.Method, name, decl, signature, parameters, null, metadata, bodyOverride: $"{keyword} {decl.GetText(_sourceText)};");
            }
            else
            {
                var kind = keyword == "const" ? NodeKind.Constant : NodeKind.Field;
                var typeText = decl.Type?.GetText(_sourceText);
                var signature = $"{keyword} {name}{(typeText is null ? string.Empty : $": {typeText}")}";

                var metadata = new NodeMetadata { IsPublic = isExported, IsPrivate = !isExported };
                Emit(kind, name, decl, signature, Array.Empty<ParameterInfo>(), typeText, metadata, bodyOverride: $"{keyword} {decl.GetText(_sourceText)};");
            }
        }
    }

    private void EmitImport(ImportDeclaration node)
    {
        var name = node.ModuleSpecifier.GetText(_sourceText).Trim('\'', '"');
        var signature = node.GetText(_sourceText);
        Emit(NodeKind.Import, name, node, signature, Array.Empty<ParameterInfo>(), null, new NodeMetadata());
    }

    /// <summary>
    /// Module-scope declarations (classes, interfaces, enums, namespaces, type
    /// aliases, top-level functions) have no access-modifier keywords in TS —
    /// only "export" — so their visibility is exported-vs-not, mirroring the
    /// JS parser's convention. Class members (<paramref name="isTopLevel"/> =
    /// false) use real accessibility keywords instead, defaulting to public.
    /// </summary>
    private static NodeMetadata MetadataOf(Node node, bool isTopLevel, bool extraGenerator = false)
    {
        var flags = node.ModifierFlagsCache;
        var isPrivate = flags.HasFlag(ModifierFlags.Private);
        var isProtected = flags.HasFlag(ModifierFlags.Protected);
        var isPublicExplicit = flags.HasFlag(ModifierFlags.Public);
        var isExported = flags.HasFlag(ModifierFlags.Export);

        bool isPublic;
        if (isTopLevel)
        {
            isPublic = isExported;
            isPrivate = !isExported;
        }
        else
        {
            isPublic = isPublicExplicit || (!isPrivate && !isProtected);
        }

        return new NodeMetadata
        {
            IsPublic = isPublic,
            IsPrivate = isPrivate,
            IsProtected = isProtected,
            IsStatic = flags.HasFlag(ModifierFlags.Static),
            IsAsync = flags.HasFlag(ModifierFlags.Async),
            IsAbstract = flags.HasFlag(ModifierFlags.Abstract),
            Extra = BuildExtra(flags, extraGenerator),
        };
    }

    private static Dictionary<string, string> BuildExtra(ModifierFlags flags, bool isGenerator)
    {
        var extra = new Dictionary<string, string>();
        if (flags.HasFlag(ModifierFlags.Readonly))
        {
            extra["readonly"] = "true";
        }

        if (isGenerator)
        {
            extra["generator"] = "true";
        }

        return extra;
    }

    private void Emit(
        NodeKind kind,
        string name,
        Node locationNode,
        string signature,
        IReadOnlyList<ParameterInfo> parameters,
        string? returnType,
        NodeMetadata metadata,
        string? bodyOverride = null)
    {
        var scopeChain = _scope.Reverse().Append(name).ToArray();
        var qualifiedName = ScopeNameBuilder.Build(scopeChain, ScopeSeparator);

        var body = bodyOverride ?? locationNode.GetText(_sourceText);
        var realStart = locationNode.Pos is { } pos ? _sourceText.IndexOf(body, Math.Max(0, pos), StringComparison.Ordinal) : -1;
        if (realStart < 0)
        {
            realStart = locationNode.Pos ?? 0;
        }

        var startLine = _lineIndex.LineOf(realStart);
        var endLine = _lineIndex.LineOf(locationNode.End ?? realStart);

        var docComment = TsDocCommentExtractor.FindFor(_sourceText, realStart);
        if (string.IsNullOrEmpty(docComment))
        {
            docComment = null;
        }

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
