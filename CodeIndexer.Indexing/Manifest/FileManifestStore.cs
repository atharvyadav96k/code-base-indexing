using System.Text.Json;

namespace CodeIndexer.Indexing.Manifest;

/// <summary>Reads and writes a session's file manifest (small JSON, alongside session.json).</summary>
public static class FileManifestStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Write(string manifestFilePath, FileManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, Options);
        File.WriteAllText(manifestFilePath, json);
    }

    /// <summary>
    /// Returns <see cref="FileManifest.Empty"/> if no manifest exists yet, if it
    /// predates the current schema (e.g. the old bare path-&gt;hash dictionary
    /// shape), or if its <see cref="FileManifest.FormatVersion"/> doesn't match
    /// <see cref="FileManifest.CurrentFormatVersion"/> (e.g. a shard format
    /// change, like switching Kind fields from strings to ints) — in every
    /// case the caller falls back to a full rebuild rather than trusting
    /// shards it can no longer parse correctly.
    /// </summary>
    public static FileManifest Read(string manifestFilePath)
    {
        if (!File.Exists(manifestFilePath))
        {
            return FileManifest.Empty;
        }

        try
        {
            var json = File.ReadAllText(manifestFilePath);
            var manifest = JsonSerializer.Deserialize<FileManifest>(json, Options);
            return manifest is { FileHashes: not null, FormatVersion: FileManifest.CurrentFormatVersion } ? manifest : FileManifest.Empty;
        }
        catch (JsonException)
        {
            return FileManifest.Empty;
        }
    }
}
