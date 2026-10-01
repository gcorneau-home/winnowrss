using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Localization;
using Winnow.Core.Filtering;
using Winnow.Core.Models;
using Winnow.Core.Services;

namespace Winnow.App.ViewModels;

/// <summary>
/// The Filter window: on/off, Ollama address and model, interests and exclusions.
/// Changes are applied only on Save, then the unread articles can be judged again.
/// </summary>
public sealed partial class FilterSettingsViewModel(
    SettingsService settings,
    FilterCriteriaService criteria,
    FeedService feeds,
    OllamaArticleFilter ollama,
    Localizer loc) : ObservableObject
{
    private IReadOnlyList<FilterCriterion> _savedCriteria = [];
    private bool _loading;

    public ObservableCollection<CriterionRowViewModel> Interests { get; } = [];
    public ObservableCollection<CriterionRowViewModel> Exclusions { get; } = [];
    public ObservableCollection<CriterionRowViewModel> Keywords { get; } = [];
    public ObservableCollection<TrustedFeedRowViewModel> Feeds { get; } = [];
    public ObservableCollection<string> AvailableModels { get; } = [];

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private string _endpoint = FilterSettings.DefaultEndpoint;

    [ObservableProperty]
    private string _model = FilterSettings.DefaultModel;

    [ObservableProperty]
    private string _connectionStatus = "";

    /// <summary>Checked automatically when the criteria change or the filter is turned on.</summary>
    [ObservableProperty]
    private bool _refilterAfterSave;

    [ObservableProperty]
    private string _errorText = "";

    /// <summary>Raised with true after saving, false on cancel; the window closes itself.</summary>
    public event Action<bool>? CloseRequested;

    public async Task LoadAsync()
    {
        _loading = true;
        var filter = await settings.GetFilterSettingsAsync();
        (Enabled, Endpoint, Model) = (filter.Enabled, filter.Endpoint, filter.Model);

        _savedCriteria = await criteria.GetAllAsync();
        foreach (var criterion in _savedCriteria)
            ListFor(criterion.Kind).Add(NewRow(criterion.Id, criterion.Kind, criterion.Text, criterion.Enabled));
        foreach (var feed in await feeds.GetAllAsync())
        {
            var row = new TrustedFeedRowViewModel(feed.Id, feed.Title, feed.SkipFilter);
            row.PropertyChanged += (_, _) => RefilterAfterSave = true;
            Feeds.Add(row);
        }
        RefilterAfterSave = false;
        _loading = false;
    }

    partial void OnEnabledChanged(bool value)
    {
        if (value && !_loading)
            RefilterAfterSave = true;
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        ConnectionStatus = "…";
        try
        {
            var models = await ollama.ListModelsAsync(Endpoint.Trim());
            AvailableModels.Clear();
            foreach (var model in models)
                AvailableModels.Add(model);
            ConnectionStatus = loc.Format("Filter_Connected", models.Count)
                + (models.Contains(Model.Trim()) ? "" : loc.Format("Filter_ModelMissing", Model.Trim()));
        }
        catch (Exception ex) when (ex is FilterUnavailableException or UriFormatException)
        {
            ConnectionStatus = loc.Format("Filter_ConnectionFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void AddInterest() => AddRow(CriterionKind.Interest);

    [RelayCommand]
    private void AddExclusion() => AddRow(CriterionKind.Exclusion);

    [RelayCommand]
    private void AddKeyword() => AddRow(CriterionKind.Keyword);

    [RelayCommand]
    private void RemoveCriterion(CriterionRowViewModel? row)
    {
        if (row is not null && ListFor(row.Kind).Remove(row))
            RefilterAfterSave = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            await settings.SetFilterSettingsAsync(new FilterSettings(Enabled, Endpoint, Model));
            await SaveCriteriaAsync();
            foreach (var feed in Feeds.Where(f => f.Changed))
                await feeds.SetSkipFilterAsync(feed.Id, feed.SkipFilter);
            CloseRequested?.Invoke(true);
        }
        catch (WinnowException ex)
        {
            ErrorText = loc.Error(ex);
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    /// <summary>Applies the differences between the rows and the saved criteria. Empty rows are dropped.</summary>
    private async Task SaveCriteriaAsync()
    {
        var rows = Interests.Concat(Exclusions).Concat(Keywords).ToList();
        var kept = rows.Where(r => r.Id != 0 && !string.IsNullOrWhiteSpace(r.Text)).Select(r => r.Id).ToHashSet();

        foreach (var removed in _savedCriteria.Where(c => !kept.Contains(c.Id)))
            await criteria.DeleteAsync(removed.Id);

        foreach (var row in rows.Where(r => !string.IsNullOrWhiteSpace(r.Text)))
        {
            var saved = _savedCriteria.FirstOrDefault(c => c.Id == row.Id);
            if (saved is null)
            {
                var id = await criteria.AddAsync(row.Kind, row.Text);
                if (!row.Enabled)
                    await criteria.UpdateAsync(id, row.Text, enabled: false);
            }
            else if (saved.Text != row.Text.Trim() || saved.Enabled != row.Enabled)
            {
                await criteria.UpdateAsync(row.Id, row.Text, row.Enabled);
            }
        }
    }

    private void AddRow(CriterionKind kind)
    {
        ListFor(kind).Add(NewRow(0, kind, "", enabled: true));
        RefilterAfterSave = true;
    }

    private CriterionRowViewModel NewRow(long id, CriterionKind kind, string text, bool enabled)
    {
        var row = new CriterionRowViewModel(id, kind) { Text = text, Enabled = enabled };
        row.PropertyChanged += (_, _) => RefilterAfterSave = true;
        return row;
    }

    private ObservableCollection<CriterionRowViewModel> ListFor(CriterionKind kind) => kind switch
    {
        CriterionKind.Interest => Interests,
        CriterionKind.Exclusion => Exclusions,
        _ => Keywords,
    };
}

/// <summary>A feed in the Filter window, with its "never filter" box.</summary>
public sealed partial class TrustedFeedRowViewModel(long id, string title, bool skipFilter) : ObservableObject
{
    private readonly bool _saved = skipFilter;

    public long Id { get; } = id;
    public string Title { get; } = title;
    public bool Changed => SkipFilter != _saved;

    [ObservableProperty]
    private bool _skipFilter = skipFilter;

    public override string ToString() => Title;
}

public sealed partial class CriterionRowViewModel(long id, CriterionKind kind) : ObservableObject
{
    /// <summary>0 for a criterion added in this session.</summary>
    public long Id { get; } = id;
    public CriterionKind Kind { get; } = kind;

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private bool _enabled = true;

    public override string ToString() => Text;
}
