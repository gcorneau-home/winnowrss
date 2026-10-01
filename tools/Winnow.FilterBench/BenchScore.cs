using Winnow.Core.Abstractions;

namespace Winnow.FilterBench;

/// <summary>One article judged by one configuration. <paramref name="Decision"/> is null when the model's answer was unusable.</summary>
/// <param name="Rule">Set when a rule (trusted feed, keyword) decided instead of the model.</param>
public sealed record BenchResult(long ArticleId, string Title, bool ShouldKeep, FilterDecision? Decision, TimeSpan Elapsed, string? Error = null, string? Rule = null)
{
    public bool IsError => Decision is null;
    public bool IsFalseReject => ShouldKeep && Decision is { Keep: false };
    public bool IsFalseKeep => !ShouldKeep && Decision is { Keep: true };
    public bool IsCorrect => Decision is not null && Decision.Keep == ShouldKeep;
}

/// <summary>How a configuration did against the user's thumbs.</summary>
public sealed record BenchScore(
    string Configuration,
    int Liked,
    int Disliked,
    int FalseRejects,
    int FalseKeeps,
    int Errors,
    double Agreement,
    TimeSpan AverageTime,
    TimeSpan P95Time)
{
    /// <summary>Share of 👍 articles the filter would have hidden: the number that matters most.</summary>
    public double FalseRejectRate => Liked == 0 ? 0 : (double)FalseRejects / Liked;
    public double FalseKeepRate => Disliked == 0 ? 0 : (double)FalseKeeps / Disliked;

    public static BenchScore From(string configuration, IReadOnlyList<BenchResult> results)
    {
        var times = results.Where(r => !r.IsError).Select(r => r.Elapsed).Order().ToList();
        var judged = results.Count(r => !r.IsError);
        return new BenchScore(
            configuration,
            results.Count(r => r.ShouldKeep),
            results.Count(r => !r.ShouldKeep),
            results.Count(r => r.IsFalseReject),
            results.Count(r => r.IsFalseKeep),
            results.Count(r => r.IsError),
            judged == 0 ? 0 : (double)results.Count(r => r.IsCorrect) / judged,
            times.Count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks((long)times.Average(t => t.Ticks)),
            times.Count == 0 ? TimeSpan.Zero : times[Math.Min(times.Count - 1, (int)Math.Ceiling(times.Count * 0.95) - 1)]);
    }

    /// <summary>Best first: fewest false rejects, then most agreement, then fastest.</summary>
    public static IReadOnlyList<BenchScore> Rank(IEnumerable<BenchScore> scores) =>
        scores.OrderBy(s => s.FalseRejectRate).ThenByDescending(s => s.Agreement).ThenBy(s => s.AverageTime).ToList();
}
