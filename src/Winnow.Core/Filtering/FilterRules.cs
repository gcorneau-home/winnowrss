using System.Text.RegularExpressions;
using Winnow.Core.Abstractions;
using Winnow.Core.Models;

namespace Winnow.Core.Filtering;

/// <summary>
/// Decisions made without the model: articles of a trusted feed are always kept, and a title containing one of the
/// user's keywords is always filtered out. Trusted feeds win: "never filter this feed" means never.
/// </summary>
public static class FilterRules
{
    /// <summary>Stored as the verdict's model, so the UI can say which rule decided.</summary>
    public const string TrustedFeed = "rule:trusted-feed";
    public const string Keyword = "rule:keyword";

    public static bool IsRule(string? model) => model is TrustedFeed or Keyword;

    /// <returns>The decision and the rule that made it, or null when the model must judge.</returns>
    public static (FilterDecision Decision, string Rule)? Apply(Article article, Feed? feed, IReadOnlyList<FilterCriterion> criteria)
    {
        if (feed is { SkipFilter: true })
            return (new FilterDecision(true, feed.Title, null), TrustedFeed);

        var keyword = criteria
            .Where(c => c.Enabled && c.Kind == CriterionKind.Keyword && !string.IsNullOrWhiteSpace(c.Text))
            .Select(c => c.Text.Trim())
            .FirstOrDefault(k => ContainsWord(article.Title, k));
        return keyword is null ? null : (new FilterDecision(false, keyword, null), Keyword);
    }

    /// <summary>Whole-word, case-insensitive: "Quiz" matches "Quiz: Python 3.15" but not "Quizlet".</summary>
    public static bool ContainsWord(string text, string word) =>
        Regex.IsMatch(text, $@"(?<!\w){Regex.Escape(word.Trim())}(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
