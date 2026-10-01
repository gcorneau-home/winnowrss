using System.Net;
using System.Text.RegularExpressions;

namespace Winnow.Core.Text;

public static partial class HtmlText
{
    /// <summary>Crude HTML to text conversion, good enough for search indexing and short descriptions.</summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var text = ScriptOrStyle().Replace(html, " ");
        text = Tag().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        return Whitespace().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
