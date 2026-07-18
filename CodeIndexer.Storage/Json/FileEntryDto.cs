namespace CodeIndexer.Storage.Json;

/// <summary>
/// One row of search-index.json — doubles as the name-search lookup and the
/// id-to-file resolver every relationship command needs, since
/// <see cref="CodeIndexer.Core.Nodes.NodeEdge.TargetNodeId"/> is an opaque hash,
/// not a name.
/// </summary>
public sealed class FileEntryDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string QualifiedName { get; set; } = string.Empty;

    public int Kind { get; set; }

    public string FilePath { get; set; } = string.Empty;
}
