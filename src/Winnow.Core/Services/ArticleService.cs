using Winnow.Core.Abstractions;
using Winnow.Core.Models;

namespace Winnow.Core.Services;

public sealed class ArticleService(IArticleRepository articles, TimeProvider time)
{
    public Task<IReadOnlyList<ArticleHeadline>> GetActiveHeadlinesAsync(long feedId, CancellationToken ct = default) =>
        articles.GetActiveHeadlinesAsync(feedId, ct);

    public Task<IReadOnlyList<ArticleHeadline>> GetArchivedHeadlinesAsync(long categoryId, CancellationToken ct = default) =>
        articles.GetArchivedHeadlinesAsync(categoryId, ct);

    public Task<IReadOnlyList<ArticleHeadline>> GetTrashedHeadlinesAsync(CancellationToken ct = default) =>
        articles.GetTrashedHeadlinesAsync(ct);

    public Task<IReadOnlyDictionary<long, int>> GetUnreadCountsAsync(CancellationToken ct = default) =>
        articles.GetUnreadCountsAsync(ct);

    /// <summary>Loads an article for reading and marks it as read.</summary>
    public async Task<ArticleDetails?> OpenAsync(long id, CancellationToken ct = default)
    {
        var details = await articles.GetDetailsAsync(id, ct);
        if (details is { Article.IsRead: false })
        {
            await articles.SetReadAsync([id], true, ct);
            details = details with { Article = details.Article with { IsRead = true } };
        }
        return details;
    }

    public Task SetReadAsync(IReadOnlyCollection<long> ids, bool isRead, CancellationToken ct = default) =>
        articles.SetReadAsync(ids, isRead, ct);

    public Task SetPinnedAsync(long id, bool isPinned, CancellationToken ct = default) =>
        articles.SetPinnedAsync(id, isPinned, ct);

    /// <summary>Applies a thumb; giving the same thumb again clears it.</summary>
    public async Task<Rating> ToggleRatingAsync(long id, Rating current, Rating requested, CancellationToken ct = default)
    {
        var rating = current == requested ? Rating.None : requested;
        await articles.SetRatingAsync(id, rating, ct);
        return rating;
    }

    public Task TrashAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default) =>
        articles.TrashAsync(ids, time.GetUtcNow(), ct);

    public Task RestoreAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default) =>
        articles.RestoreAsync(ids, ct);

    /// <summary>Permanently deletes the content of everything in the trash; returns how many articles.</summary>
    public async Task<int> EmptyTrashAsync(CancellationToken ct = default) =>
        await articles.PurgeAsync(await articles.GetTrashedIdsAsync(ct), ct);

    /// <summary>An image stored with an archived article, for offline reading.</summary>
    public async Task<ArchivedResource?> GetArchivedImageAsync(long articleId, string url, CancellationToken ct = default)
    {
        if (await articles.GetResourceAsync(articleId, url, ct) is { } image)
            return image;

        // The reading page is served over https, so the browser upgrades http:// images to https://
        // before asking for them; the archive keeps the original http:// address.
        return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? await articles.GetResourceAsync(articleId, "http://" + url["https://".Length..], ct)
            : null;
    }
}
