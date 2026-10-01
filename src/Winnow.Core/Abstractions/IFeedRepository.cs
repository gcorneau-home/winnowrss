using Winnow.Core.Models;

namespace Winnow.Core.Abstractions;

public interface IFeedRepository
{
    Task<IReadOnlyList<Feed>> GetAllAsync(CancellationToken ct = default);
    Task<Feed?> GetAsync(long id, CancellationToken ct = default);
    Task<Feed?> GetByUrlAsync(string url, CancellationToken ct = default);
    Task<long> AddAsync(Feed feed, CancellationToken ct = default);
    Task UpdateDetailsAsync(long id, string title, string url, CancellationToken ct = default);
    Task UpdateFetchStateAsync(FeedFetchState state, CancellationToken ct = default);
    Task SetSkipFilterAsync(long id, bool skipFilter, CancellationToken ct = default);
    Task MoveToCategoryAsync(long id, long categoryId, CancellationToken ct = default);

    /// <summary>Deletes the feed and all its articles.</summary>
    Task DeleteAsync(long id, CancellationToken ct = default);

    Task<int> CountArchivedArticlesAsync(long id, CancellationToken ct = default);
}
