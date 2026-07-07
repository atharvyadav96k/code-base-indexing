namespace CodeIndexer.Core.Nodes;

/// <summary>
/// Shared helper for attaching a type's raw base-type/interface names to its
/// metadata, via the existing <see cref="NodeMetadata.Extra"/> escape hatch.
/// Every parser that emits Class/Interface/Struct-shaped nodes uses this same
/// key so a single relationship resolver can read it consistently regardless
/// of which language produced the node.
/// </summary>
public static class NodeMetadataExtensions
{
    public const string BaseTypesExtraKey = "baseTypes";

    /// <summary>
    /// Records the raw (unresolved) base type/interface names a type declaration
    /// lists, e.g. from "class Foo : Bar, IBaz" or "class Foo extends Bar
    /// implements IBaz". Resolution against actual nodes happens later, at
    /// index time, once every file's nodes are known.
    /// </summary>
    public static NodeMetadata WithBaseTypes(this NodeMetadata metadata, IEnumerable<string>? baseTypeNames)
    {
        var names = baseTypeNames?.Select(n => n.Trim()).Where(n => n.Length > 0).ToArray();
        if (names is null || names.Length == 0)
        {
            return metadata;
        }

        var extra = new Dictionary<string, string>(metadata.Extra) { [BaseTypesExtraKey] = string.Join(";", names) };
        return metadata with { Extra = extra };
    }
}
