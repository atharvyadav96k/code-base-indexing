using System.Text.Json;
using CodeIndexer.Core.Nodes;
using CodeIndexer.Storage.Json;

namespace CodeIndexer.Storage;

/// <summary>
/// Reads and writes the node index as JSON, sharded per source file under
/// "&lt;indexRoot&gt;/indexed-files/&lt;relative-path&gt;/{index.json,relations.json}",
/// plus a repo-wide "search-index.json". Writes are atomic (temp file + rename)
/// so a crash mid-write can never corrupt an existing shard; reads treat
/// missing/malformed files as explicit outcomes rather than throwing.
/// </summary>
public sealed class JsonIndexStore : IIndexStore
{
    private const string IndexedFilesDirectoryName = "indexed-files";
    private const string SearchIndexFileName = "search-index.json";
    private const string IndexFileName = "index.json";
    private const string RelationsFileName = "relations.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void ResetAll(string indexRootPath)
    {
        var indexedFilesRoot = Path.Combine(indexRootPath, IndexedFilesDirectoryName);
        if (Directory.Exists(indexedFilesRoot))
        {
            Directory.Delete(indexedFilesRoot, recursive: true);
        }
    }

    public void WriteFile(string indexRootPath, string absoluteFilePath, IReadOnlyList<CodeNode> nodes)
    {
        var shardDirectory = GetShardDirectory(indexRootPath, absoluteFilePath);
        Directory.CreateDirectory(shardDirectory);

        var indexDto = new IndexFileDto
        {
            FilePath = absoluteFilePath,
            Nodes = nodes.Select(NodeDtoMapper.ToDto).ToList(),
        };
        var relationsDto = new RelationsFileDto
        {
            FilePath = absoluteFilePath,
            Relations = nodes.SelectMany(RelationDtoMapper.ToDtos).ToList(),
        };

        AtomicWriteJson(Path.Combine(shardDirectory, IndexFileName), indexDto);
        AtomicWriteJson(Path.Combine(shardDirectory, RelationsFileName), relationsDto);
    }

    public void DeleteFile(string indexRootPath, string absoluteFilePath)
    {
        var shardDirectory = GetShardDirectory(indexRootPath, absoluteFilePath);
        if (Directory.Exists(shardDirectory))
        {
            Directory.Delete(shardDirectory, recursive: true);
        }

        var indexedFilesRoot = Path.Combine(indexRootPath, IndexedFilesDirectoryName);
        DeleteEmptyAncestors(Path.GetDirectoryName(shardDirectory), indexedFilesRoot);
    }

    public void WriteSearchIndex(string indexRootPath, IReadOnlyList<CodeNode> allCurrentNodes)
    {
        var dto = new SearchIndexDto
        {
            Entries = allCurrentNodes.Select(n => new FileEntryDto
            {
                Id = n.Id,
                Name = n.Name,
                QualifiedName = n.QualifiedName,
                Kind = (int)n.Kind,
                FilePath = n.Location.FilePath,
            }).ToList(),
        };

        Directory.CreateDirectory(indexRootPath);
        AtomicWriteJson(Path.Combine(indexRootPath, SearchIndexFileName), dto);
    }

