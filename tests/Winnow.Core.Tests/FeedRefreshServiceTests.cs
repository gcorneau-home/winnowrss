using Dapper;
using Winnow.Core.Abstractions;
using Winnow.Core.Feeds;
using Winnow.Core.Models;
using Winnow.Core.Services;
using Winnow.Core.Tests.TestSupport;

namespace Winnow.Core.Tests;

public class FeedRefreshServiceTests
{
    [Fact]
    public async Task Adding_a_feed_stores_its_title_and_articles()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();

        Assert.Equal("Le Fil Bidouille - flux complet", feed.Title);
        var headlines = await app.ArticleService.GetActiveHeadlinesAsync(feed.Id);
        Assert.Equal(3, headlines.Count);
        Assert.All(headlines, h => Assert.Equal("Le Fil Bidouille - flux complet", h.FeedTitle));

        var stored = await app.Feeds.GetAsync(feed.Id);
        Assert.Equal("\"etag\"", stored!.ETag);
        Assert.Equal(app.Time.Now, stored.LastFetchedAt);
    }

    [Fact]
    public async Task Refreshing_twice_does_not_duplicate_articles()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();

        var result = await app.Refresher.RefreshAsync(feed.Id);

        Assert.Equal(0, result.Added);
        Assert.Equal(3, (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id)).Count);
    }

    [Fact]
    public async Task Trashed_articles_are_not_imported_again()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var first = (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id))[0];

        await app.ArticleService.TrashAsync([first.Id]);
        await app.Refresher.RefreshAsync(feed.Id);

        Assert.Equal(2, (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id)).Count);
        Assert.Single(await app.ArticleService.GetTrashedHeadlinesAsync());
    }

    [Fact]
    public async Task New_articles_are_pending_when_the_filter_is_on_and_unfiltered_otherwise()
    {
        using var app = new TestApp();
        var (categoryId, feed) = await app.AddSampleFeedAsync();
        Assert.All(await app.ArticleService.GetActiveHeadlinesAsync(feed.Id), h => Assert.Equal(FilterStatus.Unfiltered, h.FilterStatus));

        await app.EnableFilterAsync();
        app.Fetcher.Respond("https://other.example.com/feed", Fixtures.SampleFeed() with { Title = "Other" });
        var other = await app.FeedService.AddAsync(categoryId, "https://other.example.com/feed");

        Assert.All(await app.ArticleService.GetActiveHeadlinesAsync(other.Id), h => Assert.Equal(FilterStatus.Pending, h.FilterStatus));
    }

    [Fact]
    public async Task Fetch_failure_is_recorded_on_the_feed()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        app.Fetcher.Fail(Fixtures.SampleFeedUrl, new HttpRequestException("boom"));

        var result = await app.Refresher.RefreshAsync(feed.Id);

        Assert.False(result.Succeeded);
        Assert.Equal("boom", (await app.Feeds.GetAsync(feed.Id))!.LastError);
    }

    [Fact]
    public async Task Adding_an_unreadable_feed_fails_without_storing_it()
    {
        using var app = new TestApp();
        var categoryId = await app.CategoryService.AddAsync("Tech");
        app.Fetcher.Fail("https://bad.example.com/feed", new HttpRequestException("404"));

        await Assert.ThrowsAsync<WinnowException>(() => app.FeedService.AddAsync(categoryId, "https://bad.example.com/feed"));
        Assert.Empty(await app.FeedService.GetAllAsync());
    }

    [Fact]
    public async Task Adding_the_same_feed_twice_fails()
    {
        using var app = new TestApp();
        var (categoryId, _) = await app.AddSampleFeedAsync();

        await Assert.ThrowsAsync<WinnowException>(() => app.FeedService.AddAsync(categoryId, Fixtures.SampleFeedUrl));
    }

    [Fact]
    public async Task Concurrent_refreshes_of_one_feed_store_new_items_once()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var sample = Fixtures.SampleFeed();
        var newItem = new FeedItem { Guid = "new-item", Title = "Nouvel article" };
        app.Fetcher.Respond(Fixtures.SampleFeedUrl, sample with { Items = [newItem, .. sample.Items] });

        app.Fetcher.Gate = new TaskCompletionSource();
        var first = app.Refresher.RefreshAsync(feed.Id);
        var second = app.Refresher.RefreshAsync(feed.Id);
        app.Fetcher.Gate.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.Equal(1, results.Sum(r => r.Added));
        Assert.Equal(4, (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id)).Count);
    }

    [Fact]
    public async Task Storing_an_already_known_article_is_skipped()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var existing = await app.Articles.GetAsync((await app.ArticleService.GetActiveHeadlinesAsync(feed.Id))[0].Id);

        await app.Articles.AddRangeAsync([new NewArticle(existing! with { Id = 0 }, ["tag"])]);

        Assert.Equal(3, (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id)).Count);
    }

    [Fact]
    public async Task Refresh_all_continues_after_an_unexpected_failure()
    {
        using var app = new TestApp();
        var (categoryId, sample) = await app.AddSampleFeedAsync();
        app.Fetcher.Respond("https://other.example.com/feed", Fixtures.SampleFeed() with { Title = "Other" });
        var other = await app.FeedService.AddAsync(categoryId, "https://other.example.com/feed");
        app.Fetcher.Fail(Fixtures.SampleFeedUrl, new InvalidOperationException("unexpected"));

        var results = await app.Refresher.RefreshAllAsync();

        Assert.Equal("unexpected", results.Single(r => r.FeedId == sample.Id).Error);
        Assert.True(results.Single(r => r.FeedId == other.Id).Succeeded);
    }

    [Fact]
    public async Task Adding_a_feed_that_times_out_fails_with_a_readable_error()
    {
        using var app = new TestApp();
        var categoryId = await app.CategoryService.AddAsync("Tech");
        app.Fetcher.Fail("https://slow.example.com/feed", new TaskCanceledException("timeout"));

        var error = await Assert.ThrowsAsync<WinnowException>(
            () => app.FeedService.AddAsync(categoryId, "https://slow.example.com/feed"));
        Assert.Equal(WinnowError.FeedTimeout, error.Error);
    }

    [Fact]
    public async Task Changing_a_feed_url_to_an_existing_subscription_fails()
    {
        using var app = new TestApp();
        var (categoryId, sample) = await app.AddSampleFeedAsync();
        app.Fetcher.Respond("https://other.example.com/feed", Fixtures.SampleFeed() with { Title = "Other" });
        var other = await app.FeedService.AddAsync(categoryId, "https://other.example.com/feed");

        var error = await Assert.ThrowsAsync<WinnowException>(() => app.FeedService.UpdateAsync(other.Id, "Other", Fixtures.SampleFeedUrl));
        Assert.Equal(WinnowError.FeedUrlInUse, error.Error);
        Assert.Equal(["Le Fil Bidouille - flux complet"], error.Args);
        await app.FeedService.UpdateAsync(sample.Id, "Fil Bidouille", Fixtures.SampleFeedUrl);

        Assert.Equal("Fil Bidouille", (await app.Feeds.GetAsync(sample.Id))!.Title);
    }

    [Fact]
    public async Task Articles_round_trip_through_the_database()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var headline = (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id))[0];

        var details = await app.Articles.GetDetailsAsync(headline.Id);

        var article = details!.Article;
        Assert.Equal("Camille Martin", article.Author);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 15, 27, 37, TimeSpan.Zero), article.PublishedAt);
        Assert.Equal(ArticleState.Active, article.State);
        Assert.Equal(FilterStatus.Unfiltered, article.FilterStatus);
        Assert.Contains("batterie externe", details.Tags);
        Assert.DoesNotContain("<p>", article.ContentText);
    }

    [Fact]
    public async Task Full_text_index_follows_inserted_articles()
    {
        using var app = new TestApp();
        await app.AddSampleFeedAsync();

        var hits = await app.Connection.QueryAsync<string>(
            "SELECT a.title FROM articles_fts JOIN articles a ON a.id = articles_fts.rowid WHERE articles_fts MATCH 'refroidissement'");

        Assert.Single(hits);
    }
}
