using CodeIndexer.Core.Parsing;
using CodeIndexer.Indexing;
using CodeIndexer.Indexing.GitHooks;
using CodeIndexer.Indexing.Manifest;
using CodeIndexer.Indexing.Sessions;
using CodeIndexer.Parsing.CSharp;
using CodeIndexer.Parsing.JavaScript;
using CodeIndexer.Parsing.TypeScript;
using CodeIndexer.Search;
using CodeIndexer.Search.Relationships;
using CodeIndexer.Search.Structure;
using CodeIndexer.Storage;
using CodeIndexer.Storage.Json;

// Composition root: this is the only place a concrete parser (or the concrete
// storage implementation) is referenced.
IReadOnlyList<ICodeParser> parsers = new ICodeParser[] { new CSharpParser(), new JavaScriptParser(), new TypeScriptParser() };
IIndexStore indexStore = new JsonIndexStore();

// Flags that take a value (e.g. --depth 2) must be named explicitly here —
// everything else starting with "--" is treated as a standalone boolean flag,
// so a boolean flag can never accidentally swallow the next positional arg.
var valueFlagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--depth" };
var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var positionalArgsList = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    var a = args[i];
    if (!a.StartsWith("--", StringComparison.Ordinal))
    {
        positionalArgsList.Add(a);
        continue;
    }

    if (valueFlagNames.Contains(a) && i + 1 < args.Length)
    {
        options[a] = args[++i];
    }
    else
    {
        flags.Add(a);
    }
}

var positionalArgs = positionalArgsList.ToArray();
var command = positionalArgs.Length > 0 ? positionalArgs[0] : "help";
var arg1 = positionalArgs.Length > 1 ? positionalArgs[1] : null;
var workingDirectory = Directory.GetCurrentDirectory();

// Import/Field hits are usually reference-site noise, not the declaration being
// looked for — hide them by default so 'search' doesn't require --kind every time.
var defaultSearchKinds = Enum.GetValues<CodeIndexer.Core.Nodes.NodeKind>()
    .Where(k => k is not (CodeIndexer.Core.Nodes.NodeKind.Import or CodeIndexer.Core.Nodes.NodeKind.Field))
    .ToArray();

switch (command)
{
    case "index":
        await RunIndexAsync(arg1 ?? workingDirectory);
        break;

    case "search":
        RunSearch(arg1 ?? string.Empty, flags.Contains("--all"));
        break;

    case "get-code":
        RunGetCode(arg1 ?? string.Empty);
        break;

    case "info":
        RunInfo(arg1 ?? string.Empty);
        break;

    case "tree":
        RunTree(arg1, options.GetValueOrDefault("--depth"), flags.Contains("--full"));
        break;

    case "outline":
        RunOutline();
        break;

    case "locate":
        RunLocate(arg1 ?? string.Empty);
        break;

    case "children":
        RunChildren(arg1 ?? string.Empty);
        break;

    case "refs":
        RunRefs(arg1 ?? string.Empty);
        break;

    case "callers":
        RunCallers(arg1 ?? string.Empty);
        break;

    case "callees":
        RunCallees(arg1 ?? string.Empty);
        break;

    case "subtypes":
        RunSubtypes(arg1 ?? string.Empty);
        break;

    case "usages":
        RunUsages(arg1 ?? string.Empty);
        break;

    case "update":
        await RunUpdateAsync();
        break;

    case "verify":
        RunVerify();
        break;

    case "install-hooks":
        RunInstallHooks();
        break;

    default:
        PrintHelp();
        break;
}

async Task RunIndexAsync(string directory)
{
    var sessionManager = new SessionManager(new SessionRegistry());
    var session = sessionManager.EnsureSession(directory);

    var orchestrator = new IndexOrchestrator(parsers, indexStore);
    var result = await orchestrator.RunFullIndexAsync(session, CancellationToken.None);

    Console.WriteLine($"Indexed {result.NodesIndexed} nodes from {result.FilesDiscovered} files at {session.RootPath}");
    if (result.SkippedFiles.Count > 0)
    {
        Console.WriteLine($"Skipped {result.SkippedFiles.Count} file(s):");
        foreach (var skip in result.SkippedFiles)
        {
            Console.WriteLine($"  - {skip}");
        }
    }
}

