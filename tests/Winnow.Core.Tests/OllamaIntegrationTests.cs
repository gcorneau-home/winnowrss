using Winnow.Core.Abstractions;
using Winnow.Core.Filtering;
using Winnow.Core.Models;
using Winnow.Core.Tests.TestSupport;

namespace Winnow.Core.Tests;

/// <summary>Runs only when WINNOWRSS_OLLAMA_TESTS=1, against a local Ollama with the default model pulled.</summary>
public sealed class OllamaFactAttribute : FactAttribute
{
    public OllamaFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WINNOWRSS_OLLAMA_TESTS") != "1")
            Skip = "Set WINNOWRSS_OLLAMA_TESTS=1 to run against a local Ollama.";
    }
}

[Trait("Category", "Ollama")]
public class OllamaIntegrationTests
{
    [OllamaFact]
    public async Task The_default_model_applies_the_starting_criteria_to_the_sample_articles()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var filter = new OllamaArticleFilter(new HttpClient { Timeout = TimeSpan.FromMinutes(3) });
        var context = new FilterContext(FilterSettings.DefaultEndpoint, FilterSettings.DefaultModel,
            await app.CriteriaService.GetAllAsync(), "French");

        async Task<FilterDecision> Judge(string titleStart)
        {
            var headline = (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id)).Single(h => h.Title.StartsWith(titleStart));
            return await filter.EvaluateAsync(FilterInput.From((await app.Articles.GetAsync(headline.Id))!), context);
        }

        var ps5 = await Judge("Rebound");
        Assert.False(ps5.Keep);
        Assert.StartsWith("Consoles de jeu", ps5.Criterion);
        Assert.False(string.IsNullOrWhiteSpace(ps5.Reason));

        Assert.True((await Judge("Firefox 157")).Keep);
    }
}
