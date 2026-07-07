namespace CodeIndexer.Core.Nodes;

/// <summary>
/// Where a node lives in source: its file and line span (1-based, inclusive).
/// </summary>
public sealed record NodeLocation
{
    public required string FilePath { get; init; }

    public required int StartLine { get; init; }

    public required int EndLine { get; init; }
}
