using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Winnow.App.Localization;
using Winnow.Core.Theming;

namespace Winnow.App.Services;

/// <summary>A theme choice: built-in (name follows the interface language) or installed (the theme's own name).</summary>
public sealed class ThemeOption : INotifyPropertyChanged
{
    private readonly string? _nameKey;
    private readonly string? _name;

    private ThemeOption(string code, string? nameKey, string? name)
    {
        Code = code;
        _nameKey = nameKey;
        _name = name;
        if (nameKey is not null)
            Localizer.Instance.LanguageChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }

    public static ThemeOption BuiltIn(string code, string nameKey) => new(code, nameKey, null);
    public static ThemeOption Installed(ThemePalette palette) => new(ThemeService.InstalledPrefix + palette.Id, null, palette.Name);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Code { get; }
    public string Name => _name ?? Localizer.Instance[_nameKey!];

    // Screen readers use ToString() for combo box items.
    public override string ToString() => Name;
}

/// <summary>
/// Light, dark, following Windows, or an installed theme (VS Code colors), applied live to the whole app:
/// WPF's Fluent theme gives the base, an installed theme then replaces its brushes. Views that draw outside
/// WPF (the WebView2 reading pane) listen to <see cref="Changed"/>.
/// </summary>
public sealed class ThemeService
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";
    public const string InstalledPrefix = "installed:";

    // Brushes of WPF's Fluent theme, and WinnowRSS's own, that an installed theme recolors.
    private static readonly (string Key, Func<ThemePalette, Color> Color)[] Brushes =
    [
        ("ApplicationBackgroundBrush", p => C(p.Background)),
        ("SolidBackgroundFillColorBaseBrush", p => C(p.Background)),
        ("SolidBackgroundFillColorSecondaryBrush", p => C(p.Surface)),
        ("SolidBackgroundFillColorTertiaryBrush", p => C(p.Surface)),
        ("LayerFillColorDefaultBrush", p => C(p.Surface)),
        ("CardBackgroundFillColorDefaultBrush", p => C(p.Surface)),
        ("TextFillColorPrimaryBrush", p => C(p.Foreground)),
        ("TextFillColorSecondaryBrush", p => C(p.SecondaryForeground)),
        ("TextFillColorTertiaryBrush", p => C(p.SecondaryForeground)),
        ("ControlFillColorDefaultBrush", p => C(p.InputBackground)),
        ("ControlFillColorInputActiveBrush", p => C(p.InputBackground)),
        ("ControlFillColorSecondaryBrush", p => C(p.Hover)),
        ("ControlFillColorTertiaryBrush", p => C(p.Hover)),
        ("ControlSolidFillColorDefaultBrush", p => C(p.InputBackground)),
        ("SubtleFillColorSecondaryBrush", p => C(p.Hover)),
        ("SubtleFillColorTertiaryBrush", p => C(p.Selection)),
        ("ControlStrokeColorDefaultBrush", p => C(p.Border)),
        ("ControlStrokeColorSecondaryBrush", p => C(p.Border)),
        ("CardStrokeColorDefaultBrush", p => C(p.Border)),
        ("DividerStrokeColorDefaultBrush", p => C(p.Border)),
        ("AccentFillColorDefaultBrush", p => C(p.Accent)),
        ("AccentFillColorSecondaryBrush", p => C(p.Accent)),
        ("AccentFillColorTertiaryBrush", p => C(p.Accent)),
        ("TextOnAccentFillColorPrimaryBrush", p => C(p.AccentForeground)),
        ("AccentTextFillColorPrimaryBrush", p => C(p.Link)),
        ("AccentFillColorSelectedTextBackgroundBrush", p => C(p.Selection)),
        ("ToggleButtonBackgroundChecked", p => C(p.Accent)),
        ("ToggleButtonBackgroundCheckedPointerOver", p => C(p.Accent)),
        ("ToggleButtonBackgroundCheckedPressed", p => C(p.Accent)),
        ("ToggleButtonForegroundChecked", p => C(p.AccentForeground)),
        ("ToggleButtonForegroundCheckedPressed", p => C(p.AccentForeground)),
        ("TreeViewItemSelectionIndicatorForeground", p => C(p.Accent)),
        ("TreeViewItemBackgroundSelected", p => C(p.Selection)),
        ("ListViewItemPillFillBrush", p => C(p.Accent)),
        ("SystemFillColorCriticalBrush", p => C(p.Error)),
        ("SystemFillColorCautionBrush", p => C(p.Warning)),
        ("Winnow.WindowBackground", p => C(p.Background)),
        ("Winnow.HeaderBackground", p => C(p.Header)),
        ("Winnow.SurfaceBackground", p => C(p.Surface)),
        ("Winnow.MultiSelectBrush", p => C(p.Accent.WithAlpha(0.35))),
    ];

    // Accent colors (not brushes) that other Fluent brushes are built from.
    private static readonly string[] AccentColorKeys =
    [
        "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
    ];

    private ThemeLibrary? _library;

    public static ThemeService Instance { get; } = new();

    private ThemeService()
    {
        Themes =
        [
            ThemeOption.BuiltIn(System, "Theme_System"),
            ThemeOption.BuiltIn(Light, "Theme_Light"),
            ThemeOption.BuiltIn(Dark, "Theme_Dark"),
        ];
    }

    public event EventHandler? Changed;

    /// <summary>Built-in themes first, then the installed ones.</summary>
    public ObservableCollection<ThemeOption> Themes { get; }

    public string Current { get; private set; } = System;

    /// <summary>The installed theme in use, if any.</summary>
    public ThemePalette? Palette { get; private set; }

    /// <summary>Extra stylesheet for the reading pane (installed themes only).</summary>
    public string? ArticleCss => Palette?.ArticleCss();

    /// <summary>Whether the app is drawn dark right now (for "system", what Windows uses for apps).</summary>
    public bool IsDark => Palette?.IsDark ?? (Current == Dark || (Current == System && WindowsAppsUseDarkTheme()));

    public ThemeLibrary Library => _library ?? throw new InvalidOperationException("Call Initialize first.");

    public void Initialize(ThemeLibrary library)
    {
        _library = library;
        ReloadInstalled();
    }

    /// <summary>Rebuilds the list after themes were installed or deleted.</summary>
    public void ReloadInstalled()
    {
        while (Themes.Count > 3)
            Themes.RemoveAt(3);
        foreach (var palette in Library.GetAll())
            Themes.Add(ThemeOption.Installed(palette));
    }

    public void Apply(string code)
    {
        ThemePalette? palette = null;
        if (code.StartsWith(InstalledPrefix, StringComparison.Ordinal))
        {
            palette = _library?.Get(code[InstalledPrefix.Length..]);
            if (palette is null)
                code = System; // the installed theme was deleted
        }
        else if (code is not (System or Light or Dark))
        {
            code = System;
        }

        var resources = Application.Current.Resources;
        foreach (var (key, _) in Brushes)
            resources.Remove(key);
        foreach (var key in AccentColorKeys)
            resources.Remove(key);

#pragma warning disable WPF0001 // ThemeMode is marked experimental but is the supported way to switch the Fluent theme
        Application.Current.ThemeMode = palette is not null ? (palette.IsDark ? ThemeMode.Dark : ThemeMode.Light)
            : code switch { Light => ThemeMode.Light, Dark => ThemeMode.Dark, _ => ThemeMode.System };
#pragma warning restore WPF0001

        // Entries of Application.Resources itself take precedence over the Fluent dictionaries merged into it.
        if (palette is not null)
        {
            foreach (var (key, color) in Brushes)
                resources[key] = Frozen(color(palette));
            foreach (var key in AccentColorKeys)
                resources[key] = C(palette.Accent);
        }
        else
            SetDefaults(resources);

        Palette = palette;
        Current = code;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>WinnowRSS's own brushes when no installed theme is used: let the Fluent theme show through.</summary>
    private static void SetDefaults(ResourceDictionary resources)
    {
        var transparent = Frozen(Colors.Transparent);
        resources["Winnow.WindowBackground"] = transparent;
        resources["Winnow.HeaderBackground"] = transparent;
        resources["Winnow.SurfaceBackground"] = transparent;
        resources["Winnow.MultiSelectBrush"] = Frozen(Color.FromArgb(0x40, 0x00, 0x78, 0xD4));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color C(ThemeColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private static bool WindowsAppsUseDarkTheme() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0;
}
