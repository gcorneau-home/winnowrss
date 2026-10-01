using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Winnow.App.ViewModels;

/// <summary>A language in the summary button's list; checked when the article already has a summary in it.</summary>
public sealed partial class SummaryLanguageViewModel(string code, string name, IRelayCommand<string> summarize) : ObservableObject
{
    public string Code { get; } = code;
    public string Name { get; } = name;
    public IRelayCommand<string> Summarize { get; } = summarize;

    [ObservableProperty]
    private bool _hasSummary;
}
