using Acornima;

namespace CodeIndexer.Parsing.JavaScript.Internal;

/// <summary>
/// Finds the doc comment immediately preceding a node — a JSDoc block
/// (<c>/** ... */</c>) or a single line comment directly above it, with
/// nothing but whitespace in between.
/// </summary>
internal sealed class JsDocCommentExtractor
{
    private readonly string _sourceText;
    private readonly IReadOnlyList<(int Start, int End, string Raw)> _comments;

    public JsDocCommentExtractor(string sourceText, IReadOnlyList<(int Start, int End, string Raw)> comments)
    {
        _sourceText = sourceText;
        _comments = comments;
    }

    public string? FindFor(int nodeStart)
    {
        for (var i = _comments.Count - 1; i >= 0; i--)
        {
            var comment = _comments[i];
            if (comment.End > nodeStart)
            {
                continue;
            }

            var gap = _sourceText[comment.End..nodeStart];
            if (!string.IsNullOrWhiteSpace(gap))
            {
                return null;
            }

            return Clean(comment.Raw);
        }

        return null;
    }

    private static string? Clean(string raw)
    {
        var text = raw;
        if (text.StartsWith("/**") || text.StartsWith("/*"))
        {
            text = text.Trim('/', '*');
        }
        else if (text.StartsWith("//"))
        {
            text = text[2..];
        }

        var lines = text.Split('\n')
            .Select(line => line.Trim().TrimStart('*').Trim())
            .Where(line => line.Length > 0);

        var cleaned = string.Join(" ", lines).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}
