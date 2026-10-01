using System.Collections.Concurrent;
using System.Xml;
using Microsoft.Extensions.Logging;
using Winnow.Core.Abstractions;
using Winnow.Core.Feeds;
using Winnow.Core.Models;
using Winnow.Core.Text;

namespace Winnow.Core.Services;

public sealed record FeedRefreshResult(long FeedId, int Added, string? Error)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// Fetch → dedup → store pipeline. New articles are stored as pending when the filter is on;
/// <see cref="FilterQueueService"/> judges them afterwards so a refresh never waits for the model.
/// </summary>
public sealed class FeedRefreshService(
    IFeedRepository feeds,
    IArticleRepository articles,
    IFeedFetcher fetcher,
    SettingsService settings,
    TimeProvider time,
    ILogger<FeedRefreshService> logger)
{
    // One refresh at a time per feed, so concurrent refreshes never insert the same items twice.
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _feedLocks = new();

    /// <summary>Refreshes every feed; a failure on one feed does not stop the others.</summary>
    public async Task<IReadOnlyList<FeedRefreshResult>> RefreshAllAsync(CancellationToken ct = default)
    {
        var results = new List<FeedRefreshResult>();
        foreach (var feed in await feeds.GetAllAsync(ct))
        {
            try
            {
                results.Add(await RefreshAsync(feed, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Refreshing feed {FeedId} ({Url}) failed", feed.Id, feed.Url);
                results.Add(new FeedRefreshResult(feed.Id, 0, ex.Message));
            }
        }
        return results;
    }

    public async Task<FeedRefreshResult> RefreshAsync(long feedId, CancellationToken ct = default)
    {
        var feed = await feeds.GetAsync(feedId, ct)
            ?? throw new InvalidOperationException($"Feed {feedId} does not exist.");
        return await RefreshAsync(feed, ct);
    }

    /// <summary>Stores the new items of an already fetched feed and records the fetch state.</summary>
    public async Task<FeedRefreshResult> IngestAsync(Feed feed, FeedFetchResult fetched, CancellationToken ct = default)
    {
        using var _ = await LockFeedAsync(feed.Id, ct);
        return await IngestCoreAsync(feed, fetched, ct);
    }

    private async Task<FeedRefreshResult> RefreshAsync(Feed feed, CancellationToken ct)
    {
        using var _ = await LockFeedAsync(feed.Id, ct);

        FeedFetchResult fetched;
        try
        {
            fetched = await fetcher.FetchAsync(feed, ct);
        }
        catch (Exception ex) when (IsFetchFailure(ex, ct))
        {
            logger.LogWarning(ex, "Fetching feed {FeedId} ({Url}) failed", feed.Id, feed.Url);
            await feeds.UpdateFetchStateAsync(
                new FeedFetchState(feed.Id, feed.SiteUrl, feed.ETag, feed.LastModified, time.GetUtcNow(), ex.Message), ct);
            return new FeedRefreshResult(feed.Id, 0, ex.Message);
        }

        return await IngestCoreAsync(feed, fetched, ct);
    }

    private async Task<IDisposable> LockFeedAsync(long feedId, CancellationToken ct)
    {
        var gate = _feedLocks.GetOrAdd(feedId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    private async Task<FeedRefreshResult> IngestCoreAsync(Feed feed, FeedFetchResult fetched, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var added = 0;

        if (fetched.Feed is not null)
        {
            var knownKeys = await articles.GetKeysAsync(feed.Id, ct);
            var filterStatus = (await settings.GetFilterSettingsAsync(ct)).Enabled ? FilterStatus.Pending : FilterStatus.Unfiltered;
            var newArticles = new List<NewArticle>();

            foreach (var item in fetched.Feed.Items)
            {
                var key = ArticleKeys.Compute(item);
                if (!knownKeys.Add(key))
                    continue;

                newArticles.Add(new NewArticle(ToArticle(feed.Id, key, item, filterStatus, now), item.Tags));
                added++;
            }

            await articles.AddRangeAsync(newArticles, ct);
        }

        await feeds.UpdateFetchStateAsync(
            new FeedFetchState(feed.Id, fetched.Feed?.SiteUrl ?? feed.SiteUrl, fetched.ETag, fetched.LastModified, now, null), ct);

        return new FeedRefreshResult(feed.Id, added, null);
    }

    private static Article ToArticle(long feedId, string key, FeedItem item, FilterStatus filterStatus, DateTimeOffset now) => new()
    {
        FeedId = feedId,
        DedupKey = key,
        Link = item.Link,
        Title = item.Title,
        Author = item.Author,
        PublishedAt = item.PublishedAt,
        Summary = item.Summary,
        ContentHtml = item.ContentHtml,
        ContentText = item.ContentHtml is null ? item.Summary : HtmlText.ToPlainText(item.ContentHtml),
        State = ArticleState.Active,
        FilterStatus = filterStatus,
        FetchedAt = now,
    };

    private static bool IsFetchFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or XmlException or FormatException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested); // HttpClient timeout
}
