using Winnow.Core.Abstractions;

namespace Winnow.Core.Feeds;

public sealed class HttpResourceFetcher(HttpClient http) : IResourceFetcher
{
    private const long MaxImageBytes = 15 * 1024 * 1024;

    public async Task<FetchedResource?> FetchImageAsync(string url, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!response.IsSuccessStatusCode
            || contentType is null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || response.Content.Headers.ContentLength > MaxImageBytes)
            return null;

        var data = await response.Content.ReadAsByteArrayAsync(ct);
        return data.Length <= MaxImageBytes ? new FetchedResource(contentType, data) : null;
    }
}
