using System.Xml;
using Winnow.Core.Abstractions;
using Winnow.Core.Feeds;
using Winnow.Core.Models;

namespace Winnow.Core.Services;

public sealed class FeedService(IFeedRepository feeds, IFeedFetcher fetcher, FeedRefreshService refresher)
{
    public Task<IReadOnlyList<Feed>> GetAllAsync(CancellationToken ct = default) => feeds.GetAllAsync(ct);

    /// <summary>Fetches the feed once to validate it and get its title, then stores it with its articles.</summary>
    public async Task<Feed> AddAsync(long categoryId, string url, CancellationToken ct = default)
    {
        url = ValidUrl(url);
        if (await feeds.GetByUrlAsync(url, ct) is not null)
            throw new WinnowException(WinnowError.FeedAlreadySubscribed, "This feed is already subscribed.");

        var probe = new Feed { CategoryId = categoryId, Url = url };
        FeedFetchResult fetched;
        try
        {
            fetched = await fetcher.FetchAsync(probe, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or XmlException or FormatException)
        {
            throw new WinnowException(WinnowError.FeedUnreadable, $"Could not read the feed: {ex.Message}", [ex.Message], ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) // HttpClient timeout
        {
            throw new WinnowException(WinnowError.FeedTimeout, "The feed did not respond in time.", inner: ex);
        }

        var feed = probe with { Title = fetched.Feed?.Title ?? url, SiteUrl = fetched.Feed?.SiteUrl };
        feed = feed with { Id = await feeds.AddAsync(feed, ct) };
        await refresher.IngestAsync(feed, fetched, ct);
        return feed;
    }

    public async Task UpdateAsync(long id, string title, string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new WinnowException(WinnowError.FeedTitleEmpty, "Feed title cannot be empty.");
        url = ValidUrl(url);
        if (await feeds.GetByUrlAsync(url, ct) is { } other && other.Id != id)
            throw new WinnowException(WinnowError.FeedUrlInUse, $"This address is already used by the feed \"{other.Title}\".", [other.Title]);
        await feeds.UpdateDetailsAsync(id, title.Trim(), url, ct);
    }

    /// <summary>Trusted feed: its articles are always kept, without asking the model.</summary>
    public Task SetSkipFilterAsync(long feedId, bool skipFilter, CancellationToken ct = default) =>
        feeds.SetSkipFilterAsync(feedId, skipFilter, ct);

    /// <summary>Moves a feed to another category; its archived articles follow it.</summary>
    public Task MoveToCategoryAsync(long feedId, long categoryId, CancellationToken ct = default) =>
        feeds.MoveToCategoryAsync(feedId, categoryId, ct);

    public Task<int> CountArchivedArticlesAsync(long id, CancellationToken ct = default) =>
        feeds.CountArchivedArticlesAsync(id, ct);

    public Task DeleteAsync(long id, CancellationToken ct = default) => feeds.DeleteAsync(id, ct);

    private static string ValidUrl(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new WinnowException(WinnowError.FeedUrlInvalid, "The feed address must be an http or https URL.");
        return uri.AbsoluteUri;
    }
}
