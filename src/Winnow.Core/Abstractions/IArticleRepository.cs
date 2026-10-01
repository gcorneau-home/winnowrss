using Winnow.Core.Models;

namespace Winnow.Core.Abstractions;

public interface IArticleRepository
{
    /// <summary>All dedup keys already stored for a feed, whatever their state.</summary>
    Task<HashSet<string>> GetKeysAsync(long feedId, CancellationToken ct = default);

    /// <summary>Inserts articles with their tags in a single transaction.</summary>
    Task AddRangeAsync(IReadOnlyList<NewArticle> articles, CancellationToken ct = default);

    Task<Article?> GetAsync(long id, CancellationToken ct = default);
    Task<ArticleDetails?> GetDetailsAsync(long id, CancellationToken ct = default);

    /// <summary>Active articles of a feed, newest first.</summary>
    Task<IReadOnlyList<ArticleHeadline>> GetActiveHeadlinesAsync(long feedId, CancellationToken ct = default);

    /// <summary>Archived articles of every feed in a category, newest first.</summary>
    Task<IReadOnlyList<ArticleHeadline>> GetArchivedHeadlinesAsync(long categoryId, CancellationToken ct = default);

    Task<IReadOnlyList<ArticleHeadline>> GetTrashedHeadlinesAsync(CancellationToken ct = default);

    /// <summary>Unread active article count per feed id.</summary>
    Task<IReadOnlyDictionary<long, int>> GetUnreadCountsAsync(CancellationToken ct = default);

    Task SetReadAsync(IReadOnlyCollection<long> ids, bool isRead, CancellationToken ct = default);
    Task SetPinnedAsync(long id, bool isPinned, CancellationToken ct = default);
    Task SetRatingAsync(long id, Rating rating, CancellationToken ct = default);

    /// <summary>Moves active or archived articles to the trash.</summary>
    Task TrashAsync(IReadOnlyCollection<long> ids, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Takes articles out of the trash, back to the archive if they had been archived.</summary>
    Task RestoreAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

    /// <summary>Moves active or trashed articles to the archive; returns the ids actually archived.</summary>
    Task<IReadOnlyList<long>> ArchiveAsync(IReadOnlyCollection<long> ids, DateTimeOffset at, CancellationToken ct = default);

    Task AddResourceAsync(long articleId, string url, FetchedResource resource, CancellationToken ct = default);
    Task<ArchivedResource?> GetResourceAsync(long articleId, string url, CancellationToken ct = default);
    Task<int> CountResourcesAsync(long articleId, CancellationToken ct = default);

    Task<IReadOnlyList<long>> GetTrashedIdsAsync(CancellationToken ct = default);

    /// <summary>Read, unpinned active articles published before <paramref name="readBefore"/>, and trash older than <paramref name="trashedBefore"/>.</summary>
    Task<IReadOnlyList<long>> GetExpiredIdsAsync(DateTimeOffset readBefore, DateTimeOffset trashedBefore, CancellationToken ct = default);

    /// <summary>
    /// Deletes the content of articles for good. The rows stay (state Purged, title and dedup key only)
    /// so the next refresh does not import them again.
    /// </summary>
    Task<int> PurgeAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

    /// <summary>Articles the user gave a thumb to (content still available), newest first.</summary>
    Task<IReadOnlyList<Article>> GetRatedAsync(CancellationToken ct = default);

    /// <summary>Active articles waiting for the filter, oldest first.</summary>
    Task<IReadOnlyList<Article>> GetPendingAsync(int limit, CancellationToken ct = default);
    Task<int> CountPendingAsync(CancellationToken ct = default);
    Task SaveFilterResultAsync(long articleId, FilterResult result, CancellationToken ct = default);

    /// <summary>Marks unread active articles pending again; returns how many.</summary>
    Task<int> RequeueUnreadAsync(CancellationToken ct = default);

    /// <summary>Active and archived articles matching an FTS5 query, best first.</summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string ftsQuery, int limit, CancellationToken ct = default);
}

public sealed record NewArticle(Article Article, IReadOnlyList<string> Tags);
