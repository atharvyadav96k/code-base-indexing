namespace CodeIndexer.Search.Structure;

/// <summary>What's defined in one file — its nodes, cheap summaries only.</summary>
public sealed record FileOutline
{
    public required string FilePath { get; init; }

    public required IReadOnlyList<NodeSearchHit> Nodes { get; init; }
}