async Task RunUpdateAsync()
{
    var sessionManager = new SessionManager(new SessionRegistry());
    var resolution = sessionManager.TryResolve(workingDirectory);
    if (!resolution.Found)
    {
        Console.WriteLine("No session here. Run 'index' first.");
        return;
    }

    var orchestrator = new IndexOrchestrator(parsers, indexStore);
    var result = await orchestrator.RunIncrementalIndexAsync(resolution.Session!, CancellationToken.None);

    if (result.FellBackToFullIndex)
    {
        Console.WriteLine($"No prior manifest found — ran a full index instead. Indexed {result.NodesIndexed} nodes from {result.FilesAdded} files.");
    }
    else
    {
        Console.WriteLine(
            $"Updated: {result.FilesAdded} added, {result.FilesChanged} changed, {result.FilesRemoved} removed, {result.FilesUnchanged} unchanged. {result.NodesIndexed} nodes indexed.");
    }

    if (result.SkippedFiles.Count > 0)
    {
        Console.WriteLine($"Skipped {result.SkippedFiles.Count} file(s):");
        foreach (var skip in result.SkippedFiles)
        {
            Console.WriteLine($"  - {skip}");
        }
    }
}

void RunVerify()
{
    var sessionManager = new SessionManager(new SessionRegistry());
    var resolution = sessionManager.TryResolve(workingDirectory);
    if (!resolution.Found)
    {
        Console.WriteLine("No session here. Run 'index' first.");
        return;
    }

    var orchestrator = new IndexOrchestrator(parsers, indexStore);
    var drift = orchestrator.DetectDrift(resolution.Session!);

    if (drift.IsClean)
    {
        Console.WriteLine("Index is up to date — no drift detected.");
        return;
    }

    Console.WriteLine($"Drift detected: {drift.Added.Count} added, {drift.Changed.Count} changed, {drift.Removed.Count} removed.");
    foreach (var file in drift.Added)
    {
        Console.WriteLine($"  + {file}");
    }

    foreach (var file in drift.Changed)
    {
        Console.WriteLine($"  ~ {file}");
    }

    foreach (var file in drift.Removed)
    {
        Console.WriteLine($"  - {file}");
    }

    Console.WriteLine("Run 'update' to repair.");
}

void RunInstallHooks()
{
    var sessionManager = new SessionManager(new SessionRegistry());
    var resolution = sessionManager.TryResolve(workingDirectory);
    if (!resolution.Found)
    {
        Console.WriteLine("No session here. Run 'index' first.");
        return;
    }

    var executablePath = Environment.ProcessPath;
    if (executablePath is null)
    {
        Console.WriteLine("Could not determine this executable's path.");
        return;
    }

    var result = GitHookInstaller.Install(resolution.Session!, executablePath);
    if (!result.Success)
    {
        Console.WriteLine(result.ErrorMessage);
        return;
    }

    Console.WriteLine($"Installed hooks in {result.HooksDirectory}: {string.Join(", ", result.InstalledHookNames)}");
}

bool TryResolveSession(out Session session)
{
    session = null!;
    var sessionManager = new SessionManager(new SessionRegistry());
    var resolution = sessionManager.TryResolve(workingDirectory);
    if (!resolution.Found)
    {
        Console.WriteLine("No session here. Run 'index' first.");
        return false;
    }

    session = resolution.Session!;
    return true;
}

/// <summary>Every file the index currently knows about, from manifest.json — no node data loaded.</summary>
IReadOnlyList<string> AllManifestFiles(Session session) =>
    FileManifestStore.Read(session.ManifestFilePath).FileHashes.Keys.ToArray();

bool TryLoadSearchIndex(Session session, out IReadOnlyList<FileEntryDto> entries)
{
    entries = Array.Empty<FileEntryDto>();
    var result = indexStore.ReadSearchIndex(session.MarkerDirectoryPath);
    if (!result.Success)
    {
        Console.WriteLine($"Search index unreadable ({result.Status}): {result.Detail}. Run 'index' again to rebuild.");
        return false;
    }

    entries = result.Entries;
    return true;
}

