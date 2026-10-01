using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Winnow.Core.Abstractions;
using Winnow.Core.Filtering;
using Winnow.Core.Models;
using Winnow.Core.Services;
using Winnow.Core.Tests.TestSupport;

namespace Winnow.Core.Tests;

public class SummaryTests
{
    private const string BatteryTitle = "Test de la batterie externe VoltaFlow Pro et de son refroidissement liquide apparent";

    // ----- Service and storage -----

    [Fact]
    public async Task A_summary_is_saved_per_language_and_replaced_when_written_again()
    {
        using var app = new TestApp();
        var id = await BatteryId(app);
        var partial = new List<string>();

        var french = await app.SummaryService.SummarizeAsync(id, "fr", new SyncProgress(partial));
        await app.SummaryService.SummarizeAsync(id, "es");
        app.Time.Now += TimeSpan.FromMinutes(5);
        await app.SummaryService.SummarizeAsync(id, "fr");

        Assert.Equal($"Summary in French of {BatteryTitle}", french.Text);
        Assert.Equal(["Summary in", french.Text], partial);
        var saved = await app.SummaryService.GetAllAsync(id);
        Assert.Equal(["es", "fr"], saved.Select(s => s.Language));
        Assert.Equal(FilterSettings.DefaultModel, saved[1].Model);
        Assert.Equal(app.Time.GetUtcNow(), saved[1].CreatedAt);
    }

    [Fact]
    public async Task The_summary_uses_the_filter_model_and_the_article_text()
    {
        using var app = new TestApp();
        await app.SettingsService.SetFilterSettingsAsync(new FilterSettings(false, "http://gpu-box:11434", "qwen3:14b"));
        var id = await BatteryId(app);

        await app.SummaryService.SummarizeAsync(id, "en");

        var request = Assert.Single(app.Summarizer.Requests);
        Assert.Equal(("http://gpu-box:11434", "qwen3:14b", "English"), (request.Endpoint, request.Model, request.Language));
        Assert.Contains("façade", request.Text);
        Assert.DoesNotContain("<p>", request.Text);
    }

    [Fact]
    public async Task The_last_language_asked_for_is_remembered_and_the_interface_language_comes_first()
    {
        using var app = new TestApp();
        Assert.Null(await app.SummaryService.GetPreferredLanguageAsync());

        await app.SettingsService.SetUiLanguageAsync("fr");
        Assert.Equal("fr", await app.SummaryService.GetPreferredLanguageAsync());

        await app.SummaryService.SummarizeAsync(await BatteryId(app), "es");
        Assert.Equal("es", await app.SummaryService.GetPreferredLanguageAsync());
    }

    [Fact]
    public async Task An_unreachable_model_is_reported_and_nothing_is_saved()
    {
        using var app = new TestApp();
        var id = await BatteryId(app);
        app.Summarizer.Unavailable = true;

        var error = await Assert.ThrowsAsync<WinnowException>(() => app.SummaryService.SummarizeAsync(id, "fr"));

        Assert.Equal(WinnowError.SummaryUnavailable, error.Error);
        Assert.Contains("not reachable", (string)error.Args[0]);
        Assert.Empty(await app.SummaryService.GetAllAsync(id));
    }

    [Fact]
    public async Task Purging_an_article_deletes_its_summaries_but_archiving_keeps_them()
    {
        using var app = new TestApp();
        var (_, feed) = await app.AddSampleFeedAsync();
        var headlines = await app.ArticleService.GetActiveHeadlinesAsync(feed.Id);
        var (purged, archived) = (headlines[0].Id, headlines[1].Id);
        await app.SummaryService.SummarizeAsync(purged, "fr");
        await app.SummaryService.SummarizeAsync(archived, "fr");

        await app.Articles.PurgeAsync([purged]);
        await app.ArchiveService.ArchiveAsync([archived]);

        Assert.Empty(await app.SummaryService.GetAllAsync(purged));
        Assert.Single(await app.SummaryService.GetAllAsync(archived));
        await Assert.ThrowsAsync<WinnowException>(() => app.SummaryService.SummarizeAsync(purged, "fr")); // no text left
    }

    // ----- Ollama -----

