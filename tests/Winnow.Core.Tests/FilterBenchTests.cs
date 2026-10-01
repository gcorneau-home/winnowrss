using Winnow.Core.Abstractions;
using Winnow.Core.Filtering;
using Winnow.Core.Models;
using Winnow.Core.Tests.TestSupport;
using Winnow.FilterBench;

namespace Winnow.Core.Tests;

public class FilterBenchTests
{
    private static BenchResult Result(bool shouldKeep, bool? kept, int ms = 100) =>
        new(1, "t", shouldKeep, kept is null ? null : new FilterDecision(kept.Value, null, null), TimeSpan.FromMilliseconds(ms), kept is null ? "err" : null);

    [Fact]
    public void Score_counts_false_rejects_false_keeps_and_errors()
    {
        var score = BenchScore.From("cfg",
        [
            Result(shouldKeep: true, kept: true, 100),
            Result(shouldKeep: true, kept: false, 200),   // false reject
            Result(shouldKeep: false, kept: false, 300),
            Result(shouldKeep: false, kept: true, 400),   // false keep
            Result(shouldKeep: true, kept: null, 5000),   // error, not timed
        ]);

        Assert.Equal((3, 2, 1, 1, 1), (score.Liked, score.Disliked, score.FalseRejects, score.FalseKeeps, score.Errors));
        Assert.Equal(0.5, score.Agreement);
        Assert.Equal(1.0 / 3, score.FalseRejectRate, 3);
        Assert.Equal(TimeSpan.FromMilliseconds(250), score.AverageTime);
        Assert.Equal(TimeSpan.FromMilliseconds(400), score.P95Time);
    }

    [Fact]
    public void Ranking_puts_fewest_false_rejects_first_then_agreement_then_speed()
    {
        var careful = new BenchScore("careful", 10, 10, 0, 5, 0, 0.75, TimeSpan.FromSeconds(2), TimeSpan.Zero);
        var accurate = new BenchScore("accurate", 10, 10, 1, 0, 0, 0.95, TimeSpan.FromSeconds(1), TimeSpan.Zero);
        var fastTwin = careful with { Configuration = "fast twin", AverageTime = TimeSpan.FromSeconds(1) };

        Assert.Equal(["fast twin", "careful", "accurate"], BenchScore.Rank([accurate, careful, fastTwin]).Select(s => s.Configuration));
    }

    [Fact]
    public void Report_lists_each_configuration_mistakes()
    {
        var results = new Dictionary<string, IReadOnlyList<BenchResult>>
        {
            ["cfg"] = [Result(true, false) with { Title = "Nouveau Visual Studio", Decision = new FilterDecision(false, null, "Hors sujet.") }],
        };

        var report = BenchReport.Markdown([BenchScore.From("cfg", results["cfg"])], results, "header");

        Assert.Contains("| 1 | cfg | 1/1 (100%)", report);
        Assert.Contains("❌ hid a 👍 **Nouveau Visual Studio**: no interest — Hors sujet.", report);
    }

    [Fact]
    public void Input_variants_control_how_much_the_model_reads()
    {
        var article = new Article
        {
            Title = "Titre",
            Summary = "Une description différente",
            ContentText = string.Join(' ', Enumerable.Range(1, 500).Select(i => $"mot{i}")),
        };

        var title = InputVariant.All.Single(v => v.Name == "title").Build(article);
        var summary = InputVariant.All.Single(v => v.Name == "title+description").Build(article);
        var longExcerpt = InputVariant.All.Single(v => v.Name == "long-excerpt").Build(article);

        Assert.Equal(new FilterInput("Titre", null, null), title);
        Assert.Equal(new FilterInput("Titre", "Une description différente", null), summary);
        Assert.EndsWith("mot400 …", longExcerpt.Excerpt);
    }

    [Fact]
    public void Prompt_variants_change_only_the_rules()
    {
        var context = new FilterContext("", "", [], "French");
        var strict = PromptVariant.All.Single(v => v.Name == "strict-interests");

        var system = FilterPrompt.System(context, strict.Options);

        Assert.Contains("clearly about", system);
        Assert.Contains(FilterPromptOptions.Default.ExclusionRule, system);
        Assert.Equal(FilterPrompt.System(context), FilterPrompt.System(context, FilterPromptOptions.Default));
    }

    [Fact]
    public async Task Rated_articles_are_the_ones_with_a_thumb_and_content()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var h = await app.ArticleService.GetActiveHeadlinesAsync(feed.Id);
        await app.Articles.SetRatingAsync(h[0].Id, Rating.Up);
        await app.Articles.SetRatingAsync(h[1].Id, Rating.Down);
        await app.Articles.SetRatingAsync(h[2].Id, Rating.Up);
        await app.Articles.PurgeAsync([h[2].Id]); // content gone: cannot be judged again

        var rated = await app.Articles.GetRatedAsync();

        Assert.Equal([h[0].Id, h[1].Id], rated.Select(a => a.Id).Order());
        Assert.All(rated, a => Assert.NotNull(a.ContentText));
    }
}
