using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Search;

/// <summary>
/// The cheap projection returned by search — everything an AI needs to browse
/// and decide what to fetch, deliberately excluding <see cref="CodeNode.Body"/>.
/// </summary>
public sealed record NodeSearchHit
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string QualifiedName { get; init; }

    public required NodeKind Kind { get; init; }

    public required NodeLocation Location { get; init; }

    public required NodeSummary Summary { get; init; }

    public required NodeMetadata Metadata { get; init; }

    /// <summary>Relative rank score for this hit within its result set; higher is a better match.</summary>
    public required int Score { get; init; }

    public static NodeSearchHit FromNode(CodeNode node, int score) => new()
    {
        Id = node.Id,
        Name = node.Name,
        QualifiedName = node.QualifiedName,
        Kind = node.Kind,
        Location = node.Location,
        Summary = node.Summary,
        Metadata = node.Metadata,
        Score = score,
    };
}
