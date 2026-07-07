namespace CodeIndexer.Indexing.Sessions;

/// <summary>
/// A resolved session: an indexed workspace anchored at <see cref="RootPath"/>.
/// </summary>
public sealed record Session
{
    /// <summary>The directory containing the marker directory — the session's identity.</summary>
    public required string RootPath { get; init; }

    public string MarkerDirectoryPath => SessionPaths.MarkerDirectory(RootPath);

    public string IndexFilePath => SessionPaths.IndexFile(RootPath);

    public string MetadataFilePath => SessionPaths.MetadataFile(RootPath);
}
