using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Winnow.App.Localization;
using Winnow.App.Services;
using Winnow.Core.Filtering;
using Winnow.Core.Models;
using Winnow.Core.Services;

namespace Winnow.App.ViewModels;

public sealed partial class MainViewModel(
    CategoryService categories,
    FeedService feeds,
    ArticleService articles,
    FeedRefreshService refresher,
    ArchiveService archiver,
    SearchService search,
    RetentionService retention,
    FilterQueueService filterQueue,
    FilterCriteriaService filterCriteria,
    OllamaArticleFilter ollama,
    SettingsService settings,
    Localizer loc,
    IDialogService dialogs,
    IShellService shell,
    ThemeService themes,
    Winnow.Core.Theming.OpenVsxClient themeCatalog,
    ILogger<MainViewModel> logger) : ObservableObject, ITabHost
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RetentionInterval = TimeSpan.FromDays(1);

    private DateTime _lastRetentionRun = DateTime.MinValue;
    private bool _loadingPreferences;
    private ArticleNodeViewModel? _selectionAnchor;

    public ObservableCollection<TreeNodeViewModel> Roots { get; } = [];

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseSelectedTabCommand))]
    private TabViewModel? _selectedTab;

    partial void OnSelectedTabChanged(TabViewModel? value)
    {
        if (value is not null)
            value.LastActivated = DateTime.UtcNow;
    }

    [ObservableProperty]
    private string _searchText = "";

    /// <summary>Show articles the filter rejected (grayed out); when off they are left out of the tree.</summary>
    [ObservableProperty]
    private bool _showRejected = true;

    /// <summary>
    /// Show read articles; when off they are left out of the tree (pinned ones stay). An article read while it is
    /// off stays in place until the feed is reloaded, so the tree does not shift under the cursor.
    /// </summary>
    [ObservableProperty]
    private bool _showRead = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameCategoryCommand), nameof(DeleteCategoryCommand),
        nameof(EditFeedCommand), nameof(DeleteFeedCommand), nameof(RefreshFeedCommand))]
    private TreeNodeViewModel? _selectedNode;

    // Serializes tree rebuilds (refresh, user actions and language changes can overlap).
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    // What the status bar shows, kept as a resource key so a language change can retranslate it.
    private (string Key, object?[] Args) _statusSource = ("Status_Ready", []);

    [ObservableProperty]
    private string _status = loc["Status_Ready"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshAllCommand), nameof(RefreshFeedCommand))]
    private bool _isRefreshing;

    /// <summary>Two-letter code of the interface language; changing it retranslates the UI live and saves the preference.</summary>
    public string LanguageCode
    {
        get => loc.LanguageCode;
        set
        {
            if (!string.IsNullOrEmpty(value) && value != loc.LanguageCode)
                _ = ChangeLanguageAsync(value);
        }
    }

    /// <summary>"system", "light" or "dark"; changing it restyles the app live and saves the preference.</summary>
    public string ThemeCode
    {
        get => themes.Current;
        set
        {
            if (string.IsNullOrEmpty(value) || value == themes.Current)
                return;
            themes.Apply(value);
            OnPropertyChanged();
            _ = TryAsync(() => settings.SetUiThemeAsync(value));
        }
    }

    [RelayCommand]
    private void OpenThemes()
    {
        var window = new ThemesViewModel(themes, themeCatalog, dialogs, loc, code => ThemeCode = code);
        window.Load();
        dialogs.ShowThemes(window);
    }

    private async Task ChangeLanguageAsync(string code)
    {
        loc.SetLanguage(code);
        OnPropertyChanged(nameof(LanguageCode));
        Status = loc.Format(_statusSource.Key, _statusSource.Args);
        try
        {
            foreach (var tab in Tabs)
                tab.RefreshTexts();
            await settings.SetUiLanguageAsync(code);
            await ReloadTreeAsync(); // node tooltips embed formatted dates and texts
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Changing the interface language to {Language} failed", code);
            dialogs.ShowError(loc.Format("Error_LanguageNotSaved", ex.Message));
        }
    }

    private void SetStatus(string key, params object?[] args)
    {
        _statusSource = (key, args);
        Status = loc.Format(key, args);
    }

    public async Task InitializeAsync()
    {
        _loadingPreferences = true; // restoring the saved choice: nothing to save or reload yet
        ShowRejected = !await settings.GetHideRejectedAsync();
        ShowRead = !await settings.GetHideReadAsync();
        _loadingPreferences = false;
        await ReloadTreeAsync(expandCategories: true);
        _ = RunFilterAsync(); // articles left pending by the previous session
        _ = AutoRefreshLoopAsync();
    }

    // ----- Refresh -----

    private async Task AutoRefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(AutoRefreshInterval);
        do
        {
            if (DateTime.UtcNow - _lastRetentionRun > RetentionInterval)
                await RunRetentionAsync();
            if (!IsRefreshing)
                await RefreshAllAsync();
        }
        while (await timer.WaitForNextTickAsync());
    }

    private async Task RunRetentionAsync()
    {
        _lastRetentionRun = DateTime.UtcNow;
        try
        {
            if (await Task.Run(() => retention.RunAsync()) > 0)
                await ReloadTreeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Retention failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAllAsync()
    {
        IsRefreshing = true;
        SetStatus("Status_Refreshing");
        try
        {
            var results = await Task.Run(() => refresher.RefreshAllAsync());
            await ReloadTreeAsync();
            ShowRefreshSummary(results);
            _ = RunFilterAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Refreshing all feeds failed");
            SetStatus("Status_RefreshFailed", ex.Message);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshFeed))]
    private async Task RefreshFeedAsync(FeedNodeViewModel? node)
    {
        if ((node ?? SelectedNode) is not FeedNodeViewModel feed)
            return;

        IsRefreshing = true;
        SetStatus("Status_RefreshingFeed", feed.Name);
        try
        {
            var result = await Task.Run(() => refresher.RefreshAsync(feed.Model.Id));
            await ReloadTreeAsync();
            ShowRefreshSummary([result]);
            _ = RunFilterAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Refreshing feed {FeedId} failed", feed.Model.Id);
            SetStatus("Status_RefreshFailed", ex.Message);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private bool CanRefresh() => !IsRefreshing;
    private bool CanRefreshFeed(FeedNodeViewModel? node) => !IsRefreshing && (node ?? SelectedNode) is FeedNodeViewModel;

    private void ShowRefreshSummary(IReadOnlyCollection<FeedRefreshResult> results)
    {
        var added = results.Sum(r => r.Added);
        var failed = results.Count(r => !r.Succeeded);
        if (failed > 0)
            SetStatus("Status_RefreshedWithFailures", DateTime.Now, added, failed);
        else
            SetStatus("Status_Refreshed", DateTime.Now, added);
    }

    // ----- Categories -----

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        if (dialogs.Prompt(loc["Dialog_NewCategory_Title"], loc["Dialog_Name"]) is not { } name)
            return;

        await TryAsync(async () =>
        {
            var id = await categories.AddAsync(name);
            await ReloadTreeAsync(expandKeys: [$"category/{id}"]);
        });
    }

    [RelayCommand(CanExecute = nameof(IsCategory))]
    private async Task RenameCategoryAsync(CategoryNodeViewModel? node)
    {
        if ((node ?? SelectedNode) is not CategoryNodeViewModel category
            || dialogs.Prompt(loc["Dialog_RenameCategory_Title"], loc["Dialog_Name"], category.Name) is not { } name)
            return;

        await TryAsync(async () =>
        {
            await categories.RenameAsync(category.Model.Id, name);
            await ReloadTreeAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(IsCategory))]
    private async Task DeleteCategoryAsync(CategoryNodeViewModel? node)
    {
        if ((node ?? SelectedNode) is not CategoryNodeViewModel category)
            return;

        var archived = await categories.CountArchivedArticlesAsync(category.Model.Id);
        var feedCount = category.Children.OfType<FeedNodeViewModel>().Count();
        var message = loc.Format("Dialog_DeleteCategory_Message", category.Name, feedCount);
        if (archived > 0)
            message += "\n\n" + loc.Format("Dialog_DeleteCategory_Archived", archived);
        if (!dialogs.Confirm(loc["Dialog_DeleteCategory_Title"], message))
            return;

        await TryAsync(async () =>
        {
            await categories.DeleteAsync(category.Model.Id);
            CloseTabsOfFeeds(category.Children.OfType<FeedNodeViewModel>().Select(f => f.Model.Id).ToList());
            await ReloadTreeAsync();
        });
    }

    private bool IsCategory(CategoryNodeViewModel? node) => (node ?? SelectedNode) is CategoryNodeViewModel;

    // ----- Feeds -----

    [RelayCommand]
    private async Task AddFeedAsync(TreeNodeViewModel? node)
    {
        var category = (node ?? SelectedNode)?.Category ?? Roots.OfType<CategoryNodeViewModel>().FirstOrDefault();
        if (category is null)
        {
            dialogs.ShowError(loc["Dialog_CreateCategoryFirst"]);
            return;
        }
        if (dialogs.Prompt(loc.Format("Dialog_AddFeed_Title", category.Name), loc["Dialog_FeedAddress"]) is not { } url)
            return;

        SetStatus("Status_AddingFeed");
        await TryAsync(async () =>
        {
            var feed = await Task.Run(() => feeds.AddAsync(category.Model.Id, url));
            await ReloadTreeAsync(expandKeys: [category.Key]);
            SetStatus("Status_FeedAdded", feed.Title);
            _ = RunFilterAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(IsFeed))]
    private async Task EditFeedAsync(FeedNodeViewModel? node)
    {
        if ((node ?? SelectedNode) is not FeedNodeViewModel feed
            || dialogs.Prompt(loc["Dialog_EditFeed_Title"], [(loc["Dialog_Title"], feed.Model.Title), (loc["Dialog_Address"], feed.Model.Url)]) is not [var title, var url])
            return;

        await TryAsync(async () =>
        {
            await feeds.UpdateAsync(feed.Model.Id, title, url);
            await ReloadTreeAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(IsFeed))]
    private async Task DeleteFeedAsync(FeedNodeViewModel? node)
    {
        if ((node ?? SelectedNode) is not FeedNodeViewModel feed)
            return;

        var archived = await feeds.CountArchivedArticlesAsync(feed.Model.Id);
        var message = loc.Format("Dialog_DeleteFeed_Message", feed.Name);
        if (archived > 0)
            message += "\n\n" + loc.Format("Dialog_DeleteFeed_Archived", archived);
        if (!dialogs.Confirm(loc["Dialog_DeleteFeed_Title"], message))
            return;

        await TryAsync(async () =>
        {
            await feeds.DeleteAsync(feed.Model.Id);
            CloseTabsOfFeeds([feed.Model.Id]);
            await ReloadTreeAsync();
        });
    }

    private bool IsFeed(FeedNodeViewModel? node) => (node ?? SelectedNode) is FeedNodeViewModel;

    /// <summary>Called by the view when a feed is dropped on a category.</summary>
    public async Task MoveFeedAsync(FeedNodeViewModel feed, CategoryNodeViewModel target)
    {
        if (feed.Category == target)
            return;

        await TryAsync(async () =>
        {
            await feeds.MoveToCategoryAsync(feed.Model.Id, target.Model.Id);
            await ReloadTreeAsync(expandKeys: [target.Key]);
            SetStatus("Status_FeedMoved", feed.Name, target.Name);
        });
    }

    // ----- Article tabs -----

    /// <summary>Opens an article from the tree in a tab (or switches to it).</summary>
    [RelayCommand]
    private Task OpenArticleNodeAsync(ArticleNodeViewModel? node) =>
        node is null ? Task.CompletedTask : OpenArticleAsync(node.Model.Id);

    /// <summary>Opens an article in a tab (or switches to it) and marks it read.</summary>
    public async Task OpenArticleAsync(long articleId)
    {
        if (ArticleTab(articleId) is { } open)
        {
            SelectedTab = open;
            return;
        }

        await TryAsync(async () =>
        {
            if (await articles.OpenAsync(articleId) is not { } details)
                return;
            // A second request for the same article may have finished first while we were loading.
            if (ArticleTab(articleId) is { } alreadyOpen)
            {
                SelectedTab = alreadyOpen;
                return;
            }
            var tab = new ArticleTabViewModel(details, articles, shell, loc, this);
            Tabs.Add(tab);
            SelectedTab = tab;
            MarkReadInTree(articleId);
            EnforceTabLimit();
        });
    }

    /// <summary>Closes the least recently used article tabs beyond the limit set in Settings (search tabs stay).</summary>
    private void EnforceTabLimit()
    {
        var articleTabs = Tabs.OfType<ArticleTabViewModel>().OrderBy(t => t.LastActivated).ToList();
        foreach (var tab in articleTabs.Take(Math.Max(0, articleTabs.Count - DisplaySettings.Instance.MaxOpenTabs)))
            CloseTab(tab);
    }

    /// <summary>Opens a feed's unread (not filtered-out) articles, newest last so it ends up selected, within the tab limit.</summary>
    [RelayCommand]
    private async Task OpenUnreadArticlesAsync(FeedNodeViewModel? feed)
    {
        if ((feed ?? SelectedNode) is not FeedNodeViewModel node)
            return;
        var unread = (await articles.GetActiveHeadlinesAsync(node.Model.Id))
            .Where(h => !h.IsRead && h.FilterStatus != FilterStatus.Rejected)
            .ToList();
        var toOpen = unread.Take(DisplaySettings.Instance.MaxOpenTabs).Reverse().ToList();
        foreach (var headline in toOpen)
            await OpenArticleAsync(headline.Id);

        if (toOpen.Count < unread.Count)
            SetStatus("Status_OpenedUnreadLimited", toOpen.Count, unread.Count);
        else
            SetStatus("Status_OpenedUnread", toOpen.Count);
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var window = new SettingsViewModel(settings, DisplaySettings.Instance);
        await window.LoadAsync();
        if (dialogs.ShowSettings(window))
            EnforceTabLimit();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void CloseSelectedTab()
    {
        if (SelectedTab is { } tab)
            CloseTab(tab);
    }

    private bool HasSelectedTab() => SelectedTab is not null;

    [RelayCommand]
    private void CloseTabFromMenu(TabViewModel? tab)
    {
        if (tab is not null)
            CloseTab(tab);
    }

    [RelayCommand]
    private void CloseOtherTabs(TabViewModel? keep)
    {
        foreach (var tab in Tabs.Where(t => t != keep).ToList())
            Tabs.Remove(tab);
        SelectedTab = keep;
    }

    [RelayCommand]
    private void CloseAllTabs() => Tabs.Clear();

    /// <summary>Closes the tabs of articles that no longer exist (their feed or category was deleted).</summary>
    private void CloseTabsOfFeeds(IReadOnlyCollection<long> feedIds)
    {
        foreach (var tab in Tabs.OfType<ArticleTabViewModel>().Where(t => feedIds.Contains(t.FeedId)).ToList())
            CloseTab(tab);
    }

    public void CloseTab(TabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;
        Tabs.RemoveAt(index);
        if (SelectedTab is null && Tabs.Count > 0)
            SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
    }

    private ArticleTabViewModel? ArticleTab(long articleId) =>
        Tabs.OfType<ArticleTabViewModel>().FirstOrDefault(t => t.ArticleId == articleId);

    /// <summary>Reloads open tabs of articles whose state changed (archived, restored).</summary>
    private async Task RefreshArticleTabsAsync(IEnumerable<long> articleIds)
    {
        foreach (var id in articleIds)
        {
            if (ArticleTab(id) is not { } tab || await articles.OpenAsync(id) is not { } details)
                continue;
            var wasSelected = SelectedTab == tab;
            var fresh = new ArticleTabViewModel(details, articles, shell, loc, this);
            Tabs[Tabs.IndexOf(tab)] = fresh;
            if (wasSelected || SelectedTab is null)
                SelectedTab = fresh;
        }
    }

    public void OnArticlePinnedChanged(long articleId, bool isPinned)
    {
        foreach (var node in ArticleNodes(articleId))
            node.IsPinned = isPinned;
    }

    public void ReportError(Exception error)
    {
        logger.LogError(error, "Article action failed");
        dialogs.ShowError(loc.Format("Error_OperationFailed", error.Message));
    }

    private void MarkReadInTree(long articleId)
    {
        foreach (var node in ArticleNodes(articleId).Where(n => !n.IsRead).ToList())
            UpdateUnreadBadge(node, () => node.IsRead = true);
    }

    /// <summary>Applies a change to an article node and adjusts its feed's unread badge if it now counts differently.</summary>
    private static void UpdateUnreadBadge(ArticleNodeViewModel node, Action change)
    {
        var counted = node.CountsAsUnread;
        change();
        if (node.Parent is FeedNodeViewModel feed && counted != node.CountsAsUnread)
        {
            feed.UnreadCount = Math.Max(0, feed.UnreadCount + (node.CountsAsUnread ? 1 : -1));
            feed.Category?.UpdateUnreadCount();
        }
    }

    private IEnumerable<ArticleNodeViewModel> ArticleNodes(long articleId) =>
        AllNodes().OfType<ArticleNodeViewModel>().Where(n => n.Model.Id == articleId);

    // ----- Article actions: archive, trash, restore -----

    [RelayCommand]
    private Task ArchiveArticlesAsync(ArticleNodeViewModel? node) => ArchiveAsync(SelectedArticleIds(node));

    [RelayCommand]
    private Task TrashArticlesAsync(ArticleNodeViewModel? node) => TrashAsync(SelectedArticleIds(node));

    [RelayCommand]
    private Task RestoreArticlesAsync(ArticleNodeViewModel? node) => RestoreAsync(SelectedArticleIds(node));

    public async Task ArchiveAsync(IReadOnlyCollection<long> articleIds)
    {
        if (articleIds.Count == 0)
            return;
        SetStatus("Status_Archiving");
        await TryAsync(async () =>
        {
            var result = await Task.Run(() => archiver.ArchiveAsync(articleIds));
            await ReloadTreeAsync();
            await RefreshArticleTabsAsync(articleIds);
            if (result.ImagesFailed > 0)
                SetStatus("Status_ArchivedWithFailures", result.Articles, result.ImagesSaved, result.ImagesFailed);
            else
                SetStatus("Status_Archived", result.Articles, result.ImagesSaved);
        });
    }

    public async Task TrashAsync(IReadOnlyCollection<long> articleIds)
    {
        if (articleIds.Count == 0)
            return;
        await TryAsync(async () =>
        {
            await articles.TrashAsync(articleIds);
            foreach (var tab in Tabs.OfType<ArticleTabViewModel>().Where(t => articleIds.Contains(t.ArticleId)).ToList())
                CloseTab(tab);
            await ReloadTreeAsync();
            SetStatus("Status_ArticlesTrashed", articleIds.Count);
        });
    }

    public async Task RestoreAsync(IReadOnlyCollection<long> articleIds)
    {
        if (articleIds.Count == 0)
            return;
        await TryAsync(async () =>
        {
            await articles.RestoreAsync(articleIds);
            await ReloadTreeAsync();
            await RefreshArticleTabsAsync(articleIds);
            SetStatus("Status_ArticlesRestored", articleIds.Count);
        });
    }

    [RelayCommand]
    private async Task EmptyTrashAsync()
    {
        var count = (await articles.GetTrashedHeadlinesAsync()).Count;
        if (count == 0 || !dialogs.Confirm(loc["Dialog_EmptyTrash_Title"], loc.Format("Dialog_EmptyTrash_Message", count)))
            return;

        await TryAsync(async () =>
        {
            var purged = await articles.EmptyTrashAsync();
            foreach (var tab in Tabs.OfType<ArticleTabViewModel>().Where(t => t.IsTrashed).ToList())
                CloseTab(tab);
            await ReloadTreeAsync();
            SetStatus("Status_TrashEmptied", purged);
        });
    }

    // ----- Multi-selection in the tree -----

    /// <summary>
    /// Called by the view on clicks: plain click selects one article, Ctrl toggles, Shift selects a range,
    /// Ctrl+Shift adds a range to the selection.
    /// </summary>
    public void SelectArticle(ArticleNodeViewModel node, ArticleSelectMode mode)
    {
        if (mode is ArticleSelectMode.Range or ArticleSelectMode.AddRange
            && _selectionAnchor?.Parent is { } parent && parent == node.Parent)
        {
            var siblings = parent.Children.OfType<ArticleNodeViewModel>().ToList();
            var (from, to) = (siblings.IndexOf(_selectionAnchor), siblings.IndexOf(node));
            if (mode == ArticleSelectMode.Range)
                ClearArticleSelection();
            for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                siblings[i].IsMultiSelected = true;
            return;
        }

        if (mode == ArticleSelectMode.Toggle)
        {
            node.IsMultiSelected = !node.IsMultiSelected;
        }
        else
        {
            ClearArticleSelection();
            node.IsMultiSelected = true;
        }
        _selectionAnchor = node;
    }

    public void ClearArticleSelection()
    {
        foreach (var node in AllNodes().OfType<ArticleNodeViewModel>())
            node.IsMultiSelected = false;
    }

    /// <summary>The multi-selection if <paramref name="node"/> is part of it (or no node is given), else just that article.</summary>
    private IReadOnlyCollection<long> SelectedArticleIds(ArticleNodeViewModel? node)
    {
        if (node is not null && !node.IsMultiSelected)
            return [node.Model.Id];

        var selected = AllNodes().OfType<ArticleNodeViewModel>().Where(n => n.IsMultiSelected).Select(n => n.Model.Id).ToList();
        if (selected.Count == 0 && SelectedNode is ArticleNodeViewModel current)
            selected.Add(current.Model.Id);
        return selected;
    }

    // ----- Filter -----

    /// <summary>Judges pending articles in the background; the tree and open tabs follow each verdict.</summary>
    private async Task RunFilterAsync()
    {
        var progress = new Progress<FilterProgress>(OnFilterProgress); // reports come back on the UI thread
        try
        {
            var result = await Task.Run(() => filterQueue.RunPendingAsync(progress));
            if (result.Unavailable is { } why)
                SetStatus("Status_FilterUnavailable", why);
            else if (result.Errors > 0)
                SetStatus("Status_FilterDoneWithErrors", result.Kept, result.Rejected, result.Errors);
            else if (result.Total > 0)
                SetStatus("Status_FilterDone", result.Kept, result.Rejected);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Filtering failed");
            SetStatus("Status_FilterUnavailable", ex.Message);
        }
    }

    private void OnFilterProgress(FilterProgress progress)
    {
        SetStatus("Status_Filtering", progress.Remaining);
        foreach (var node in ArticleNodes(progress.ArticleId).ToList())
        {
            node.ApplyVerdict(progress.Status, progress.Criterion, progress.Reason, progress.Model);
            if (node.IsRejected && !ShowRejected && node.Parent is FeedNodeViewModel feed)
                feed.Children.Remove(node);
        }
        ArticleTab(progress.ArticleId)?.ApplyVerdict(progress.Status, progress.Criterion, progress.Reason, progress.Model);

        // Re-read the badges: the article may be in a feed that was never expanded, so it has no node to adjust.
        _ = RefreshUnreadCountsAsync();
    }

    private async Task RefreshUnreadCountsAsync()
    {
        try
        {
            var unread = await articles.GetUnreadCountsAsync();
            foreach (var category in Roots.OfType<CategoryNodeViewModel>())
            {
                foreach (var feed in category.Children.OfType<FeedNodeViewModel>())
                    feed.UnreadCount = unread.GetValueOrDefault(feed.Model.Id);
                category.UpdateUnreadCount();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Refreshing unread counts failed");
        }
    }

    partial void OnShowRejectedChanged(bool value)
    {
        if (_loadingPreferences)
            return;
        _ = TryAsync(async () =>
        {
            await settings.SetHideRejectedAsync(!value);
            await ReloadTreeAsync();
        });
    }

    partial void OnShowReadChanged(bool value)
    {
        if (_loadingPreferences)
            return;
        _ = TryAsync(async () =>
        {
            await settings.SetHideReadAsync(!value);
            await ReloadTreeAsync();
        });
    }

    [RelayCommand]
    private async Task OpenFilterSettingsAsync()
    {
        var window = new FilterSettingsViewModel(settings, filterCriteria, feeds, ollama, loc);
        await window.LoadAsync();
        if (!dialogs.ShowFilterSettings(window))
            return;

        await TryAsync(async () =>
        {
            if (window.RefilterAfterSave)
                await filterQueue.RequeueUnreadAsync();
            await ReloadTreeAsync();
            _ = RunFilterAsync();
        });
    }

    // ----- Search -----

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = SearchText.Trim();
        if (query.Length == 0)
            return;

        await TryAsync(async () =>
        {
            var hits = await search.SearchAsync(query);
            var tab = new SearchTabViewModel(query, hits, loc, this);
            // One search tab, replaced by each new search.
            if (Tabs.OfType<SearchTabViewModel>().FirstOrDefault() is { } previous)
                Tabs[Tabs.IndexOf(previous)] = tab;
            else
                Tabs.Insert(0, tab);
            SelectedTab = tab;
        });
    }

    // ----- Tree -----

    private async Task ReloadTreeAsync(bool expandCategories = false, IReadOnlyCollection<string>? expandKeys = null)
    {
        await _reloadGate.WaitAsync();
        try
        {
            await ReloadTreeCoreAsync(expandCategories, expandKeys);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task ReloadTreeCoreAsync(bool expandCategories, IReadOnlyCollection<string>? expandKeys)
    {
        var expanded = AllNodes().Where(n => n.IsExpanded).Select(n => n.Key).ToHashSet();
        expanded.UnionWith(expandKeys ?? []);
        var selectedKey = SelectedNode?.Key;

        var allCategories = await categories.GetAllAsync();
        var allFeeds = await feeds.GetAllAsync();
        var unread = await articles.GetUnreadCountsAsync();

        Roots.Clear();
        foreach (var category in allCategories)
        {
            var node = new CategoryNodeViewModel(category);
            foreach (var feed in allFeeds.Where(f => f.CategoryId == category.Id))
                node.Children.Add(new FeedNodeViewModel(feed, unread.GetValueOrDefault(feed.Id), node, LoadFeedArticlesAsync));
            node.Children.Add(new ArchiveNodeViewModel(node, LoadArchiveAsync));
            node.UpdateUnreadCount();
            Roots.Add(node);

            if (expandCategories)
                expanded.Add(node.Key);
        }
        Roots.Add(new TrashNodeViewModel(LoadTrashAsync));

        foreach (var root in Roots)
            await RestoreExpansionAsync(root, expanded);

        if (selectedKey is not null && AllNodes().FirstOrDefault(n => n.Key == selectedKey) is { } selected)
            selected.IsSelected = true;
    }

    private static async Task RestoreExpansionAsync(TreeNodeViewModel node, HashSet<string> expandedKeys)
    {
        if (!expandedKeys.Contains(node.Key))
            return;
        if (node is LazyNodeViewModel lazy)
            await lazy.EnsureLoadedAsync();
        node.IsExpanded = true;
        foreach (var child in node.Children.ToList())
            await RestoreExpansionAsync(child, expandedKeys);
    }

    private IEnumerable<TreeNodeViewModel> AllNodes()
    {
        var stack = new Stack<TreeNodeViewModel>(Roots);
        while (stack.TryPop(out var node))
        {
            yield return node;
            foreach (var child in node.Children)
                stack.Push(child);
        }
    }

    private async Task<IEnumerable<TreeNodeViewModel>> LoadFeedArticlesAsync(LazyNodeViewModel node) =>
        (await articles.GetActiveHeadlinesAsync(((FeedNodeViewModel)node).Model.Id))
            .Where(h => ShowRejected || h.FilterStatus != FilterStatus.Rejected)
            .Where(h => ShowRead || !h.IsRead || h.IsPinned)
            .Select(h => new ArticleNodeViewModel(h, node));

    private async Task<IEnumerable<TreeNodeViewModel>> LoadArchiveAsync(LazyNodeViewModel node) =>
        (await articles.GetArchivedHeadlinesAsync(node.Category!.Model.Id))
            .Select(h => new ArticleNodeViewModel(h, node));

    private async Task<IEnumerable<TreeNodeViewModel>> LoadTrashAsync(LazyNodeViewModel node) =>
        (await articles.GetTrashedHeadlinesAsync())
            .Select(h => new ArticleNodeViewModel(h, node));

    /// <summary>Runs an action; failures reset the status and are shown in a dialog (unexpected ones are also logged).</summary>
    private async Task TryAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (WinnowException ex)
        {
            SetStatus("Status_Ready");
            dialogs.ShowError(loc.Error(ex));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Operation failed");
            SetStatus("Status_Ready");
            dialogs.ShowError(loc.Format("Error_OperationFailed", ex.Message));
        }
    }
}
