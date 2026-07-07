using System.Text.RegularExpressions;
using CodeIndexer.Core.Nodes;
using CodeIndexer.Core.Scope;

namespace CodeIndexer.Indexing.Relationships;

/// <summary>
/// Populates <see cref="CodeNode.Edges"/> across the whole project's node set,
/// once every file has been parsed. This is deliberately a post-processing
/// pass over the flat node list — not a per-language concern — so it works
/// identically regardless of which parsers produced the nodes.
/// </summary>
/// <remarks>
/// Edges are resolved by matching names against other nodes already in the
/// index, not via each language's real semantic model (e.g. Roslyn symbol
/// binding). That means:
/// <list type="bullet">
/// <item>Containment and Imports are unambiguous (qualified-name equality).</item>
/// <item>Inherits/Implements resolve a base-type name only when exactly one
/// Class/Interface node in the project has that simple name; ambiguous or
/// external (unresolvable) base types are silently skipped.</item>
/// <item>Calls are the most approximate: a regex finds call-shaped
/// identifiers in a method's body text and links to another method only when
/// its name is unique across the whole project. Overloaded or same-named
/// methods in different types are skipped rather than guessed at.</item>
/// </list>
/// This trades completeness for not asserting a relationship the resolver
/// isn't actually confident about.
/// </remarks>
public static class RelationshipResolver
{
    private static readonly Regex CallPattern = new(@"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

    private static readonly HashSet<string> CallLikeKeywordsToSkip = new(StringComparer.Ordinal)
    {
        "if", "for", "foreach", "while", "switch", "catch", "using", "return", "new", "typeof",
        "nameof", "sizeof", "await", "async", "function", "get", "set", "super", "this", "base",
        "throw", "yield", "in", "of", "instanceof", "delete", "void",
    };

    public static IReadOnlyList<CodeNode> Resolve(IReadOnlyList<CodeNode> nodes)
    {
        // Node IDs are meant to be unique, but this must never crash on a real
        // repo just because two declarations happened to hash identically (or
        // the same file was somehow discovered twice) — last-write-wins here,
        // same as everywhere else nodes are keyed by ID.
        var edgesByNodeId = new Dictionary<string, List<NodeEdge>>();
        foreach (var node in nodes)
        {
            edgesByNodeId[node.Id] = new List<NodeEdge>();
        }

        ResolveContainment(nodes, edgesByNodeId);
        ResolveInheritance(nodes, edgesByNodeId);
        ResolveCalls(nodes, edgesByNodeId);
        ResolveImports(nodes, edgesByNodeId);

        return nodes.Select(n => n with { Edges = edgesByNodeId[n.Id] }).ToArray();
    }

    private static void ResolveContainment(IReadOnlyList<CodeNode> nodes, Dictionary<string, List<NodeEdge>> edgesByNodeId)
    {
        // A qualified name is not a unique key on its own: multiple files can
        // reopen the same namespace (every file in a folder declaring
        // "namespace App.Services;"), each producing its own Namespace node
        // with an identical QualifiedName. Picking an arbitrary one of those
        // would misattribute containment to the wrong file. The containing
        // declaration for a node always lives in the same file, so prefer a
        // same-file match and only fall back to a cross-file one (nested
        // types/classes, where qualified names really are unique) when no
        // same-file candidate exists.
        var byQualifiedName = nodes.ToLookup(n => n.QualifiedName);

        foreach (var node in nodes)
        {
            if (node.ScopeChain.Count <= 1)
            {
                continue;
            }

            var parentChain = node.ScopeChain.Take(node.ScopeChain.Count - 1).ToArray();
            var parentQualifiedName = ScopeNameBuilder.Build(parentChain, node.ScopeSeparator);

            var candidates = byQualifiedName[parentQualifiedName];
            var parent = candidates.FirstOrDefault(c => c.Location.FilePath == node.Location.FilePath)
                ?? candidates.FirstOrDefault();

            if (parent is not null)
            {
                edgesByNodeId[parent.Id].Add(new NodeEdge { Kind = EdgeKind.Contains, TargetNodeId = node.Id });
            }
        }
    }

    private static void ResolveInheritance(IReadOnlyList<CodeNode> nodes, Dictionary<string, List<NodeEdge>> edgesByNodeId)
    {
        var typeNodesByName = nodes
            .Where(n => n.Kind is NodeKind.Class or NodeKind.Interface or NodeKind.Struct)
            .ToLookup(n => n.Name);

        foreach (var node in nodes)
        {
            if (node.Kind is not (NodeKind.Class or NodeKind.Interface or NodeKind.Struct))
            {
                continue;
            }

            if (!node.Metadata.Extra.TryGetValue(NodeMetadataExtensions.BaseTypesExtraKey, out var raw))
            {
                continue;
            }

            foreach (var rawName in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var simpleName = SimplifyTypeName(rawName);
                var candidates = typeNodesByName[simpleName].Where(c => c.Id != node.Id).ToArray();
                if (candidates.Length != 1)
                {
                    continue;
                }

                var target = candidates[0];
                var kind = target.Kind == NodeKind.Interface ? EdgeKind.Implements : EdgeKind.Inherits;
                edgesByNodeId[node.Id].Add(new NodeEdge { Kind = kind, TargetNodeId = target.Id });
            }
        }
    }

    private static void ResolveCalls(IReadOnlyList<CodeNode> nodes, Dictionary<string, List<NodeEdge>> edgesByNodeId)
    {
        var methodsByName = nodes.Where(n => n.Kind == NodeKind.Method).ToLookup(n => n.Name);

        foreach (var node in nodes)
        {
            if (node.Kind != NodeKind.Method)
            {
                continue;
            }

            var seenTargets = new HashSet<string>();
            foreach (Match match in CallPattern.Matches(node.Body))
            {
                var calleeName = match.Groups[1].Value;
                if (calleeName == node.Name || CallLikeKeywordsToSkip.Contains(calleeName))
                {
                    continue;
                }

                var candidates = methodsByName[calleeName].Where(c => c.Id != node.Id).ToArray();
                if (candidates.Length != 1)
                {
                    continue;
                }

                if (seenTargets.Add(candidates[0].Id))
                {
                    edgesByNodeId[node.Id].Add(new NodeEdge { Kind = EdgeKind.Calls, TargetNodeId = candidates[0].Id });
                }
            }
        }
    }

    private static void ResolveImports(IReadOnlyList<CodeNode> nodes, Dictionary<string, List<NodeEdge>> edgesByNodeId)
    {
        var namespacesByQualifiedName = nodes
            .Where(n => n.Kind == NodeKind.Namespace)
            .GroupBy(n => n.QualifiedName)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var node in nodes)
        {
            if (node.Kind != NodeKind.Import)
            {
                continue;
            }

            if (namespacesByQualifiedName.TryGetValue(node.Name, out var target))
            {
                edgesByNodeId[node.Id].Add(new NodeEdge { Kind = EdgeKind.Imports, TargetNodeId = target.Id });
            }
        }
    }

    private static string SimplifyTypeName(string rawName)
    {
        var name = rawName;

        var genericIndex = name.IndexOf('<');
        if (genericIndex >= 0)
        {
            name = name[..genericIndex];
        }

        var dotIndex = name.LastIndexOf('.');
        if (dotIndex >= 0)
        {
            name = name[(dotIndex + 1)..];
        }

        return name.Trim();
    }
}
