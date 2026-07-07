using System.Text;
using CodeIndexer.Core.Nodes;
using CodeIndexer.Storage.Internal;

namespace CodeIndexer.Storage;

/// <summary>
/// Reads and writes the whole node index to a single binary file. Writes are
/// atomic (temp file + rename) so a crash mid-write can never corrupt the
/// existing index; reads detect version mismatches and truncation/corruption
/// explicitly rather than throwing into caller code.
/// </summary>
public sealed class BinaryIndexStore
{
    private static readonly Encoding HeaderEncoding = Encoding.ASCII;

    public void Write(string filePath, IReadOnlyList<CodeNode> nodes)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempFilePath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream, HeaderEncoding))
        {
            writer.Write(HeaderEncoding.GetBytes(BinaryIndexFormat.MagicHeader));
            writer.Write(BinaryIndexFormat.CurrentVersion);
            writer.Write(nodes.Count);

            foreach (var node in nodes)
            {
                NodeBinarySerializer.Write(writer, node);
            }
        }

        File.Move(tempFilePath, filePath, overwrite: true);
    }

    public IndexReadResult Read(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return IndexReadResult.NotFound;
        }

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream, HeaderEncoding);

            var magicBytes = reader.ReadBytes(BinaryIndexFormat.MagicHeader.Length);
            var magic = HeaderEncoding.GetString(magicBytes);
            if (magic != BinaryIndexFormat.MagicHeader)
            {
                return IndexReadResult.Corrupted($"Bad magic header: '{magic}'.");
            }

            var version = reader.ReadInt32();
            if (version != BinaryIndexFormat.CurrentVersion)
            {
                return IndexReadResult.VersionMismatch(version);
            }

            var count = reader.ReadInt32();
            var nodes = new CodeNode[count];
            for (var i = 0; i < count; i++)
            {
                nodes[i] = NodeBinarySerializer.Read(reader);
            }

            return IndexReadResult.Ok(nodes);
        }
        catch (EndOfStreamException ex)
        {
            return IndexReadResult.Corrupted($"Truncated index file: {ex.Message}");
        }
        catch (IOException ex)
        {
            return IndexReadResult.Corrupted($"I/O error reading index file: {ex.Message}");
        }
    }
}
