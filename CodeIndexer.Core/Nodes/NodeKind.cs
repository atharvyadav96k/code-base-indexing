namespace CodeIndexer.Core.Nodes;

/// <summary>
/// The fixed, language-agnostic taxonomy of constructs a parser can emit.
/// Every language parser maps its own grammar onto this set — no parser-specific
/// kinds are allowed to leak upward into shared components.
/// </summary>
public enum NodeKind
{
    Namespace,
    Class,
    Interface,
    Struct,
    Enum,
    Method,
    Property,
    Field,
    Constant,
    Import,
}
