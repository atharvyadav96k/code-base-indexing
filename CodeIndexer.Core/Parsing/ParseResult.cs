using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Core.Parsing;

/// <summary>
/// Outcome of parsing one file. A failure (syntax error, unsupported construct)
/// is an expected, explicit result — never an exception — so a single bad file
/// can never crash an indexing run.
/// </summary>
public sealed record ParseResult
{
    public required bool Success { get; init; }

    public IReadOnlyList<CodeNode> Nodes { get; init; } = Array.Empty<CodeNode>();

    public string? ErrorMessage { get; init; }

    public static ParseResult Ok(IReadOnlyList<CodeNode> nodes) => new() { Success = true, Nodes = nodes };

    public static ParseResult Failed(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };
}
