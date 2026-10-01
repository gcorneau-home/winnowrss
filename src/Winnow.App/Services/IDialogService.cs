namespace Winnow.App.Services;

/// <summary>Modal interactions, kept behind an interface so view models never touch windows.</summary>
public interface IDialogService
{
    /// <summary>Asks for one value; null when cancelled.</summary>
    string? Prompt(string title, string label, string initialValue = "");

    /// <summary>Asks for several values at once; null when cancelled.</summary>
    string[]? Prompt(string title, IReadOnlyList<(string Label, string InitialValue)> fields);

    bool Confirm(string title, string message);

    void ShowError(string message);

    /// <summary>Asks for a file to open; null when cancelled. <paramref name="filter"/> uses the "Name|*.ext" format.</summary>
    string? PickFile(string filter);

    bool ShowSettings(ViewModels.SettingsViewModel viewModel);

    void ShowThemes(ViewModels.ThemesViewModel viewModel);

    /// <summary>Shows the Filter window; true if the user saved.</summary>
    bool ShowFilterSettings(ViewModels.FilterSettingsViewModel viewModel);
}
