using Microsoft.Extensions.Logging;
using Winnow.Core.Abstractions;

namespace Winnow.Core.Services;

/// <summary>How long articles are kept before their content is purged.</summary>
public sealed record RetentionPolicy(TimeSpan ReadArticles, TimeSpan Trash)
{
    public static RetentionPolicy Default { get; } = new(TimeSpan.FromDays(30), TimeSpan.FromDays(30));
}

/// <summary>
/// Purges read articles and old trash. Pinned, rated (👍/👎) and archived articles are never purged; unread ones
/// wait until read. Rated articles keep their content because the filter bench and learning from thumbs need it.
/// </summary>
public sealed class RetentionService(
    IArticleRepository articles,
    RetentionPolicy policy,
    TimeProvider time,
    ILogger<RetentionService> logger)
{
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var expired = await articles.GetExpiredIdsAsync(now - policy.ReadArticles, now - policy.Trash, ct);
        var purged = await articles.PurgeAsync(expired, ct);
        if (purged > 0)
            logger.LogInformation("Retention purged {Count} article(s)", purged);
        return purged;
    }
}
