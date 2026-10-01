using System.Net;
using Winnow.Core.Models;

namespace Winnow.Core.Reading;

/// <summary>Builds the HTML document shown in the reading pane for an article.</summary>
public static class ArticleHtml
{
    // No scripts, no plugins; images, media and styles may come from anywhere (the article's site, CDNs).
    private const string ContentSecurityPolicy =
        "default-src 'none'; img-src * data:; media-src *; style-src 'unsafe-inline' *; font-src * data:; frame-src https:";

    private const string Style =
        """
        :root { color-scheme: light dark; }
        body { font: 16px/1.65 "Segoe UI", system-ui, sans-serif; margin: 0; padding: 20px 28px 48px; }
        article { max-width: 760px; margin: 0 auto; overflow-wrap: break-word; }
        img, video, iframe { max-width: 100%; height: auto; }
        figure { margin: 1.2em 0; }
        pre, code { font-family: "Cascadia Code", Consolas, monospace; font-size: 0.92em; }
        pre { overflow-x: auto; padding: 12px; border-radius: 6px; background: rgba(127, 127, 127, 0.12); }
        blockquote { margin: 1em 0; padding-left: 1em; border-left: 3px solid rgba(127, 127, 127, 0.4); opacity: 0.9; }
        a { color: #2b7cd3; }
        @media (prefers-color-scheme: dark) { a { color: #6cb4ff; } }
        """;

    /// <summary>Adds a stylesheet after the built-in one (theme colors), so it wins.</summary>
    public static string WithExtraStyle(string html, string css)
    {
        var end = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return end < 0 ? html : html.Insert(end, $"<style>{css}</style>\n");
    }

    /// <summary>
    /// Wraps the article content (or its summary when the feed gave no content) in a standalone page.
    /// A &lt;base&gt; pointing to the article makes relative links and images resolve against its site.
    /// </summary>
    public static string Build(Article article)
    {
        var body = article.ContentHtml
            ?? $"<p>{WebUtility.HtmlEncode(article.Summary ?? "")}</p>";
        if (article.State == ArticleState.Archived)
            body = ArticleImages.KeepOnlyPlainSources(body);
        var baseTag = Uri.TryCreate(article.Link, UriKind.Absolute, out var link)
            ? $"<base href=\"{WebUtility.HtmlEncode(link.AbsoluteUri)}\">"
            : "";

        return $"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="{ContentSecurityPolicy}">
            <meta name="referrer" content="no-referrer">
            {baseTag}
            <style>{Style}</style>
            </head>
            <body><article>{body}</article></body>
            </html>
            """;
    }
}
