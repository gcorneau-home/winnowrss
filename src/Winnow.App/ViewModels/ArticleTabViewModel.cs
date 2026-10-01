using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Localization;
using Winnow.App.Services;
using Winnow.Core.Models;
using Winnow.Core.Reading;
using Winnow.Core.Services;

namespace Winnow.App.ViewModels;

public sealed partial class ArticleTabViewModel : TabViewModel
{
    private readonly Article _article;
    private readonly ArticleService _articles;
    private readonly IShellService _shell;
    private readonly Localizer _loc;
    private bool _isPinned;
    private FilterStatus _filterStatus;
    private string? _filterCriterion;
    private string? _filterReason;
    private string? _filterModel;

    public ArticleTabViewModel(ArticleDetails details, ArticleService articles, IShellService shell, Localizer loc, ITabHost host)
        : base(host)
    {
        _article = details.Article;
        _articles = articles;
        _shell = shell;
        _loc = loc;
        _isPinned = _article.IsPinned;
        (_filterStatus, _filterCriterion, _filterReason, _filterModel) =
            (_article.FilterStatus, _article.FilterCriterion, _article.FilterReason, _article.FilterModel);
        _rating = _article.Rating;
        FeedTitle = details.FeedTitle;
        Tags = details.Tags;
        Html = ArticleHtml.Build(_article);
    }

    public long ArticleId => _article.Id;
    public long FeedId => _article.FeedId;
    public string FeedTitle { get; }
    public string Title => _article.Title;
    public string? Summary => _article.Summary;
    public IReadOnlyList<string> Tags { get; }
    public string? Link => _article.Link;
    public bool HasLink => _article.Link is not null;
    public bool IsArchived => _article.State == ArticleState.Archived;
    public bool IsTrashed => _article.State == ArticleState.Trashed;

    // Several articles of one feed can be open: the title tells the tabs apart, the tooltip adds the feed.
    public override string Header => Title;
    public override string ToolTip => $"{FeedTitle}\n{Title}";

    /// <summary>The page shown by the reading pane.</summary>
    public string Html { get; }

    /// <summary>"By author · date · Archived", formatted for the current language.</summary>
    public string MetaText => string.Join(" · ", new[]
    {
        _article.Author is { } author ? _loc.Format("Article_By", author) : null,
        _article.PublishedAt?.ToLocalTime().ToString("f", _loc.Culture),
        IsArchived ? _loc["Article_StateArchived"] : IsTrashed ? _loc["Article_StateTrashed"] : null,
    }.Where(s => s is not null));

    /// <summary>What the filter decided, and why (empty if the article was never filtered).</summary>
    public string VerdictText => FilterVerdictText.Describe(_loc, _filterStatus, _filterCriterion, _filterReason, _filterModel) ?? "";
    public bool IsFilteredOut => _filterStatus == FilterStatus.Rejected;

    public void ApplyVerdict(FilterStatus status, string? criterion, string? reason, string? model)
    {
        (_filterStatus, _filterCriterion, _filterReason, _filterModel) = (status, criterion, reason, model);
        OnPropertyChanged(nameof(VerdictText));
        OnPropertyChanged(nameof(IsFilteredOut));
    }

    // Bound two-way to a toggle button: clicking asks for the change, the property follows once it is saved.
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (value != _isPinned)
                _ = SetPinnedAsync(value);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThumbUp), nameof(IsThumbDown))]
    private Rating _rating;

    public bool IsThumbUp
    {
        get => Rating == Rating.Up;
        set => _ = RateAsync(Rating.Up);
    }

    public bool IsThumbDown
    {
        get => Rating == Rating.Down;
        set => _ = RateAsync(Rating.Down);
    }

    [RelayCommand(CanExecute = nameof(CanArchive))]
    private Task ArchiveAsync() => Host.ArchiveAsync([ArticleId]);

    private bool CanArchive() => !IsArchived;

    [RelayCommand]
    private Task DeleteAsync() => Host.TrashAsync([ArticleId]);

    [RelayCommand]
    private Task RestoreAsync() => Host.RestoreAsync([ArticleId]);

    [RelayCommand(CanExecute = nameof(HasLink))]
    private void OpenInBrowser() => _shell.OpenInBrowser(_article.Link!);

    /// <summary>A link clicked inside the article opens in the default browser.</summary>
    public void OpenLink(string url) => _shell.OpenInBrowser(url);

    /// <summary>For an archived article, the stored copy of an image it shows; null otherwise.</summary>
    public Task<ArchivedResource?> GetArchivedImageAsync(string url) =>
        IsArchived ? _articles.GetArchivedImageAsync(ArticleId, url) : Task.FromResult<ArchivedResource?>(null);

    public override void RefreshTexts()
    {
        base.RefreshTexts();
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(VerdictText));
    }

    private async Task SetPinnedAsync(bool isPinned)
    {
        try
        {
            await _articles.SetPinnedAsync(ArticleId, isPinned);
            _isPinned = isPinned;
            Host.OnArticlePinnedChanged(ArticleId, isPinned);
        }
        catch (Exception ex)
        {
            Host.ReportError(ex);
        }
        OnPropertyChanged(nameof(IsPinned)); // also resets the toggle if saving failed
    }

    private async Task RateAsync(Rating requested)
    {
        try
        {
            Rating = await _articles.ToggleRatingAsync(ArticleId, Rating, requested);
        }
        catch (Exception ex)
        {
            Host.ReportError(ex);
        }
        OnPropertyChanged(nameof(IsThumbUp));
        OnPropertyChanged(nameof(IsThumbDown));
    }
}
