namespace CodeIndexer.Storage.Json;

/// <summary>
/// JSON shape of a single <see cref="CodeIndexer.Core.Nodes.NodeEdge"/>, keyed by
/// the source node it originates from. A file's relations.json holds one row per
/// edge whose source node is defined in that file, regardless of which file the
/// target lives in.
/// </summary>
public sealed class RelationDto
{
    public string SourceNodeId { get; set; } = string.Empty;

    public int Kind { get; set; }

    public string TargetNodeId { get; set; } = string.Empty;
}
