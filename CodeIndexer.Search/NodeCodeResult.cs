using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Search;

/// <summary>Outcome of fetching a node's full body by ID — an explicit "not found" instead of null.</summary>
public sealed record NodeCodeResult
{
    public required bool Found { get; init; }

    public string? Body { get; init; }

    public NodeLocation? Location { get; init; }

    /// <summary>Content hash at the time of retrieval, for the caller to detect staleness later.</summary>
    public string? ContentHash { get; init; }

    public static NodeCodeResult Ok(CodeNode node) => new()
    {
        Found = true,
        Body = node.Body,
        Location = node.Location,
        ContentHash = node.ContentHash,
    };

    public static NodeCodeResult NotFound { get; } = new() { Found = false };
}
