using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Winnow.Core.Abstractions;

namespace Winnow.Core.Filtering;

/// <summary>
/// Summarizes an article with the same local Ollama model as the filter, streaming the answer so the reader sees
/// it being written. Give it its own HttpClient: only the wait for the first token counts against its timeout.
/// </summary>
public sealed partial class OllamaSummarizer(HttpClient http) : IArticleSummarizer
{
    /// <summary>Longer articles are cut: about 4,000 words fit in the context window with the instructions and the answer.</summary>
    public const int MaxWords = 4000;

    public async Task<string> SummarizeAsync(SummaryRequest request, IProgress<string>? partial = null, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["stream"] = true,
            ["think"] = false,
            ["keep_alive"] = "10m",
            ["options"] = new JsonObject { ["temperature"] = 0.2, ["num_ctx"] = OllamaHttp.ContextLength },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = SystemPrompt(request.Language) },
                new JsonObject { ["role"] = "user", ["content"] = UserPrompt(request) }),
        };

        var message = new HttpRequestMessage(HttpMethod.Post, OllamaHttp.Url(request.Endpoint, "api/chat")) { Content = JsonContent.Create(body) };
        using var response = await OllamaHttp.SendAsync(
            () => http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct), request.Model, ct);

        // One JSON object per line: {"message":{"content":"..."},"done":false}, or {"error":"..."}.
        var text = new StringBuilder();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0)
                continue;
            JsonObject chunk;
            try
            {
                chunk = JsonNode.Parse(line) as JsonObject ?? throw OllamaHttp.NotOllama(request.Endpoint, null);
            }
            catch (JsonException ex)
            {
                throw OllamaHttp.NotOllama(request.Endpoint, ex);
            }

            if (chunk["error"]?.GetValue<string>() is { } error)
                throw new FilterResponseException($"Ollama: {error}");
            if (chunk["message"]?["content"]?.GetValue<string>() is { Length: > 0 } piece)
            {
                text.Append(piece);
                partial?.Report(text.ToString());
            }
            if (chunk["done"]?.GetValue<bool>() == true)
                break;
        }

        var summary = Tidy(text.ToString());
        return summary.Length > 0 ? summary : throw new FilterResponseException("The model wrote an empty summary.");
    }

    internal static string SystemPrompt(string language) =>
        $"""
        You summarize articles for a busy reader. Always write in {language}, even when the article is in another
        language: translate what you take from it.
        Format, in plain text (no Markdown headings, no bold):
        - first, ONE short sentence (at most 30 words) that gives the gist of the article;
        - then an empty line;
        - then 3 to 5 key points, one per line, each starting with "- ", with the facts that matter (names, numbers, dates).
        Stay faithful to the article: no opinion, nothing that is not in it, no introduction like "This article".
        """;

    /// <summary>The article as given to the model: its title and text, cut after <see cref="MaxWords"/> words.</summary>
    public static string UserPrompt(SummaryRequest request)
    {
        // Cut after MaxWords words, keeping the paragraphs.
        var text = request.Text.Trim();
        if (Word().Matches(text) is { Count: > MaxWords } words)
            text = text[..words[MaxWords].Index].TrimEnd() + " […]";
        // Small models tend to drift to the article's language and to plain paragraphs: the reminder comes last.
        return $"Title: {request.Title}\n\n{text}\n\n(Write the summary in {request.Language}: one short sentence, an empty line, then 3 to 5 lines starting with \"- \".)";
    }

    /// <summary>Removes trailing spaces and keeps at most one empty line between paragraphs.</summary>
    internal static string Tidy(string text) =>
        BlankLines().Replace(string.Join('\n', text.ReplaceLineEndings("\n").Split('\n').Select(line => line.TrimEnd())), "\n\n").Trim();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"\S+")]
    private static partial Regex Word();
}
