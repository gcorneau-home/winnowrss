using System.Windows;
using Winnow.App.Views;

namespace Winnow.App.Services;

public sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current.MainWindow is { IsVisible: true } w ? w : null;

    public string? Prompt(string title, string label, string initialValue = "") =>
        Prompt(title, [(label, initialValue)])?[0];

    public string[]? Prompt(string title, IReadOnlyList<(string Label, string InitialValue)> fields)
    {
        var dialog = new InputDialog(title, fields) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Values : null;
    }

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public string? PickFile(string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public bool ShowSettings(ViewModels.SettingsViewModel viewModel) =>
        new SettingsWindow(viewModel) { Owner = Owner }.ShowDialog() == true;

    public void ShowThemes(ViewModels.ThemesViewModel viewModel) =>
        new ThemesWindow(viewModel) { Owner = Owner }.ShowDialog();

    public bool ShowFilterSettings(ViewModels.FilterSettingsViewModel viewModel) =>
        new FilterSettingsWindow(viewModel) { Owner = Owner }.ShowDialog() == true;

    public void ShowError(string message) =>
        Show(message, "WinnowRSS", MessageBoxButton.OK, MessageBoxImage.Error);

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}
