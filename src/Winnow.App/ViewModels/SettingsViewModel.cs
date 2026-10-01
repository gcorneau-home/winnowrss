using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;
using Winnow.Core.Models;
using Winnow.Core.Services;

namespace Winnow.App.ViewModels;

/// <summary>
/// The Settings window. Choices are previewed in the window and applied to the whole app (and saved) on Save.
/// </summary>
public sealed partial class SettingsViewModel(SettingsService settings, DisplaySettings display, Action showAbout) : ObservableObject
{
    [ObservableProperty] private bool _openOnSingleClick;
    [ObservableProperty] private string _maxOpenTabs = "";
    [ObservableProperty] private bool _unreadBold;
    [ObservableProperty] private bool _unreadDot;
    [ObservableProperty] private bool _dimRead;
    [ObservableProperty] private bool _rejectedDimmed;
    [ObservableProperty] private bool _rejectedItalic;
    [ObservableProperty] private bool _rejectedIcon;
    [ObservableProperty] private bool _rejectedStrikethrough;
    [ObservableProperty] private int _articleZoom;

    public IReadOnlyList<int> ZoomLevels => DisplayPreferences.ZoomLevels;

    public bool OpenOnDoubleClick
    {
        get => !OpenOnSingleClick;
        set => OpenOnSingleClick = !value;
    }

    partial void OnOpenOnSingleClickChanged(bool value) => OnPropertyChanged(nameof(OpenOnDoubleClick));

    [RelayCommand]
    private void ShowAbout() => showAbout();

    /// <summary>Raised with true after saving, false on cancel; the window closes itself.</summary>
    public event Action<bool>? CloseRequested;

    public async Task LoadAsync()
    {
        var p = await settings.GetDisplayPreferencesAsync();
        OpenOnSingleClick = p.OpenOnSingleClick;
        MaxOpenTabs = p.MaxOpenTabs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        (UnreadBold, UnreadDot, DimRead) = (p.UnreadCues.HasFlag(UnreadCues.Bold), p.UnreadCues.HasFlag(UnreadCues.Dot), p.UnreadCues.HasFlag(UnreadCues.DimRead));
        RejectedDimmed = p.RejectedCues.HasFlag(RejectedCues.Dimmed);
        RejectedItalic = p.RejectedCues.HasFlag(RejectedCues.Italic);
        RejectedIcon = p.RejectedCues.HasFlag(RejectedCues.Icon);
        RejectedStrikethrough = p.RejectedCues.HasFlag(RejectedCues.Strikethrough);
        ArticleZoom = p.ArticleZoom;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var preferences = new DisplayPreferences(
            OpenOnSingleClick,
            int.TryParse(MaxOpenTabs.Trim(), out var max) ? Math.Clamp(max, DisplayPreferences.MinTabs, DisplayPreferences.MaxTabs)
                : DisplayPreferences.Default.MaxOpenTabs,
            (UnreadBold ? UnreadCues.Bold : 0) | (UnreadDot ? UnreadCues.Dot : 0) | (DimRead ? UnreadCues.DimRead : 0),
            (RejectedDimmed ? RejectedCues.Dimmed : 0) | (RejectedItalic ? RejectedCues.Italic : 0)
                | (RejectedIcon ? RejectedCues.Icon : 0) | (RejectedStrikethrough ? RejectedCues.Strikethrough : 0),
            ArticleZoom);

        await settings.SetDisplayPreferencesAsync(preferences);
        display.Apply(preferences);
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
