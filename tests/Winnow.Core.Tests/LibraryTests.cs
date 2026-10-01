using Dapper;
using Winnow.Core.Models;
using Winnow.Core.Services;
using Winnow.Core.Tests.TestSupport;

namespace Winnow.Core.Tests;

/// <summary>Categories, feeds and article actions.</summary>
public class LibraryTests
{
    [Fact]
    public async Task Migration_sets_schema_version_and_is_idempotent()
    {
        using var app = new TestApp();
        app.Database.Migrate();

        var latest = Winnow.Data.Migrator.LoadScripts().Max(s => s.Version);
        Assert.Equal(latest, await app.Connection.ExecuteScalarAsync<long>("PRAGMA user_version"));
    }

    [Fact]
    public async Task Category_names_are_validated_and_trimmed()
    {
        using var app = new TestApp();

        var error = await Assert.ThrowsAsync<WinnowException>(() => app.CategoryService.AddAsync("  "));
        Assert.Equal(WinnowError.CategoryNameEmpty, error.Error);
        var id = await app.CategoryService.AddAsync("  Tech ");
        await app.CategoryService.RenameAsync(id, "Dev");

        var category = Assert.Single(await app.CategoryService.GetAllAsync());
        Assert.Equal("Dev", category.Name);
    }

    [Fact]
    public async Task Moving_a_feed_moves_its_archived_articles_to_the_new_category_archive()
    {
        using var app = new TestApp();
        var (techId, feed) = await app.AddSampleFeedAsync();
        var otherId = await app.CategoryService.AddAsync("Divers");
        var article = (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id))[0];
        await Archive(app, article.Id);

        await app.FeedService.MoveToCategoryAsync(feed.Id, otherId);

        Assert.Equal(otherId, (await app.Feeds.GetAsync(feed.Id))!.CategoryId);
        Assert.Empty(await app.ArticleService.GetArchivedHeadlinesAsync(techId));
        Assert.Single(await app.ArticleService.GetArchivedHeadlinesAsync(otherId));
    }

    [Fact]
    public async Task Deleting_a_category_removes_its_feeds_and_articles()
    {
        using var app = new TestApp();
        var (categoryId, feed) = await app.AddSampleFeedAsync();
        await Archive(app, (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id))[0].Id);

        Assert.Equal(1, await app.CategoryService.CountArchivedArticlesAsync(categoryId));
        await app.CategoryService.DeleteAsync(categoryId);

        Assert.Empty(await app.FeedService.GetAllAsync());
        Assert.Equal(0L, await app.Connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM articles"));
        Assert.Equal(0L, await app.Connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM article_tags"));
    }

    [Fact]
    public async Task Opening_an_article_marks_it_read_and_updates_unread_counts()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        Assert.Equal(3, (await app.ArticleService.GetUnreadCountsAsync())[feed.Id]);
        var headline = (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id))[0];

        var details = await app.ArticleService.OpenAsync(headline.Id);

        Assert.True(details!.Article.IsRead);
        Assert.Equal("Le Fil Bidouille - flux complet", details.FeedTitle);
        Assert.Equal(2, (await app.ArticleService.GetUnreadCountsAsync())[feed.Id]);
    }

    [Fact]
    public async Task Same_thumb_twice_clears_the_rating()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var id = (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id))[0].Id;

        var up = await app.ArticleService.ToggleRatingAsync(id, Rating.None, Rating.Up);
        var down = await app.ArticleService.ToggleRatingAsync(id, up, Rating.Down);
        var cleared = await app.ArticleService.ToggleRatingAsync(id, down, Rating.Down);

        Assert.Equal((Rating.Up, Rating.Down, Rating.None), (up, down, cleared));
        Assert.Equal(Rating.None, (await app.Articles.GetAsync(id))!.Rating);
    }

    [Fact]
    public async Task Restoring_returns_articles_to_where_they_were()
    {
        using var app = new TestApp();
        var (categoryId, feed) = await app.AddSampleFeedAsync();
        var headlines = await app.ArticleService.GetActiveHeadlinesAsync(feed.Id);
        var (active, archived) = (headlines[0].Id, headlines[1].Id);
        await Archive(app, archived);

        await app.ArticleService.TrashAsync([active, archived]);
        Assert.Equal(2, (await app.ArticleService.GetTrashedHeadlinesAsync()).Count);
        await app.ArticleService.RestoreAsync([active, archived]);

        Assert.Empty(await app.ArticleService.GetTrashedHeadlinesAsync());
        Assert.Equal(ArticleState.Active, (await app.Articles.GetAsync(active))!.State);
        Assert.Equal(archived, Assert.Single(await app.ArticleService.GetArchivedHeadlinesAsync(categoryId)).Id);
    }

    [Fact]
    public async Task Pinning_is_persisted()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var id = (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id))[0].Id;

        await app.ArticleService.SetPinnedAsync(id, true);

        Assert.True((await app.Articles.GetAsync(id))!.IsPinned);
    }

    [Fact]
    public async Task Display_preferences_have_defaults_round_trip_and_clamp_the_tab_limit()
    {
        using var app = new TestApp();
        Assert.Equal(DisplayPreferences.Default, await app.SettingsService.GetDisplayPreferencesAsync());

        var chosen = new DisplayPreferences(true, 3, UnreadCues.Dot | UnreadCues.DimRead, RejectedCues.Strikethrough | RejectedCues.Icon, 150);
        await app.SettingsService.SetDisplayPreferencesAsync(chosen);
        Assert.Equal(chosen, await app.SettingsService.GetDisplayPreferencesAsync());

        await app.SettingsService.SetDisplayPreferencesAsync(chosen with { MaxOpenTabs = 500, UnreadCues = UnreadCues.None, ArticleZoom = 140 });
        var clamped = await app.SettingsService.GetDisplayPreferencesAsync();
        Assert.Equal((DisplayPreferences.MaxTabs, UnreadCues.None, 150), (clamped.MaxOpenTabs, clamped.UnreadCues, clamped.ArticleZoom));
    }

    [Fact]
    public async Task Hiding_read_articles_is_remembered()
    {
        using var app = new TestApp();
        Assert.False(await app.SettingsService.GetHideReadAsync());

        await app.SettingsService.SetHideReadAsync(true);

        Assert.True(await app.SettingsService.GetHideReadAsync());
    }

    [Fact]
    public async Task Ui_language_preference_is_persisted()
    {
        using var app = new TestApp();
        Assert.Null(await app.SettingsService.GetUiLanguageAsync());

        await app.SettingsService.SetUiLanguageAsync("fr");
        await app.SettingsService.SetUiLanguageAsync("en");

        Assert.Equal("en", await app.SettingsService.GetUiLanguageAsync());
    }

    // Stand-in until ArchiveService exists (step 4).
    private static Task Archive(TestApp app, long id) =>
        app.Connection.ExecuteAsync("UPDATE articles SET state = 1, archived_at = '2026-09-30T12:00:00.0000000+00:00' WHERE id = @id", new { id });
}
