using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Dapper;
using Winnow.Core.Abstractions;
using Winnow.Core.Filtering;
using Winnow.Core.Models;
using Winnow.Core.Services;
using Winnow.Core.Tests.TestSupport;

namespace Winnow.Core.Tests;

public class FilterTests
{
    private const string Ps5 = "Rebound, le jailbreak qui débloque presque toutes les PS5";

    private static readonly FilterContext Context = new("http://localhost:11434", "qwen3:8b",
    [
        new FilterCriterion { Kind = CriterionKind.Interest, Text = "Programmation C# et .NET", SortOrder = 2 },
        new FilterCriterion { Kind = CriterionKind.Interest, Text = "Programmation Python", SortOrder = 1 },
        new FilterCriterion { Kind = CriterionKind.Interest, Text = "Désactivé", Enabled = false, SortOrder = 3 },
        new FilterCriterion { Kind = CriterionKind.Exclusion, Text = "Consoles de jeu", SortOrder = 4 },
    ], "French");

    // ----- What the model reads -----

    [Fact]
    public void Input_drops_a_description_that_repeats_the_start_of_the_text()
    {
        var input = FilterInput.From(new Article
        {
            Title = " Titre ",
            Summary = "Depuis quelques jours, la scène PlayStation 5 ne parle que de…",
            ContentText = "Depuis quelques jours, la scène PlayStation 5 ne parle que de Rebound !",
        });

        Assert.Equal("Titre", input.Title);
        Assert.Null(input.Description);
        Assert.StartsWith("Depuis quelques jours", input.Excerpt);
    }

    [Fact]
    public void Input_excerpt_is_capped_in_words_and_characters()
    {
        var words = string.Join(' ', Enumerable.Range(1, 400).Select(i => $"mot{i}"));
        var longWords = string.Join(' ', Enumerable.Repeat(new string('x', 40), 100));

        var byWords = FilterInput.From(new Article { Title = "t", Summary = "Autre chose", ContentText = words });
        var byChars = FilterInput.From(new Article { Title = "t", ContentText = longWords });

        Assert.Equal("Autre chose", byWords.Description);
        Assert.EndsWith("mot150 …", byWords.Excerpt);
        Assert.True(byChars.Excerpt!.Length <= FilterInput.ExcerptMaxChars + 2);
    }

    [Fact]
    public void Prompt_lists_enabled_criteria_in_order_and_asks_for_the_reason_language()
    {
        var system = FilterPrompt.System(Context);

        Assert.Contains("1. Programmation Python\n2. Programmation C# et .NET", system.ReplaceLineEndings("\n"));
        Assert.Contains("1. Consoles de jeu", system);
        Assert.DoesNotContain("Désactivé", system);
        Assert.Contains("in French", system);
        Assert.Contains("When in doubt, name the closest one", system);
        Assert.Contains("\"exclusion\"", system);

        var user = FilterPrompt.User(new FilterInput("Titre", null, "Début"));
        Assert.Equal("Title: Titre\nBeginning of the article: Début", user.ReplaceLineEndings("\n"));
    }

    // ----- Decision rule -----

    [Theory]
    [InlineData("Consoles de jeu", "Programmation Python", false, "Consoles de jeu")]           // an exclusion wins over an interest
    [InlineData(null, "2. programmation c# et .net", true, "Programmation C# et .NET")]        // numbering and case cleaned up
    [InlineData(null, null, false, null)]                                                      // relates to no interest
    [InlineData("null", "null", false, null)]                                                  // "null" as text
    [InlineData("Jeux vidéo et e-sport", "Programmation Python", true, "Programmation Python")] // made-up exclusion is ignored
    [InlineData("Désactivé", null, false, null)]                                               // disabled criteria do not count
    [InlineData(null, "Sécurité informatique", true, "Sécurité informatique")]                 // an unlisted interest still keeps (generous)
    public void The_code_decides_from_the_model_answers(string? exclusion, string? interest, bool keep, string? criterion)
    {
        var decision = new FilterAnswer(exclusion, interest, "raison").Decide(Context);

        Assert.Equal(new FilterDecision(keep, criterion, "raison"), decision);
    }

    // ----- Ollama client -----

