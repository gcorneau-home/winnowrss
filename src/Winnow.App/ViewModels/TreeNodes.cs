using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.App.Localization;
using Winnow.Core.Models;

namespace Winnow.App.ViewModels;

public abstract partial class TreeNodeViewModel(TreeNodeViewModel? parent) : ObservableObject
{
    public TreeNodeViewModel? Parent { get; } = parent;
    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    /// <summary>Stable identity used to restore expansion and selection after a reload.</summary>
    public abstract string Key { get; }

    public abstract string Name { get; }

    // UI Automation (screen readers, UI tests) names tree items after their data item.
    public override string ToString() => Name;

    /// <summary>Extra state announced by screen readers (UI Automation ItemStatus).</summary>
    public virtual string AutomationStatus => "";

    /// <summary>The category this node belongs to, if any.</summary>
    public CategoryNodeViewModel? Category => this as CategoryNodeViewModel ?? Parent?.Category;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsExpandedChanged(bool value) => OnExpandedChanged(value);

    protected virtual void OnExpandedChanged(bool expanded) { }
}

/// <summary>A node whose children are loaded the first time it is expanded.</summary>
public abstract class LazyNodeViewModel : TreeNodeViewModel
{
    private readonly Func<LazyNodeViewModel, Task<IEnumerable<TreeNodeViewModel>>> _loader;
    private Task? _loading;

    protected LazyNodeViewModel(TreeNodeViewModel? parent, Func<LazyNodeViewModel, Task<IEnumerable<TreeNodeViewModel>>> loader)
        : base(parent)
    {
        _loader = loader;
        Children.Add(new PlaceholderNodeViewModel(this, Localizer.Instance["Node_Loading"]));
    }

    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    protected override void OnExpandedChanged(bool expanded)
    {
        if (expanded)
            _ = EnsureLoadedAsync();
    }

    private async Task LoadAsync()
    {
        IReadOnlyList<TreeNodeViewModel> items;
        try
        {
            items = (await _loader(this)).ToList();
        }
        catch (Exception ex)
        {
            items = [new PlaceholderNodeViewModel(this, Localizer.Instance.Format("Node_LoadError", ex.Message))];
        }

        Children.Clear();
        foreach (var item in items)
            Children.Add(item);
    }
}

public sealed partial class CategoryNodeViewModel(Category category) : TreeNodeViewModel(null)
{
    public Category Model { get; } = category;
    public override string Key => $"category/{Model.Id}";
    public override string Name => Model.Name;

    [ObservableProperty]
    private int _unreadCount;

    public void UpdateUnreadCount() => UnreadCount = Children.OfType<FeedNodeViewModel>().Sum(f => f.UnreadCount);
}

public sealed partial class FeedNodeViewModel(
    Feed feed,
    int unreadCount,
    CategoryNodeViewModel parent,
    Func<LazyNodeViewModel, Task<IEnumerable<TreeNodeViewModel>>> loader)
    : LazyNodeViewModel(parent, loader)
{
    public Feed Model { get; } = feed;
    public override string Key => $"feed/{Model.Id}";
    public override string Name => Model.Title;
    public bool HasError => Model.LastError is not null;
    public string ToolTip => HasError
        ? $"{Model.Url}\n{Localizer.Instance.Format("Feed_LastRefreshFailed", Model.LastError)}"
        : Model.Url;

    [ObservableProperty]
    private int _unreadCount = unreadCount;
}

public sealed partial class ArticleNodeViewModel(ArticleHeadline headline, TreeNodeViewModel parent) : TreeNodeViewModel(parent)
{
    public ArticleHeadline Model { get; } = headline;
    public override string Key => $"article/{Model.Id}";
    public override string Name => Model.Title;

    public string ToolTip => Verdict is { } verdict ? $"{Origin}\n{verdict}" : Origin;

    private string Origin => Model.PublishedAt is { } date
        ? $"{Model.FeedTitle} · {date.ToLocalTime():g}"
        : Model.FeedTitle;

    // ----- Filter verdict (updated live while the filter queue runs) -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRejected), nameof(IsPending), nameof(HasFilterError), nameof(ToolTip), nameof(AutomationStatus), nameof(CountsAsUnread))]
    private FilterStatus _filterStatus = headline.FilterStatus;

    private string? _filterCriterion = headline.FilterCriterion;
    private string? _filterReason = headline.FilterReason;
    private string? _filterModel = headline.FilterModel;

    public bool IsRejected => FilterStatus == FilterStatus.Rejected;
    public bool IsPending => FilterStatus == FilterStatus.Pending;
    public bool HasFilterError => FilterStatus == FilterStatus.Error;

    /// <summary>Does this article count in its feed's unread badge?</summary>
    public bool CountsAsUnread => !IsRead && !IsRejected;

    private string? Verdict => FilterVerdictText.Describe(Localizer.Instance, FilterStatus, _filterCriterion, _filterReason, _filterModel);

    public override string AutomationStatus => Verdict ?? "";

    public void ApplyVerdict(FilterStatus status, string? criterion, string? reason, string? model)
    {
        _filterCriterion = criterion;
        _filterReason = reason;
        _filterModel = model;
        FilterStatus = status;
        OnPropertyChanged(nameof(ToolTip));
        OnPropertyChanged(nameof(AutomationStatus));
    }

    public bool IsArchived => Model.State == ArticleState.Archived;
    public bool IsInTrash => Model.State == ArticleState.Trashed;

    /// <summary>Part of the multi-selection (Ctrl/Shift+click) that article actions apply to.</summary>
    [ObservableProperty]
    private bool _isMultiSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountsAsUnread))]
    private bool _isRead = headline.IsRead;

    [ObservableProperty]
    private bool _isPinned = headline.IsPinned;
}

public sealed class ArchiveNodeViewModel(
    CategoryNodeViewModel parent,
    Func<LazyNodeViewModel, Task<IEnumerable<TreeNodeViewModel>>> loader)
    : LazyNodeViewModel(parent, loader)
{
    public override string Key => $"archive/{Category!.Model.Id}";
    public override string Name => Localizer.Instance["Node_Archive"];
}

public sealed class TrashNodeViewModel(Func<LazyNodeViewModel, Task<IEnumerable<TreeNodeViewModel>>> loader)
    : LazyNodeViewModel(null, loader)
{
    public override string Key => "trash";
    public override string Name => Localizer.Instance["Node_Trash"];
}

public sealed class PlaceholderNodeViewModel(TreeNodeViewModel parent, string text) : TreeNodeViewModel(parent)
{
    public override string Key => $"{Parent!.Key}/placeholder";
    public override string Name => text;
}

public enum ArticleSelectMode
{
    Single,
    Toggle,
    Range,
    AddRange,
}
