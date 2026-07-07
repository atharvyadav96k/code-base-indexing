using CodeIndexer.Core.Parsing;
using CodeIndexer.Indexing;
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

var command = args.Length > 0 ? args[0] : "help";
var arg1 = args.Length > 1 ? args[1] : null;
var workingDirectory = Directory.GetCurrentDirectory();

switch (command)
{
    case "index":
        await RunIndexAsync(arg1 ?? workingDirectory);
        break;

    case "search":
        RunSearch(arg1 ?? string.Empty);
        break;

    case "get-code":
        RunGetCode(arg1 ?? string.Empty);
        break;

    case "info":
        RunInfo(arg1 ?? string.Empty);
        break;

    case "tree":
        RunTree();
        break;

    case "outline":
        RunOutline();
        break;

    case "locate":
        RunLocate(arg1 ?? string.Empty);
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

void RunSearch(string pattern)
{
    if (!TryLoadSessionNodes(out var nodes))
    {
        return;
    }

    var hits = new NodeSearchEngine().Search(nodes, new SearchQuery { NamePattern = pattern, MaxResults = 25 });
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

void RunTree()
{
    if (!TryLoadSessionNodes(out var nodes))
    {
        return;
    }

    var sessionManager = new SessionManager(new SessionRegistry());
    var resolution = sessionManager.TryResolve(workingDirectory);
    var files = nodes.Select(n => n.Location.FilePath).Distinct().ToArray();
    var tree = DirectoryTreeBuilder.Build(resolution.Session!.RootPath, files);
    PrintTree(tree, 0);
}

void PrintTree(DirectoryTreeNode node, int depth)
{
    Console.WriteLine(new string(' ', depth * 2) + node.Name);
    foreach (var child in node.Children)
    {
        PrintTree(child, depth + 1);
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

void PrintNodeList(IReadOnlyList<CodeIndexer.Core.Nodes.CodeNode> matches)
{
    if (matches.Count == 0)
    {
        Console.WriteLine("(none found)");
        return;
    }

    foreach (var node in matches)
    {
        Console.WriteLine($"{node.Id}  {node.Kind,-10} {node.QualifiedName}  ({node.Location.FilePath}:{node.Location.StartLine})");
    }
}

void RunRefs(string nodeId)
{
    if (!TryLoadSessionNodes(out var nodes))
    {
        return;
    }

    var hits = new ReferenceFinder().FindReferences(nodes, nodeId);
    if (hits.Count == 0)
    {
        Console.WriteLine("(no references found)");
        return;
    }

    foreach (var hit in hits)
    {
        Console.WriteLine($"{hit.Source.Id}  {hit.Kind,-10} {hit.Source.QualifiedName}  ({hit.Source.Location.FilePath}:{hit.Source.Location.StartLine})");
    }
}

void RunCallers(string nodeId)
{
    if (!TryLoadSessionNodes(out var nodes))
    {
        return;
    }

    PrintNodeList(new ReferenceFinder().GetCallers(nodes, nodeId));
}

void RunCallees(string nodeId)
{
    if (!TryLoadSessionNodes(out var nodes))
    {
        return;
    }

    PrintNodeList(new ReferenceFinder().GetCallees(nodes, nodeId));
}

void RunSubtypes(string nodeId)
{
    if (!TryLoadSessionNodes(out var nodes))
    {
        return;
    }

    PrintNodeList(new ReferenceFinder().GetSubtypes(nodes, nodeId));
}

void PrintHelp()
{
    Console.WriteLine("""
        CodeIndexer — usage:
          index [path]         Full re-index of the session rooted at path (default: cwd)
          search <pattern>     Search node names, ranked (prints id + name only)
          info <nodeId>        Print kind, path, signature, and doc comment for a node by ID
          get-code <nodeId>    Print the full body of a node by ID
          tree                 Print the directory tree of indexed files
          outline               Print the namespace/scope outline
          locate <fragment>    Find files by name or path fragment
          refs <nodeId>        Find every node that references this one (any edge kind)
          callers <nodeId>     Find nodes that call this one
          callees <nodeId>     Find nodes this one calls
          subtypes <nodeId>    Find types that inherit from/implement this one
        """);
}
