namespace Winnow.Core.Models;

/// <summary>A summary of an article written by the model, in one language (two-letter code).</summary>
public sealed record ArticleSummary(long ArticleId, string Language, string Text, string Model, DateTimeOffset CreatedAt);
