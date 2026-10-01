using System.Globalization;
using System.Text;

namespace Winnow.FilterBench;

/// <summary>The ranking table (console and Markdown) and, per configuration, the articles it got wrong.</summary>
public static class BenchReport
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Table(IReadOnlyList<BenchScore> ranked)
    {
        var table = new StringBuilder();
        table.AppendLine("| # | Configuration | False rejects (👍 hidden) | False keeps (👎 shown) | Agreement | Errors | Avg time | P95 |");
        table.AppendLine("|---|---|---|---|---|---|---|---|");
        for (var i = 0; i < ranked.Count; i++)
        {
            var s = ranked[i];
            table.AppendLine(string.Create(Invariant,
                $"| {i + 1} | {s.Configuration} | {s.FalseRejects}/{s.Liked} ({Percent(s.FalseRejectRate)}) | {s.FalseKeeps}/{s.Disliked} ({Percent(s.FalseKeepRate)}) | {Percent(s.Agreement)} | {s.Errors} | {s.AverageTime.TotalSeconds:0.0} s | {s.P95Time.TotalSeconds:0.0} s |"));
        }
        return table.ToString();
    }

    private static string Percent(double rate) => string.Create(Invariant, $"{rate * 100:0}%");

    public static string Markdown(
        IReadOnlyList<BenchScore> ranked,
        IReadOnlyDictionary<string, IReadOnlyList<BenchResult>> results,
        string header)
    {
        var report = new StringBuilder();
        report.AppendLine("# Filter bench");
        report.AppendLine();
        report.AppendLine(header);
        report.AppendLine();
        report.AppendLine("Ranked by false rejects (liked articles the filter would hide), then agreement, then speed.");
        report.AppendLine();
        report.Append(Table(ranked));

        foreach (var score in ranked)
        {
            var wrong = results[score.Configuration].Where(r => !r.IsCorrect).OrderBy(r => r.IsFalseKeep).ToList();
            report.AppendLine();
            report.AppendLine($"## {score.Configuration}");
            report.AppendLine();
            if (wrong.Count == 0)
            {
                report.AppendLine("No mistakes.");
                continue;
            }
            foreach (var r in wrong)
            {
                var kind = r.IsError ? "⚠️ error" : r.IsFalseReject ? "❌ hid a 👍" : "➖ showed a 👎";
                var why = r.IsError ? r.Error
                    : r.Rule is not null ? $"{r.Rule}: {r.Decision!.Criterion}"
                    : $"{r.Decision!.Criterion ?? "no interest"} — {r.Decision.Reason}";
                report.AppendLine($"- {kind} **{r.Title}**: {why}");
            }
        }
        return report.ToString();
    }
}
