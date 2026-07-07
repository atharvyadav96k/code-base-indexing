namespace CodeIndexer.Indexing;

/// <summary>Summary of one full-index run, for reporting to the caller/AI.</summary>
public sealed record IndexRunResult
{
    public required int FilesDiscovered { get; init; }

    public required int NodesIndexed { get; init; }

    /// <summary>Files that failed to parse, with the reason — never a crash, just a skip + log entry.</summary>
    public required IReadOnlyList<string> SkippedFiles { get; init; }
}
