using CodeIndexer.Indexing.Sessions;
using Xunit;

namespace CodeIndexer.Tests.Indexing;

public class SessionManagerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _registryFile;

    public SessionManagerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "codeindex-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);
        _registryFile = Path.Combine(_tempRoot, "registry.json");
    }

    private SessionManager CreateManager() => new(new SessionRegistry(_registryFile));

    [Fact]
    public void TryResolve_NoMarker_ReturnsNotFound()
    {
        var manager = CreateManager();

        var result = manager.TryResolve(_tempRoot);

        Assert.False(result.Found);
    }

    [Fact]
    public void EnsureSession_CreatesMarkerDirectoryAtGivenPath()
    {
        var manager = CreateManager();

        var session = manager.EnsureSession(_tempRoot);

        Assert.Equal(Path.GetFullPath(_tempRoot), session.RootPath);
        Assert.True(Directory.Exists(session.MarkerDirectoryPath));
        Assert.True(File.Exists(session.MetadataFilePath));
    }

    [Fact]
    public void TryResolve_FromChildDirectory_WalksUpToRoot()
    {
        var manager = CreateManager();
        var session = manager.EnsureSession(_tempRoot);

        var childDir = Path.Combine(_tempRoot, "src", "nested");
        Directory.CreateDirectory(childDir);

        var result = manager.TryResolve(childDir);

        Assert.True(result.Found);
        Assert.Equal(session.RootPath, result.Session!.RootPath);
    }

    [Fact]
    public void TryResolve_NestedSession_NearestMarkerWins()
    {
        var manager = CreateManager();
        manager.EnsureSession(_tempRoot);

        var nestedRoot = Path.Combine(_tempRoot, "nested-project");
        Directory.CreateDirectory(nestedRoot);
        var nestedSession = manager.EnsureSession(nestedRoot);

        var deepChild = Path.Combine(nestedRoot, "src");
        Directory.CreateDirectory(deepChild);

        var result = manager.TryResolve(deepChild);

        Assert.True(result.Found);
        Assert.Equal(nestedSession.RootPath, result.Session!.RootPath);
    }

    [Fact]
    public void EnsureSession_CalledTwice_ReturnsSameSessionWithoutRecreating()
    {
        var manager = CreateManager();

        var first = manager.EnsureSession(_tempRoot);
        var second = manager.EnsureSession(_tempRoot);

        Assert.Equal(first.RootPath, second.RootPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
