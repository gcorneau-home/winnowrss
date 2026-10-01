using Dapper;
using Winnow.Core.Abstractions;
using Winnow.Core.Models;

namespace Winnow.Data;

public sealed class ArticleRepository(WinnowDatabase db) : IArticleRepository
{
    private const string ArticleColumns =
        """
        id, feed_id, dedup_key, link, title, author, published_at, summary, content_html, content_text,
        is_read, is_pinned, rating, state, trashed_at, archived_at, fetched_at,
        filter_status, filter_criterion, filter_reason, filter_version, filter_model, filtered_at
        """;

    private const string HeadlineSelect =
        """
        SELECT a.id, a.feed_id, f.title AS feed_title, a.title, a.published_at,
               a.is_read, a.is_pinned, a.rating, a.state, a.filter_status, a.filter_criterion, a.filter_reason, a.filter_model
        FROM articles a JOIN feeds f ON f.id = a.feed_id
        """;

    private const string NewestFirst = "ORDER BY COALESCE(a.published_at, a.fetched_at) DESC, a.id DESC";

    public async Task<HashSet<string>> GetKeysAsync(long feedId, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var keys = await c.QueryAsync<string>("SELECT dedup_key FROM articles WHERE feed_id = @feedId", new { feedId });
        return keys.ToHashSet();
    }

    public async Task AddRangeAsync(IReadOnlyList<NewArticle> articles, CancellationToken ct = default)
    {
        if (articles.Count == 0)
            return;

        await using var c = await db.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        foreach (var (article, tags) in articles)
        {
            // OR IGNORE: an article already stored under the same key is skipped instead of failing the batch.
            var id = await c.ExecuteScalarAsync<long?>(
                """
                INSERT OR IGNORE INTO articles (feed_id, dedup_key, link, title, author, published_at, summary,
                    content_html, content_text, is_read, is_pinned, rating, state, trashed_at, archived_at,
                    filter_status, fetched_at)
                VALUES (@FeedId, @DedupKey, @Link, @Title, @Author, @PublishedAt, @Summary,
                    @ContentHtml, @ContentText, @IsRead, @IsPinned, @Rating, @State, @TrashedAt, @ArchivedAt,
                    @FilterStatus, @FetchedAt)
                RETURNING id
                """, article, tx);
            if (id is null)
                continue;

            await c.ExecuteAsync(
                "INSERT OR IGNORE INTO article_tags (article_id, name) VALUES (@id, @name)",
                tags.Select(name => new { id, name }), tx);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<Article?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<Article>(
            $"SELECT {ArticleColumns} FROM articles WHERE id = @id", new { id });
    }

    public async Task<ArticleDetails?> GetDetailsAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var article = await c.QuerySingleOrDefaultAsync<Article>(
            $"SELECT {ArticleColumns} FROM articles WHERE id = @id", new { id });
        if (article is null)
            return null;

        var feedTitle = await c.ExecuteScalarAsync<string>(
            "SELECT title FROM feeds WHERE id = @FeedId", new { article.FeedId });
        var tags = await c.QueryAsync<string>(
            "SELECT name FROM article_tags WHERE article_id = @id ORDER BY name COLLATE NOCASE", new { id });
        return new ArticleDetails(article, feedTitle ?? "", tags.AsList());
    }

