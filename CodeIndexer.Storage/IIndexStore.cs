using CodeIndexer.Core.Nodes;
using CodeIndexer.Storage.Json;

namespace CodeIndexer.Storage;

/// <summary>
/// Persists the node index as a JSON structure sharded per source file, rooted
/// at a session's marker directory (e.g. ".codeindex"). Every method takes the
/// marker directory's absolute path plus the absolute source file path(s) the
/// caller already has (from discovery/manifest) — this interface has no
/// knowledge of session/root-path concepts beyond that.
/// </summary>
public interface IIndexStore
{
    /// <summary>Deletes the entire indexed-files/ shard tree, for a clean full rebuild.</summary>
    void ResetAll(string indexRootPath);

    /// <summary>Writes (overwrites) one file's index.json + relations.json (outbound edges only).</summary>
    void WriteFile(string indexRootPath, string absoluteFilePath, IReadOnlyList<CodeNode> nodes);

    /// <summary>Deletes one file's shard folder, cleaning up now-empty parent directories.</summary>
    void DeleteFile(string indexRootPath, string absoluteFilePath);

    /// <summary>Fully rewrites search-index.json from the current full node set.</summary>
    void WriteSearchIndex(string indexRootPath, IReadOnlyList<CodeNode> allCurrentNodes);

    /// <summary>Fast path: loads just one file's shard, edges reattached.</summary>
    IndexReadResult ReadFile(string indexRootPath, string absoluteFilePath);

    /// <summary>Loads and concatenates several files' shards (e.g. the full graph, driven by manifest.json's file list).</summary>
    IndexReadResult ReadFiles(string indexRootPath, IReadOnlyList<string> absoluteFilePaths);

    /// <summary>
    /// Loads only relations.json (no bodies/signatures) across the given files —
    /// the primitive reverse-lookup commands (refs/callers/subtypes/usages)
    /// scan to find edges by target id without loading every file's full shard.
    /// </summary>
    IReadOnlyList<RelationDto> ReadRelations(string indexRootPath, IReadOnlyList<string> absoluteFilePaths);

    SearchIndexReadResult ReadSearchIndex(string indexRootPath);
}
