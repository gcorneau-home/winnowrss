using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Winnow.Core.Services;

namespace Winnow.Core.Theming;

/// <summary>An extension of the Themes category on Open VSX.</summary>
public sealed record ThemeExtension(
    string Namespace,
    string Name,
    string DisplayName,
    string? Description,
    string Version,
    long Downloads,
    string DownloadUrl)
{
    public string Id => $"{Namespace}.{Name}";
}

/// <summary>
/// Searches and downloads VS Code color themes from Open VSX (open-vsx.org), the open registry of VS Code extensions.
/// </summary>
public sealed class OpenVsxClient(HttpClient http, string baseUrl = OpenVsxClient.DefaultBaseUrl)
{
    public const string DefaultBaseUrl = "https://open-vsx.org/";

    public async Task<IReadOnlyList<ThemeExtension>> SearchAsync(string query, CancellationToken ct = default)
    {
        // Sorted by relevance (the default): sorting by downloads lets very popular icon themes, which share
        // the Themes category, crowd out the color themes that match the query.
        var url = new Uri(new Uri(baseUrl), $"api/-/search?category=Themes&size=30&query={Uri.EscapeDataString(query.Trim())}");
        var body = await CallAsync(() => http.GetFromJsonAsync<JsonObject>(url, ct));
        return body?["extensions"]?.AsArray()
            .OfType<JsonObject>()
            .Select(e => new ThemeExtension(
                (string?)e["namespace"] ?? "",
                (string?)e["name"] ?? "",
                (string?)e["displayName"] ?? (string?)e["name"] ?? "",
                (string?)e["description"],
                (string?)e["version"] ?? "",
                (long?)e["downloadCount"] ?? 0,
                (string?)e["files"]?["download"] ?? ""))
            .Where(e => e.DownloadUrl.Length > 0)
            .ToList() ?? [];
    }

    /// <summary>The extension package (.vsix), which holds the theme files.</summary>
    public Task<byte[]> DownloadAsync(ThemeExtension extension, CancellationToken ct = default) =>
        CallAsync(() => http.GetByteArrayAsync(extension.DownloadUrl, ct));

    private static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException
                                   || (ex is TaskCanceledException tce && !tce.CancellationToken.IsCancellationRequested))
        {
            throw new WinnowException(WinnowError.ThemeCatalogUnavailable, $"Open VSX is not reachable: {ex.Message}", [ex.Message], ex);
        }
    }
}
