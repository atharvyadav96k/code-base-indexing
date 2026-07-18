namespace CodeIndexer.Storage.Json;

/// <summary>Root shape of a shard's relations.json.</summary>
public sealed class RelationsFileDto
{
    public string FilePath { get; set; } = string.Empty;

    public List<RelationDto> Relations { get; set; } = new();
}
