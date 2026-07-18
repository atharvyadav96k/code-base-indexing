namespace CodeIndexer.Storage.Json;

/// <summary>JSON shape of <see cref="CodeIndexer.Core.Nodes.NodeMetadata"/>.</summary>
public sealed class MetadataDto
{
    public bool IsPublic { get; set; }

    public bool IsPrivate { get; set; }

    public bool IsProtected { get; set; }

    public bool IsInternal { get; set; }

    public bool IsStatic { get; set; }

    public bool IsAsync { get; set; }

    public bool IsAbstract { get; set; }

    public bool IsVirtual { get; set; }

    public bool IsOverride { get; set; }

    public bool IsTest { get; set; }

    public Dictionary<string, string> Extra { get; set; } = new();
}
