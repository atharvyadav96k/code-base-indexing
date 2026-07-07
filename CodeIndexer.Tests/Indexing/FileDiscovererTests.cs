using CodeIndexer.Indexing.Discovery;
using Xunit;

namespace CodeIndexer.Tests.Indexing;

public class FileDiscovererTests : IDisposable
{
    private readonly string _tempRoot;

    public FileDiscovererTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "codeindex-discovery-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);
    }

    private void WriteFile(string relativePath, string content = "")
    {
        var fullPath = Path.Combine(_tempRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static FileDiscoveryOptions CsharpOptions(bool respectGitignore = true) => new()
    {
        IncludeExtensions = new[] { ".cs" },
        RespectGitignore = respectGitignore,
    };

    [Fact]
    public void Discover_ReturnsOnlyMatchingExtensions()
    {
        WriteFile("Program.cs");
        WriteFile("readme.md");
        WriteFile("data.json");

        var results = new FileDiscoverer().Discover(_tempRoot, CsharpOptions());

        Assert.Single(results);
        Assert.EndsWith("Program.cs", results[0]);
    }

    [Fact]
    public void Discover_SkipsAlwaysExcludedDirectories()
    {
        WriteFile("src/App.cs");
        WriteFile("bin/Debug/Generated.cs");
        WriteFile("obj/Temp.cs");

        var results = new FileDiscoverer().Discover(_tempRoot, CsharpOptions());

        Assert.Single(results);
        Assert.EndsWith("App.cs", results[0]);
    }

    [Fact]
    public void Discover_RespectsGitignore_UnanchoredPattern()
    {
        WriteFile("src/App.cs");
        WriteFile("src/Generated.cs");
        WriteFile(".gitignore", "Generated.cs\n");

        var results = new FileDiscoverer().Discover(_tempRoot, CsharpOptions());

        Assert.Single(results);
        Assert.EndsWith("App.cs", results[0]);
    }

    [Fact]
    public void Discover_RespectsGitignore_DirectoryPattern()
    {
        WriteFile("src/App.cs");
        WriteFile("vendor/Third.cs");
        WriteFile(".gitignore", "vendor/\n");

        var results = new FileDiscoverer().Discover(_tempRoot, CsharpOptions());

        Assert.Single(results);
        Assert.EndsWith("App.cs", results[0]);
    }

    [Fact]
    public void Discover_IgnoreDisabled_ReturnsGitignoredFiles()
    {
        WriteFile("src/App.cs");
        WriteFile("src/Generated.cs");
        WriteFile(".gitignore", "Generated.cs\n");

        var results = new FileDiscoverer().Discover(_tempRoot, CsharpOptions(respectGitignore: false));

        Assert.Equal(2, results.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
