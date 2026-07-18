namespace CodeIndexer.Storage.Json;

/// <summary>JSON shape of <see cref="CodeIndexer.Core.Nodes.ParameterInfo"/>.</summary>
public sealed class ParameterDto
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;
}
