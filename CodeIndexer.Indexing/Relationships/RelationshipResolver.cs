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
/// <item>References cover a parameter/field/property type naming another
/// indexed type — chiefly constructor-injected dependencies — resolved the
/// same unambiguous-name-match way, including one level of generic unwrapping
/// (e.g. "Task&lt;AuthService&gt;" resolves to AuthService).</item>
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
        var skipsByNodeId = new Dictionary<string, List<string>>();
        foreach (var node in nodes)
        {
            edgesByNodeId[node.Id] = new List<NodeEdge>();
            skipsByNodeId[node.Id] = new List<string>();
        }

        ResolveContainment(nodes, edgesByNodeId);
        ResolveInheritance(nodes, edgesByNodeId, skipsByNodeId);
        ResolveCalls(nodes, edgesByNodeId, skipsByNodeId);
        ResolveImports(nodes, edgesByNodeId);
        ResolveTypeUsages(nodes, edgesByNodeId, skipsByNodeId);

        return nodes.Select(n => n with { Edges = edgesByNodeId[n.Id], SkippedRelationships = skipsByNodeId[n.Id] }).ToArray();
    }

    /// <summary>Describes an ambiguous candidate for a diagnostic skip note (not a real edge — the resolver isn't confident enough to assert one).</summary>
    private static string DescribeCandidate(CodeNode candidate) => $"{candidate.Id} @ {candidate.Location.FilePath}";

    private static string BuildAmbiguityNote(string relationshipDescription, string simpleName, IReadOnlyList<CodeNode> candidates) =>
        $"{relationshipDescription} '{simpleName}' skipped: {candidates.Count} ambiguous candidates ({string.Join(", ", candidates.Select(DescribeCandidate))})";

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

    private static void ResolveInheritance(IReadOnlyList<CodeNode> nodes, Dictionary<string, List<NodeEdge>> edgesByNodeId, Dictionary<string, List<string>> skipsByNodeId)
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
                if (candidates.Length > 1)
                {
                    skipsByNodeId[node.Id].Add(BuildAmbiguityNote("inheritance from", simpleName, candidates));
                }

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

    private static void ResolveCalls(IReadOnlyList<CodeNode> nodes, Dictionary<string, List<NodeEdge>> edgesByNodeId, Dictionary<string, List<string>> skipsByNodeId)
    {
        var methodsByName = nodes.Where(n => n.Kind == NodeKind.Method).ToLookup(n => n.Name);

        foreach (var node in nodes)
        {
            if (node.Kind != NodeKind.Method)
            {
                continue;
            }

            var seenTargets = new HashSet<string>();
            var seenAmbiguousNames = new HashSet<string>();
            foreach (Match match in CallPattern.Matches(node.Body))
            {
                var calleeName = match.Groups[1].Value;
                if (calleeName == node.Name || CallLikeKeywordsToSkip.Contains(calleeName))
                {
                    continue;
                }

                var candidates = methodsByName[calleeName].Where(c => c.Id != node.Id).ToArray();
                if (candidates.Length > 1 && seenAmbiguousNames.Add(calleeName))
                {
                    skipsByNodeId[node.Id].Add(BuildAmbiguityNote("call to", calleeName, candidates));
                }

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

    /// <summary>
    /// A method parameter, field, or property type that names another indexed
    /// Class/Interface/Struct — covers usages that are neither a call nor an
    /// inheritance relationship, most commonly constructor-injected
    /// dependencies (e.g. "constructor(private authService: AuthService)").
    /// </summary>
    private static void ResolveTypeUsages(IReadOnlyList<CodeNode> nodes, Dictionary<string, List<NodeEdge>> edgesByNodeId, Dictionary<string, List<string>> skipsByNodeId)
    {
        var typeNodesByName = nodes
            .Where(n => n.Kind is NodeKind.Class or NodeKind.Interface or NodeKind.Struct)
            .ToLookup(n => n.Name);

        foreach (var node in nodes)
        {
            IEnumerable<string> typeTexts = node.Kind switch
            {
                NodeKind.Method => node.Summary.Parameters.Select(p => p.Type)
                    .Concat(node.Summary.ReturnType is { Length: > 0 } rt ? new[] { rt } : Array.Empty<string>()),
                NodeKind.Field or NodeKind.Property => node.Summary.ReturnType is { Length: > 0 } t ? new[] { t } : Array.Empty<string>(),
                _ => Array.Empty<string>(),
            };

            var seenTargets = new HashSet<string>();
            var seenAmbiguousNames = new HashSet<string>();
            foreach (var typeText in typeTexts)
            {
                foreach (var candidateName in ExtractCandidateTypeNames(typeText))
                {
                    var candidates = typeNodesByName[candidateName].Where(c => c.Id != node.Id).ToArray();
                    if (candidates.Length > 1 && seenAmbiguousNames.Add(candidateName))
                    {
                        skipsByNodeId[node.Id].Add(BuildAmbiguityNote("type usage of", candidateName, candidates));
                    }

                    if (candidates.Length != 1)
                    {
                        continue;
                    }

                    if (seenTargets.Add(candidates[0].Id))
                    {
                        edgesByNodeId[node.Id].Add(new NodeEdge { Kind = EdgeKind.References, TargetNodeId = candidates[0].Id });
                    }
                }
            }
        }
    }

    /// <summary>
    /// The type name itself, plus (for a generic type like "Task&lt;AuthService&gt;"
    /// or "Dictionary&lt;string, AuthService&gt;") each top-level generic argument —
    /// covers the common case where the interesting reference is wrapped in a
    /// container/task/promise type rather than named directly.
    /// </summary>
    private static IEnumerable<string> ExtractCandidateTypeNames(string typeText)
    {
        var cleaned = typeText.TrimEnd('?').Replace("[]", string.Empty).Trim();
        if (cleaned.Length == 0)
        {
            yield break;
        }

        yield return SimplifyTypeName(cleaned);

        var genericStart = cleaned.IndexOf('<');
        if (genericStart < 0 || !cleaned.EndsWith('>'))
        {
            yield break;
        }

        var inner = cleaned[(genericStart + 1)..^1];
        foreach (var part in SplitTopLevel(inner))
        {
            var simplified = SimplifyTypeName(part.Trim());
            if (simplified.Length > 0)
            {
                yield return simplified;
            }
        }
    }

    private static IEnumerable<string> SplitTopLevel(string text)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '<')
            {
                depth++;
            }
            else if (text[i] == '>')
            {
                depth--;
            }
            else if (text[i] == ',' && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }

        yield return text[start..];
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