/// <summary>Resolves a node ID to the file that defines it via search-index.json, without loading any shard.</summary>
bool TryResolveNodeFile(Session session, string nodeId, out string filePath)
{
    filePath = string.Empty;
    if (!TryLoadSearchIndex(session, out var entries))
    {
        return false;
    }

    var entry = entries.FirstOrDefault(e => e.Id == nodeId);
    if (entry is null)
    {
        return false;
    }

    filePath = entry.FilePath;
    return true;
}

/// <summary>Resolves node IDs to their defining files via search-index.json.</summary>
IReadOnlyList<string> ResolveFilesForIds(Session session, IReadOnlyCollection<string> nodeIds)
{
    if (nodeIds.Count == 0 || !TryLoadSearchIndex(session, out var entries))
    {
        return Array.Empty<string>();
    }

    return entries.Where(e => nodeIds.Contains(e.Id)).Select(e => e.FilePath).Distinct().ToArray();
}

/// <summary>Loads <paramref name="primaryFilePath"/>'s shard plus any of <paramref name="extraFilePaths"/> not already covered.</summary>
IReadOnlyList<CodeIndexer.Core.Nodes.CodeNode> LoadNodesForRelationshipQuery(Session session, string primaryFilePath, IReadOnlyList<string> extraFilePaths)
{
    var files = new List<string> { primaryFilePath };
    files.AddRange(extraFilePaths.Where(f => !string.Equals(f, primaryFilePath, StringComparison.OrdinalIgnoreCase)));
    return indexStore.ReadFiles(session.MarkerDirectoryPath, files).Nodes;
}

/// <summary>
/// Full-graph load, used only for outline (which genuinely needs every node)
/// and as the lazy fallback for relationship-diagnostic messages when a
/// relations-only scan came back empty and the ambiguity note it should
/// explain could live on any node in the project.
/// </summary>
IReadOnlyList<CodeIndexer.Core.Nodes.CodeNode> LoadAllNodesOrEmpty(Session session)
{
    var result = indexStore.ReadFiles(session.MarkerDirectoryPath, AllManifestFiles(session));
    if (!result.Success)
    {
        Console.WriteLine($"Index unreadable ({result.Status}): {result.Detail}. Run 'index' again to rebuild.");
        return Array.Empty<CodeIndexer.Core.Nodes.CodeNode>();
    }

    return result.Nodes;
}

/// <summary>Missing edge targets of <paramref name="kind"/> not already present in <paramref name="alreadyLoaded"/>, resolved to their files.</summary>
IReadOnlyList<string> ResolveMissingTargetFiles(Session session, CodeIndexer.Core.Nodes.CodeNode source, CodeIndexer.Core.Nodes.EdgeKind kind, IReadOnlyList<CodeIndexer.Core.Nodes.CodeNode> alreadyLoaded)
{
    var loadedIds = new HashSet<string>(alreadyLoaded.Select(n => n.Id));
    var missingIds = source.Edges
        .Where(e => e.Kind == kind && !loadedIds.Contains(e.TargetNodeId))
        .Select(e => e.TargetNodeId)
        .ToHashSet();

    return ResolveFilesForIds(session, missingIds);
}

void RunSearch(string pattern, bool includeAll)
{
    if (!TryResolveSession(out var session) || !TryLoadSearchIndex(session, out var entries))
    {
        return;
    }

    var hits = new NodeSearchEngine().SearchEntries(entries, pattern, includeAll ? null : defaultSearchKinds, 25);
    foreach (var hit in hits)
    {
        var lang = LanguageOf(hit.FilePath);
        Console.WriteLine($"{hit.Id}  [{lang}]{new string(' ', Math.Max(1, 5 - lang.Length))}{hit.Kind,-10} {hit.Name}");
    }
}

