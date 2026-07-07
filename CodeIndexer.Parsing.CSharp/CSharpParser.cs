using Microsoft.CodeAnalysis.CSharp;
using CodeIndexer.Core.Parsing;
using CodeIndexer.Parsing.CSharp.Internal;

namespace CodeIndexer.Parsing.CSharp;

/// <summary>
/// The C# implementation of <see cref="ICodeParser"/>, backed by Roslyn. This
/// is the only place in the repository allowed to reference Roslyn types or
/// encode C#-specific assumptions.
/// </summary>
public sealed class CSharpParser : ICodeParser
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".cs" };

    public string ScopeSeparator => ".";

    public Task<ParseResult> ParseFileAsync(string filePath, string sourceText, CancellationToken cancellationToken)
    {
        try
        {
            var options = new CSharpParseOptions(documentationMode: Microsoft.CodeAnalysis.DocumentationMode.Parse);
            var tree = CSharpSyntaxTree.ParseText(sourceText, options, path: filePath, cancellationToken: cancellationToken);
            var root = tree.GetRoot(cancellationToken);

            var diagnostics = tree.GetDiagnostics(root)
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .ToList();

            if (diagnostics.Count > 0)
            {
                var message = string.Join("; ", diagnostics.Take(3).Select(d => d.ToString()));
                return Task.FromResult(ParseResult.Failed($"Syntax errors in {filePath}: {message}"));
            }

            var walker = new CSharpNodeWalker(filePath, tree);
            walker.Visit(root);

            return Task.FromResult(ParseResult.Ok(walker.Nodes));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ParseResult.Failed($"Failed to parse {filePath}: {ex.Message}"));
        }
    }
}
