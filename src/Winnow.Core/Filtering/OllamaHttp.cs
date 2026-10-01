using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Winnow.Core.Filtering;

/// <summary>Calls to a local Ollama server shared by the filter and the summarizer, with errors worded for the user.</summary>
internal static class OllamaHttp
{
    /// <summary>
    /// Context window asked of the model, the same for every call: Ollama reloads the model when it changes, and
    /// summaries need room for a long article (about 4,000 words plus the answer).
    /// </summary>
    internal const int ContextLength = 8192;

    /// <param name="model">The model asked for, to explain a 404; null for calls that do not use one.</param>
    internal static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send, string? model, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await send();
        }
        catch (HttpRequestException ex)
        {
            throw new FilterUnavailableException($"Ollama is not reachable: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) // HttpClient timeout, not a cancellation
        {
            throw new FilterUnavailableException("Ollama did not answer in time.", ex);
        }

        if (response.StatusCode == HttpStatusCode.NotFound && model is not null)
        {
            response.Dispose();
            throw new FilterUnavailableException($"The model \"{model}\" is not installed in Ollama (ollama pull {model}).");
        }
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new FilterUnavailableException($"Ollama answered {(int)status} {status}.");
        }
        return response;
    }

    /// <summary>The JSON body of a successful answer; anything else means the endpoint is not an Ollama server.</summary>
    internal static async Task<JsonObject> ReadBodyAsync(HttpResponseMessage response, string endpoint, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<JsonObject>(ct) ?? throw NotOllama(endpoint, null);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw NotOllama(endpoint, ex);
        }
    }

    internal static FilterUnavailableException NotOllama(string endpoint, Exception? inner) =>
        new($"{endpoint} did not answer like an Ollama server.", inner);

    internal static Uri Url(string endpoint, string path) => new(new Uri(endpoint.TrimEnd('/') + "/"), path);
}
