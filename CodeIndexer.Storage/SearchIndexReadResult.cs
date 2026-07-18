using CodeIndexer.Storage.Json;

namespace CodeIndexer.Storage;

/// <summary>Outcome of reading search-index.json — corruption/absence are explicit, not exceptions.</summary>
public sealed record SearchIndexReadResult
{
    public required IndexReadStatus Status { get; init; }

    public IReadOnlyList<FileEntryDto> Entries { get; init; } = Array.Empty<FileEntryDto>();

    public string? Detail { get; init; }

    public bool Success => Status == IndexReadStatus.Success;

    public static SearchIndexReadResult Ok(IReadOnlyList<FileEntryDto> entries) =>
        new() { Status = IndexReadStatus.Success, Entries = entries };

    public static SearchIndexReadResult NotFound { get; } = new() { Status = IndexReadStatus.NotFound };

    public static SearchIndexReadResult Corrupted(string detail) =>
        new() { Status = IndexReadStatus.Corrupted, Detail = detail };
}
