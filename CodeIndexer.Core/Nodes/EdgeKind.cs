namespace CodeIndexer.Core.Nodes;

/// <summary>
/// The fixed taxonomy of relationships between nodes, resolved by a
/// post-processing pass over the whole node set after every file is parsed.
/// </summary>
public enum EdgeKind
{
    Contains,
    Calls,
    Inherits,
    Implements,
    Imports,

    /// <summary>
    /// A method parameter, field, or property whose declared type is another
    /// indexed Class/Interface — covers usages that are neither a call nor an
    /// inheritance relationship, most commonly constructor-injected
    /// dependencies (e.g. "constructor(private authService: AuthService)").
    /// </summary>
    References,
}
