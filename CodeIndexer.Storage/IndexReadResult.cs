using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Storage;

/// <summary>
/// Outcome of reading one or more shards. Corruption is an expected, explicit
/// outcome — never an exception — so callers can trigger a clean rebuild
/// instead of crashing.
/// </summary>
public sealed record IndexReadResult
{
    public required IndexReadStatus Status { get; init; }

    public IReadOnlyList<CodeNode> Nodes { get; init; } = Array.Empty<CodeNode>();

    public string? Detail { get; init; }

    public bool Success => Status == IndexReadStatus.Success;

    public static IndexReadResult Ok(IReadOnlyList<CodeNode> nodes) =>
        new() { Status = IndexReadStatus.Success, Nodes = nodes };

    public static IndexReadResult NotFound { get; } = new() { Status = IndexReadStatus.NotFound, Nodes = Array.Empty<CodeNode>() };

    public static IndexReadResult Corrupted(string detail) =>
        new() { Status = IndexReadStatus.Corrupted, Detail = detail };
}
