using Winnow.Core.Filtering;

namespace Winnow.Core.Abstractions;

/// <summary>Writes a short summary of an article with a language model.</summary>
public interface IArticleSummarizer
{
    /// <summary>Returns the whole summary; <paramref name="partial"/> receives the text so far while it is written.</summary>
    /// <exception cref="FilterUnavailableException">The model cannot be reached or is not installed.</exception>
    /// <exception cref="FilterResponseException">The model answered with an error or nothing.</exception>
    Task<string> SummarizeAsync(SummaryRequest request, IProgress<string>? partial = null, CancellationToken ct = default);
}

/// <param name="Language">English name of the language to write in ("French"), whatever the article's language.</param>
public sealed record SummaryRequest(string Endpoint, string Model, string Title, string Text, string Language);
