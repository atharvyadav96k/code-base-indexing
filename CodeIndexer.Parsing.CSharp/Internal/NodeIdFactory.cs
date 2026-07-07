using System.Security.Cryptography;
using System.Text;
using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Parsing.CSharp.Internal;

/// <summary>
/// Builds a stable node ID from the file, qualified name, kind, and signature —
/// stable across re-parses as long as the declaration itself doesn't change,
/// and distinct for overloads sharing a name.
/// </summary>
internal static class NodeIdFactory
{
    public static string Create(string filePath, string qualifiedName, NodeKind kind, string signature)
    {
        var key = $"{filePath}|{qualifiedName}|{kind}|{signature}";
        var bytes = Encoding.UTF8.GetBytes(key);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }
}