    public async Task<IReadOnlyList<ArticleHeadline>> GetActiveHeadlinesAsync(long feedId, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync<ArticleHeadline>(
            $"{HeadlineSelect} WHERE a.feed_id = @feedId AND a.state = @active {NewestFirst}",
            new { feedId, active = ArticleState.Active });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ArticleHeadline>> GetArchivedHeadlinesAsync(long categoryId, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync<ArticleHeadline>(
            $"{HeadlineSelect} WHERE f.category_id = @categoryId AND a.state = @archived {NewestFirst}",
            new { categoryId, archived = ArticleState.Archived });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ArticleHeadline>> GetTrashedHeadlinesAsync(CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync<ArticleHeadline>(
            $"{HeadlineSelect} WHERE a.state = @trashed ORDER BY a.trashed_at DESC, a.id DESC",
            new { trashed = ArticleState.Trashed });
        return rows.AsList();
    }

    public async Task<IReadOnlyDictionary<long, int>> GetUnreadCountsAsync(CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync<(long FeedId, int Count)>(
            """
            SELECT feed_id, COUNT(*) FROM articles
            WHERE state = @active AND is_read = 0 AND filter_status <> @rejected
            GROUP BY feed_id
            """,
            new { active = ArticleState.Active, rejected = FilterStatus.Rejected });
        return rows.ToDictionary(r => r.FeedId, r => r.Count);
    }

    public async Task SetReadAsync(IReadOnlyCollection<long> ids, bool isRead, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("UPDATE articles SET is_read = @isRead WHERE id IN @ids", new { ids, isRead });
    }

    public async Task SetPinnedAsync(long id, bool isPinned, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("UPDATE articles SET is_pinned = @isPinned WHERE id = @id", new { id, isPinned });
    }

    public async Task SetRatingAsync(long id, Rating rating, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("UPDATE articles SET rating = @rating WHERE id = @id", new { id, rating });
    }

    public async Task TrashAsync(IReadOnlyCollection<long> ids, DateTimeOffset at, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync(
            "UPDATE articles SET state = @trashed, trashed_at = @at WHERE id IN @ids AND state IN (@active, @archived)",
            new { ids, at, trashed = ArticleState.Trashed, active = ArticleState.Active, archived = ArticleState.Archived });
    }

    public async Task<IReadOnlyList<long>> ArchiveAsync(IReadOnlyCollection<long> ids, DateTimeOffset at, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var archived = await c.QueryAsync<long>(
            """
            UPDATE articles SET state = @archived, archived_at = @at, trashed_at = NULL
            WHERE id IN @ids AND state IN (@active, @trashed)
            RETURNING id
            """,
            new { ids, at, archived = ArticleState.Archived, active = ArticleState.Active, trashed = ArticleState.Trashed });
        return archived.AsList();
    }

    public async Task AddResourceAsync(long articleId, string url, FetchedResource resource, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync(
            """
            INSERT INTO archived_resources (article_id, original_url, content_type, data)
            VALUES (@articleId, @url, @ContentType, @Data)
            ON CONFLICT (article_id, original_url) DO UPDATE SET content_type = excluded.content_type, data = excluded.data
            """,
            new { articleId, url, resource.ContentType, resource.Data });
    }

    public async Task<ArchivedResource?> GetResourceAsync(long articleId, string url, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var row = await c.QuerySingleOrDefaultAsync<(string ContentType, byte[] Data)?>(
            "SELECT content_type, data FROM archived_resources WHERE article_id = @articleId AND original_url = @url",
            new { articleId, url });
        return row is { } r ? new ArchivedResource(r.ContentType, r.Data) : null;
    }

    public async Task<int> CountResourcesAsync(long articleId, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM archived_resources WHERE article_id = @articleId", new { articleId });
    }

    public async Task<IReadOnlyList<long>> GetTrashedIdsAsync(CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var ids = await c.QueryAsync<long>("SELECT id FROM articles WHERE state = @trashed", new { trashed = ArticleState.Trashed });
        return ids.AsList();
    }

    public async Task<IReadOnlyList<long>> GetExpiredIdsAsync(DateTimeOffset readBefore, DateTimeOffset trashedBefore, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var ids = await c.QueryAsync<long>(
            """
            SELECT id FROM articles
            WHERE (state = @active AND is_pinned = 0 AND rating = 0 AND (is_read = 1 OR filter_status = @rejected)
                   AND COALESCE(published_at, fetched_at) < @readBefore)
               OR (state = @trashed AND trashed_at < @trashedBefore)
            """,
            new { readBefore, trashedBefore, active = ArticleState.Active, trashed = ArticleState.Trashed, rejected = FilterStatus.Rejected });
        return ids.AsList();
    }

    public async Task<int> PurgeAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return 0;

        await using var c = await db.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var purged = await c.ExecuteAsync(
            """
            UPDATE articles
            SET state = @purged, summary = NULL, content_html = NULL, content_text = NULL,
                author = NULL, trashed_at = NULL, archived_at = NULL, is_pinned = 0
            WHERE id IN @ids AND state <> @purged
            """, new { ids, purged = ArticleState.Purged }, tx);
        await c.ExecuteAsync("DELETE FROM article_tags WHERE article_id IN @ids", new { ids }, tx);
        await c.ExecuteAsync("DELETE FROM archived_resources WHERE article_id IN @ids", new { ids }, tx);
        await tx.CommitAsync(ct);
        return purged;
    }

    public async Task<IReadOnlyList<Article>> GetRatedAsync(CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync<Article>(
            $"SELECT {ArticleColumns} FROM articles WHERE rating <> @none AND state <> @purged ORDER BY COALESCE(published_at, fetched_at) DESC",
            new { none = Rating.None, purged = ArticleState.Purged });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<Article>> GetPendingAsync(int limit, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync<Article>(
            $"SELECT {ArticleColumns} FROM articles WHERE filter_status = @pending AND state = @active ORDER BY fetched_at, id LIMIT @limit",
            new { limit, pending = FilterStatus.Pending, active = ArticleState.Active });
        return rows.AsList();
    }

    public async Task<int> CountPendingAsync(CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM articles WHERE filter_status = @pending AND state = @active",
            new { pending = FilterStatus.Pending, active = ArticleState.Active });
    }

    public async Task SaveFilterResultAsync(long articleId, FilterResult result, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync(
            """
            UPDATE articles
            SET filter_status = @Status, filter_criterion = @Criterion, filter_reason = @Reason,
                filter_version = @Version, filter_model = @Model, filtered_at = @At
            WHERE id = @articleId
            """,
            new { articleId, result.Status, result.Criterion, result.Reason, result.Version, result.Model, result.At });
    }

    public async Task<int> RequeueUnreadAsync(CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteAsync(
            "UPDATE articles SET filter_status = @pending WHERE state = @active AND is_read = 0",
            new { pending = FilterStatus.Pending, active = ArticleState.Active });
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string ftsQuery, int limit, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var hits = await c.QueryAsync<SearchHit>(
            """
            SELECT a.id, a.feed_id, f.title AS feed_title, a.title, a.published_at, a.state,
                   snippet(articles_fts, 3, '', '', '…', 24) AS snippet
            FROM articles_fts
            JOIN articles a ON a.id = articles_fts.rowid
            JOIN feeds f ON f.id = a.feed_id
            WHERE articles_fts MATCH @ftsQuery AND a.state IN (@active, @archived)
            ORDER BY bm25(articles_fts)
            LIMIT @limit
            """,
            new { ftsQuery, limit, active = ArticleState.Active, archived = ArticleState.Archived });
        return hits.AsList();
    }

    public async Task RestoreAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync(
            """
            UPDATE articles
            SET state = CASE WHEN archived_at IS NOT NULL THEN @archived ELSE @active END, trashed_at = NULL
            WHERE id IN @ids AND state = @trashed
            """,
            new { ids, trashed = ArticleState.Trashed, active = ArticleState.Active, archived = ArticleState.Archived });
    }
}
