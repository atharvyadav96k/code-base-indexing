using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Storage;

/// <summary>
/// Outcome of reading an index file. Corruption and version mismatches are
/// expected, explicit outcomes — never exceptions — so callers can trigger a
/// clean rebuild instead of crashing.
/// </summary>
public sealed record IndexReadResult
{
    public required IndexReadStatus Status { get; init; }

    public IReadOnlyList<CodeNode> Nodes { get; init; } = Array.Empty<CodeNode>();

    public string? Detail { get; init; }

    public bool Success => Status == IndexReadStatus.Success;

    public static IndexReadResult Ok(IReadOnlyList<CodeNode> nodes) =>
        new() { Status = IndexReadStatus.Success, Nodes = nodes };

    public static IndexReadResult NotFound { get; } = new() { Status = IndexReadStatus.NotFound };

    public static IndexReadResult VersionMismatch(int foundVersion) =>
        new() { Status = IndexReadStatus.VersionMismatch, Detail = $"Found version {foundVersion}, expected {BinaryIndexFormat.CurrentVersion}." };

    public static IndexReadResult Corrupted(string detail) =>
        new() { Status = IndexReadStatus.Corrupted, Detail = detail };
}
