using System.Net;
using System.Text.RegularExpressions;

namespace Winnow.Core.Reading;

public static partial class ArticleImages
{
    /// <summary>
    /// Absolute http(s) URLs of the &lt;img src&gt; in an article, resolved against its link the same way
    /// the reading pane's &lt;base&gt; resolves them, so stored images match what the page requests.
    /// </summary>
    public static IReadOnlyList<string> Extract(string? html, string? articleLink)
    {
        if (string.IsNullOrEmpty(html))
            return [];

        Uri.TryCreate(articleLink, UriKind.Absolute, out var baseUri);
        var urls = new List<string>();
        foreach (Match match in ImageSource().Matches(html))
        {
            var src = WebUtility.HtmlDecode(match.Groups["src"].Value).Trim();
            var resolved = Uri.TryCreate(src, UriKind.Absolute, out var absolute) ? absolute
                : baseUri is not null && Uri.TryCreate(baseUri, src, out var relative) ? relative
                : null;
            if (resolved is { Scheme: "http" or "https" } && !urls.Contains(resolved.AbsoluteUri))
                urls.Add(resolved.AbsoluteUri);
        }
        return urls;
    }

    /// <summary>
    /// Removes responsive image candidates (srcset, &lt;source&gt;) so the browser uses the plain src,
    /// which is the one kept in the archive.
    /// </summary>
    public static string KeepOnlyPlainSources(string html) =>
        PictureSource().Replace(ResponsiveAttribute().Replace(html, ""), "");

    [GeneratedRegex("""<img\b[^>]*?\ssrc\s*=\s*(["'])(?<src>[^"']+)\1""", RegexOptions.IgnoreCase)]
    private static partial Regex ImageSource();

    [GeneratedRegex("""\s(srcset|sizes)\s*=\s*("[^"]*"|'[^']*')""", RegexOptions.IgnoreCase)]
    private static partial Regex ResponsiveAttribute();

    [GeneratedRegex("""<source\b[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex PictureSource();
}
