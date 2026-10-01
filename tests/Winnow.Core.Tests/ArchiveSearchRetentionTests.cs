using Winnow.Core.Abstractions;
using Winnow.Core.Models;
using Winnow.Core.Reading;
using Winnow.Core.Services;
using Winnow.Core.Tests.TestSupport;

namespace Winnow.Core.Tests;

public class ArchiveSearchRetentionTests
{
    private const string BatteryTitle = "Test de la batterie externe VoltaFlow Pro et de son refroidissement liquide apparent";

    // ----- Archive -----

    [Fact]
    public void Image_urls_are_resolved_against_the_article_link()
    {
        var urls = ArticleImages.Extract(
            """<p><img src="/a/b.jpg" alt=""><img class="x" src='https://cdn.example.com/c.png?w=1&amp;h=2'><img src="data:image/png;base64,AA"><img src="/a/b.jpg"></p>""",
            "https://fil-bidouille.example/post.html");

        Assert.Equal(["https://fil-bidouille.example/a/b.jpg", "https://cdn.example.com/c.png?w=1&h=2"], urls);
    }

    [Fact]
    public async Task Archiving_stores_the_article_images()
    {
        using var app = new TestApp();
        var (categoryId, feed) = await app.AddSampleFeedAsync();
        var article = (await app.Articles.GetAsync((await Headline(app, feed.Id, BatteryTitle)).Id))!;
        var imageUrls = ArticleImages.Extract(article.ContentHtml, article.Link);
        app.Images.Missing.Add(imageUrls[^1]);

        var result = await app.ArchiveService.ArchiveAsync([article.Id]);

        Assert.Equal(new ArchiveResult(1, imageUrls.Count - 1, 1), result);
        Assert.Equal(ArticleState.Archived, (await app.Articles.GetAsync(article.Id))!.State);
        Assert.Equal(article.Id, Assert.Single(await app.ArticleService.GetArchivedHeadlinesAsync(categoryId)).Id);
        Assert.Equal(imageUrls.Count - 1, await app.Articles.CountResourcesAsync(article.Id));
        Assert.Equal("image/png", (await app.ArticleService.GetArchivedImageAsync(article.Id, imageUrls[0]))!.ContentType);
        Assert.Null(await app.ArticleService.GetArchivedImageAsync(article.Id, imageUrls[^1]));
    }

    [Fact]
    public async Task Archived_http_images_are_found_when_the_browser_asks_for_https()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var id = (await Headline(app, feed.Id, BatteryTitle)).Id;
        await app.Articles.AddResourceAsync(id, "http://old.example.com/a.png", new FetchedResource("image/png", [7]));

        var image = await app.ArticleService.GetArchivedImageAsync(id, "https://old.example.com/a.png");

