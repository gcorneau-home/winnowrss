using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Winnow.App.ViewModels;

/// <summary>What tabs need from the window that hosts them.</summary>
public interface ITabHost
{
    void CloseTab(TabViewModel tab);
    Task OpenArticleAsync(long articleId);
    Task ArchiveAsync(IReadOnlyCollection<long> articleIds);
    Task TrashAsync(IReadOnlyCollection<long> articleIds);
    Task RestoreAsync(IReadOnlyCollection<long> articleIds);
    void OnArticlePinnedChanged(long articleId, bool isPinned);
    void ReportError(Exception error);
}

/// <summary>A tab of the reading area (an article or search results).</summary>
public abstract partial class TabViewModel(ITabHost host) : ObservableObject
{
    protected ITabHost Host { get; } = host;

    /// <summary>When the tab was last shown; the least recently used tabs close first when there are too many.</summary>
    public DateTime LastActivated { get; set; } = DateTime.UtcNow;

    public abstract string Header { get; }
    public abstract string ToolTip { get; }

    // Let one shared content template pick the view for the tab type.
    public ArticleTabViewModel? AsArticle => this as ArticleTabViewModel;
    public SearchTabViewModel? AsSearch => this as SearchTabViewModel;
    public bool IsArticle => AsArticle is not null;
    public bool IsSearch => AsSearch is not null;

    [RelayCommand]
    private void Close() => Host.CloseTab(this);

    /// <summary>Called after a language change.</summary>
    public virtual void RefreshTexts()
    {
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(ToolTip));
    }

    // UI Automation names tab items after their data item.
    public override string ToString() => Header;
}
