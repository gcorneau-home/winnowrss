using Microsoft.Extensions.Logging;
using Winnow.Core.Abstractions;
using Winnow.Core.Reading;

namespace Winnow.Core.Services;

public sealed record ArchiveResult(int Articles, int ImagesSaved, int ImagesFailed);

/// <summary>Moves articles to the archive and stores their images, so they stay readable if the site disappears.</summary>
public sealed class ArchiveService(
    IArticleRepository articles,
    IResourceFetcher fetcher,
    TimeProvider time,
    ILogger<ArchiveService> logger)
{
    private const int ParallelDownloads = 4;

    public async Task<ArchiveResult> ArchiveAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        // The state change comes first: the article is archived even if some images cannot be fetched.
        var archived = await articles.ArchiveAsync(ids, time.GetUtcNow(), ct);

        int saved = 0, failed = 0;
        foreach (var id in archived)
        {
            if (await articles.GetAsync(id, ct) is not { } article)
                continue;

            // Download in parallel, then write one at a time (SQLite has a single writer).
            var urls = ArticleImages.Extract(article.ContentHtml, article.Link);
            var images = new FetchedResource?[urls.Count];
            await Parallel.ForAsync(0, urls.Count, new ParallelOptions { MaxDegreeOfParallelism = ParallelDownloads, CancellationToken = ct },
                async (i, token) => images[i] = await TryFetchAsync(id, urls[i], token));

            for (var i = 0; i < urls.Count; i++)
            {
                if (images[i] is { } image)
                {
                    await articles.AddResourceAsync(id, urls[i], image, ct);
                    saved++;
                }
                else
                {
                    failed++;
                }
            }
        }

        return new ArchiveResult(archived.Count, saved, failed);
    }

    private async Task<FetchedResource?> TryFetchAsync(long articleId, string url, CancellationToken ct)
    {
        try
        {
            return await fetcher.FetchImageAsync(url, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not archive image {Url} of article {ArticleId}", url, articleId);
            return null;
        }
    }
}
