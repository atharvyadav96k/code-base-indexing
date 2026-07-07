namespace CodeIndexer.Indexing.Sessions;

/// <summary>
/// Result of attempting to resolve a session by walking up from a directory.
/// An explicit result type instead of a null/exception, per the "expected
/// failures" convention: "no session here" is a normal outcome, not an error.
/// </summary>
public sealed record SessionResolution
{
    public required bool Found { get; init; }

    public Session? Session { get; init; }

    public static SessionResolution Ok(Session session) => new() { Found = true, Session = session };

    public static SessionResolution NotFound { get; } = new() { Found = false };
}