        Assert.Equal([7], image!.Data);
    }

    [Fact]
    public async Task Archiving_from_the_trash_and_archiving_twice()
    {
        using var app = new TestApp();
        var (categoryId, feed) = await app.AddSampleFeedAsync();
        var id = (await Headline(app, feed.Id, BatteryTitle)).Id;
        await app.ArticleService.TrashAsync([id]);

        Assert.Equal(1, (await app.ArchiveService.ArchiveAsync([id])).Articles);
        Assert.Equal(0, (await app.ArchiveService.ArchiveAsync([id])).Articles); // already archived: nothing to do

        Assert.Empty(await app.ArticleService.GetTrashedHeadlinesAsync());
        Assert.Single(await app.ArticleService.GetArchivedHeadlinesAsync(categoryId));
    }

    [Fact]
    public void Archived_pages_drop_responsive_image_candidates()
    {
        var html = ArticleHtml.Build(new Article
        {
            State = ArticleState.Archived,
            ContentHtml = """<picture><source srcset="big.webp"><img src="a.jpg" srcset="a-2x.jpg 2x" sizes="100vw"></picture>""",
        });

        Assert.Contains("""<img src="a.jpg">""", html);
        Assert.DoesNotContain("srcset", html);
        Assert.DoesNotContain("<source", html);
    }

    // ----- Search -----

    [Theory]
    [InlineData("linux", "\"linux\"*")]
    [InlineData("  C#  -NOT OR  ", "\"C\"* \"NOT\"* \"OR\"*")]
    [InlineData("l’été", "\"l\"* \"été\"*")]
    [InlineData(" ;-) ", null)]
    public void Search_text_becomes_a_safe_prefix_query(string text, string? expected) =>
        Assert.Equal(expected, SearchService.BuildQuery(text));

    [Fact]
    public async Task Search_matches_word_prefixes_without_accents_and_shows_a_snippet()
    {
        using var app = new TestApp();
        await app.AddSampleFeedAsync();

        var hit = Assert.Single(await app.SearchService.SearchAsync("REFROIDISS gouttes"));

        Assert.Equal(BatteryTitle, hit.Title);
        Assert.Equal("Le Fil Bidouille - flux complet", hit.FeedTitle);
        Assert.False(string.IsNullOrWhiteSpace(hit.Snippet));
        Assert.Single(await app.SearchService.SearchAsync("facade")); // the text says "façade"
    }

    [Fact]
    public async Task Search_includes_archived_but_not_trashed_or_purged_articles()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var battery = await Headline(app, feed.Id, BatteryTitle);
        var jailbreak = await Headline(app, feed.Id, "Rebound, le jailbreak qui débloque presque toutes les PS5");
        var firefox = await Headline(app, feed.Id, "Firefox 157 passe en mode compact et change de look");

        await app.ArchiveService.ArchiveAsync([battery.Id]);
        await app.ArticleService.TrashAsync([jailbreak.Id]);
        await app.Articles.PurgeAsync([firefox.Id]);

        Assert.Single(await app.SearchService.SearchAsync("VoltaFlow"));   // archived
        Assert.Empty(await app.SearchService.SearchAsync("jailbreak PS5")); // trashed
        Assert.Empty(await app.SearchService.SearchAsync("Firefox Nova"));  // purged
    }

    // ----- Trash and retention -----

    [Fact]
    public async Task Emptying_the_trash_purges_content_and_keeps_articles_from_coming_back()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var id = (await Headline(app, feed.Id, BatteryTitle)).Id;
        await app.ArticleService.TrashAsync([id]);

        Assert.Equal(1, await app.ArticleService.EmptyTrashAsync());
        await app.Refresher.RefreshAsync(feed.Id);

        var purged = (await app.Articles.GetAsync(id))!;
        Assert.Equal(ArticleState.Purged, purged.State);
        Assert.Null(purged.ContentHtml);
        Assert.Empty(await app.ArticleService.GetTrashedHeadlinesAsync());
        Assert.Equal(2, (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id)).Count);
    }

    [Fact]
    public async Task Retention_purges_old_read_articles_but_not_pinned_or_unread_ones()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var h = await app.ArticleService.GetActiveHeadlinesAsync(feed.Id); // all published 2026-09-30
        var (read, pinnedRead, unread) = (h[0].Id, h[1].Id, h[2].Id);
        await app.ArticleService.SetReadAsync([read, pinnedRead], true);
        await app.ArticleService.SetPinnedAsync(pinnedRead, true);

        app.Time.Now = app.Time.Now.AddDays(20);
        Assert.Equal(0, await app.RetentionService.RunAsync()); // too recent

        app.Time.Now = app.Time.Now.AddDays(20);
        Assert.Equal(1, await app.RetentionService.RunAsync());

        Assert.Equal(ArticleState.Purged, (await app.Articles.GetAsync(read))!.State);
        Assert.Equal(ArticleState.Active, (await app.Articles.GetAsync(pinnedRead))!.State);
        Assert.Equal(ArticleState.Active, (await app.Articles.GetAsync(unread))!.State);
    }

    [Fact]
    public async Task Retention_keeps_rated_articles()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var h = await app.ArticleService.GetActiveHeadlinesAsync(feed.Id);
        await app.ArticleService.SetReadAsync([h[0].Id, h[1].Id, h[2].Id], true);
        await app.Articles.SetRatingAsync(h[0].Id, Rating.Up);
        await app.Articles.SetRatingAsync(h[1].Id, Rating.Down);

        app.Time.Now = app.Time.Now.AddDays(31);
        Assert.Equal(1, await app.RetentionService.RunAsync());

        Assert.Equal(2, (await app.Articles.GetRatedAsync()).Count);
        Assert.Equal(ArticleState.Purged, (await app.Articles.GetAsync(h[2].Id))!.State);
    }

    [Fact]
    public async Task Retention_keeps_archives_and_empties_old_trash()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var h = await app.ArticleService.GetActiveHeadlinesAsync(feed.Id);
        await app.ArticleService.SetReadAsync([h[0].Id], true);
        await app.ArchiveService.ArchiveAsync([h[0].Id]);
        await app.ArticleService.TrashAsync([h[1].Id]);

        app.Time.Now = app.Time.Now.AddDays(31);
        await app.RetentionService.RunAsync();

        Assert.Equal(ArticleState.Archived, (await app.Articles.GetAsync(h[0].Id))!.State);
        Assert.Equal(ArticleState.Purged, (await app.Articles.GetAsync(h[1].Id))!.State);
    }

    private static async Task<ArticleHeadline> Headline(TestApp app, long feedId, string title) =>
        (await app.ArticleService.GetActiveHeadlinesAsync(feedId)).Single(a => a.Title == title);
}
