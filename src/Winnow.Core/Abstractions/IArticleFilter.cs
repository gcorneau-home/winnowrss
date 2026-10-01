using Winnow.Core.Filtering;
using Winnow.Core.Models;

namespace Winnow.Core.Abstractions;

/// <summary>Judges an article against the user's interests and exclusions.</summary>
public interface IArticleFilter
{
    /// <exception cref="FilterUnavailableException">The model cannot be reached; the article should stay pending.</exception>
    /// <exception cref="FilterResponseException">The model answered something unusable for this article.</exception>
    Task<FilterDecision> EvaluateAsync(FilterInput input, FilterContext context, CancellationToken ct = default);
}

/// <summary>Everything besides the article that a verdict depends on.</summary>
public sealed record FilterContext(
    string Endpoint,
    string Model,
    IReadOnlyList<FilterCriterion> Criteria,
    string ReasonLanguage);

/// <param name="Criterion">Text of the interest (kept) or exclusion (rejected) that decided, if any.</param>
public sealed record FilterDecision(bool Keep, string? Criterion, string? Reason)
{
    public static FilterDecision Kept { get; } = new(true, null, null);
}

public interface IFilterCriteriaRepository
{
    Task<IReadOnlyList<FilterCriterion>> GetAllAsync(CancellationToken ct = default);
    Task<long> AddAsync(CriterionKind kind, string text, CancellationToken ct = default);
    Task UpdateAsync(long id, string text, bool enabled, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}
