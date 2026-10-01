using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Localization;
using Winnow.App.Services;
using Winnow.Core.Services;
using Winnow.Core.Theming;

namespace Winnow.App.ViewModels;

/// <summary>
/// The Themes window: installed themes (apply, delete), import of a VS Code theme file, and search and install
/// from Open VSX. Applying goes through <paramref name="apply"/> so the choice is saved like the toolbar's.
/// </summary>
public sealed partial class ThemesViewModel(
    ThemeService themes,
    OpenVsxClient catalog,
    IDialogService dialogs,
    Localizer loc,
    Action<string> apply) : ObservableObject
{
    public ObservableCollection<InstalledThemeViewModel> Installed { get; } = [];
    public ObservableCollection<CatalogThemeViewModel> Results { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _query = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    public void Load() => RefreshInstalled();

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        await RunAsync(async () =>
        {
            var found = await catalog.SearchAsync(Query);
            Results.Clear();
            foreach (var extension in found)
                Results.Add(new CatalogThemeViewModel(extension, loc, InstallAsync));
            Status = found.Count == 0 ? loc.Format("Themes_NoResults", Query) : loc.Format("Themes_ResultCount", found.Count);
        });
    }

    private bool CanSearch() => !IsBusy && Query.Trim().Length > 0;

    private Task InstallAsync(ThemeExtension extension) => RunAsync(async () =>
    {
        Status = loc.Format("Themes_Installing", extension.DisplayName);
        var package = await catalog.DownloadAsync(extension);
        var added = themes.Library.ImportVsix(new MemoryStream(package), extension.Id);
        UseNewTheme(added[0].Id, loc.Format("Themes_Installed", extension.DisplayName, added.Count));
    });

    [RelayCommand]
    private Task ImportFileAsync() => RunAsync(() =>
    {
        if (dialogs.PickFile(loc["Themes_ImportFilter"]) is not { } path)
            return Task.CompletedTask;
        var added = themes.Library.ImportFile(path);
        UseNewTheme(added[0].Id, loc.Format("Themes_Installed", Path.GetFileName(path), added.Count));
        return Task.CompletedTask;
    });

    [RelayCommand]
    private void Apply(InstalledThemeViewModel? theme)
    {
        if (theme is not null)
            apply(ThemeService.InstalledPrefix + theme.Id);
    }

    [RelayCommand]
    private void Delete(InstalledThemeViewModel? theme)
    {
        if (theme is null)
            return;
        var wasCurrent = themes.Current == ThemeService.InstalledPrefix + theme.Id;
        themes.Library.Delete(theme.Id);
        themes.ReloadInstalled();
        RefreshInstalled();
        if (wasCurrent)
            apply(ThemeService.System);
    }

    /// <summary>A theme was added: list it and use it right away.</summary>
    private void UseNewTheme(string appliedId, string status)
    {
        themes.ReloadInstalled();
        RefreshInstalled();
        apply(ThemeService.InstalledPrefix + appliedId);
        Status = status;
    }

    private void RefreshInstalled()
    {
        Installed.Clear();
        foreach (var palette in themes.Library.GetAll())
            Installed.Add(new InstalledThemeViewModel(palette));
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (WinnowException ex)
        {
            Status = loc.Error(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed class InstalledThemeViewModel(ThemePalette palette)
{
    public string Id => palette.Id;
    public string Name => palette.Name;
    public string? Source => palette.Source;

    // Swatches shown next to the name, as "#RRGGBB" strings WPF converts to brushes.
    public string Background => palette.Background.ToString();
    public string Surface => palette.Surface.ToString();
    public string Foreground => palette.Foreground.ToString();
    public string Accent => palette.Accent.ToString();

    public override string ToString() => Name;
}

public sealed partial class CatalogThemeViewModel(ThemeExtension extension, Localizer loc, Func<ThemeExtension, Task> install)
{
    public string Name => extension.DisplayName;
    public string? Description => extension.Description;
    public string Details => loc.Format("Themes_Details", extension.Namespace, extension.Downloads.ToString("N0", loc.Culture));

    [RelayCommand]
    private Task InstallAsync() => install(extension);

    public override string ToString() => Name;
}
