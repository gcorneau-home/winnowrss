using Dapper;
using Winnow.Core.Abstractions;
using Winnow.Core.Models;

namespace Winnow.Data;

public sealed class FeedRepository(WinnowDatabase db) : IFeedRepository
{
    private const string Columns =
        "id, category_id, title, url, site_url, e_tag, last_modified, last_fetched_at, last_error, skip_filter";

    public async Task<IReadOnlyList<Feed>> GetAllAsync(CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync<Feed>($"SELECT {Columns} FROM feeds ORDER BY title COLLATE NOCASE");
        return rows.AsList();
    }

    public async Task<Feed?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<Feed>($"SELECT {Columns} FROM feeds WHERE id = @id", new { id });
    }

    public async Task<Feed?> GetByUrlAsync(string url, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<Feed>($"SELECT {Columns} FROM feeds WHERE url = @url", new { url });
    }

    public async Task<long> AddAsync(Feed feed, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<long>(
            """
            INSERT INTO feeds (category_id, title, url, site_url)
            VALUES (@CategoryId, @Title, @Url, @SiteUrl)
            RETURNING id
            """, feed);
    }

    public async Task UpdateDetailsAsync(long id, string title, string url, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("UPDATE feeds SET title = @title, url = @url WHERE id = @id", new { id, title, url });
    }

    public async Task UpdateFetchStateAsync(FeedFetchState state, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync(
            """
            UPDATE feeds
            SET site_url = @SiteUrl, e_tag = @ETag, last_modified = @LastModified,
                last_fetched_at = @FetchedAt, last_error = @Error
            WHERE id = @FeedId
            """, state);
    }

    public async Task SetSkipFilterAsync(long id, bool skipFilter, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("UPDATE feeds SET skip_filter = @skipFilter WHERE id = @id", new { id, skipFilter });
    }

    public async Task MoveToCategoryAsync(long id, long categoryId, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("UPDATE feeds SET category_id = @categoryId WHERE id = @id", new { id, categoryId });
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("DELETE FROM feeds WHERE id = @id", new { id });
    }

    public async Task<int> CountArchivedArticlesAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM articles WHERE feed_id = @id AND state = @archived",
            new { id, archived = ArticleState.Archived });
    }
}
