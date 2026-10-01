namespace Winnow.Core.Theming;

/// <summary>
/// Picks WinnowRSS colors out of a VS Code theme's ~500 color keys, with fallbacks: themes define different subsets.
/// </summary>
public static class ThemePaletteMapper
{
    private static readonly ThemeColor DarkBase = new(255, 0x1E, 0x1E, 0x1E);
    private static readonly ThemeColor LightBase = new(255, 0xFF, 0xFF, 0xFF);

    public static ThemePalette FromVsCode(VsCodeTheme.Entry theme, string id, string? source)
    {
        ThemeColor? Get(params string[] keys) =>
            keys.Select(k => theme.Colors.TryGetValue(k, out var v) ? ThemeColor.TryParse(v) : null).FirstOrDefault(c => c is not null);

        var editor = Get("editor.background");
        var isDark = theme.IsDark ?? (editor?.Luminance ?? 0) < 0.4;
        var background = Opaque(editor ?? (isDark ? DarkBase : LightBase), isDark);
        var foreground = Opaque(Get("editor.foreground", "foreground") ?? (isDark ? LightBase : DarkBase), background);

        var surface = Opaque(Get("sideBar.background", "panel.background") ?? background, background);
        var accent = Opaque(Get("focusBorder", "button.background", "activityBarBadge.background", "progressBar.background")
            ?? new ThemeColor(255, 0x00, 0x78, 0xD4), background);
        var selection = Get("list.activeSelectionBackground", "editor.selectionBackground") ?? accent.WithAlpha(0.35);

        return new ThemePalette
        {
            Id = id,
            Name = theme.Name,
            Source = source,
            IsDark = isDark,
            Background = background,
            Surface = surface,
            Header = Opaque(Get("titleBar.activeBackground", "editorGroupHeader.tabsBackground") ?? surface, background),
            Foreground = foreground,
            SecondaryForeground = Opaque(Get("descriptionForeground", "sideBarSectionHeader.foreground") ?? foreground.WithAlpha(0.7), background),
            Accent = accent,
            AccentForeground = Opaque(Get("button.foreground") ?? Contrasting(accent), accent),
            Selection = Opaque(selection, surface),
            SelectionForeground = Opaque(Get("list.activeSelectionForeground") ?? foreground, background),
            Hover = Opaque(Get("list.hoverBackground") ?? foreground.WithAlpha(0.08), surface),
            Border = Opaque(Get("panel.border", "sideBar.border", "editorGroup.border", "contrastBorder") ?? foreground.WithAlpha(0.2), background),
            InputBackground = Opaque(Get("input.background", "dropdown.background") ?? surface, background),
            InputForeground = Opaque(Get("input.foreground", "dropdown.foreground") ?? foreground, background),
            Link = Opaque(Get("textLink.foreground") ?? accent, background),
            Warning = Opaque(Get("editorWarning.foreground", "notificationsWarningIcon.foreground") ?? new ThemeColor(255, 0xFF, 0xA5, 0x00), background),
            Error = Opaque(Get("errorForeground", "editorError.foreground") ?? new ThemeColor(255, 0xF1, 0x4C, 0x4C), background),
        };
    }

    // Translucent theme colors are flattened onto what they are drawn over, so every brush is solid.
    private static ThemeColor Opaque(ThemeColor color, ThemeColor over) => color.A == 255 ? color : color.Over(over, color.A / 255.0);

    private static ThemeColor Opaque(ThemeColor color, bool isDark) => Opaque(color, isDark ? DarkBase : LightBase);

    private static ThemeColor Contrasting(ThemeColor color) => color.Luminance > 0.45 ? new ThemeColor(255, 0, 0, 0) : new ThemeColor(255, 255, 255, 255);
}
