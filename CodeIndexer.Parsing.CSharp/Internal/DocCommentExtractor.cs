using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeIndexer.Parsing.CSharp.Internal;

/// <summary>Extracts the leading /// XML doc comment text (if any) from a declaration's leading trivia.</summary>
internal static class DocCommentExtractor
{
    public static string? Extract(SyntaxNode node)
    {
        var docTrivia = node.GetLeadingTrivia()
            .Select(t => t.GetStructure())
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();

        if (docTrivia is null)
        {
            return null;
        }

        var lines = docTrivia.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.XmlTextSyntax>()
            .SelectMany(x => x.TextTokens)
            .Select(t => t.Text.Trim())
            .Where(t => t.Length > 0);

        var text = string.Join(" ", lines).Trim();
        return text.Length == 0 ? null : text;
    }
}
