using System.Text.RegularExpressions;
using Winnow.Core.Models;

namespace Winnow.Core.Filtering;

/// <summary>
/// What the model reads to judge an article: title, description and the beginning of the text.
/// Enough to recognize the topic while keeping each call short.
/// </summary>
public sealed partial record FilterInput(string Title, string? Description, string? Excerpt)
{
    public const int ExcerptWords = 150;
    public const int ExcerptMaxChars = 1200;

    /// <param name="excerptWords">Words of the article text to include; 0 for none (title and description only).</param>
    /// <param name="includeDescription">False to judge on the title (and excerpt) alone.</param>
    public static FilterInput From(Article article, int excerptWords = ExcerptWords, bool includeDescription = true)
    {
        var description = includeDescription ? Normalize(article.Summary) : null;
        var text = excerptWords > 0 ? Normalize(article.ContentText) : null;

        // Many feeds use the start of the article as its description: don't send it twice.
        if (description is not null && text is not null && StartsAlike(text, description))
            description = null;

        return new FilterInput(article.Title.Trim(), description, text is null ? null : Truncate(text, excerptWords));
    }

    private static string Truncate(string text, int maxWords)
    {
        // About 8 characters per word, and never less than the default budget.
        var maxChars = Math.Max(ExcerptMaxChars, maxWords * 8);
        var words = Whitespace().Split(text);
        var excerpt = string.Join(' ', words.Take(maxWords));
        if (excerpt.Length > maxChars)
        {
            var cut = excerpt.LastIndexOf(' ', maxChars);
            excerpt = excerpt[..(cut > 0 ? cut : maxChars)];
        }
        return excerpt.Length < text.Length ? excerpt + " …" : excerpt;
    }

    private static bool StartsAlike(string text, string description)
    {
        var prefix = description[..Math.Min(80, description.Length)];
        return text.Contains(prefix.TrimEnd('.', '…', ' '), StringComparison.OrdinalIgnoreCase);
    }

    private static string? Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
