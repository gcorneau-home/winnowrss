using Winnow.Core.Abstractions;

namespace Winnow.Core.Filtering;

/// <summary>Keeps everything; for tests and as a stand-in when no model is used.</summary>
public sealed class PassThroughFilter : IArticleFilter
{
    public Task<FilterDecision> EvaluateAsync(FilterInput input, FilterContext context, CancellationToken ct = default) =>
        Task.FromResult(FilterDecision.Kept);
}