    public IndexReadResult ReadFile(string indexRootPath, string absoluteFilePath)
    {
        var shardDirectory = GetShardDirectory(indexRootPath, absoluteFilePath);
        var indexPath = Path.Combine(shardDirectory, IndexFileName);
        var relationsPath = Path.Combine(shardDirectory, RelationsFileName);

        if (!File.Exists(indexPath))
        {
            return IndexReadResult.NotFound;
        }

        try
        {
            var indexDto = ReadJson<IndexFileDto>(indexPath);
            var relationsDto = File.Exists(relationsPath) ? ReadJson<RelationsFileDto>(relationsPath) : new RelationsFileDto();

            if (indexDto is null)
            {
                return IndexReadResult.Corrupted($"Malformed index.json at {indexPath}.");
            }

            var edgesBySourceId = (relationsDto?.Relations ?? new List<RelationDto>())
                .GroupBy(r => r.SourceNodeId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<NodeEdge>)g.Select(RelationDtoMapper.ToDomain).ToArray());

            var nodes = indexDto.Nodes
                .Select(dto => NodeDtoMapper.ToDomain(dto, edgesBySourceId.GetValueOrDefault(dto.Id, Array.Empty<NodeEdge>())))
                .ToArray();

            return IndexReadResult.Ok(nodes);
        }
        catch (JsonException ex)
        {
            return IndexReadResult.Corrupted($"Malformed JSON in shard for {absoluteFilePath}: {ex.Message}");
        }
        catch (IOException ex)
        {
            return IndexReadResult.Corrupted($"I/O error reading shard for {absoluteFilePath}: {ex.Message}");
        }
    }

    public IndexReadResult ReadFiles(string indexRootPath, IReadOnlyList<string> absoluteFilePaths)
    {
        var allNodes = new List<CodeNode>();
        foreach (var filePath in absoluteFilePaths)
        {
            var result = ReadFile(indexRootPath, filePath);
            if (result.Status == IndexReadStatus.Corrupted)
            {
                return result;
            }

            allNodes.AddRange(result.Nodes);
        }

        return IndexReadResult.Ok(allNodes);
    }

    public IReadOnlyList<RelationDto> ReadRelations(string indexRootPath, IReadOnlyList<string> absoluteFilePaths)
    {
        var relations = new List<RelationDto>();
        foreach (var filePath in absoluteFilePaths)
        {
            var relationsPath = Path.Combine(GetShardDirectory(indexRootPath, filePath), RelationsFileName);
            if (!File.Exists(relationsPath))
            {
                continue;
            }

            var dto = ReadJson<RelationsFileDto>(relationsPath);
            if (dto is not null)
            {
                relations.AddRange(dto.Relations);
            }
        }

        return relations;
    }

    public SearchIndexReadResult ReadSearchIndex(string indexRootPath)
    {
        var path = Path.Combine(indexRootPath, SearchIndexFileName);
        if (!File.Exists(path))
        {
            return SearchIndexReadResult.NotFound;
        }

        try
        {
            var dto = ReadJson<SearchIndexDto>(path);
            return dto is null
                ? SearchIndexReadResult.Corrupted($"Malformed search-index.json at {path}.")
                : SearchIndexReadResult.Ok(dto.Entries);
        }
        catch (JsonException ex)
        {
            return SearchIndexReadResult.Corrupted($"Malformed search-index.json: {ex.Message}");
        }
        catch (IOException ex)
        {
            return SearchIndexReadResult.Corrupted($"I/O error reading search-index.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Shard directory mirrors the source tree under indexed-files/ rather than
    /// flattening filenames, avoiding same-filename collisions across
    /// directories while staying human-debuggable. The source root is derived
    /// as the marker directory's parent, since indexRootPath is always
    /// "&lt;sourceRoot&gt;/.codeindex".
    /// </summary>
    public static string GetShardDirectory(string indexRootPath, string absoluteFilePath)
    {
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(indexRootPath))
            ?? throw new InvalidOperationException($"Cannot determine source root from index root '{indexRootPath}'.");

        var relative = Path.GetRelativePath(sourceRoot, absoluteFilePath);
        var sanitizedSegments = relative
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Select(SanitizeSegment);

        return Path.Combine(new[] { indexRootPath, IndexedFilesDirectoryName }.Concat(sanitizedSegments).ToArray());
    }

    private static string SanitizeSegment(string segment)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return invalid.Aggregate(segment, (current, ch) => current.Replace(ch, '_'));
    }

    private static void DeleteEmptyAncestors(string? directory, string stopAtInclusive)
    {
        var stopFull = Path.GetFullPath(stopAtInclusive);
        while (directory is not null && Directory.Exists(directory))
        {
            var full = Path.GetFullPath(directory);
            if (!full.StartsWith(stopFull, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                break;
            }

            Directory.Delete(directory);
            if (string.Equals(full, stopFull, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            directory = Path.GetDirectoryName(directory);
        }
    }

    private static void AtomicWriteJson<T>(string filePath, T value)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempFilePath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tempFilePath, JsonSerializer.Serialize(value, Options));
        File.Move(tempFilePath, filePath, overwrite: true);
    }

    private static T? ReadJson<T>(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}
