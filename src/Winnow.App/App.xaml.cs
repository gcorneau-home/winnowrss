using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Winnow.App.Localization;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Abstractions;
using Winnow.Core.Feeds;
using Winnow.Core.Filtering;
using Winnow.Core.Services;
using Winnow.Core.Theming;
using Winnow.Data;

namespace Winnow.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        try
        {
            _host = BuildHost();
            _host.Services.GetRequiredService<WinnowDatabase>().Migrate();
            var settings = _host.Services.GetRequiredService<SettingsService>();
            Localizer.Instance.SetLanguage(await settings.GetUiLanguageAsync() ?? Localizer.DefaultLanguageCode);
            DisplaySettings.Instance.Apply(await settings.GetDisplayPreferencesAsync());
            ThemeService.Instance.Initialize(_host.Services.GetRequiredService<ThemeLibrary>());
            ThemeService.Instance.Apply(await settings.GetUiThemeAsync() ?? ThemeService.System);
        }
        catch (Exception ex)
        {
            // Without a main window the app would otherwise stay alive invisibly.
            _host?.Services.GetService<ILogger<App>>()?.LogError(ex, "Startup failed");
            Localizer.Instance.SetLanguage(Localizer.DefaultLanguageCode);
            MessageBox.Show(Localizer.Instance.Format("Error_StartupFailed", ex.Message),
                Localizer.Instance["Error_Unexpected_Title"], MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
        await window.ViewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;

        // WINNOWRSS_DB lets development and manual tests use a throwaway database.
        var databasePath = Environment.GetEnvironmentVariable("WINNOWRSS_DB") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinnowRSS", "winnow.db");
        services.AddSingleton(WinnowDatabase.ForFile(databasePath));
        services.AddSingleton<ICategoryRepository, CategoryRepository>();
        services.AddSingleton<IFeedRepository, FeedRepository>();
        services.AddSingleton<IArticleRepository, ArticleRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();

        services.AddSingleton(CreateHttpClient());
        services.AddSingleton<IFeedFetcher, SyndicationFeedFetcher>();
        services.AddSingleton<IResourceFetcher, HttpResourceFetcher>();
        // Its own client: loading a model into memory can take a minute on the first call.
        services.AddSingleton(new OllamaArticleFilter(new HttpClient { Timeout = TimeSpan.FromMinutes(3) }));
        services.AddSingleton<IArticleFilter>(sp => sp.GetRequiredService<OllamaArticleFilter>());
        // Summaries stream: the timeout only covers the wait for the first words (the model may need loading).
        services.AddSingleton<IArticleSummarizer>(new OllamaSummarizer(new HttpClient { Timeout = TimeSpan.FromMinutes(3) }));
        services.AddSingleton<SummaryService>();
        services.AddSingleton<IFilterCriteriaRepository, FilterCriteriaRepository>();
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<CategoryService>();
        services.AddSingleton<FeedService>();
        services.AddSingleton<ArticleService>();
        services.AddSingleton<FeedRefreshService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<FilterCriteriaService>();
        services.AddSingleton<FilterQueueService>();
        services.AddSingleton<ArchiveService>();
        services.AddSingleton<SearchService>();
        services.AddSingleton(RetentionPolicy.Default);
        services.AddSingleton<RetentionService>();

        services.AddSingleton(Localizer.Instance);
        services.AddSingleton(ThemeService.Instance);
        // WINNOWRSS_THEMES and WINNOWRSS_OPENVSX let tests use a throwaway theme folder and a local catalog.
        services.AddSingleton(new ThemeLibrary(Environment.GetEnvironmentVariable("WINNOWRSS_THEMES")
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "themes")));
        services.AddSingleton(sp => new OpenVsxClient(sp.GetRequiredService<HttpClient>(),
            Environment.GetEnvironmentVariable("WINNOWRSS_OPENVSX") ?? OpenVsxClient.DefaultBaseUrl));
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IShellService, ShellService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WinnowRSS", "0.1"));
        return client;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "Unhandled exception");
        MessageBox.Show(e.Exception.Message, Localizer.Instance["Error_Unexpected_Title"], MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
