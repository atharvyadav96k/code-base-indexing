using System.Security.Cryptography;
using System.Text;

namespace CodeIndexer.Core.Nodes;

/// <summary>
/// Computes the content hash used on <see cref="CodeNode.ContentHash"/> for
/// retrieval-time staleness detection.
/// </summary>
public static class ContentHasher
{
    public static string Hash(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
