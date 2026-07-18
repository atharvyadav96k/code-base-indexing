namespace CodeIndexer.Storage.Json;

/// <summary>Root shape of search-index.json.</summary>
public sealed class SearchIndexDto
{
    public int FormatVersion { get; set; } = 1;

    public List<FileEntryDto> Entries { get; set; } = new();
}
