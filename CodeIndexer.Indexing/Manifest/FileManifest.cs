namespace CodeIndexer.Indexing.Manifest;

/// <summary>
/// Tracks the content hash of every indexed file at the time it was last
/// parsed, so a later run can tell, per file, whether it's unchanged (skip),
/// changed (re-parse), or gone (drop its nodes) — the file-granularity
/// change detection an incremental update needs.
/// </summary>
public sealed record FileManifest
{
    /// <summary>
    /// Current on-disk schema version. Bump this whenever a change to the
    /// shard format (index.json/relations.json/search-index.json) would break
    /// reading data written by an older version — <see cref="FileManifestStore.Read"/>
    /// treats a mismatch the same as a missing manifest, forcing a full
    /// rebuild instead of silently trusting shards it can no longer parse.
    /// </summary>
    public const int CurrentFormatVersion = 2;

    /// <summary>Schema version this manifest was written under.</summary>
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>When this manifest was last written.</summary>
    public DateTimeOffset IndexedAtUtc { get; init; }

    /// <summary>File path -> content hash, as of the last successful parse of that file.</summary>
    public required IReadOnlyDictionary<string, string> FileHashes { get; init; }

    public static FileManifest Empty { get; } = new() { FileHashes = new Dictionary<string, string>() };
}
