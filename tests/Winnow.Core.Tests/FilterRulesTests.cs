using Dapper;
using Winnow.Core.Filtering;
using Winnow.Core.Models;
using Winnow.Core.Tests.TestSupport;

namespace Winnow.Core.Tests;

public class FilterRulesTests
{
    private const string Ps5 = "Rebound, le jailbreak qui débloque presque toutes les PS5";

    [Theory]
    [InlineData("Quiz: What's New in Python 3.15", "Quiz", true)]
    [InlineData("The Real Python Podcast – Episode #311", "podcast", true)]
    [InlineData("Quizlet adds AI flashcards", "Quiz", false)]   // whole word only
    [InlineData("Les jeux PS5 du mois", "PS5", true)]
    [InlineData("C# 14 et .NET 10", "C#", true)]                // symbols are matched literally
    public void Keywords_match_whole_words_in_any_case(string title, string keyword, bool expected) =>
        Assert.Equal(expected, FilterRules.ContainsWord(title, keyword));

    [Fact]
    public void Trusted_feeds_win_over_keywords()
    {
        var article = new Article { Title = "Quiz: protéines" };
        var keyword = new FilterCriterion { Kind = CriterionKind.Keyword, Text = "Quiz" };

        var trusted = FilterRules.Apply(article, new Feed { Title = "STAT", SkipFilter = true }, [keyword]);
        var other = FilterRules.Apply(article, new Feed { Title = "Real Python" }, [keyword]);
        var disabled = FilterRules.Apply(article, null, [keyword with { Enabled = false }]);

        Assert.Equal((new Abstractions.FilterDecision(true, "STAT", null), FilterRules.TrustedFeed), trusted);
        Assert.Equal((new Abstractions.FilterDecision(false, "Quiz", null), FilterRules.Keyword), other);
        Assert.Null(disabled);
    }

    [Fact]
    public async Task The_queue_applies_rules_without_asking_the_model()
    {
        using var app = new TestApp();
        await app.EnableFilterAsync();
        var (categoryId, sample) = await app.AddSampleFeedAsync();
        await app.CriteriaService.AddAsync(CriterionKind.Keyword, "PS5");
        app.Fetcher.Respond("https://trusted.example.com/feed", Fixtures.SampleFeed() with { Title = "Trusted" });
        var trusted = await app.FeedService.AddAsync(categoryId, "https://trusted.example.com/feed");
        await app.FeedService.SetSkipFilterAsync(trusted.Id, true);
        app.Filter.RejectKeywords["Firefox"] = "Nouvelles applications"; // the model would reject Firefox

        var result = await app.FilterQueue.RunPendingAsync();

        // Sample feed: PS5 by keyword, the two others by the model; Trusted: all three kept by the rule.
        Assert.Equal(2, app.Filter.Inputs.Count);
        Assert.Equal((4, 2), (result.Kept, result.Rejected));
        var ps5 = (await app.ArticleService.GetActiveHeadlinesAsync(sample.Id)).Single(h => h.Title == Ps5);
        Assert.Equal((FilterStatus.Rejected, "PS5", FilterRules.Keyword), (ps5.FilterStatus, ps5.FilterCriterion, ps5.FilterModel));
        Assert.All(await app.ArticleService.GetActiveHeadlinesAsync(trusted.Id),
            h => Assert.Equal((FilterStatus.Kept, FilterRules.TrustedFeed), (h.FilterStatus, h.FilterModel)));
    }

    [Fact]
    public async Task Keywords_are_not_sent_to_the_model()
    {
        using var app = new TestApp();
        await app.EnableFilterAsync();
        await app.CriteriaService.AddAsync(CriterionKind.Keyword, "Podcast");
        var context = new Abstractions.FilterContext("", "", await app.CriteriaService.GetAllAsync(), "French");

        Assert.DoesNotContain("Podcast", FilterPrompt.System(context));
    }
}
