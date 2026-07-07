namespace CodeIndexer.Core.Scope;

/// <summary>
/// Builds a fully-qualified name from a generic scope chain. This is the one
/// piece of "namespace" logic shared across all languages: each language
/// parser supplies its own separator, but the join itself is not special-cased.
/// </summary>
public static class ScopeNameBuilder
{
    public static string Build(IReadOnlyList<string> scopeChain, string separator)
    {
        return string.Join(separator, scopeChain);
    }
}