    [Fact]
    public async Task Ollama_streams_the_summary_with_the_language_and_context_size()
    {
        var ollama = new StreamingOllama("La batterie", " VoltaFlow Pro.\n  \n\n- Point un  ", "\n- Point deux\n");
        var partial = new List<string>();

        var text = await new OllamaSummarizer(new HttpClient(ollama))
            .SummarizeAsync(Request("French", "Un texte."), new SyncProgress(partial));

        Assert.Equal("La batterie VoltaFlow Pro.\n\n- Point un\n- Point deux", text);
        Assert.Equal(3, partial.Count);
        Assert.Equal("http://localhost:11434/api/chat", ollama.LastUri);
        Assert.True(ollama.LastBody!["stream"]!.GetValue<bool>());
        Assert.False(ollama.LastBody["think"]!.GetValue<bool>());
        Assert.Equal(8192, ollama.LastBody["options"]!["num_ctx"]!.GetValue<int>());
        Assert.Contains("Always write in French", ollama.LastBody["messages"]![0]!["content"]!.GetValue<string>());
        Assert.StartsWith("Title: Titre\n\nUn texte.\n\n(Write the summary in French:", ollama.LastBody["messages"]![1]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void Long_articles_are_cut_but_keep_their_paragraphs()
    {
        var text = "Premier paragraphe.\n\n" + string.Join(' ', Enumerable.Repeat("mot", OllamaSummarizer.MaxWords + 50));

        var prompt = OllamaSummarizer.UserPrompt(Request("French", text));

        Assert.StartsWith("Title: Titre\n\nPremier paragraphe.\n\nmot mot", prompt);
        Assert.EndsWith("mot […]\n\n(Write the summary in French: one short sentence, an empty line, then 3 to 5 lines starting with \"- \".)", prompt);
        Assert.Equal(OllamaSummarizer.MaxWords,
            prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w is "mot" or "Premier" or "paragraphe."));
    }

    [Fact]
    public async Task Ollama_errors_are_reported()
    {
        var midStream = new OllamaSummarizer(new HttpClient(new StreamingOllama("Début") { ErrorLine = "model crashed" }));
        var empty = new OllamaSummarizer(new HttpClient(new StreamingOllama()));
        var missing = new OllamaSummarizer(new HttpClient(new StreamingOllama { Status = HttpStatusCode.NotFound }));
        var html = new OllamaSummarizer(new HttpClient(new StreamingOllama { RawBody = "<html>Proxy</html>" }));

        Assert.Contains("model crashed", (await Assert.ThrowsAsync<FilterResponseException>(() => midStream.SummarizeAsync(Request()))).Message);
        await Assert.ThrowsAsync<FilterResponseException>(() => empty.SummarizeAsync(Request()));
        Assert.Contains("ollama pull qwen3:8b", (await Assert.ThrowsAsync<FilterUnavailableException>(() => missing.SummarizeAsync(Request()))).Message);
        await Assert.ThrowsAsync<FilterUnavailableException>(() => html.SummarizeAsync(Request()));
    }

    private static SummaryRequest Request(string language = "French", string text = "Un texte.") =>
        new(FilterSettings.DefaultEndpoint, FilterSettings.DefaultModel, "Titre", text, language);

    private static async Task<long> BatteryId(TestApp app)
    {
        var (_, feed) = await app.AddSampleFeedAsync();
        return (await app.ArticleService.GetActiveHeadlinesAsync(feed.Id)).Single(h => h.Title == BatteryTitle).Id;
    }

    private sealed class SyncProgress(List<string> reports) : IProgress<string>
    {
        public void Report(string value) => reports.Add(value);
    }

    /// <summary>Answers /api/chat like Ollama in streaming mode: one JSON object per line.</summary>
    private sealed class StreamingOllama(params string[] pieces) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string? ErrorLine { get; init; }
        public string? RawBody { get; init; }
        public string? LastUri { get; private set; }
        public JsonObject? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri!.AbsoluteUri;
            LastBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();

            var lines = new StringBuilder();
            foreach (var piece in pieces)
                lines.AppendLine(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = piece }, ["done"] = false }.ToJsonString());
            lines.AppendLine(ErrorLine is { } error
                ? new JsonObject { ["error"] = error }.ToJsonString()
                : new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = "" }, ["done"] = true }.ToJsonString());
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(RawBody ?? lines.ToString(), Encoding.UTF8, "application/x-ndjson"),
            };
        }
    }
}
