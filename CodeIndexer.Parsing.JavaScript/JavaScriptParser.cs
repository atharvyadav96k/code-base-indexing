using Acornima;
using Acornima.Ast;
using CodeIndexer.Core.Parsing;
using CodeIndexer.Parsing.JavaScript.Internal;

namespace CodeIndexer.Parsing.JavaScript;

/// <summary>
/// The JavaScript implementation of <see cref="ICodeParser"/>, backed by
/// Acornima. This is the only place in the repository allowed to reference
/// Acornima types or encode JS-specific assumptions.
/// </summary>
public sealed class JavaScriptParser : ICodeParser
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".js", ".mjs", ".cjs", ".jsx" };

    public string ScopeSeparator => ".";

    public Task<ParseResult> ParseFileAsync(string filePath, string sourceText, CancellationToken cancellationToken)
    {
        try
        {
            var comments = new List<(int Start, int End, string Raw)>();
            var options = new ParserOptions
            {
                OnComment = (in Comment comment) =>
                    comments.Add((comment.Start, comment.End, sourceText[comment.Start..comment.End])),
            };

            var program = ParseAsModuleOrScript(sourceText, filePath, options, out var parseError);
            if (program is null)
            {
                return Task.FromResult(ParseResult.Failed($"Syntax errors in {filePath}: {parseError}"));
            }

            var docComments = new JsDocCommentExtractor(sourceText, comments);
            var walker = new JsNodeWalker(filePath, sourceText, docComments);
            walker.Visit(program);

            return Task.FromResult(ParseResult.Ok(walker.Nodes));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ParseResult.Failed($"Failed to parse {filePath}: {ex.Message}"));
        }
    }

    private static Program? ParseAsModuleOrScript(string sourceText, string filePath, ParserOptions options, out string? error)
    {
        var parser = new Parser(options);

        try
        {
            var module = parser.ParseModule(sourceText, filePath);
            error = null;
            return module;
        }
        catch (SyntaxErrorException moduleError)
        {
            try
            {
                var script = parser.ParseScript(sourceText, filePath);
                error = null;
                return script;
            }
            catch (SyntaxErrorException scriptError)
            {
                error = $"{moduleError.Message} (as module); {scriptError.Message} (as script)";
                return null;
            }
        }
    }
}
