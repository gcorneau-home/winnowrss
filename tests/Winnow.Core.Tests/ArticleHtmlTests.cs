using Winnow.Core.Models;
using Winnow.Core.Reading;

namespace Winnow.Core.Tests;

public class ArticleHtmlTests
{
    [Fact]
    public void Wraps_content_with_base_link_and_security_policy()
    {
        var html = ArticleHtml.Build(new Article
        {
            Link = "https://fil-bidouille.example/un-article.html",
            ContentHtml = "<p>Bonjour <img src=\"/img/a.png\"></p>",
        });

        Assert.Contains("<base href=\"https://fil-bidouille.example/un-article.html\">", html);
        Assert.Contains("<p>Bonjour <img src=\"/img/a.png\"></p>", html);
        Assert.Contains("Content-Security-Policy", html);
        Assert.Contains("default-src 'none'", html); // no scripts
    }

    [Fact]
    public void A_theme_stylesheet_comes_after_the_built_in_one()
    {
        var html = ArticleHtml.WithExtraStyle(ArticleHtml.Build(new Article { ContentHtml = "<p>x</p>" }), "body { color: #F8F8F2; }");

        Assert.True(html.IndexOf("body { color: #F8F8F2; }") > html.IndexOf("font: 16px"));
        Assert.True(html.IndexOf("body { color: #F8F8F2; }") < html.IndexOf("</head>"));
    }

    [Fact]
    public void Falls_back_to_the_encoded_summary_without_content()
    {
        var html = ArticleHtml.Build(new Article { Summary = "Résumé <court> & simple" });

        Assert.Contains("&lt;court&gt; &amp; simple</p>", html);
        Assert.DoesNotContain("<court>", html);
        Assert.DoesNotContain("<base", html);
    }
}
