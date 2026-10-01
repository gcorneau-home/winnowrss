namespace Winnow.Core.Models;

/// <summary>How unread articles stand out in the tree (any combination).</summary>
[Flags]
public enum UnreadCues
{
    None = 0,
    Bold = 1,
    /// <summary>A colored dot before the title.</summary>
    Dot = 2,
    /// <summary>Read articles are dimmed, so unread ones stand out by contrast.</summary>
    DimRead = 4,
}

/// <summary>How articles filtered out by the interest filter look in the tree (any combination).</summary>
[Flags]
public enum RejectedCues
{
    None = 0,
    Dimmed = 1,
    Italic = 2,
    Icon = 4,
    Strikethrough = 8,
}

/// <summary>Reading and display preferences from the Settings window.</summary>
/// <param name="ArticleZoom">Default zoom of the reading pane, in percent.</param>
public sealed record DisplayPreferences(
    bool OpenOnSingleClick, int MaxOpenTabs, UnreadCues UnreadCues, RejectedCues RejectedCues, int ArticleZoom)
{
    public const int MinTabs = 1;
    public const int MaxTabs = 50;

    /// <summary>The zoom levels offered, in percent (the browser's own steps).</summary>
    public static IReadOnlyList<int> ZoomLevels { get; } = [50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250, 300];

    public static DisplayPreferences Default { get; } =
        new(false, 10, UnreadCues.Bold | UnreadCues.Dot, RejectedCues.Dimmed | RejectedCues.Italic | RejectedCues.Icon, 100);

    /// <summary>The offered zoom level closest to <paramref name="percent"/>.</summary>
    public static int NearestZoom(int percent) => ZoomLevels.MinBy(level => Math.Abs(level - percent));
}
