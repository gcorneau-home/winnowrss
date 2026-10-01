using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Winnow.Core.Services;

namespace Winnow.Core.Theming;

/// <summary>
/// Installed themes, one palette file each in a folder. Importing the same theme again replaces it.
/// </summary>
public sealed class ThemeLibrary(string folder)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new ThemeColorConverter() } };

    public IReadOnlyList<ThemePalette> GetAll() =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.json").Select(Load).OfType<ThemePalette>().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    public ThemePalette? Get(string id) => Load(Path.Combine(folder, Slug(id) + ".json"));

    /// <summary>Imports a VS Code theme (.json) or extension package (.vsix); returns the themes added.</summary>
    public IReadOnlyList<ThemePalette> ImportFile(string path)
    {
        try
        {
            if (Path.GetExtension(path).Equals(".vsix", StringComparison.OrdinalIgnoreCase))
            {
                using var vsix = File.OpenRead(path);
                return ImportVsix(vsix, Path.GetFileNameWithoutExtension(path));
            }

            var name = Path.GetFileNameWithoutExtension(path);
            var entry = VsCodeTheme.Parse(File.ReadAllText(path, Encoding.UTF8), name,
                include => File.Exists(Path.Combine(Path.GetDirectoryName(path)!, include))
                    ? File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, include)) : null);
            return [Save(entry, Path.GetFileName(path))];
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            throw new WinnowException(WinnowError.ThemeInvalid, $"Not a usable theme: {ex.Message}", [ex.Message], ex);
        }
    }

    public IReadOnlyList<ThemePalette> ImportVsix(Stream vsix, string source)
    {
        try
        {
            var themes = VsCodeTheme.ReadVsix(vsix);
            if (themes.Count == 0)
                throw new WinnowException(WinnowError.ThemeNone, "This extension contains no color theme.");
            return themes.Select(t => Save(t, source)).ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException)
        {
            throw new WinnowException(WinnowError.ThemeInvalid, $"Not a usable theme: {ex.Message}", [ex.Message], ex);
        }
    }

    public void Delete(string id)
    {
        var path = Path.Combine(folder, Slug(id) + ".json");
        if (File.Exists(path))
            File.Delete(path);
    }

    private ThemePalette Save(VsCodeTheme.Entry entry, string source)
    {
        var palette = ThemePaletteMapper.FromVsCode(entry, Slug($"{source}-{entry.Name}"), source);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, palette.Id + ".json"), JsonSerializer.Serialize(palette, Json));
        return palette;
    }

    private static ThemePalette? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<ThemePalette>(File.ReadAllText(path), Json) : null;
        }
        catch (JsonException)
        {
            return null; // a damaged file is skipped rather than breaking the theme list
        }
    }

    /// <summary>File-name-safe identifier: lowercase letters and digits separated by dashes.</summary>
    public static string Slug(string text)
    {
        var slug = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
            slug.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        return string.Join('-', slug.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed class ThemeColorConverter : JsonConverter<ThemeColor>
    {
        public override ThemeColor Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            ThemeColor.TryParse(reader.GetString()) ?? throw new JsonException("Invalid color.");

        public override void Write(Utf8JsonWriter writer, ThemeColor value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
