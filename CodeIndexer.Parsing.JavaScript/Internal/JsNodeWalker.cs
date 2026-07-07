using Acornima;
using Acornima.Ast;
using CodeIndexer.Core.Nodes;
using CodeIndexer.Core.Scope;

namespace CodeIndexer.Parsing.JavaScript.Internal;

/// <summary>
/// Walks a JS AST (from Acornima), emitting shared <see cref="CodeNode"/>s.
/// Purely syntax-driven, like the C# walker: no cross-file resolution, and it
/// deliberately does not descend into function/method bodies — only
/// declarations that shape the browsable scope tree are modeled.
/// </summary>
internal sealed class JsNodeWalker : AstVisitor
{
    private const string ScopeSeparator = ".";

    private readonly string _filePath;
    private readonly string _sourceText;
    private readonly JsDocCommentExtractor _docComments;
    private readonly Stack<string> _scope = new();
    private readonly List<CodeNode> _nodes = new();
    private bool _pendingExported;

    public JsNodeWalker(string filePath, string sourceText, JsDocCommentExtractor docComments)
    {
        _filePath = filePath;
        _sourceText = sourceText;
        _docComments = docComments;
    }

    public IReadOnlyList<CodeNode> Nodes => _nodes;

    protected override object? VisitClassDeclaration(ClassDeclaration node)
    {
        var name = node.Id?.Name ?? "default";
        var superText = node.SuperClass is Identifier superId ? $" extends {superId.Name}" : string.Empty;
        var signature = $"class {name}{superText}";

        Emit(NodeKind.Class, name, node, signature, Array.Empty<ParameterInfo>(), null, new NodeMetadata { IsPrivate = true });

        _scope.Push(name);
        var result = base.VisitClassDeclaration(node);
        _scope.Pop();
        return result;
    }

    protected override object? VisitFunctionDeclaration(FunctionDeclaration node)
    {
        var name = node.Id?.Name ?? "default";
        var parameters = ParamRenderer.RenderAll(node.Params);
        var paramList = string.Join(", ", parameters.Select(p => p.Name));
        var asyncText = node.Async ? "async " : string.Empty;
        var generatorMark = node.Generator ? "*" : string.Empty;
        var signature = $"{asyncText}function{generatorMark} {name}({paramList})";

        var metadata = new NodeMetadata
        {
            IsAsync = node.Async,
            IsPrivate = true,
            Extra = node.Generator ? new Dictionary<string, string> { ["generator"] = "true" } : new Dictionary<string, string>(),
        };

        Emit(NodeKind.Method, name, node, signature, parameters, null, metadata);

        // Deliberately not descending into the body — local declarations inside a
        // function are not modeled, matching the C# parser's own behavior.
        return null;
    }

    protected override object? VisitMethodDefinition(MethodDefinition node)
    {
        var name = NameOf(node.Key);
        var isPrivateName = name.StartsWith('#');
        var prefix = node.Kind switch
        {
            PropertyKind.Get => "get ",
            PropertyKind.Set => "set ",
            _ => string.Empty,
        };

        var parameters = ParamRenderer.RenderAll(node.Value.Params);
        var paramList = string.Join(", ", parameters.Select(p => p.Name));
        var staticText = node.Static ? "static " : string.Empty;
        var asyncText = node.Value.Async ? "async " : string.Empty;
        var generatorMark = node.Value.Generator ? "*" : string.Empty;
        var signature = $"{staticText}{asyncText}{prefix}{generatorMark}{name}({paramList})";

        var metadata = new NodeMetadata
        {
            IsStatic = node.Static,
            IsAsync = node.Value.Async,
            IsPublic = !isPrivateName,
            IsPrivate = isPrivateName,
            Extra = node.Value.Generator ? new Dictionary<string, string> { ["generator"] = "true" } : new Dictionary<string, string>(),
        };

        Emit(NodeKind.Method, name, node, signature, parameters, null, metadata);
        return null;
    }

    protected override object? VisitPropertyDefinition(PropertyDefinition node)
    {
        var name = NameOf(node.Key);
        var isPrivateName = name.StartsWith('#');
        var staticText = node.Static ? "static " : string.Empty;

        if (node.Value is ArrowFunctionExpression or FunctionExpression)
        {
            var (isAsync, _, paramNodes) = FunctionShapeOf(node.Value);
            var parameters = ParamRenderer.RenderAll(paramNodes);
            var paramList = string.Join(", ", parameters.Select(p => p.Name));
            var asyncText = isAsync ? "async " : string.Empty;
            var signature = $"{staticText}{name} = {asyncText}({paramList}) => ...";

            var metadata = new NodeMetadata
            {
                IsStatic = node.Static,
                IsAsync = isAsync,
                IsPublic = !isPrivateName,
                IsPrivate = isPrivateName,
            };

            Emit(NodeKind.Method, name, node, signature, parameters, null, metadata);
        }
        else
        {
            var valueText = node.Value is Literal literal ? $" = {literal.Raw}" : node.Value is not null ? " = …" : string.Empty;
            var signature = $"{staticText}{name}{valueText}";

            var metadata = new NodeMetadata
            {
                IsStatic = node.Static,
                IsPublic = !isPrivateName,
                IsPrivate = isPrivateName,
            };

            Emit(NodeKind.Field, name, node, signature, Array.Empty<ParameterInfo>(), null, metadata);
        }

        return null;
    }

