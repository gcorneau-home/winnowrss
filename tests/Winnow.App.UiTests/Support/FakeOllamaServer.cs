using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Winnow.App.UiTests.Support;

/// <summary>
/// Answers like Ollama's /api/chat and /api/tags, deterministically: an article mentioning "PS5" is rejected
/// for the game consoles exclusion, and so is any article containing a one-word exclusion from the prompt.
/// Streaming requests are summaries: "Summary in {language}: {title}" then two points, sent in pieces.
/// </summary>
public sealed class FakeOllamaServer : IDisposable
{
    public const string ConsolesExclusion = "Consoles de jeu (PlayStation, Xbox, Nintendo, jeux sur console)";

    private readonly HttpListener _listener = new();

    public FakeOllamaServer()
    {
        Endpoint = $"http://localhost:{FeedServer.FreePort()}/";
        _listener.Prefixes.Add(Endpoint);
        _listener.Start();
        _ = ServeAsync();
    }

    public string Endpoint { get; }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (!_listener.IsListening)
            {
                return;
            }

            var answer = context.Request.Url!.AbsolutePath switch
            {
                "/api/tags" => """{"models":[{"name":"qwen3:8b"}]}""",
                "/api/chat" => JsonNode.Parse(context.Request.InputStream)!.AsObject() is var request
                    && request["stream"]?.GetValue<bool>() == true ? Summary(request) : Chat(request),
                _ => null,
            };
            if (answer is null)
            {
                context.Response.StatusCode = 404;
            }
            else
            {
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(answer));
            }
            context.Response.Close();
        }
    }

    private static string Chat(JsonObject request)
    {
        var system = (string)request["messages"]![0]!["content"]!;
        var article = (string)request["messages"]![1]!["content"]!;

        // One-word exclusions listed in the prompt ("3. batterie").
        var exclusions = system[system.IndexOf("(exclusions)", StringComparison.Ordinal)..]
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 3 && char.IsDigit(line[0]) && line.Contains(". "))
            .Select(line => line[(line.IndexOf(". ", StringComparison.Ordinal) + 2)..])
            .Where(text => !text.Contains(' '));

        var verdict = article.Contains("PS5", StringComparison.OrdinalIgnoreCase)
            ? Answer(ConsolesExclusion, null, "Il est question de PS5.")
            : exclusions.FirstOrDefault(e => article.Contains(e, StringComparison.OrdinalIgnoreCase)) is { } word
                ? Answer(word, null, $"Il est question de {word}.")
                : Answer(null, "Nouvelles applications et logiciels", "Parle d'un logiciel.");

        return new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = verdict } }.ToJsonString();
    }

    private static string Summary(JsonObject request)
    {
        var article = (string)request["messages"]![1]!["content"]!;
        var language = article[(article.LastIndexOf("(Write the summary in ", StringComparison.Ordinal) + 22)..].Split(':')[0];
        var title = article.Split('\n')[0]["Title: ".Length..];

        string[] pieces = [$"Summary in {language}: ", title, "\n\n- First point\n", "- Second point"];
        return string.Concat(pieces.Select(piece => Line(piece, done: false) + "\n")) + Line("", done: true) + "\n";

        static string Line(string content, bool done) =>
            new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content }, ["done"] = done }.ToJsonString();
    }

    private static string Answer(string? exclusion, string? interest, string reason) =>
        new JsonObject { ["exclusion"] = exclusion, ["interest"] = interest, ["reason"] = reason }.ToJsonString();

    public void Dispose() => _listener.Close();
}
