using Winnow.Core.Feeds;
using Winnow.Core.Tests.TestSupport;
using Winnow.Core.Text;

namespace Winnow.Core.Tests;

public class FeedParserTests
{
    [Fact]
    public void Parses_full_content_rss_with_creator()
    {
        var feed = Fixtures.SampleFeed();

        Assert.Equal("Le Fil Bidouille - flux complet", feed.Title);
        Assert.Equal("https://fil-bidouille.example/", feed.SiteUrl);
        Assert.Equal(3, feed.Items.Count);

        var first = feed.Items[0];
        Assert.Equal("Test de la batterie externe VoltaFlow Pro et de son refroidissement liquide apparent", first.Title);
        Assert.Equal("Camille Martin", first.Author);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 15, 27, 37, TimeSpan.Zero), first.PublishedAt);
        Assert.StartsWith("https://fil-bidouille.example/test-batterie", first.Guid);
        Assert.DoesNotContain("utm_", first.Link);
        Assert.Contains("<p>", first.ContentHtml);
        Assert.Contains("batterie externe", first.Tags);
        Assert.StartsWith("– Contient des liens affiliés", first.Summary);
    }

    [Fact]
    public void Parses_atom_feed()
    {
        var feed = Fixtures.Parse("""
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Atom blog</title>
              <link rel="alternate" href="https://blog.example.com/" />
              <entry>
                <id>urn:uuid:1</id>
                <title>Hello</title>
                <link rel="alternate" href="/posts/hello" />
                <updated>2026-09-01T10:00:00Z</updated>
                <author><name>Ada</name></author>
                <content type="html">&lt;p&gt;Body&lt;/p&gt;</content>
                <category term="csharp" />
              </entry>
            </feed>
            """, "https://blog.example.com/atom.xml");

        var item = Assert.Single(feed.Items);
        Assert.Equal("Atom blog", feed.Title);
        Assert.Equal("urn:uuid:1", item.Guid);
        Assert.Equal("https://blog.example.com/posts/hello", item.Link);
        Assert.Equal("Ada", item.Author);
        Assert.Equal("<p>Body</p>", item.ContentHtml);
        Assert.Equal(["csharp"], item.Tags);
    }

    [Fact]
    public void Key_falls_back_to_normalized_link_without_guid()
    {
        var feed = Fixtures.Parse("""
            <rss version="2.0"><channel><title>T</title>
              <item><title>A</title><link>https://ex.com/a?id=3&amp;utm_source=rss#top</link></item>
            </channel></rss>
            """);

        Assert.Equal("https://ex.com/a?id=3#top", ArticleKeys.Compute(feed.Items[0]));
    }

    [Theory]
    [InlineData("https://ex.com/a?utm_source=x&utm_medium=y", "https://ex.com/a")]
    [InlineData("https://ex.com/a?b=1&UTM_campaign=z", "https://ex.com/a?b=1")]
    [InlineData("https://ex.com/changelog.html#v1.3", "https://ex.com/changelog.html#v1.3")]
    [InlineData("not a url", "not a url")]
    public void NormalizeLink_strips_tracking_parameters(string input, string expected) =>
        Assert.Equal(expected, ArticleKeys.NormalizeLink(input));

    [Fact]
    public void ToPlainText_strips_tags_scripts_and_entities()
    {
        var text = HtmlText.ToPlainText("<p>Caf&eacute;  <b>C#</b></p><script>alert(1)</script>\n<p>fin</p>");
        Assert.Equal("Café C# fin", text);
    }
}