string LanguageOf(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
{
    ".cs" => "c#",
    ".js" or ".mjs" or ".cjs" or ".jsx" => "js",
    ".ts" or ".mts" or ".cts" => "ts",
    var ext => ext.TrimStart('.'),
};

void RunInfo(string nodeId)
{
    if (!TryResolveSession(out var session) || !TryResolveNodeFile(session, nodeId, out var filePath))
    {
        Console.WriteLine("Node not found.");
        return;
    }

    var readResult = indexStore.ReadFile(session.MarkerDirectoryPath, filePath);
    var node = readResult.Nodes.FirstOrDefault(n => n.Id == nodeId);
    if (node is null)
    {
        Console.WriteLine("Node not found.");
        return;
    }

    Console.WriteLine($"{node.Kind}  {node.QualifiedName}");
    Console.WriteLine($"path: {node.Location.FilePath}:{node.Location.StartLine}");
    Console.WriteLine($"signature: {node.Summary.Signature}");
    if (node.Summary.DocComment is { Length: > 0 } doc)
    {
        Console.WriteLine($"doc: {doc}");
    }
}

void RunGetCode(string nodeId)
{
    if (!TryResolveSession(out var session) || !TryResolveNodeFile(session, nodeId, out var filePath))
    {
        Console.WriteLine("Node not found.");
        return;
    }

    var readResult = indexStore.ReadFile(session.MarkerDirectoryPath, filePath);
    var result = new NodeRetriever().GetCode(readResult.Nodes, nodeId);
    if (!result.Found)
    {
        Console.WriteLine("Node not found.");
        return;
    }

    Console.WriteLine(result.Body);
}

const int DefaultTreeDepth = 3;

void RunTree(string? scopePath, string? depthOption, bool full)
{
    if (!TryResolveSession(out var session))
    {
        return;
    }

    var files = AllManifestFiles(session);
    var tree = DirectoryTreeBuilder.Build(session.RootPath, files);

    if (!string.IsNullOrEmpty(scopePath))
    {
        var scoped = FindSubtree(tree, scopePath);
        if (scoped is null)
        {
            Console.WriteLine($"(no indexed folder matches '{scopePath}')");
            return;
        }

        tree = scoped;
    }

    var maxDepth = DefaultTreeDepth;
    if (full)
    {
        maxDepth = int.MaxValue;
    }
    else if (depthOption is not null)
    {
        if (int.TryParse(depthOption, out var parsed) && parsed > 0)
        {
            maxDepth = parsed;
        }
        else
        {
            Console.WriteLine($"Invalid --depth value '{depthOption}'; using default of {DefaultTreeDepth}.");
        }
    }

    PrintTree(tree, 0, maxDepth);
}

DirectoryTreeNode? FindSubtree(DirectoryTreeNode root, string relativePath)
{
    var normalized = relativePath.Replace('\\', '/').Trim('/');
    if (normalized.Length == 0)
    {
        return root;
    }

    var current = root;
    foreach (var segment in normalized.Split('/'))
    {
        var next = current.Children.FirstOrDefault(c => c.IsDirectory && string.Equals(c.Name, segment, StringComparison.OrdinalIgnoreCase));
        if (next is null)
        {
            return null;
        }

        current = next;
    }

    return current;
}

// Depth is capped by default (folders can otherwise dump thousands of vendored/
// leaf files before anything relevant appears) — truncation is reported inline
// at the exact point it happens rather than as a single opaque total, and
// --depth/--full are always available to see everything.
void PrintTree(DirectoryTreeNode node, int depth, int maxDepth)
{
    Console.WriteLine(new string(' ', depth * 2) + node.Name + (node.IsDirectory ? "/" : string.Empty));

    if (node.IsDirectory && depth >= maxDepth)
    {
        if (node.Children.Count > 0)
        {
            var noun = node.Children.Count == 1 ? "entry" : "entries";
            Console.WriteLine(new string(' ', (depth + 1) * 2) + $"... {node.Children.Count} more {noun} not shown (use --depth or --full)");
        }

        return;
    }

    foreach (var child in node.Children)
    {
        PrintTree(child, depth + 1, maxDepth);
    }
}

void RunOutline()
{
    if (!TryResolveSession(out var session))
    {
        return;
    }

    var nodes = LoadAllNodesOrEmpty(session);
    var outline = ScopeOutlineBuilder.Build(nodes);
    foreach (var top in outline)
    {
        PrintOutline(top, 0);
    }
}

void PrintOutline(ScopeOutlineNode node, int depth)
{
    var kindLabel = node.Kind is { } k ? $" [{k}]" : string.Empty;
    Console.WriteLine(new string(' ', depth * 2) + node.Name + kindLabel);
    foreach (var child in node.Children)
    {
        PrintOutline(child, depth + 1);
    }
}

void RunLocate(string fragment)
{
    if (!TryResolveSession(out var session))
    {
        return;
    }

    var files = AllManifestFiles(session);
    var matches = FileLocator.Locate(files, fragment);
    foreach (var match in matches)
    {
        Console.WriteLine(match);
    }
}

// Unlike 'children', these results can span multiple files, so the path can't
// just be dropped — but it's still redundant to repeat it on every line when
// several hits share a file, so print each file path once as a group header.
void PrintNodeList(IReadOnlyList<CodeIndexer.Core.Nodes.CodeNode> matches)
{
    if (matches.Count == 0)
    {
        Console.WriteLine("(none found)");
        return;
    }

    foreach (var group in matches.GroupBy(n => n.Location.FilePath).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine(group.Key);
        foreach (var node in group.OrderBy(n => n.Location.StartLine))
        {
            Console.WriteLine($"  {node.Id}  {node.Kind,-10} {node.QualifiedName}  (line {node.Location.StartLine})");
        }
    }
}

// A relationship command returning zero matches can mean either "genuinely no
// relationship" or "RelationshipResolver found >1 same-named candidate and
// declined to guess" — these look identical to the caller unless surfaced.
// SkippedRelationships notes are name-text matches, not exact edges, and may
// live on a node anywhere in the project (not necessarily one already loaded
// for the fast path), so the full graph is loaded lazily only in this rare,
// diagnostic-only branch.
void PrintRelationshipResult(IReadOnlyList<CodeIndexer.Core.Nodes.CodeNode> matches, Func<IReadOnlyList<CodeIndexer.Core.Nodes.CodeNode>> loadAllNodes, CodeIndexer.Core.Nodes.CodeNode target)
{
    if (matches.Count > 0)
    {
        PrintNodeList(matches);
        return;
    }

    var allNodes = loadAllNodes();
    var skips = allNodes
        .SelectMany(n => n.SkippedRelationships)
        .Where(note => note.Contains($"'{target.Name}'", StringComparison.Ordinal))
        .Distinct()
        .ToArray();

    if (skips.Length == 0)
    {
        Console.WriteLine("(none found)");
        return;
    }

    Console.WriteLine($"(none found — but {skips.Length} ambiguous resolution(s) elsewhere in the project may involve '{target.Name}', not asserted as edges)");
    foreach (var skip in skips)
    {
        Console.WriteLine($"  skipped: {skip}");
    }
}

void RunChildren(string nodeId)
{
    if (!TryResolveSession(out var session) || !TryResolveNodeFile(session, nodeId, out var filePath))
    {
        Console.WriteLine("(node not found)");
        return;
    }

    var localNodes = indexStore.ReadFile(session.MarkerDirectoryPath, filePath).Nodes;
    var container = localNodes.FirstOrDefault(n => n.Id == nodeId);
    if (container is null)
    {
        Console.WriteLine("(node not found)");
        return;
    }

    // Containment almost always stays within one file, but the resolver can
    // fall back to a cross-file parent for reopened namespaces — pull in
    // whichever other files are actually needed rather than assuming.
    var extraFiles = ResolveMissingTargetFiles(session, container, CodeIndexer.Core.Nodes.EdgeKind.Contains, localNodes);
    var nodes = extraFiles.Count == 0 ? localNodes : LoadNodesForRelationshipQuery(session, filePath, extraFiles);

    PrintChildren(new ReferenceFinder().GetChildren(nodes, nodeId));
}

// Children always live in the same file as their container (containment never
// crosses files for plain members), so repeating the full path per line is
// pure noise — a line number is all that's needed here.
void PrintChildren(IReadOnlyList<CodeIndexer.Core.Nodes.CodeNode> children)
{
    if (children.Count == 0)
    {
        Console.WriteLine("(none found)");
        return;
    }

    foreach (var node in children)
    {
        Console.WriteLine($"{node.Id}  {node.Kind,-10} {node.Name}  (line {node.Location.StartLine})");
    }
}

void RunRefs(string nodeId)
{
    if (!TryResolveSession(out var session) || !TryResolveNodeFile(session, nodeId, out var targetFile))
    {
        Console.WriteLine("(node not found)");
        return;
    }

    var allFiles = AllManifestFiles(session);
    var relations = indexStore.ReadRelations(session.MarkerDirectoryPath, allFiles);
    var sourceIds = relations.Where(r => r.TargetNodeId == nodeId).Select(r => r.SourceNodeId).ToHashSet();
    var sourceFiles = ResolveFilesForIds(session, sourceIds);
    var nodes = LoadNodesForRelationshipQuery(session, targetFile, sourceFiles);

    var target = nodes.FirstOrDefault(n => n.Id == nodeId);
    if (target is null)
    {
        Console.WriteLine("(node not found)");
        return;
    }

    var hits = new ReferenceFinder().FindReferences(nodes, nodeId);
    if (hits.Count > 0)
    {
        PrintReferenceHits(hits);
        return;
    }

    PrintRelationshipResult(Array.Empty<CodeIndexer.Core.Nodes.CodeNode>(), () => LoadAllNodesOrEmpty(session), target);
}

void PrintReferenceHits(IReadOnlyList<ReferenceHit> hits)
{
    foreach (var group in hits.GroupBy(h => h.Source.Location.FilePath).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine(group.Key);
        foreach (var hit in group.OrderBy(h => h.Source.Location.StartLine))
        {
            Console.WriteLine($"  {hit.Source.Id}  {hit.Kind,-10} {hit.Source.QualifiedName}  (line {hit.Source.Location.StartLine})");
        }
    }
}

bool TryRequireKind(IReadOnlyList<CodeIndexer.Core.Nodes.CodeNode> nodes, string nodeId, string commandName, out CodeIndexer.Core.Nodes.CodeNode? node, params CodeIndexer.Core.Nodes.NodeKind[] allowedKinds)
{
    node = nodes.FirstOrDefault(n => n.Id == nodeId);
    if (node is null)
    {
        Console.WriteLine("(node not found)");
        return false;
    }

    var error = NodeKindGuard.Validate(node, commandName, allowedKinds);
    if (error is not null)
    {
        Console.WriteLine(error);
        return false;
    }

    return true;
}

/// <summary>
/// Shared shape for the reverse-lookup relationship commands (callers/
/// subtypes/usages): resolve the target's own file to check its kind, scan
/// every file's relations.json (not their bodies) for matching edges, then
/// load only the distinct files that actually matched before delegating to
/// <see cref="ReferenceFinder"/>.
/// </summary>
void RunReverseRelationshipQuery(
    string nodeId,
    string commandName,
    CodeIndexer.Core.Nodes.NodeKind[] allowedKinds,
    Func<string, bool> relationKindMatches,
    Func<IReadOnlyList<CodeIndexer.Core.Nodes.CodeNode>, string, IReadOnlyList<CodeIndexer.Core.Nodes.CodeNode>> selectMatches)
{
    if (!TryResolveSession(out var session) || !TryResolveNodeFile(session, nodeId, out var targetFile))
    {
        Console.WriteLine("(node not found)");
        return;
    }

    var targetFileNodes = indexStore.ReadFile(session.MarkerDirectoryPath, targetFile).Nodes;
    if (!TryRequireKind(targetFileNodes, nodeId, commandName, out var target, allowedKinds))
    {
        return;
    }

    var allFiles = AllManifestFiles(session);
    var relations = indexStore.ReadRelations(session.MarkerDirectoryPath, allFiles);
    var sourceIds = relations.Where(r => r.TargetNodeId == nodeId && relationKindMatches(r.Kind)).Select(r => r.SourceNodeId).ToHashSet();
    var sourceFiles = ResolveFilesForIds(session, sourceIds);
    var nodes = LoadNodesForRelationshipQuery(session, targetFile, sourceFiles);

    PrintRelationshipResult(selectMatches(nodes, nodeId), () => LoadAllNodesOrEmpty(session), target!);
}

void RunCallers(string nodeId) => RunReverseRelationshipQuery(
    nodeId, "callers", new[] { CodeIndexer.Core.Nodes.NodeKind.Method },
    kind => kind == nameof(CodeIndexer.Core.Nodes.EdgeKind.Calls),
    (nodes, id) => new ReferenceFinder().GetCallers(nodes, id));

void RunSubtypes(string nodeId) => RunReverseRelationshipQuery(
    nodeId, "subtypes", new[] { CodeIndexer.Core.Nodes.NodeKind.Class, CodeIndexer.Core.Nodes.NodeKind.Interface, CodeIndexer.Core.Nodes.NodeKind.Struct },
    kind => kind is nameof(CodeIndexer.Core.Nodes.EdgeKind.Inherits) or nameof(CodeIndexer.Core.Nodes.EdgeKind.Implements),
    (nodes, id) => new ReferenceFinder().GetSubtypes(nodes, id));

void RunUsages(string nodeId) => RunReverseRelationshipQuery(
    nodeId, "usages", new[] { CodeIndexer.Core.Nodes.NodeKind.Class, CodeIndexer.Core.Nodes.NodeKind.Interface, CodeIndexer.Core.Nodes.NodeKind.Struct, CodeIndexer.Core.Nodes.NodeKind.Enum },
    kind => kind == nameof(CodeIndexer.Core.Nodes.EdgeKind.References),
    (nodes, id) => new ReferenceFinder().GetUsages(nodes, id));

void RunCallees(string nodeId)
{
    if (!TryResolveSession(out var session) || !TryResolveNodeFile(session, nodeId, out var filePath))
    {
        Console.WriteLine("(node not found)");
        return;
    }

    var localNodes = indexStore.ReadFile(session.MarkerDirectoryPath, filePath).Nodes;
    if (!TryRequireKind(localNodes, nodeId, "callees", out var source, CodeIndexer.Core.Nodes.NodeKind.Method))
    {
        return;
    }

    var extraFiles = ResolveMissingTargetFiles(session, source!, CodeIndexer.Core.Nodes.EdgeKind.Calls, localNodes);
    var nodes = extraFiles.Count == 0 ? localNodes : LoadNodesForRelationshipQuery(session, filePath, extraFiles);

    PrintNodeList(new ReferenceFinder().GetCallees(nodes, nodeId));
}

void PrintHelp()
{
    Console.WriteLine("""
        CodeIndexer — usage:
          index [path]         Full re-index of the session rooted at path (default: cwd)
          search <pattern>     Search node names, ranked (hides Import/Field by default; add --all to include them)
          info <nodeId>        Print kind, path, signature, and doc comment for a node by ID
          get-code <nodeId>    Print the full body of a node by ID
          tree [path] [--depth N] [--full]  Print the directory tree, optionally scoped to [path]; capped at depth 3 by default (--depth N or --full to see more)
          outline               Print the namespace/scope outline
          locate <fragment>    Find files by name or path fragment
          children <nodeId>    List direct members (methods/fields/nested types) declared inside this node
          refs <nodeId>        Find every node that references this one (any edge kind)
          callers <nodeId>     Find nodes that call this one
          callees <nodeId>     Find nodes this one calls
          subtypes <nodeId>    Find types that inherit from/implement this one
          usages <nodeId>      Find parameters/fields/properties typed as this one (e.g. DI usage)
          update               Incremental update: re-parse only changed files, drop deleted ones
          verify               Report drift (added/changed/removed files) without changing anything
          install-hooks        Install post-commit/post-merge/post-checkout git hooks that run 'update'
        """);
}
