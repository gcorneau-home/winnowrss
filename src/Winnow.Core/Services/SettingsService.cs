using System.Globalization;
using Winnow.Core.Abstractions;
using Winnow.Core.Models;

namespace Winnow.Core.Services;

/// <summary>Typed access to user preferences.</summary>
public sealed class SettingsService(ISettingsRepository settings)
{
    private const string UiLanguageKey = "ui.language";
    private const string UiThemeKey = "ui.theme";
    private const string FilterEnabledKey = "filter.enabled";
    private const string FilterEndpointKey = "filter.endpoint";
    private const string FilterModelKey = "filter.model";
    private const string FilterVersionKey = "filter.version";
    private const string HideRejectedKey = "view.hideRejected";
    private const string OpenOnSingleClickKey = "ui.openOnSingleClick";
    private const string MaxOpenTabsKey = "ui.maxOpenTabs";
    private const string UnreadCuesKey = "ui.unreadCues";
    private const string RejectedCuesKey = "ui.rejectedCues";
    private const string ArticleZoomKey = "ui.articleZoom";
    private const string HideReadKey = "view.hideRead";
    private const string SummaryLanguageKey = "summary.language";

    /// <summary>Two-letter language code of the interface, or null if never chosen.</summary>
    public Task<string?> GetUiLanguageAsync(CancellationToken ct = default) => settings.GetAsync(UiLanguageKey, ct);

    public Task SetUiLanguageAsync(string languageCode, CancellationToken ct = default) =>
        settings.SetAsync(UiLanguageKey, languageCode, ct);

    /// <summary>"system", "light" or "dark"; null if never chosen.</summary>
    public Task<string?> GetUiThemeAsync(CancellationToken ct = default) => settings.GetAsync(UiThemeKey, ct);

    public Task SetUiThemeAsync(string theme, CancellationToken ct = default) => settings.SetAsync(UiThemeKey, theme, ct);

    /// <summary>The filter is off until the user turns it on (it needs Ollama and a model).</summary>
    public async Task<FilterSettings> GetFilterSettingsAsync(CancellationToken ct = default) => new(
        await settings.GetAsync(FilterEnabledKey, ct) == "true",
        await settings.GetAsync(FilterEndpointKey, ct) ?? FilterSettings.DefaultEndpoint,
        await settings.GetAsync(FilterModelKey, ct) ?? FilterSettings.DefaultModel);

    public async Task SetFilterSettingsAsync(FilterSettings filter, CancellationToken ct = default)
    {
        await settings.SetAsync(FilterEnabledKey, filter.Enabled ? "true" : "false", ct);
        await settings.SetAsync(FilterEndpointKey, filter.Endpoint.Trim(), ct);
        await settings.SetAsync(FilterModelKey, filter.Model.Trim(), ct);
    }

    /// <summary>Incremented whenever the criteria change, and stored with each verdict.</summary>
    public async Task<int> GetFilterVersionAsync(CancellationToken ct = default) =>
        int.TryParse(await settings.GetAsync(FilterVersionKey, ct), out var version) ? version : 1;

    public async Task<int> BumpFilterVersionAsync(CancellationToken ct = default)
    {
        var version = await GetFilterVersionAsync(ct) + 1;
        await settings.SetAsync(FilterVersionKey, version.ToString(CultureInfo.InvariantCulture), ct);
        return version;
    }

    public async Task<DisplayPreferences> GetDisplayPreferencesAsync(CancellationToken ct = default)
    {
        var defaults = DisplayPreferences.Default;
        var maxTabs = int.TryParse(await settings.GetAsync(MaxOpenTabsKey, ct), CultureInfo.InvariantCulture, out var max) ? max : defaults.MaxOpenTabs;
        return new DisplayPreferences(
            await settings.GetAsync(OpenOnSingleClickKey, ct) == "true",
            Math.Clamp(maxTabs, DisplayPreferences.MinTabs, DisplayPreferences.MaxTabs),
            Enum.TryParse<UnreadCues>(await settings.GetAsync(UnreadCuesKey, ct), out var unread) ? unread : defaults.UnreadCues,
            Enum.TryParse<RejectedCues>(await settings.GetAsync(RejectedCuesKey, ct), out var rejected) ? rejected : defaults.RejectedCues,
            int.TryParse(await settings.GetAsync(ArticleZoomKey, ct), CultureInfo.InvariantCulture, out var zoom)
                ? DisplayPreferences.NearestZoom(zoom) : defaults.ArticleZoom);
    }

    public async Task SetDisplayPreferencesAsync(DisplayPreferences preferences, CancellationToken ct = default)
    {
        await settings.SetAsync(OpenOnSingleClickKey, preferences.OpenOnSingleClick ? "true" : "false", ct);
        await settings.SetAsync(MaxOpenTabsKey,
            Math.Clamp(preferences.MaxOpenTabs, DisplayPreferences.MinTabs, DisplayPreferences.MaxTabs).ToString(CultureInfo.InvariantCulture), ct);
        await settings.SetAsync(UnreadCuesKey, preferences.UnreadCues.ToString(), ct);
        await settings.SetAsync(RejectedCuesKey, preferences.RejectedCues.ToString(), ct);
        await settings.SetAsync(ArticleZoomKey,
            DisplayPreferences.NearestZoom(preferences.ArticleZoom).ToString(CultureInfo.InvariantCulture), ct);
    }

    public async Task<bool> GetHideRejectedAsync(CancellationToken ct = default) =>
        await settings.GetAsync(HideRejectedKey, ct) == "true";

    public Task SetHideRejectedAsync(bool hide, CancellationToken ct = default) =>
        settings.SetAsync(HideRejectedKey, hide ? "true" : "false", ct);

    /// <summary>The language of the last summary asked for (two-letter code), or null.</summary>
    public Task<string?> GetSummaryLanguageAsync(CancellationToken ct = default) => settings.GetAsync(SummaryLanguageKey, ct);

    public Task SetSummaryLanguageAsync(string languageCode, CancellationToken ct = default) =>
        settings.SetAsync(SummaryLanguageKey, languageCode, ct);

    public async Task<bool> GetHideReadAsync(CancellationToken ct = default) =>
        await settings.GetAsync(HideReadKey, ct) == "true";

    public Task SetHideReadAsync(bool hide, CancellationToken ct = default) =>
        settings.SetAsync(HideReadKey, hide ? "true" : "false", ct);
}
