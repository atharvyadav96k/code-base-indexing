using System.Text.Json;

namespace CodeIndexer.Indexing.Sessions;

/// <summary>Reads and writes a session's small JSON metadata file (not the binary index itself).</summary>
public static class SessionMetadataStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Write(string metadataFilePath, SessionMetadata metadata)
    {
        var json = JsonSerializer.Serialize(metadata, Options);
        File.WriteAllText(metadataFilePath, json);
    }

    public static SessionMetadata? Read(string metadataFilePath)
    {
        if (!File.Exists(metadataFilePath))
        {
            return null;
        }

        var json = File.ReadAllText(metadataFilePath);
        return JsonSerializer.Deserialize<SessionMetadata>(json, Options);
    }
}
