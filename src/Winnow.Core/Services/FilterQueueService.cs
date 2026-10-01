using Microsoft.Extensions.Logging;
using Winnow.Core.Abstractions;
using Winnow.Core.Filtering;
using Winnow.Core.Models;

namespace Winnow.Core.Services;

/// <summary>Reported after each verdict, so the UI can update that article right away.</summary>
public sealed record FilterProgress(int Done, int Remaining, long ArticleId, FilterStatus Status, string? Criterion, string? Reason, string? Model);

/// <param name="Unavailable">Why filtering stopped early (model server down…), if it did.</param>
public sealed record FilterRunResult(int Kept, int Rejected, int Errors, string? Unavailable)
{
    public static FilterRunResult Nothing { get; } = new(0, 0, 0, null);
    public int Total => Kept + Rejected + Errors;

    public FilterRunResult Add(FilterRunResult other) =>
        new(Kept + other.Kept, Rejected + other.Rejected, Errors + other.Errors, other.Unavailable ?? Unavailable);
}

/// <summary>
/// Judges pending articles one at a time, after the refresh has stored them. If the model server is down,
/// articles stay pending and the next run picks them up.
/// </summary>
public sealed class FilterQueueService(
    IArticleRepository articles,
    IFeedRepository feeds,
    IArticleFilter filter,
    FilterCriteriaService criteria,
    SettingsService settings,
    TimeProvider time,
    ILogger<FilterQueueService> logger)
{
    private const int BatchSize = 20;
    private readonly SemaphoreSlim _running = new(1, 1);
    private int _rerunRequested;

    /// <summary>
    /// Processes every pending article. If a run is already in progress, this returns at once and the
    /// running one takes another pass, so articles requeued meanwhile are not left behind.
    /// </summary>
    public async Task<FilterRunResult> RunPendingAsync(IProgress<FilterProgress>? progress = null, CancellationToken ct = default)
    {
        if (!await _running.WaitAsync(0, ct))
        {
            Interlocked.Exchange(ref _rerunRequested, 1);
            return FilterRunResult.Nothing;
        }

        var total = FilterRunResult.Nothing;
        try
        {
            do
            {
                Interlocked.Exchange(ref _rerunRequested, 0);
                total = total.Add(await RunCoreAsync(progress, ct));
            }
            while (total.Unavailable is null && Volatile.Read(ref _rerunRequested) == 1);
        }
        finally
        {
            _running.Release();
        }

        // A request that arrived between the last check and the release above.
        return total.Unavailable is null && Volatile.Read(ref _rerunRequested) == 1
            ? total.Add(await RunPendingAsync(progress, ct))
            : total;
    }

    /// <summary>Puts unread active articles back in the queue, e.g. after the criteria changed; returns how many.</summary>
    public Task<int> RequeueUnreadAsync(CancellationToken ct = default) => articles.RequeueUnreadAsync(ct);

    private async Task<FilterRunResult> RunCoreAsync(IProgress<FilterProgress>? progress, CancellationToken ct)
    {
        int kept = 0, rejected = 0, errors = 0;

        while (true)
        {
            // Settings and criteria are read for each batch, so changes made during a long run apply.
            var filterSettings = await settings.GetFilterSettingsAsync(ct);
            if (!filterSettings.Enabled || await articles.GetPendingAsync(BatchSize, ct) is not { Count: > 0 } batch)
                break;

            var version = await settings.GetFilterVersionAsync(ct);
            var context = new FilterContext(
                filterSettings.Endpoint,
                filterSettings.Model,
                await criteria.GetAllAsync(ct),
                LanguageName(await settings.GetUiLanguageAsync(ct)));
            var remaining = await articles.CountPendingAsync(ct);
            var feedsById = (await feeds.GetAllAsync(ct)).ToDictionary(f => f.Id);

            foreach (var article in batch)
            {
                FilterResult result;
                try
                {
                    // Trusted feeds and keywords decide without the model (and much faster).
                    if (FilterRules.Apply(article, feedsById.GetValueOrDefault(article.FeedId), context.Criteria) is var (ruled, rule))
                    {
                        result = new FilterResult(ruled.Keep ? FilterStatus.Kept : FilterStatus.Rejected,
                            ruled.Criterion, null, version, rule, time.GetUtcNow());
                        await Save(article.Id, result);
                        continue;
                    }

                    var decision = await filter.EvaluateAsync(FilterInput.From(article), context, ct);
                    if (await settings.GetFilterVersionAsync(ct) != version)
                        break; // criteria changed while the model was thinking: judge it again with the new ones
                    result = new FilterResult(decision.Keep ? FilterStatus.Kept : FilterStatus.Rejected,
                        decision.Criterion, decision.Reason, version, context.Model, time.GetUtcNow());
                }
                catch (FilterUnavailableException ex)
                {
                    logger.LogWarning(ex, "Filter unavailable, {Count} article(s) stay pending", remaining);
                    return new FilterRunResult(kept, rejected, errors, ex.Message);
                }
                catch (FilterResponseException ex)
                {
                    logger.LogWarning(ex, "Filter could not judge article {ArticleId}", article.Id);
                    result = new FilterResult(FilterStatus.Error, null, ex.Message, version, context.Model, time.GetUtcNow());
                }

                await Save(article.Id, result);
            }

            async Task Save(long articleId, FilterResult result)
            {
                await articles.SaveFilterResultAsync(articleId, result, ct);
                switch (result.Status)
                {
                    case FilterStatus.Kept: kept++; break;
                    case FilterStatus.Rejected: rejected++; break;
                    default: errors++; break;
                }
                remaining = Math.Max(0, remaining - 1);
                progress?.Report(new FilterProgress(kept + rejected + errors, remaining, articleId, result.Status, result.Criterion, result.Reason, result.Model));
            }
        }

        return new FilterRunResult(kept, rejected, errors, null);
    }

    private static string LanguageName(string? code) => code switch
    {
        "fr" => "French",
        _ => "English",
    };
}
