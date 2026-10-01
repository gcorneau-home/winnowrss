using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Winnow.Core.Theming;

/// <summary>
/// Reads Visual Studio Code color themes: a theme JSON file (with comments and trailing commas allowed, and
/// "include" of a base theme), or a .vsix extension package that may contain several themes.
/// </summary>
public static class VsCodeTheme
{
    private static readonly JsonDocumentOptions Jsonc = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>One theme found in a file or a package.</summary>
    public sealed record Entry(string Name, bool? IsDark, IReadOnlyDictionary<string, string> Colors);

    /// <param name="readRelative">Reads a file referenced by "include", relative to this one; null if unavailable.</param>
    public static Entry Parse(string json, string fallbackName, Func<string, string?>? readRelative = null, bool? isDarkHint = null)
    {
        var root = ParseObject(json);
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // A theme can extend another one; its own colors win.
        if (root["include"]?.GetValue<string>() is { } include && readRelative?.Invoke(include) is { } baseJson)
            foreach (var (key, value) in Parse(baseJson, fallbackName, readRelative).Colors)
                colors[key] = value;

        if (root["colors"] is JsonObject own)
            foreach (var (key, value) in own)
                if (value is JsonValue v && v.TryGetValue<string>(out var color))
                    colors[key] = color;

        var type = root["type"]?.GetValue<string>();
        var isDark = type is null ? isDarkHint : !type.Equals("light", StringComparison.OrdinalIgnoreCase);
        return new Entry(root["name"]?.GetValue<string>() ?? fallbackName, isDark, colors);
    }

    /// <summary>The themes declared by a VS Code extension package (contributes.themes in its package.json).</summary>
    public static IReadOnlyList<Entry> ReadVsix(Stream vsix)
    {
        using var zip = new ZipArchive(vsix, ZipArchiveMode.Read);
        string? Read(string path) =>
            zip.GetEntry(path.Replace('\\', '/')) is { } entry ? new StreamReader(entry.Open(), Encoding.UTF8).ReadToEnd() : null;

        var package = ParseObject(Read("extension/package.json") ?? throw new InvalidDataException("Not a VS Code extension (no extension/package.json)."));
        var themes = new List<Entry>();
        foreach (var theme in package["contributes"]?["themes"]?.AsArray() ?? [])
        {
            if (theme?["path"]?.GetValue<string>() is not { } relative)
                continue;
            var path = Combine("extension", relative);
            if (Read(path) is not { } json)
                continue;

            var uiTheme = theme["uiTheme"]?.GetValue<string>();
            var hint = uiTheme is null ? (bool?)null : uiTheme != "vs" && uiTheme != "hc-light";
            var label = theme["label"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(relative);
            var entry = Parse(json, label, include => Read(Combine(Path.GetDirectoryName(path)!, include)), hint);
            themes.Add(entry with { Name = label });
        }
        return themes;
    }

    private static JsonObject ParseObject(string json) =>
        JsonNode.Parse(json, documentOptions: Jsonc) as JsonObject ?? throw new InvalidDataException("A theme must be a JSON object.");

    private static string Combine(string folder, string relative)
    {
        var parts = new List<string>(folder.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var part in relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else if (part != ".") parts.Add(part);
        }
        return string.Join('/', parts);
    }
}
