using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>The About window: version, credits and links to the project.</summary>
public sealed partial class AboutViewModel(IShellService shell)
{
    public string Version => AppInfo.Version;

    [RelayCommand]
    private void OpenRepository() => shell.OpenInBrowser(AppInfo.RepositoryUrl);

    [RelayCommand]
    private void OpenLicense() => shell.OpenInBrowser($"{AppInfo.RepositoryUrl}/blob/main/LICENSE");

    [RelayCommand]
    private void OpenNotices() => shell.OpenInBrowser($"{AppInfo.RepositoryUrl}/blob/main/THIRD-PARTY-NOTICES.md");
}
