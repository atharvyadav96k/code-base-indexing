namespace CodeIndexer.Core.Parsing;

/// <summary>
/// The one interface every language module implements. The shared core talks
/// only to this contract — never to a concrete parser or its underlying
/// grammar/parsing library — which is what keeps Core language-agnostic.
/// </summary>
public interface ICodeParser
{
    /// <summary>File extensions this parser owns, including the leading dot (e.g. ".cs").</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>The separator this language uses to join scope chains into a qualified name (e.g. "." for C#).</summary>
    string ScopeSeparator { get; }

    /// <summary>
    /// Parses one file's source text into nodes. Must never throw for malformed
    /// input — a parse failure is reported via <see cref="ParseResult.Success"/>
    /// so the caller can log and skip it.
    /// </summary>
    Task<ParseResult> ParseFileAsync(string filePath, string sourceText, CancellationToken cancellationToken);
}
