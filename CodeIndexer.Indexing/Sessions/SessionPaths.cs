namespace CodeIndexer.Indexing.Sessions;

/// <summary>
/// Well-known file/directory names inside a session's marker directory.
/// </summary>
public static class SessionPaths
{
    /// <summary>The hidden marker directory that anchors a session, analogous to ".git".</summary>
    public const string MarkerDirectoryName = ".codeindex";

    public const string IndexFileName = "index.bin";

    public const string MetadataFileName = "session.json";

    public static string MarkerDirectory(string rootPath) => Path.Combine(rootPath, MarkerDirectoryName);

    public static string IndexFile(string rootPath) => Path.Combine(MarkerDirectory(rootPath), IndexFileName);

    public static string MetadataFile(string rootPath) => Path.Combine(MarkerDirectory(rootPath), MetadataFileName);
}
