using System.Text.RegularExpressions;
using Winnow.Core.Abstractions;
using Winnow.Core.Models;

namespace Winnow.Core.Services;

/// <summary>Full-text search over active and archived articles.</summary>
public sealed partial class SearchService(IArticleRepository articles)
{
    private const int MaxResults = 200;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string text, CancellationToken ct = default) =>
        BuildQuery(text) is { } query ? await articles.SearchAsync(query, MaxResults, ct) : [];

    /// <summary>
    /// Turns what the user typed into an FTS5 query: every word must appear, as a word prefix
    /// ("linu" finds "Linux"). Quoting each word keeps FTS5 operators and punctuation from breaking the query.
    /// </summary>
    public static string? BuildQuery(string text)
    {
        var words = Word().Matches(text).Select(m => $"\"{m.Value}\"*").ToList();
        return words.Count == 0 ? null : string.Join(' ', words);
    }

    [GeneratedRegex(@"[\p{L}\p{N}_]+")]
    private static partial Regex Word();
}
