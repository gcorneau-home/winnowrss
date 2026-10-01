using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.Core.Models;

namespace Winnow.App.Services;

/// <summary>
/// The reading and display preferences in effect, as bindable flags: the tree's triggers read them directly
/// (Source={x:Static services:DisplaySettings.Instance}), so a change applies everywhere at once.
/// </summary>
public sealed partial class DisplaySettings : ObservableObject
{
    public static DisplaySettings Instance { get; } = new();

    private DisplaySettings() => Apply(DisplayPreferences.Default);

    [ObservableProperty] private bool _openOnSingleClick;
    [ObservableProperty] private int _maxOpenTabs;
    [ObservableProperty] private bool _unreadBold;
    [ObservableProperty] private bool _unreadDot;
    [ObservableProperty] private bool _dimRead;
    [ObservableProperty] private bool _rejectedDimmed;
    [ObservableProperty] private bool _rejectedItalic;
    [ObservableProperty] private bool _rejectedIcon;
    [ObservableProperty] private bool _rejectedStrikethrough;
    [ObservableProperty] private int _articleZoom;

    public void Apply(DisplayPreferences preferences)
    {
        OpenOnSingleClick = preferences.OpenOnSingleClick;
        MaxOpenTabs = preferences.MaxOpenTabs;
        UnreadBold = preferences.UnreadCues.HasFlag(UnreadCues.Bold);
        UnreadDot = preferences.UnreadCues.HasFlag(UnreadCues.Dot);
        DimRead = preferences.UnreadCues.HasFlag(UnreadCues.DimRead);
        RejectedDimmed = preferences.RejectedCues.HasFlag(RejectedCues.Dimmed);
        RejectedItalic = preferences.RejectedCues.HasFlag(RejectedCues.Italic);
        RejectedIcon = preferences.RejectedCues.HasFlag(RejectedCues.Icon);
        RejectedStrikethrough = preferences.RejectedCues.HasFlag(RejectedCues.Strikethrough);
        ArticleZoom = preferences.ArticleZoom;
    }
}
