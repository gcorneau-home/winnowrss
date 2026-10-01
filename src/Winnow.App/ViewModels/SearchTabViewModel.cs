using CommunityToolkit.Mvvm.Input;
using Winnow.App.Localization;
using Winnow.Core.Models;

namespace Winnow.App.ViewModels;

/// <summary>Results of a global search; one such tab is reused for each new search.</summary>
public sealed class SearchTabViewModel : TabViewModel
{
    private readonly Localizer _loc;

    public SearchTabViewModel(string query, IReadOnlyList<SearchHit> hits, Localizer loc, ITabHost host) : base(host)
    {
        _loc = loc;
        Query = query;
        Results = hits.Select(h => new SearchResultViewModel(h, loc, host)).ToList();
    }

    public string Query { get; }
    public IReadOnlyList<SearchResultViewModel> Results { get; }
    public bool HasResults => Results.Count > 0;

    public override string Header => _loc.Format("Search_TabHeader", Query);
    public override string ToolTip => Summary;

    public string Summary => HasResults
        ? _loc.Format("Search_ResultCount", Results.Count, Query)
        : _loc.Format("Search_NoResults", Query);

    public override void RefreshTexts()
    {
        base.RefreshTexts();
        OnPropertyChanged(nameof(Summary));
        foreach (var result in Results)
            result.RefreshTexts();
    }
}

public sealed partial class SearchResultViewModel(SearchHit hit, Localizer loc, ITabHost host)
    : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Title => hit.Title;
    public string Snippet => hit.Snippet;

    /// <summary>"Feed · date · Archived".</summary>
    public string Details => string.Join(" · ", new[]
    {
        hit.FeedTitle,
        hit.PublishedAt?.ToLocalTime().ToString("d", loc.Culture),
        hit.State == ArticleState.Archived ? loc["Article_StateArchived"] : null,
    }.Where(s => s is not null));

    [RelayCommand]
    private Task OpenAsync() => host.OpenArticleAsync(hit.Id);

    public void RefreshTexts() => OnPropertyChanged(nameof(Details));

    public override string ToString() => Title;
}
