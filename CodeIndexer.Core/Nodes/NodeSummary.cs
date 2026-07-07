namespace CodeIndexer.Core.Nodes;

/// <summary>
/// The cheap, browsable projection of a node — what an AI agent scans across
/// many search results before deciding to fetch a full body.
/// </summary>
public sealed record NodeSummary
{
    public required string Name { get; init; }

    /// <summary>Rendered signature, e.g. "public async Task&lt;User&gt; GetUserAsync(int id)".</summary>
    public required string Signature { get; init; }

    public required IReadOnlyList<ParameterInfo> Parameters { get; init; }

    /// <summary>Return type, or null for nodes without one (fields, classes, etc.).</summary>
    public string? ReturnType { get; init; }

    /// <summary>Leading doc comment text, if any (e.g. XML doc, JSDoc), stripped of comment markers.</summary>
    public string? DocComment { get; init; }

    public required int LineCount { get; init; }
}
