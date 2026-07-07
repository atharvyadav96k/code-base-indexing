using Acornima.Ast;
using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Parsing.JavaScript.Internal;

/// <summary>
/// Renders a function/method parameter node into a browsable form. JS has no
/// static types, so <see cref="ParameterInfo.Type"/> is always "any" — the
/// interesting information is the parameter's shape (destructuring, default,
/// rest) captured in its name text.
/// </summary>
internal static class ParamRenderer
{
    public static IReadOnlyList<ParameterInfo> RenderAll(IEnumerable<Node> parameters) =>
        parameters.Select(p => new ParameterInfo { Name = RenderName(p), Type = "any" }).ToArray();

    private static string RenderName(Node param) => param switch
    {
        Identifier id => id.Name,
        AssignmentPattern assignment => $"{RenderName(assignment.Left)} = {RenderExpression(assignment.Right)}",
        RestElement rest => "..." + RenderName(rest.Argument),
        ObjectPattern => "{ }",
        ArrayPattern => "[ ]",
        _ => param.TypeText,
    };

    private static string RenderExpression(Node expression) => expression switch
    {
        Literal literal => literal.Raw,
        Identifier id => id.Name,
        _ => "…",
    };
}
