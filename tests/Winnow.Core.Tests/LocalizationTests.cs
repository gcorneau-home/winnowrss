using System.Text.RegularExpressions;
using System.Xml.Linq;
using Winnow.Core.Services;

namespace Winnow.Core.Tests;

/// <summary>Checks the UI string tables of Winnow.App (read as files; the WPF project cannot be referenced here).</summary>
public partial class LocalizationTests
{
    private static readonly string ResourcesDir = Path.Combine(RepoRoot(), "src", "Winnow.App", "Resources");

    private static string[] TranslationFiles() =>
        Directory.GetFiles(ResourcesDir, "Strings.*.resx").Select(f => Path.GetFileName(f)).Order().ToArray();

    public static TheoryData<string> Translations() => new(TranslationFiles());

    [Fact]
    public void French_and_spanish_translations_exist() =>
        Assert.Equal(["Strings.es.resx", "Strings.fr.resx"], TranslationFiles());

    [Theory]
    [MemberData(nameof(Translations))]
    public void Each_translation_has_the_english_keys_and_placeholders(string file)
    {
        var english = Load("Strings.resx");
        var translation = Load(file);

        Assert.Empty(english.Keys.Except(translation.Keys));
        Assert.Empty(translation.Keys.Except(english.Keys));
        Assert.All(english, e => Assert.Equal(Placeholders(e.Value), Placeholders(translation[e.Key])));
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("fr", "French")]
    [InlineData("es", "Spanish")]
    [InlineData("xx-unknown", "English")]
    public void Language_codes_have_english_names_for_the_model(string code, string name) =>
        Assert.Equal(name, Winnow.Core.Models.LanguageNames.English(code));

    [Fact]
    public void Every_error_code_has_a_message()
    {
        var english = Load("Strings.resx");

        Assert.All(Enum.GetNames<WinnowError>(), code => Assert.Contains($"Error_{code}", english.Keys));
    }

    private static Dictionary<string, string> Load(string file) =>
        XDocument.Load(Path.Combine(ResourcesDir, file)).Root!
            .Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);

    private static string[] Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order().ToArray();

    [GeneratedRegex(@"\{(\d+)")]
    private static partial Regex Placeholder();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WinnowRSS.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
