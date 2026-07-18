namespace CodeIndexer.Storage.Json;

/// <summary>Root shape of a shard's index.json.</summary>
public sealed class IndexFileDto
{
    public string FilePath { get; set; } = string.Empty;

    public List<NodeDto> Nodes { get; set; } = new();
}