    [Fact]
    public async Task Ollama_request_asks_for_structured_output_without_thinking()
    {
        var handler = new FakeOllama("""{"exclusion": "1. consoles de jeu", "interest": null, "reason": "Parle de PS5."}""");
        var filter = new OllamaArticleFilter(new HttpClient(handler));

        var decision = await filter.EvaluateAsync(new FilterInput(Ps5, null, null), Context);

        Assert.Equal(new FilterDecision(false, "Consoles de jeu", "Parle de PS5."), decision);
        Assert.Equal("http://localhost:11434/api/chat", handler.LastUri);
        var request = handler.LastBody!;
        Assert.Equal("qwen3:8b", (string?)request["model"]);
        Assert.False((bool)request["think"]!);
        Assert.False((bool)request["stream"]!);
        Assert.Equal(0, (int)request["options"]!["temperature"]!);
        Assert.Equal(["exclusion", "interest", "reason"], request["format"]!["required"]!.AsArray().Select(n => (string?)n));
        Assert.Contains(Ps5, (string?)request["messages"]![1]!["content"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"interest": null, "reason": "no exclusion field"}""")]
    [InlineData("""{"exclusion": 3, "interest": null, "reason": ""}""")]
    public async Task Unusable_answers_are_response_errors(string content)
    {
        var filter = new OllamaArticleFilter(new HttpClient(new FakeOllama(content)));

        await Assert.ThrowsAsync<FilterResponseException>(() => filter.EvaluateAsync(new FilterInput("t", null, null), Context));
    }

    [Fact]
    public async Task Unreachable_server_or_missing_model_means_unavailable()
    {
        var down = new OllamaArticleFilter(new HttpClient(new FakeOllama(null) { Fail = true }));
        var missingModel = new OllamaArticleFilter(new HttpClient(new FakeOllama(null) { Status = HttpStatusCode.NotFound }));

        await Assert.ThrowsAsync<FilterUnavailableException>(() => down.EvaluateAsync(new FilterInput("t", null, null), Context));
        var error = await Assert.ThrowsAsync<FilterUnavailableException>(() => missingModel.EvaluateAsync(new FilterInput("t", null, null), Context));
        Assert.Contains("ollama pull qwen3:8b", error.Message);
    }

    [Fact]
    public async Task A_server_that_does_not_answer_like_Ollama_means_unavailable()
    {
        var html = new OllamaArticleFilter(new HttpClient(new FakeOllama(null) { RawBody = "<html>Proxy login</html>" }));
        var wrongShape = new OllamaArticleFilter(new HttpClient(new FakeOllama(null) { RawBody = """{"message": "hello"}""" }));

        var error = await Assert.ThrowsAsync<FilterUnavailableException>(() => html.EvaluateAsync(new FilterInput("t", null, null), Context));
        Assert.Contains("did not answer like an Ollama server", error.Message);
        await Assert.ThrowsAsync<FilterUnavailableException>(() => wrongShape.EvaluateAsync(new FilterInput("t", null, null), Context));
        await Assert.ThrowsAsync<FilterUnavailableException>(() => html.ListModelsAsync("http://localhost:11434"));
    }

    [Fact]
    public async Task Cancelling_is_not_reported_as_an_outage()
    {
        var filter = new OllamaArticleFilter(new HttpClient(new FakeOllama("{}") { Delay = TimeSpan.FromSeconds(30) }));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => filter.EvaluateAsync(new FilterInput("t", null, null), Context, cancel.Token));
    }

    [Fact]
    public async Task Installed_models_are_listed()
    {
        var handler = new FakeOllama(null) { RawBody = """{"models":[{"name":"qwen3:8b"},{"name":"llama3.1:8b"}]}""" };
        var filter = new OllamaArticleFilter(new HttpClient(handler));

        Assert.Equal(["llama3.1:8b", "qwen3:8b"], await filter.ListModelsAsync("http://localhost:11434/"));
        Assert.Equal("http://localhost:11434/api/tags", handler.LastUri);
    }

    // ----- Criteria -----

    [Fact]
    public async Task Starting_criteria_are_seeded_and_every_change_bumps_the_version()
    {
        using var app = new TestApp();
        var criteria = await app.CriteriaService.GetAllAsync();
        Assert.Equal(6, criteria.Count(c => c.Kind == CriterionKind.Interest));
        Assert.Contains(criteria, c => c.Kind == CriterionKind.Exclusion && c.Text.StartsWith("Consoles de jeu"));
        Assert.Contains(criteria, c => c.Kind == CriterionKind.Exclusion && c.Text.StartsWith("Apple et Mac"));
        var version = await app.SettingsService.GetFilterVersionAsync();

        var id = await app.CriteriaService.AddAsync(CriterionKind.Exclusion, "  Matériel et gadgets ");
        await app.CriteriaService.UpdateAsync(id, "Gadgets", enabled: false);
        await app.CriteriaService.DeleteAsync(id);

        Assert.Equal(version + 3, await app.SettingsService.GetFilterVersionAsync());
        var empty = await Assert.ThrowsAsync<WinnowException>(() => app.CriteriaService.AddAsync(CriterionKind.Interest, " "));
        Assert.Equal(WinnowError.CriterionEmpty, empty.Error);
    }

    // ----- Queue -----

    [Fact]
    public async Task Queue_judges_pending_articles_and_stores_the_verdict()
    {
        using var app = new TestApp();
        await app.EnableFilterAsync();
        await app.SettingsService.SetUiLanguageAsync("fr");
        app.Filter.RejectKeywords["PS5"] = "Consoles de jeu (PlayStation, Xbox, Nintendo, jeux sur console)";
        var (_, feed) = await app.AddSampleFeedAsync();
        var progress = new List<FilterProgress>();

        var result = await app.FilterQueue.RunPendingAsync(new Progress(progress));

        Assert.Equal(new FilterRunResult(2, 1, 0, null), result);
        Assert.Equal([2, 1, 0], progress.Select(p => p.Remaining));
        Assert.Equal("French", app.Filter.Contexts[0].ReasonLanguage);
        Assert.Equal(9, app.Filter.Contexts[0].Criteria.Count);

        var ps5 = (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id)).Single(h => h.Title == Ps5);
        var stored = (await app.Articles.GetAsync(ps5.Id))!;
        Assert.Equal(FilterStatus.Rejected, stored.FilterStatus);
        Assert.StartsWith("Consoles de jeu", stored.FilterCriterion);
        Assert.Equal("Il est question de PS5.", stored.FilterReason);
        Assert.Equal("qwen3:8b", stored.FilterModel);
        Assert.Equal(await app.SettingsService.GetFilterVersionAsync(), stored.FilterVersion);
        Assert.Equal(app.Time.Now, stored.FilteredAt);

        // Rejected articles stay in the feed but do not count as unread.
        Assert.Equal(3, (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id)).Count);
        Assert.Equal(2, (await app.ArticleService.GetUnreadCountsAsync())[feed.Id]);
    }

    [Fact]
    public async Task Queue_stops_when_the_model_is_unavailable_and_resumes_later()
    {
        using var app = new TestApp();
        await app.EnableFilterAsync();
        var (_, feed) = await app.AddSampleFeedAsync();
        app.Filter.Unavailable = true;

        var down = await app.FilterQueue.RunPendingAsync();

        Assert.Equal("Ollama is not reachable", down.Unavailable);
        Assert.Single(app.Filter.Inputs); // no retry loop
        Assert.Equal(3, await app.Articles.CountPendingAsync());

        app.Filter.Unavailable = false;
        Assert.Equal(3, (await app.FilterQueue.RunPendingAsync()).Kept);
        Assert.Equal(0, await app.Articles.CountPendingAsync());
    }

    [Fact]
    public async Task An_unusable_answer_marks_that_article_in_error_and_the_others_continue()
    {
        using var app = new TestApp();
        await app.EnableFilterAsync();
        var (_, feed) = await app.AddSampleFeedAsync();
        app.Filter.GarbageFor = "Firefox";

        var result = await app.FilterQueue.RunPendingAsync();

        Assert.Equal(new FilterRunResult(2, 0, 1, null), result);
        var firefox = (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id)).Single(h => h.Title.StartsWith("Firefox"));
        Assert.Equal(FilterStatus.Error, firefox.FilterStatus);
        Assert.StartsWith("Unreadable answer", firefox.FilterReason);
    }

    [Fact]
    public async Task Nothing_is_judged_while_the_filter_is_off()
    {
        using var app = new TestApp();
        await app.AddSampleFeedAsync();
        await app.Articles.RequeueUnreadAsync();

        Assert.Equal(FilterRunResult.Nothing, await app.FilterQueue.RunPendingAsync());
        Assert.Empty(app.Filter.Inputs);
    }

    [Fact]
    public async Task Requeue_rejudges_unread_articles_with_the_new_criteria()
    {
        using var app = new TestApp();
        await app.EnableFilterAsync();
        var (_, feed) = await app.AddSampleFeedAsync();
        await app.FilterQueue.RunPendingAsync();
        var read = (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id))[0].Id;
        await app.ArticleService.SetReadAsync([read], true);

        await app.CriteriaService.AddAsync(CriterionKind.Exclusion, "Matériel et gadgets");
        app.Filter.RejectKeywords["batterie"] = "Matériel et gadgets";
        Assert.Equal(2, await app.FilterQueue.RequeueUnreadAsync());
        await app.FilterQueue.RunPendingAsync();

        var headlines = await app.ArticleService.GetActiveHeadlinesAsync(feed.Id);
        var battery = headlines.Single(h => h.Title.StartsWith("Test de la batterie"));
        Assert.Equal(battery.Id == read ? FilterStatus.Kept : FilterStatus.Rejected, battery.FilterStatus);
        Assert.Equal("Matériel et gadgets", app.Filter.Contexts[^1].Criteria.Last(c => c.Kind == CriterionKind.Exclusion).Text);
    }

    [Fact]
    public async Task Criteria_changed_during_a_run_apply_to_every_requeued_article()
    {
        using var app = new TestApp();
        await app.EnableFilterAsync();
        await app.AddSampleFeedAsync();
        app.Filter.DuringFirstEvaluation = async () =>
        {
            // The user edits the criteria and asks to refilter while the queue is busy.
            await app.CriteriaService.AddAsync(CriterionKind.Exclusion, "Matériel et gadgets");
            await app.FilterQueue.RequeueUnreadAsync();
            Assert.Equal(FilterRunResult.Nothing, await app.FilterQueue.RunPendingAsync()); // already running
        };

        await app.FilterQueue.RunPendingAsync();

        var latest = await app.SettingsService.GetFilterVersionAsync();
        Assert.Equal(0, await app.Articles.CountPendingAsync());
        var versions = await app.Connection.QueryAsync<int>("SELECT filter_version FROM articles");
        Assert.All(versions, v => Assert.Equal(latest, v));
        Assert.Contains(app.Filter.Contexts[^1].Criteria, c => c.Text == "Matériel et gadgets");
    }

    [Fact]
    public async Task Unread_rejected_articles_are_purged_after_the_retention_period()
    {
        using var app = new TestApp();
        await app.EnableFilterAsync();
        app.Filter.RejectKeywords["PS5"] = "Consoles de jeu";
        var (_, feed) = await app.AddSampleFeedAsync();
        await app.FilterQueue.RunPendingAsync();

        app.Time.Now = app.Time.Now.AddDays(31);
        Assert.Equal(1, await app.RetentionService.RunAsync());

        var remaining = await app.ArticleService.GetActiveHeadlinesAsync(feed.Id);
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, h => h.Title == Ps5);
    }

    /// <summary>Synchronous IProgress, so the reported values can be asserted right after the run.</summary>
    private sealed class Progress(List<FilterProgress> reports) : IProgress<FilterProgress>
    {
        public void Report(FilterProgress value) => reports.Add(value);
    }

    /// <summary>Answers like Ollama's /api/chat (the verdict is the message content) or /api/tags.</summary>
    private sealed class FakeOllama(string? content) : HttpMessageHandler
    {
        public bool Fail { get; init; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string? RawBody { get; init; }
        public TimeSpan Delay { get; init; }
        public string? LastUri { get; private set; }
        public JsonObject? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri!.AbsoluteUri;
            if (request.Content is not null)
                LastBody = JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!.AsObject();
            if (Fail)
                throw new HttpRequestException("Connection refused");
            await Task.Delay(Delay, ct);

            var body = RawBody ?? new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content } }.ToJsonString();
            return new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
