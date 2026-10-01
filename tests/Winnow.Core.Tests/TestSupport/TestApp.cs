using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.Core.Abstractions;
using Winnow.Core.Feeds;
using Winnow.Core.Filtering;
using Winnow.Core.Models;
using Winnow.Core.Services;
using Winnow.Data;

namespace Winnow.Core.Tests.TestSupport;

/// <summary>Services wired to a private in-memory database and a fake fetcher.</summary>
public sealed class TestApp : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public TestApp()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"test-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        // A shared in-memory database lives as long as one connection stays open.
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();

        Database = new WinnowDatabase(connectionString);
        Database.Migrate();

        Categories = new CategoryRepository(Database);
        Feeds = new FeedRepository(Database);
        Articles = new ArticleRepository(Database);

        SettingsService = new SettingsService(new SettingsRepository(Database));
        Refresher = new FeedRefreshService(Feeds, Articles, Fetcher, SettingsService, Time,
            NullLogger<FeedRefreshService>.Instance);
        CategoryService = new CategoryService(Categories);
        FeedService = new FeedService(Feeds, Fetcher, Refresher);
        ArticleService = new ArticleService(Articles, Time);
        CriteriaService = new FilterCriteriaService(new FilterCriteriaRepository(Database), SettingsService);
        FilterQueue = new FilterQueueService(Articles, Feeds, Filter, CriteriaService, SettingsService, Time,
            NullLogger<FilterQueueService>.Instance);
        ArchiveService = new ArchiveService(Articles, Images, Time, NullLogger<ArchiveService>.Instance);
        SearchService = new SearchService(Articles);
        RetentionService = new RetentionService(Articles, RetentionPolicy.Default, Time, NullLogger<RetentionService>.Instance);
        SummaryService = new SummaryService(Articles, Summarizer, SettingsService, Time);
    }

    public WinnowDatabase Database { get; }
    public SqliteConnection Connection => _keepAlive;
    public ManualTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
    public FakeFeedFetcher Fetcher { get; } = new();

    public CategoryRepository Categories { get; }
    public FeedRepository Feeds { get; }
    public ArticleRepository Articles { get; }

    public FeedRefreshService Refresher { get; }
    public CategoryService CategoryService { get; }
    public FeedService FeedService { get; }
    public ArticleService ArticleService { get; }
    public SettingsService SettingsService { get; }
    public FilterCriteriaService CriteriaService { get; }
    public FilterQueueService FilterQueue { get; }
    public FakeArticleFilter Filter { get; } = new();

    /// <summary>Turns the filter on (as the user would in the Filter window).</summary>
    public Task EnableFilterAsync() =>
        SettingsService.SetFilterSettingsAsync(new FilterSettings(true, FilterSettings.DefaultEndpoint, FilterSettings.DefaultModel));
    public ArchiveService ArchiveService { get; }
    public SearchService SearchService { get; }
    public RetentionService RetentionService { get; }
    public SummaryService SummaryService { get; }
    public FakeSummarizer Summarizer { get; } = new();
    public FakeResourceFetcher Images { get; } = new();

    /// <summary>Creates a category and subscribes to the sample feed fixture in it.</summary>
    public async Task<(long CategoryId, Feed Feed)> AddSampleFeedAsync(string category = "Tech")
    {
        var categoryId = await CategoryService.AddAsync(category);
        Fetcher.Respond(Fixtures.SampleFeedUrl, Fixtures.SampleFeed());
        var feed = await FeedService.AddAsync(categoryId, Fixtures.SampleFeedUrl);
        return (categoryId, feed);
    }

    public void Dispose() => _keepAlive.Dispose();
}

public sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class FakeFeedFetcher : IFeedFetcher
{
    private readonly Dictionary<string, Func<FeedFetchResult>> _responses = new();

    public void Respond(string url, ParsedFeed feed) =>
        _responses[new Uri(url).AbsoluteUri] = () => new FeedFetchResult(feed, "\"etag\"", null);

    public void Fail(string url, Exception exception) =>
        _responses[new Uri(url).AbsoluteUri] = () => throw exception;

    /// <summary>When set, every fetch waits for it before answering.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public async Task<FeedFetchResult> FetchAsync(Feed feed, CancellationToken ct = default)
    {
        if (Gate is not null)
            await Gate.Task;
        return _responses[feed.Url]();
    }
}

/// <summary>Serves a tiny image for every URL, except those marked as missing.</summary>
public sealed class FakeResourceFetcher : IResourceFetcher
{
    public HashSet<string> Missing { get; } = [];

    public Task<FetchedResource?> FetchImageAsync(string url, CancellationToken ct = default) =>
        Task.FromResult(Missing.Contains(url) ? null : new FetchedResource("image/png", [1, 2, 3]));
}

/// <summary>Rejects articles whose title contains a keyword; can simulate an unreachable or confused model.</summary>
/// <summary>Writes "Summary in {language} of {title}" in two pieces, or fails like an unreachable Ollama.</summary>
public sealed class FakeSummarizer : IArticleSummarizer
{
    public bool Unavailable { get; set; }
    public List<SummaryRequest> Requests { get; } = [];

    public Task<string> SummarizeAsync(SummaryRequest request, IProgress<string>? partial = null, CancellationToken ct = default)
    {
        Requests.Add(request);
        if (Unavailable)
            throw new FilterUnavailableException("Ollama is not reachable: connection refused");
        var text = $"Summary in {request.Language} of {request.Title}";
        partial?.Report(text[..10]);
        partial?.Report(text);
        return Task.FromResult(text);
    }
}

public sealed class FakeArticleFilter : IArticleFilter
{
    public Dictionary<string, string> RejectKeywords { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Unavailable { get; set; }
    public string? GarbageFor { get; set; }
    public List<FilterContext> Contexts { get; } = [];
    public List<FilterInput> Inputs { get; } = [];

    /// <summary>Runs once, while the first article is being judged (to simulate the user acting meanwhile).</summary>
    public Func<Task>? DuringFirstEvaluation { get; set; }

    public async Task<FilterDecision> EvaluateAsync(FilterInput input, FilterContext context, CancellationToken ct = default)
    {
        Contexts.Add(context);
        Inputs.Add(input);
        if (DuringFirstEvaluation is { } action)
        {
            DuringFirstEvaluation = null;
            await action();
        }
        return Decide(input);
    }

    private FilterDecision Decide(FilterInput input)
    {
        if (Unavailable)
            throw new FilterUnavailableException("Ollama is not reachable");
        if (GarbageFor is not null && input.Title.Contains(GarbageFor, StringComparison.OrdinalIgnoreCase))
            throw new FilterResponseException("Unreadable answer from the model: ???");

        var match = RejectKeywords.FirstOrDefault(k => input.Title.Contains(k.Key, StringComparison.OrdinalIgnoreCase));
        return match.Key is null
            ? new FilterDecision(true, "Nouvelles applications et logiciels", "Parle d'une application.")
            : new FilterDecision(false, match.Value, $"Il est question de {match.Key}.");
    }
}

public static class Fixtures
{
    public const string SampleFeedUrl = "https://fil-bidouille.example/feedfull.xml";

    public static ParsedFeed SampleFeed()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-feed.xml"));
        return FeedParser.Parse(stream, new Uri(SampleFeedUrl));
    }

    public static ParsedFeed Parse(string xml, string url = "https://example.com/feed") =>
        FeedParser.Parse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)), new Uri(url));
}
