using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Storage.Json;

/// <summary>Maps a node's outbound <see cref="NodeEdge"/>s to/from <see cref="RelationDto"/> rows.</summary>
public static class RelationDtoMapper
{
    public static IEnumerable<RelationDto> ToDtos(CodeNode node) =>
        node.Edges.Select(edge => new RelationDto
        {
            SourceNodeId = node.Id,
            Kind = (int)edge.Kind,
            TargetNodeId = edge.TargetNodeId,
        });

    public static NodeEdge ToDomain(RelationDto dto) => new()
    {
        Kind = (EdgeKind)dto.Kind,
        TargetNodeId = dto.TargetNodeId,
    };
}
