using Winnow.Core.Feeds;
using Winnow.Core.Models;

namespace Winnow.Core.Abstractions;

public interface IFeedFetcher
{
    /// <summary>Downloads and parses a feed, using its ETag/Last-Modified for conditional requests.</summary>
    Task<FeedFetchResult> FetchAsync(Feed feed, CancellationToken ct = default);
}
