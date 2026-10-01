using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;
using Winnow.Core.Services;

namespace Winnow.App.Localization;

public sealed record LanguageOption(string Code, string Name)
{
    // Screen readers use ToString() for combo box items.
    public override string ToString() => Name;
}

/// <summary>
/// UI strings for the current language. XAML binds to the indexer (see <see cref="TrExtension"/>),
/// so raising a change on "Item[]" retranslates the whole interface live.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    private static readonly ResourceManager Strings =
        new("Winnow.App.Resources.Strings", typeof(Localizer).Assembly);

    // Captured before we change them. Windows has a display language (UI culture) and separate regional
    // formats (culture); e.g. French display with English date formats.
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentCulture;
    private static readonly CultureInfo SystemUICulture = CultureInfo.CurrentUICulture;

    public static Localizer Instance { get; } = new();

    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("en", "English"),
        new("fr", "Français"),
    ];

    private Localizer() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after every binding has been told to retranslate.</summary>
    public event EventHandler? LanguageChanged;

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");

    public string LanguageCode => Culture.TwoLetterISOLanguageName;

    /// <summary>Display language of Windows if supported, else English.</summary>
    public static string DefaultLanguageCode =>
        Languages.Any(l => l.Code == SystemUICulture.TwoLetterISOLanguageName) ? SystemUICulture.TwoLetterISOLanguageName : "en";

    public string this[string key] => Strings.GetString(key, Culture) ?? $"[{key}]";

    public string Format(string key, params object?[] args) => string.Format(Culture, this[key], args);

    public string Error(WinnowException error) => Format($"Error_{error.Error}", [.. error.Args]);

    public void SetLanguage(string code)
    {
        if (Languages.All(l => l.Code != code))
            code = "en";

        // Prefer the user's own regional variant of the chosen language (e.g. fr-CA over neutral fr).
        Culture = SystemCulture.TwoLetterISOLanguageName == code ? SystemCulture
            : SystemUICulture.TwoLetterISOLanguageName == code ? SystemUICulture
            : CultureInfo.GetCultureInfo(code);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = Culture;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageCode)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
