namespace CodeIndexer.Storage.Json;

/// <summary>
/// JSON shape of a <see cref="CodeIndexer.Core.Nodes.CodeNode"/>, persisted in a
/// file's index.json. Deliberately excludes edges — those are split out into
/// <see cref="RelationDto"/> rows in the sibling relations.json at write time and
/// reattached at load time.
/// </summary>
public sealed class NodeDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> ScopeChain { get; set; } = new();

    public string ScopeSeparator { get; set; } = string.Empty;

    public string QualifiedName { get; set; } = string.Empty;

    public int Kind { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public int StartLine { get; set; }

    public int EndLine { get; set; }

    public string SummaryName { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;

    public List<ParameterDto> Parameters { get; set; } = new();

    public string? ReturnType { get; set; }

    public string? DocComment { get; set; }

    public int LineCount { get; set; }

    public string Body { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;

    public MetadataDto Metadata { get; set; } = new();

    public List<string> SkippedRelationships { get; set; } = new();
}
