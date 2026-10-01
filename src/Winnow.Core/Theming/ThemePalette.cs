using System.Globalization;

namespace Winnow.Core.Theming;

/// <summary>An sRGB color with alpha; parses and prints VS Code's "#RRGGBB" / "#RRGGBBAA".</summary>
public readonly record struct ThemeColor(byte A, byte R, byte G, byte B)
{
    public static ThemeColor? TryParse(string? text)
    {
        if (text is null || !text.StartsWith('#'))
            return null;
        var hex = text[1..];
        // Short forms "#RGB" and "#RGBA" double each digit.
        if (hex.Length is 3 or 4)
            hex = string.Concat(hex.Select(c => $"{c}{c}"));
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return null;
        return hex.Length == 6
            ? new ThemeColor(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : new ThemeColor((byte)value, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8)); // alpha comes last in VS Code
    }

    /// <summary>"#RRGGBB" for opaque colors, "#RRGGBBAA" otherwise (VS Code and CSS order).</summary>
    public override string ToString() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    /// <summary>Relative luminance (0 black … 1 white), to tell dark themes from light ones.</summary>
    public double Luminance
    {
        get
        {
            static double Channel(byte c)
            {
                var s = c / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);
        }
    }

    /// <summary>This color drawn over <paramref name="background"/> with the given opacity, as an opaque color.</summary>
    public ThemeColor Over(ThemeColor background, double opacity)
    {
        byte Mix(byte fg, byte bg) => (byte)Math.Round(fg * opacity + bg * (1 - opacity));
        return new ThemeColor(255, Mix(R, background.R), Mix(G, background.G), Mix(B, background.B));
    }

    public ThemeColor WithAlpha(double opacity) => this with { A = (byte)Math.Round(255 * opacity) };
}

/// <summary>The colors WinnowRSS needs, taken from a theme (VS Code or other) and applied to the whole interface.</summary>
public sealed record ThemePalette
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsDark { get; init; }

    /// <summary>Where it came from, e.g. "dracula-theme.theme-dracula" or a file name.</summary>
    public string? Source { get; init; }

    public ThemeColor Background { get; init; }
    public ThemeColor Surface { get; init; }
    public ThemeColor Header { get; init; }
    public ThemeColor Foreground { get; init; }
    public ThemeColor SecondaryForeground { get; init; }
    public ThemeColor Accent { get; init; }
    public ThemeColor AccentForeground { get; init; }
    public ThemeColor Selection { get; init; }
    public ThemeColor SelectionForeground { get; init; }
    public ThemeColor Hover { get; init; }
    public ThemeColor Border { get; init; }
    public ThemeColor InputBackground { get; init; }
    public ThemeColor InputForeground { get; init; }
    public ThemeColor Link { get; init; }
    public ThemeColor Warning { get; init; }
    public ThemeColor Error { get; init; }

    /// <summary>CSS for the reading pane, so articles use the theme's colors.</summary>
    public string ArticleCss() => $$"""
        :root { color-scheme: {{(IsDark ? "dark" : "light")}}; }
        html, body { background: {{Background}}; color: {{Foreground}}; }
        a, a:visited { color: {{Link}}; }
        pre, code { background: {{Surface}}; }
        blockquote { border-left-color: {{Border}}; }
        ::selection { background: {{Selection}}; color: {{SelectionForeground}}; }
        """;
}
