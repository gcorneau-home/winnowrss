using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Winnow.Core.Abstractions;
using Winnow.Core.Filtering;
using Winnow.Core.Models;
using Winnow.Data;
using Winnow.FilterBench;

// Compares filter configurations (model × prompt × input) against the user's thumbs:
// 👍 = should be kept, 👎 = should be filtered out. Reads the database, never writes to it.

const string Usage = """
    Usage: Winnow.FilterBench [options]

      --db <path>          Database to read (default: %LOCALAPPDATA%\WinnowRSS\winnow.db)
      --models <a,b>       Ollama models (default: the model set in the Filter window)
      --prompts <a,b|all>  Prompt variants: default, broad-exclusions, strict-interests, broad+strict (default: all)
      --inputs <a,b|all>   Input variants: title, title+description, excerpt, long-excerpt (default: excerpt)
      --limit <n>          Use at most n rated articles (newest first)
      --out <file>         Markdown report (default: filter-bench-<date>.md in the current folder)
      --endpoint <url>     Ollama address (default: the one set in the Filter window)
    """;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(Usage);
    return 0;
}

var options = Args(args);
var dbPath = options.GetValueOrDefault("db") ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinnowRSS", "winnow.db");
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Database not found: {dbPath}");
    return 1;
}

// Read-only: the bench must never change the user's data.
var db = new WinnowDatabase(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
var settings = new Winnow.Core.Services.SettingsService(new SettingsRepository(db));
var filterSettings = await settings.GetFilterSettingsAsync();
var criteria = await new FilterCriteriaRepository(db).GetAllAsync();
var feedsById = (await new FeedRepository(db).GetAllAsync()).ToDictionary(f => f.Id);
var language = await settings.GetUiLanguageAsync() == "fr" ? "French" : "English";

var rated = await new ArticleRepository(db).GetRatedAsync();
if (options.GetValueOrDefault("limit") is { } limit)
    rated = rated.Take(int.Parse(limit, CultureInfo.InvariantCulture)).ToList();

var liked = rated.Count(a => a.Rating == Rating.Up);
var disliked = rated.Count - liked;
Console.WriteLine($"{rated.Count} rated article(s): {liked} 👍, {disliked} 👎 — {dbPath}");
if (rated.Count == 0)
{
    Console.WriteLine("Rate articles with 👍 (you want to see it) or 👎 (you would rather not) in WinnowRSS, then run the bench again.");
    return 1;
}
if (liked < 10 || disliked < 10)
    Console.WriteLine("⚠️  Fewer than 10 👍 or 👎: the results will not say much yet. Aim for 100+ ratings with both kinds.");

var endpoint = options.GetValueOrDefault("endpoint") ?? filterSettings.Endpoint;
var models = List(options.GetValueOrDefault("models")) ?? [filterSettings.Model];
var prompts = Pick(options.GetValueOrDefault("prompts"), PromptVariant.All, p => p.Name) ?? PromptVariant.All;
var inputs = Pick(options.GetValueOrDefault("inputs"), InputVariant.All, i => i.Name) ?? [InputVariant.All.Single(i => i.Name == "excerpt")];

var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
var results = new Dictionary<string, IReadOnlyList<BenchResult>>();
var scores = new List<BenchScore>();

try
{
    foreach (var model in models) // model outermost: switching models means loading one into memory
    {
        var context = new FilterContext(endpoint, model, criteria, language);
        Console.Write($"Loading {model}… ");
        await new OllamaArticleFilter(http).EvaluateAsync(new FilterInput("warm-up", null, null), context);
        Console.WriteLine("ready.");

        foreach (var prompt in prompts)
        foreach (var input in inputs)
        {
            var name = $"{model} · {prompt.Name} · {input.Name}";
            var filter = new OllamaArticleFilter(http, prompt.Options);
            var runs = new List<BenchResult>();
            Console.Write($"{name} ");
            foreach (var article in rated)
            {
                runs.Add(await JudgeAsync(filter, article, feedsById.GetValueOrDefault(article.FeedId), input, context));
                Console.Write(runs[^1] switch { { IsError: true } => "!", { IsCorrect: true } => ".", _ => "x" });
            }
            Console.WriteLine();
            results[name] = runs;
            scores.Add(BenchScore.From(name, runs));
        }
    }
}
catch (FilterUnavailableException ex)
{
    Console.Error.WriteLine($"Ollama unavailable: {ex.Message}");
    if (scores.Count == 0)
        return 1;
}

var ranked = BenchScore.Rank(scores);
Console.WriteLine();
Console.Write(BenchReport.Table(ranked));

var outPath = options.GetValueOrDefault("out") ?? $"filter-bench-{DateTime.Now:yyyyMMdd-HHmm}.md";
var header = $"{DateTime.Now:yyyy-MM-dd HH:mm} — {rated.Count} rated article(s) ({liked} 👍, {disliked} 👎), " +
    $"{criteria.Count(c => c.Enabled)} active criteria, reasons in {language}.";
await File.WriteAllTextAsync(outPath, BenchReport.Markdown(ranked, results, header));
Console.WriteLine($"\nReport with every mistake: {Path.GetFullPath(outPath)}");
return 0;

static async Task<BenchResult> JudgeAsync(OllamaArticleFilter filter, Article article, Feed? feed, InputVariant input, FilterContext context)
{
    var shouldKeep = article.Rating == Rating.Up;
    var watch = Stopwatch.StartNew();

    // The same rules as the app (trusted feeds, keywords) come before the model.
    if (FilterRules.Apply(article, feed, context.Criteria) is var (ruled, rule))
        return new BenchResult(article.Id, article.Title, shouldKeep, ruled, watch.Elapsed, Rule: rule);

    try
    {
        var decision = await filter.EvaluateAsync(input.Build(article), context);
        return new BenchResult(article.Id, article.Title, shouldKeep, decision, watch.Elapsed);
    }
    catch (FilterResponseException ex)
    {
        return new BenchResult(article.Id, article.Title, shouldKeep, null, watch.Elapsed, ex.Message);
    }
}

static Dictionary<string, string> Args(string[] args)
{
    var parsed = new Dictionary<string, string>();
    for (var i = 0; i + 1 < args.Length; i++)
        if (args[i].StartsWith("--", StringComparison.Ordinal))
            parsed[args[i][2..]] = args[++i];
    return parsed;
}

static List<string>? List(string? value) =>
    value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

static IReadOnlyList<T>? Pick<T>(string? value, IReadOnlyList<T> all, Func<T, string> name)
{
    if (value is null || value == "all")
        return value is null ? null : all;
    var picked = List(value)!.Select(v => all.FirstOrDefault(x => name(x) == v)
        ?? throw new ArgumentException($"Unknown variant \"{v}\". Known: {string.Join(", ", all.Select(name))}")).ToList();
    return picked;
}
