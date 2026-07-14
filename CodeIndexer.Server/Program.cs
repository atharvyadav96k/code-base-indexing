using CodeIndexer.Core.Parsing;
using CodeIndexer.Indexing;
using CodeIndexer.Indexing.GitHooks;
using CodeIndexer.Indexing.Sessions;
using CodeIndexer.Parsing.CSharp;
using CodeIndexer.Parsing.JavaScript;
using CodeIndexer.Parsing.TypeScript;
using CodeIndexer.Search;
using CodeIndexer.Search.Relationships;
using CodeIndexer.Search.Structure;
using CodeIndexer.Storage;

// Composition root: this is the only place a concrete parser is referenced.
IReadOnlyList<ICodeParser> parsers = new ICodeParser[] { new CSharpParser(), new JavaScriptParser(), new TypeScriptParser() };

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

    var orchestrator = new IndexOrchestrator(parsers);
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

    var orchestrator = new IndexOrchestrator(parsers);
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

    var orchestrator = new IndexOrchestrator(parsers);
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

bool TryLoadSessionNodes(out IReadOnlyList<CodeIndexer.Core.Nodes.CodeNode> nodes)
{
    nodes = Array.Empty<CodeIndexer.Core.Nodes.CodeNode>();
    var sessionManager = new SessionManager(new SessionRegistry());
    var resolution = sessionManager.TryResolve(workingDirectory);
    if (!resolution.Found)
    {
        Console.WriteLine("No session here. Run 'index' first.");
        return false;
    }

    var readResult = new BinaryIndexStore().Read(resolution.Session!.IndexFilePath);
    if (!readResult.Success)
    {
        Console.WriteLine($"Index unreadable ({readResult.Status}): {readResult.Detail}. Run 'index' again to rebuild.");
        return false;
    }

    nodes = readResult.Nodes;
    return true;
}

void RunSearch(string pattern, bool includeAll)
{
    if (!TryLoadSessionNodes(out var nodes))
    {
        return;
    }

    var query = new SearchQuery
    {
        NamePattern = pattern,
        MaxResults = 25,
        Kinds = includeAll ? null : defaultSearchKinds,
    };
    var hits = new NodeSearchEngine().Search(nodes, query);
    foreach (var hit in hits)
    {
        var lang = LanguageOf(hit.Location.FilePath);
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
    if (!TryLoadSessionNodes(out var nodes))
    {
        return;
    }

    var node = nodes.FirstOrDefault(n => n.Id == nodeId);
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
    if (!TryLoadSessionNodes(out var nodes))
    {
        return;
    }

    var result = new NodeRetriever().GetCode(nodes, nodeId);
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
    if (!TryLoadSessionNodes(out var nodes))
    {
        return;
    }

    var sessionManager = new SessionManager(new SessionRegistry());
    var resolution = sessionManager.TryResolve(workingDirectory);
    var files = nodes.Select(n => n.Location.FilePath).Distinct().ToArray();
    var tree = DirectoryTreeBuilder.Build(resolution.Session!.RootPath, files);

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
    if (!TryLoadSessionNodes(out var nodes))
    {
        return;
    }

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
    if (!TryLoadSessionNodes(out var nodes))
    {
        return;
    }

    var files = nodes.Select(n => n.Location.FilePath).Distinct().ToArray();
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
// SkippedRelationships notes are name-text matches, not exact edges, so this
// is a diagnostic hint, not a guarantee the skip really concerns this node.
void PrintRelationshipResult(IReadOnlyList<CodeIndexer.Core.Nodes.CodeNode> matches, IReadOnlyList<CodeIndexer.Core.Nodes.CodeNode> allNodes, CodeIndexer.Core.Nodes.CodeNode target)
{
    if (matches.Count > 0)
    {
        PrintNodeList(matches);
        return;
    }

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
    if (!TryLoadSessionNodes(out var nodes))
    {
        return;
    }

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
    if (!TryLoadSessionNodes(out var nodes))
    {
        return;
    }

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

    PrintRelationshipResult(Array.Empty<CodeIndexer.Core.Nodes.CodeNode>(), nodes, target);
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

void RunCallers(string nodeId)
{
    if (!TryLoadSessionNodes(out var nodes) || !TryRequireKind(nodes, nodeId, "callers", out var target, CodeIndexer.Core.Nodes.NodeKind.Method))
    {
        return;
    }

    PrintRelationshipResult(new ReferenceFinder().GetCallers(nodes, nodeId), nodes, target!);
}

void RunCallees(string nodeId)
{
    if (!TryLoadSessionNodes(out var nodes) || !TryRequireKind(nodes, nodeId, "callees", out _, CodeIndexer.Core.Nodes.NodeKind.Method))
    {
        return;
    }

    PrintNodeList(new ReferenceFinder().GetCallees(nodes, nodeId));
}

void RunSubtypes(string nodeId)
{
    if (!TryLoadSessionNodes(out var nodes) || !TryRequireKind(nodes, nodeId, "subtypes", out var target, CodeIndexer.Core.Nodes.NodeKind.Class, CodeIndexer.Core.Nodes.NodeKind.Interface, CodeIndexer.Core.Nodes.NodeKind.Struct))
    {
        return;
    }

    PrintRelationshipResult(new ReferenceFinder().GetSubtypes(nodes, nodeId), nodes, target!);
}

void RunUsages(string nodeId)
{
    if (!TryLoadSessionNodes(out var nodes) || !TryRequireKind(nodes, nodeId, "usages", out var target, CodeIndexer.Core.Nodes.NodeKind.Class, CodeIndexer.Core.Nodes.NodeKind.Interface, CodeIndexer.Core.Nodes.NodeKind.Struct, CodeIndexer.Core.Nodes.NodeKind.Enum))
    {
        return;
    }

    PrintRelationshipResult(new ReferenceFinder().GetUsages(nodes, nodeId), nodes, target!);
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
