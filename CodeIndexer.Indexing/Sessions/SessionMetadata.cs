namespace CodeIndexer.Indexing.Sessions;

/// <summary>
/// Persisted metadata about a session, stored alongside the index file.
/// </summary>
public sealed record SessionMetadata
{
    public required string RootPath { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}
