using Winnow.Core.Abstractions;
using Winnow.Core.Filtering;
using Winnow.Core.Models;

namespace Winnow.Core.Services;

/// <summary>Summaries of articles on request, kept with the article (one per language) so they show again later.</summary>
public sealed class SummaryService(
    IArticleRepository articles,
    IArticleSummarizer summarizer,
    SettingsService settings,
    TimeProvider time)
{
    public Task<IReadOnlyList<ArticleSummary>> GetAllAsync(long articleId, CancellationToken ct = default) =>
        articles.GetSummariesAsync(articleId, ct);

    /// <summary>The language of the last summary asked for; at first, the interface's (null: follow Windows).</summary>
    public async Task<string?> GetPreferredLanguageAsync(CancellationToken ct = default) =>
        await settings.GetSummaryLanguageAsync(ct) ?? await settings.GetUiLanguageAsync(ct);

    /// <summary>
    /// Writes a summary of the article in <paramref name="language"/> (two-letter code) with the filter's model,
    /// replacing any earlier one in that language. <paramref name="partial"/> receives the text while it is written.
    /// </summary>
    public async Task<ArticleSummary> SummarizeAsync(
        long articleId, string language, IProgress<string>? partial = null, CancellationToken ct = default)
    {
        var article = await articles.GetAsync(articleId, ct);
        var text = article?.ContentText is { Length: > 0 } content ? content : article?.Summary;
        if (article is null || string.IsNullOrWhiteSpace(text))
            throw new WinnowException(WinnowError.SummaryNothingToSummarize, $"Article {articleId} has no text to summarize.");

        await settings.SetSummaryLanguageAsync(language, ct);
        var model = await settings.GetFilterSettingsAsync(ct);
        string written;
        try
        {
            written = await summarizer.SummarizeAsync(
                new SummaryRequest(model.Endpoint, model.Model, article.Title, text, LanguageNames.English(language)), partial, ct);
        }
        catch (Exception ex) when (ex is FilterUnavailableException or FilterResponseException)
        {
            throw new WinnowException(WinnowError.SummaryUnavailable, ex.Message, [ex.Message], ex);
        }

        var summary = new ArticleSummary(articleId, language, written, model.Model, time.GetUtcNow());
        await articles.SaveSummaryAsync(summary, ct);
        return summary;
    }
}
