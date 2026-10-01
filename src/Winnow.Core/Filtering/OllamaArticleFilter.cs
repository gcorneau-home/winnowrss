using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Winnow.Core.Abstractions;

namespace Winnow.Core.Filtering;

/// <summary>
/// Asks a local Ollama model for a verdict through its native chat API. Give it its own HttpClient with a long
/// timeout: the first call after a while loads the model into memory, which can take a minute.
/// </summary>
public sealed class OllamaArticleFilter(HttpClient http, FilterPromptOptions? prompt = null) : IArticleFilter
{
    public async Task<FilterDecision> EvaluateAsync(FilterInput input, FilterContext context, CancellationToken ct = default)
    {
        var request = new JsonObject
        {
            ["model"] = context.Model,
            ["stream"] = false,
            ["think"] = false, // Qwen3 would otherwise spend the answer on hidden reasoning
            ["keep_alive"] = "10m",
            ["format"] = FilterPrompt.Schema(),
            ["options"] = new JsonObject { ["temperature"] = 0, ["num_ctx"] = OllamaHttp.ContextLength },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = FilterPrompt.System(context, prompt) },
                new JsonObject { ["role"] = "user", ["content"] = FilterPrompt.User(input) }),
        };

        using var response = await OllamaHttp.SendAsync(() => http.PostAsJsonAsync(OllamaHttp.Url(context.Endpoint, "api/chat"), request, ct), context.Model, ct);
        var body = await OllamaHttp.ReadBodyAsync(response, context.Endpoint, ct);
        string? content;
        try
        {
            content = body["message"]?["content"]?.GetValue<string>();
        }
        catch (InvalidOperationException ex)
        {
            throw OllamaHttp.NotOllama(context.Endpoint, ex);
        }
        return Parse(content).Decide(context);
    }

    /// <summary>Models installed in Ollama, for the settings window and the connection test.</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(string endpoint, CancellationToken ct = default)
    {
        using var response = await OllamaHttp.SendAsync(() => http.GetAsync(OllamaHttp.Url(endpoint, "api/tags"), ct), null, ct);
        var body = await OllamaHttp.ReadBodyAsync(response, endpoint, ct);
        try
        {
            return body["models"]?.AsArray()
                .Select(m => m?["name"]?.GetValue<string>())
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];
        }
        catch (InvalidOperationException ex)
        {
            throw OllamaHttp.NotOllama(endpoint, ex);
        }
    }

    internal static FilterAnswer Parse(string? content)
    {
        try
        {
            var answer = JsonNode.Parse(content ?? "") as JsonObject
                ?? throw new FilterResponseException("The model did not answer with a JSON object.");
            if (!answer.ContainsKey("exclusion") || !answer.ContainsKey("interest"))
                throw new FilterResponseException("The model's answer lacks \"exclusion\" or \"interest\".");
            return new FilterAnswer(
                answer["exclusion"]?.GetValue<string>(),
                answer["interest"]?.GetValue<string>(),
                answer["reason"]?.GetValue<string>()?.Trim());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new FilterResponseException($"Unreadable answer from the model: {Shorten(content)}", ex);
        }
    }

    private static string Shorten(string? text) =>
        text is null ? "(empty)" : text.Length <= 200 ? text : text[..200] + "…";
}