    protected override object? VisitVariableDeclaration(VariableDeclaration node)
    {
        var keyword = node.Kind switch
        {
            VariableDeclarationKind.Const => "const",
            VariableDeclarationKind.Let => "let",
            _ => "var",
        };

        foreach (var declarator in node.Declarations)
        {
            if (declarator.Id is not Identifier idNode)
            {
                continue; // skip destructuring patterns in v1
            }

            var name = idNode.Name;
            var declaratorText = _sourceText[declarator.Start..declarator.End];

            if (declarator.Init is ArrowFunctionExpression or FunctionExpression)
            {
                var (isAsync, isGenerator, paramNodes) = FunctionShapeOf(declarator.Init);
                var parameters = ParamRenderer.RenderAll(paramNodes);
                var paramList = string.Join(", ", parameters.Select(p => p.Name));
                var asyncText = isAsync ? "async " : string.Empty;
                var signature = $"{keyword} {asyncText}{name} = ({paramList}) => ...";

                var metadata = new NodeMetadata
                {
                    IsAsync = isAsync,
                    IsPrivate = true,
                    Extra = isGenerator ? new Dictionary<string, string> { ["generator"] = "true" } : new Dictionary<string, string>(),
                };

                Emit(NodeKind.Method, name, declarator, signature, parameters, null, metadata, bodyOverride: $"{keyword} {declaratorText};");
            }
            else
            {
                var kind = node.Kind == VariableDeclarationKind.Const ? NodeKind.Constant : NodeKind.Field;
                var valueText = declarator.Init is Literal literal ? $" = {literal.Raw}" : declarator.Init is not null ? " = …" : string.Empty;
                var signature = $"{keyword} {name}{valueText}";

                Emit(kind, name, declarator, signature, Array.Empty<ParameterInfo>(), null, new NodeMetadata { IsPrivate = true }, bodyOverride: $"{keyword} {declaratorText};");
            }
        }

        return null;
    }

    protected override object? VisitImportDeclaration(ImportDeclaration node)
    {
        var name = node.Source.Value;
        var signature = _sourceText[node.Start..node.End];
        Emit(NodeKind.Import, name, node, signature, Array.Empty<ParameterInfo>(), null, new NodeMetadata());
        return null;
    }

    protected override object? VisitExportNamedDeclaration(ExportNamedDeclaration node)
    {
        if (node.Declaration is null)
        {
            // `export { foo, bar };` re-export form — no declaration to attach the flag to.
            return null;
        }

        _pendingExported = true;
        return base.VisitExportNamedDeclaration(node);
    }

    protected override object? VisitExportDefaultDeclaration(ExportDefaultDeclaration node)
    {
        _pendingExported = true;
        var result = base.VisitExportDefaultDeclaration(node);
        _pendingExported = false; // in case Declaration was a bare expression that never reached Emit
        return result;
    }

    private static (bool IsAsync, bool IsGenerator, IEnumerable<Node> Params) FunctionShapeOf(Node? value) => value switch
    {
        ArrowFunctionExpression arrow => (arrow.Async, false, arrow.Params),
        FunctionExpression func => (func.Async, func.Generator, func.Params),
        _ => (false, false, Enumerable.Empty<Node>()),
    };

    private static string NameOf(Expression key) => key switch
    {
        Identifier id => id.Name,
        PrivateIdentifier priv => "#" + priv.Name,
        Literal { Value: string literalName } => literalName,
        _ => key.TypeText,
    };

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
        if (_pendingExported)
        {
            metadata = metadata with { IsPublic = true, IsPrivate = false };
            _pendingExported = false;
        }

        var scopeChain = _scope.Reverse().Append(name).ToArray();
        var qualifiedName = ScopeNameBuilder.Build(scopeChain, ScopeSeparator);

        var startLine = locationNode.Location.Start.Line;
        var endLine = locationNode.Location.End.Line;

        var body = bodyOverride ?? _sourceText[locationNode.Start..locationNode.End];
        var docComment = _docComments.FindFor(locationNode.Start);

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
