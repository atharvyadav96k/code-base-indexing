using System.Text.Json;

namespace CodeIndexer.Indexing.Sessions;

/// <summary>
/// A global list of known session roots, keyed by path, for listing/inspecting/
/// removing sessions from outside any one of them. Stored outside any session,
/// under the user's profile directory.
/// </summary>
public sealed class SessionRegistry
{
    private readonly string _registryFilePath;

    public SessionRegistry(string? registryFilePath = null)
    {
        _registryFilePath = registryFilePath ?? DefaultRegistryPath();
    }

    private static string DefaultRegistryPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "codeindex", "registry.json");
    }

    public void Register(string rootPath)
    {
        var normalized = Path.GetFullPath(rootPath);
        var roots = ReadAll();

        if (roots.Contains(normalized))
        {
            return;
        }

        roots.Add(normalized);
        WriteAll(roots);
    }

    public void Remove(string rootPath)
    {
        var normalized = Path.GetFullPath(rootPath);
        var roots = ReadAll();

        if (roots.Remove(normalized))
        {
            WriteAll(roots);
        }
    }

    public IReadOnlyList<string> List() => ReadAll();

    private List<string> ReadAll()
    {
        if (!File.Exists(_registryFilePath))
        {
            return new List<string>();
        }

        var json = File.ReadAllText(_registryFilePath);
        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }

    private void WriteAll(List<string> roots)
    {
        var directory = Path.GetDirectoryName(_registryFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(roots, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_registryFilePath, json);
    }
}
