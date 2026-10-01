using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Winnow.App.Localization;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Reading;

namespace Winnow.App.Views;

/// <summary>
/// View plumbing for the reading pane: WebView2 setup, serving the article page, routing clicked links
/// to the browser and in-page search. The tab control keeps one instance for all tabs and swaps its DataContext
/// (null while a search tab is shown, in which case the last article stays loaded but hidden).
/// </summary>
public partial class ArticleView : UserControl
{
    // Pages are served from this virtual host so no file is written and the 2 MB NavigateToString limit does not apply.
    private const string Host = "winnow.local";

    private static Task<CoreWebView2Environment>? _environment;

    private Task<bool>? _browserReady;
    private ArticleTabViewModel? _article;
    private string _lastFindTerm = "";

    public ArticleView()
    {
        InitializeComponent();
        DataContextChanged += async (_, _) => await ShowArticleAsync();
        ThemeService.Instance.Changed += (_, _) => ApplyColorScheme(reload: true);
        ApplyZoom();
        DisplaySettings.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DisplaySettings.ArticleZoom))
                ApplyZoom();
        };
    }

    // The default zoom from Settings; Ctrl + mouse wheel still changes it until the next change in Settings.
    private void ApplyZoom() => Browser.ZoomFactor = DisplaySettings.Instance.ArticleZoom / 100.0;

    /// <summary>Puts the cursor in the search box (Ctrl+F).</summary>
    public void FocusFind()
    {
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private async Task ShowArticleAsync()
    {
        if (DataContext is not ArticleTabViewModel article || article == _article)
            return;

        _article = article;
        ResetFind();

        // One initialization per view, however many articles are shown while it runs.
        if (!await (_browserReady ??= InitializeBrowserAsync()))
        {
            _browserReady = null; // try again with the next article
            return;
        }

        // Another article may have been selected while the browser was starting; show only the current one.
        if (_article == article)
            Browser.CoreWebView2.Navigate($"https://{Host}/article/{article.ArticleId}");
    }

    private async Task<bool> InitializeBrowserAsync()
    {
        try
        {
            var environment = _environment ??= CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinnowRSS", "WebView2"));
            await Browser.EnsureCoreWebView2Async(await environment);
        }
        catch (Exception ex)
        {
            if (_environment?.IsFaulted == true)
                _environment = null; // e.g. runtime missing: retry once it is installed
            BrowserError.Text = Localizer.Instance.Format("Error_WebViewUnavailable", ex.Message);
            BrowserError.Visibility = Visibility.Visible;
            return false;
        }

        BrowserError.Visibility = Visibility.Collapsed;
        if (Browser.CoreWebView2 is not { } core)
            return false;
        ApplyColorScheme(reload: false);
        core.Settings.IsScriptEnabled = false; // feed content is untrusted
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.AddWebResourceRequestedFilter($"https://{Host}/*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image); // archived images
        core.WebResourceRequested += OnWebResourceRequested;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.Find.MatchCountChanged += (_, _) => ShowMatches();
        core.Find.ActiveMatchIndexChanged += (_, _) => ShowMatches();
        return true;
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_article is not { } article)
            return;

        if (e.Request.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase))
        {
            if (!e.Request.Uri.EndsWith($"/article/{article.ArticleId}", StringComparison.Ordinal))
                return;
            var html = ThemeService.Instance.ArticleCss is { } css ? ArticleHtml.WithExtraStyle(article.Html, css) : article.Html;
            var page = new MemoryStream(Encoding.UTF8.GetBytes(html));
            e.Response = Browser.CoreWebView2.Environment.CreateWebResourceResponse(
                page, 200, "OK", "Content-Type: text/html; charset=utf-8");
        }
        else if (article.IsArchived)
        {
            // Serve the copy stored with the archive; without one the request goes to the network as usual.
            _ = ServeArchivedImageAsync(e, e.GetDeferral(), article);
        }
    }

    private async Task ServeArchivedImageAsync(
        CoreWebView2WebResourceRequestedEventArgs e, CoreWebView2Deferral deferral, ArticleTabViewModel article)
    {
        try
        {
            if (await article.GetArchivedImageAsync(e.Request.Uri) is { } image)
                e.Response = Browser.CoreWebView2.Environment.CreateWebResourceResponse(
                    new MemoryStream(image.Data), 200, "OK", $"Content-Type: {image.ContentType}");
        }
        catch (Exception)
        {
            // Fall back to the network, as for an image that was never archived.
        }
        finally
        {
            deferral.Complete();
        }
    }

    // Anything that would leave the article page opens in the default browser instead.
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase))
            return;
        e.Cancel = true;

        if (InPageAnchor(e.Uri) is { } anchor)
            ScrollTo(anchor);
        else if (e.IsUserInitiated)
            _article?.OpenLink(e.Uri);
    }

    /// <summary>
    /// The &lt;base&gt; tag turns "#note" into "https://site/article#note": recognize those as links within the page.
    /// </summary>
    private string? InPageAnchor(string target)
    {
        if (_article?.Link is not { } link
            || !Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Fragment.Length <= 1
            || !Uri.TryCreate(link, UriKind.Absolute, out var articleUri))
            return null;

        return uri.GetLeftPart(UriPartial.Query) == articleUri.GetLeftPart(UriPartial.Query)
            ? Uri.UnescapeDataString(uri.Fragment[1..])
            : null;
    }

    // Page scripts are disabled, but scripts run by the host through ExecuteScriptAsync still work.
    private void ScrollTo(string anchor)
    {
        var id = JsonSerializer.Serialize(anchor);
        _ = Browser.CoreWebView2.ExecuteScriptAsync(
            $"(document.getElementById({id}) || document.getElementsByName({id})[0])?.scrollIntoView()");
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        _article?.OpenLink(e.Uri);
    }

    private void ApplyColorScheme(bool reload = false)
    {
        if (Browser.CoreWebView2 is not { } core)
            return;
        var theme = ThemeService.Instance;
        core.Profile.PreferredColorScheme = theme.Palette is { } palette
            ? (palette.IsDark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light)
            : theme.Current switch
            {
                ThemeService.Light => CoreWebView2PreferredColorScheme.Light,
                ThemeService.Dark => CoreWebView2PreferredColorScheme.Dark,
                _ => CoreWebView2PreferredColorScheme.Auto,
            };

        if (!reload || _article is null)
            return;

        // Switching WPF's theme restyles the window but not the browser's own native window, which can stay blank.
        // Hiding and showing it forces a repaint; the page is served locally, so reloading it is instant.
        Browser.Visibility = Visibility.Hidden;
        Dispatcher.InvokeAsync(() =>
        {
            Browser.Visibility = Visibility.Visible;
            core.Reload();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    // ----- In-page search -----

    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FocusFind();
            e.Handled = true;
        }
    }

    private async void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (Browser.CoreWebView2 is not { } core)
            return;

        if (e.Key == Key.Escape)
        {
            ResetFind();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var term = FindBox.Text.Trim();
            if (term.Length == 0)
            {
                ResetFind();
            }
            else if (term != _lastFindTerm)
            {
                _lastFindTerm = term;
                var options = core.Environment.CreateFindOptions();
                options.FindTerm = term;
                options.SuppressDefaultFindDialog = true;
                options.ShouldHighlightAllMatches = true;
                await core.Find.StartAsync(options);
            }
            else if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                core.Find.FindPrevious();
            }
            else
            {
                core.Find.FindNext();
            }
        }
    }

    private void ShowMatches()
    {
        var find = Browser.CoreWebView2.Find;
        MatchText.Text = _lastFindTerm.Length == 0 ? ""
            : find.MatchCount == 0 ? "0/0"
            : $"{find.ActiveMatchIndex}/{find.MatchCount}";
    }

    private void ResetFind()
    {
        _lastFindTerm = "";
        FindBox.Text = "";
        MatchText.Text = "";
        Browser.CoreWebView2?.Find.Stop();
    }
}
