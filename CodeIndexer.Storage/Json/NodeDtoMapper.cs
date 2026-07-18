using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Storage.Json;

/// <summary>Maps between the domain <see cref="CodeNode"/> and its JSON shape, <see cref="NodeDto"/>.</summary>
public static class NodeDtoMapper
{
    public static NodeDto ToDto(CodeNode node) => new()
    {
        Id = node.Id,
        Name = node.Name,
        ScopeChain = node.ScopeChain.ToList(),
        ScopeSeparator = node.ScopeSeparator,
        QualifiedName = node.QualifiedName,
        Kind = (int)node.Kind,
        FilePath = node.Location.FilePath,
        StartLine = node.Location.StartLine,
        EndLine = node.Location.EndLine,
        SummaryName = node.Summary.Name,
        Signature = node.Summary.Signature,
        Parameters = node.Summary.Parameters.Select(p => new ParameterDto { Name = p.Name, Type = p.Type }).ToList(),
        ReturnType = node.Summary.ReturnType,
        DocComment = node.Summary.DocComment,
        LineCount = node.Summary.LineCount,
        Body = node.Body,
        ContentHash = node.ContentHash,
        Metadata = new MetadataDto
        {
            IsPublic = node.Metadata.IsPublic,
            IsPrivate = node.Metadata.IsPrivate,
            IsProtected = node.Metadata.IsProtected,
            IsInternal = node.Metadata.IsInternal,
            IsStatic = node.Metadata.IsStatic,
            IsAsync = node.Metadata.IsAsync,
            IsAbstract = node.Metadata.IsAbstract,
            IsVirtual = node.Metadata.IsVirtual,
            IsOverride = node.Metadata.IsOverride,
            IsTest = node.Metadata.IsTest,
            Extra = new Dictionary<string, string>(node.Metadata.Extra),
        },
        SkippedRelationships = node.SkippedRelationships.ToList(),
    };

    /// <summary>Reconstructs the domain node, reattaching <paramref name="edges"/> (loaded from the sibling relations.json).</summary>
    public static CodeNode ToDomain(NodeDto dto, IReadOnlyList<NodeEdge> edges) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        ScopeChain = dto.ScopeChain,
        ScopeSeparator = dto.ScopeSeparator,
        QualifiedName = dto.QualifiedName,
        Kind = (NodeKind)dto.Kind,
        Location = new NodeLocation { FilePath = dto.FilePath, StartLine = dto.StartLine, EndLine = dto.EndLine },
        Summary = new NodeSummary
        {
            Name = dto.SummaryName,
            Signature = dto.Signature,
            Parameters = dto.Parameters.Select(p => new ParameterInfo { Name = p.Name, Type = p.Type }).ToArray(),
            ReturnType = dto.ReturnType,
            DocComment = dto.DocComment,
            LineCount = dto.LineCount,
        },
        Body = dto.Body,
        ContentHash = dto.ContentHash,
        Metadata = new NodeMetadata
        {
            IsPublic = dto.Metadata.IsPublic,
            IsPrivate = dto.Metadata.IsPrivate,
            IsProtected = dto.Metadata.IsProtected,
            IsInternal = dto.Metadata.IsInternal,
            IsStatic = dto.Metadata.IsStatic,
            IsAsync = dto.Metadata.IsAsync,
            IsAbstract = dto.Metadata.IsAbstract,
            IsVirtual = dto.Metadata.IsVirtual,
            IsOverride = dto.Metadata.IsOverride,
            IsTest = dto.Metadata.IsTest,
            Extra = new Dictionary<string, string>(dto.Metadata.Extra),
        },
        Edges = edges,
        SkippedRelationships = dto.SkippedRelationships,
    };
}
