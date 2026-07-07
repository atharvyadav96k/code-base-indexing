using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeIndexer.Parsing.CSharp.Internal;

/// <summary>
/// Renders a browsable, header-only signature for a declaration — modifiers
/// through parameter/base list, never the body — from Roslyn syntax nodes
/// directly rather than slicing source text.
/// </summary>
internal static class SignatureBuilder
{
    public static string ForType(TypeDeclarationSyntax node, string keyword)
    {
        var modifiers = ModifiersText(node.Modifiers);
        var typeParams = node.TypeParameterList?.ToString() ?? string.Empty;
        var baseList = node.BaseList is { } b ? " : " + string.Join(", ", b.Types.Select(t => t.ToString())) : string.Empty;

        return Join(modifiers, keyword, node.Identifier.Text + typeParams + baseList);
    }

    public static string ForEnum(EnumDeclarationSyntax node)
    {
        var modifiers = ModifiersText(node.Modifiers);
        var baseList = node.BaseList is { } b ? " : " + string.Join(", ", b.Types.Select(t => t.ToString())) : string.Empty;

        return Join(modifiers, "enum", node.Identifier.Text + baseList);
    }

    public static string ForMethod(MethodDeclarationSyntax node)
    {
        var modifiers = ModifiersText(node.Modifiers);
        var typeParams = node.TypeParameterList?.ToString() ?? string.Empty;
        var parameters = node.ParameterList.ToString();

        return Join(modifiers, node.ReturnType.ToString(), node.Identifier.Text + typeParams + parameters);
    }

    public static string ForProperty(PropertyDeclarationSyntax node)
    {
        var modifiers = ModifiersText(node.Modifiers);
        var accessors = node.AccessorList is { } list
            ? " { " + string.Join(" ", list.Accessors.Select(a => a.Keyword.Text + ";")) + " }"
            : " => ...;";

        return Join(modifiers, node.Type.ToString(), node.Identifier.Text) + accessors;
    }

    public static string ForField(FieldDeclarationSyntax node, VariableDeclaratorSyntax variable)
    {
        var modifiers = ModifiersText(node.Modifiers);

        return Join(modifiers, node.Declaration.Type.ToString(), variable.Identifier.Text) + ";";
    }

    private static string ModifiersText(SyntaxTokenList modifiers) =>
        string.Join(" ", modifiers.Select(m => m.Text));

    private static string Join(params string[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
