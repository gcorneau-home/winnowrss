using System.Net;
using Winnow.Core.Abstractions;
using Winnow.Core.Models;

namespace Winnow.Core.Feeds;

public sealed class SyndicationFeedFetcher(HttpClient http) : IFeedFetcher
{
    public async Task<FeedFetchResult> FetchAsync(Feed feed, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, feed.Url);
        if (feed.ETag is not null)
            request.Headers.TryAddWithoutValidation("If-None-Match", feed.ETag);
        if (feed.LastModified is not null)
            request.Headers.TryAddWithoutValidation("If-Modified-Since", feed.LastModified);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotModified)
            return new FeedFetchResult(null, feed.ETag, feed.LastModified);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsByteArrayAsync(ct);
        var finalUri = response.RequestMessage?.RequestUri ?? new Uri(feed.Url);
        var parsed = FeedParser.Parse(new MemoryStream(body), finalUri);

        return new FeedFetchResult(
            parsed,
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified?.ToString("R"));
    }
}
