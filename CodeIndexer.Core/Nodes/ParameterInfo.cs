namespace CodeIndexer.Core.Nodes;

/// <summary>
/// A single parameter of a method/function-shaped node, as it should appear in
/// a browsable signature.
/// </summary>
public sealed record ParameterInfo
{
    public required string Name { get; init; }

    public required string Type { get; init; }
}
