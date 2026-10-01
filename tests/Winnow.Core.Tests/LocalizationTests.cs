using System.Text.RegularExpressions;
using System.Xml.Linq;
using Winnow.Core.Services;

namespace Winnow.Core.Tests;

/// <summary>Checks the UI string tables of Winnow.App (read as files; the WPF project cannot be referenced here).</summary>
public partial class LocalizationTests
{
    private static readonly string ResourcesDir = Path.Combine(RepoRoot(), "src", "Winnow.App", "Resources");

    [Fact]
    public void English_and_french_tables_have_the_same_keys_and_placeholders()
    {
        var english = Load("Strings.resx");
        var french = Load("Strings.fr.resx");

        Assert.Empty(english.Keys.Except(french.Keys));
        Assert.Empty(french.Keys.Except(english.Keys));
        Assert.All(english, e => Assert.Equal(Placeholders(e.Value), Placeholders(french[e.Key])));
    }

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
