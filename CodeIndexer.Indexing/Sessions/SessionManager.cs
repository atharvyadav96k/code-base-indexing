namespace CodeIndexer.Indexing.Sessions;

/// <summary>
/// Resolves and creates sessions. Root discovery walks up from a starting
/// directory until it finds a marker directory; the nearest one wins, so a
/// nested session shadows an outer one exactly like nested ".git" repos.
/// </summary>
public sealed class SessionManager
{
    private readonly SessionRegistry _registry;

    public SessionManager(SessionRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Walks up from <paramref name="startDirectory"/> looking for the nearest marker directory.</summary>
    public SessionResolution TryResolve(string startDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));

        while (current is not null)
        {
            var markerPath = SessionPaths.MarkerDirectory(current.FullName);
            if (Directory.Exists(markerPath))
            {
                return SessionResolution.Ok(new Session { RootPath = current.FullName });
            }

            current = current.Parent;
        }

        return SessionResolution.NotFound;
    }

    /// <summary>
    /// Returns the existing session found by walking up from <paramref name="directory"/>,
    /// or creates a brand-new one rooted exactly at <paramref name="directory"/> if none exists.
    /// </summary>
    public Session EnsureSession(string directory)
    {
        var resolution = TryResolve(directory);
        if (resolution.Found)
        {
            return resolution.Session!;
        }

        return CreateSession(directory);
    }

    private Session CreateSession(string directory)
    {
        var rootPath = Path.GetFullPath(directory);
        var session = new Session { RootPath = rootPath };

        Directory.CreateDirectory(session.MarkerDirectoryPath);

        var metadata = new SessionMetadata { RootPath = rootPath, CreatedAtUtc = DateTimeOffset.UtcNow };
        SessionMetadataStore.Write(session.MetadataFilePath, metadata);

        _registry.Register(rootPath);

        return session;
    }
}
