using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace Winnow.App.UiTests.Support;

/// <summary>
/// Runs WinnowRSS on a throwaway database and drives it through UI Automation patterns only
/// (Invoke, Value, Toggle, ExpandCollapse, SelectionItem): the real mouse and keyboard are never used.
/// </summary>
public sealed class AppSession : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly string _databasePath;
    private readonly bool _ownsDatabase;
    private readonly Application _app;

    /// <param name="databasePath">Reuse this database (to test what survives a restart); the caller deletes it.</param>
    public AppSession(string? databasePath = null)
    {
        _ownsDatabase = databasePath is null;
        _databasePath = databasePath ?? NewDatabasePath();
        var start = new ProcessStartInfo(AppExecutable()) { UseShellExecute = false };
        start.Environment["WINNOWRSS_DB"] = _databasePath;
        start.Environment["WINNOWRSS_THEMES"] = _databasePath + ".themes"; // installed themes follow the database
        start.Environment["WINNOWRSS_OPENVSX"] = Catalog.BaseUrl;
        _app = Application.Launch(start);
        Window = _app.GetMainWindow(Automation, Timeout) ?? throw new InvalidOperationException("Main window not found.");
    }

    public UIA3Automation Automation { get; } = new();
    public Window Window { get; }
    public FeedServer Feeds { get; } = new();
    public FakeOllamaServer Ollama { get; } = new();
    public FakeOpenVsxServer Catalog { get; } = new();

    // ----- Finding things -----

    public AutomationElement ById(string automationId) =>
        WaitFor(() => Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), $"element '{automationId}'");

    public TreeItem TreeItem(string name) =>
        WaitFor(() => Window.FindFirstDescendant(cf => cf.ByControlType(ControlType.TreeItem).And(cf.ByName(name)))?.AsTreeItem(),
            $"tree item '{name}'");

    public TreeItem? TryTreeItem(string name) =>
        Window.FindFirstDescendant(cf => cf.ByControlType(ControlType.TreeItem).And(cf.ByName(name)))?.AsTreeItem();

    public Tab ArticleTabs => ById("ArticleTabs").AsTab();

    /// <summary>Open article tabs; the tab control is hidden (absent from the UI tree) when there are none.</summary>
    public int OpenTabCount =>
        Window.FindFirstDescendant(cf => cf.ByAutomationId("ArticleTabs"))?.AsTab().TabItems.Length ?? 0;

    // ----- Actions -----

    public void Invoke(string automationId) => ById(automationId).Patterns.Invoke.Pattern.Invoke();

    /// <summary>Fills the app's input dialog (fields in order) and confirms it.</summary>
    public void FillDialog(params string[] values)
    {
        var dialog = Dialog("InputDialog");
        var fields = dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit));
        for (var i = 0; i < values.Length; i++)
            fields[i].Patterns.Value.Pattern.SetValue(values[i]);
        WaitFor(() => dialog.FindFirstDescendant(cf => cf.ByAutomationId("Ok")), "OK button").Patterns.Invoke.Pattern.Invoke();
    }

    /// <summary>A window opened by the app; an owned dialog shows up under its owner in the UI Automation tree.</summary>
    public AutomationElement Dialog(string automationId) =>
        WaitFor(() => Window.FindFirstChild(cf => cf.ByAutomationId(automationId))
            ?? _app.GetAllTopLevelWindows(Automation).FirstOrDefault(w => w.AutomationId == automationId), automationId);

    /// <summary>
    /// Opens the Filter window, turns the filter on against <paramref name="endpoint"/>, lets the caller adjust
    /// more settings, and saves.
    /// </summary>
    public void EnableFilter(string endpoint, Action<AutomationElement>? configure = null)
    {
        Invoke("FilterSettings");
        var window = Dialog("FilterSettingsWindow");
        Find(window, "FilterEnabled").AsCheckBox().IsChecked = true;
        Find(window, "FilterEndpoint").Patterns.Value.Pattern.SetValue(endpoint);
        configure?.Invoke(window);
        Assert.True(Find(window, "RefilterAfterSave").AsCheckBox().IsChecked,
            "turning the filter on proposes to filter the unread articles");
        Find(window, "SaveFilter").Patterns.Invoke.Pattern.Invoke();
    }

    /// <summary>A descendant of <paramref name="root"/>, waiting for it to appear.</summary>
    public static AutomationElement Find(AutomationElement root, string automationId) =>
        WaitFor(() => root.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), automationId);

    /// <summary>The filter verdict a screen reader would announce for a tree item.</summary>
    public static string ItemStatus(AutomationElement item) => item.Properties.ItemStatus.ValueOrDefault ?? "";

    public string StatusText => ById("Status").Name;

    /// <summary>Creates a category, selects it and subscribes to the local copy of the sample feed.</summary>
    public TreeItem AddCategoryWithSampleFeed(string category = "Tech") =>
        AddCategoryWithFeed(Feeds.SampleFeedUrl, "Le Fil Bidouille - flux complet", category);

    public TreeItem AddCategoryWithFeed(string url, string feedTitle, string category = "Tech")
    {
        Invoke("AddCategory");
        FillDialog(category);
        TreeItem(category).Patterns.SelectionItem.Pattern.Select();
        Invoke("AddFeed");
        FillDialog(url);
        return TreeItem(feedTitle);
    }

    public void Expand(TreeItem item) => item.Patterns.ExpandCollapse.Pattern.Expand();

    /// <summary>Opens an article the way a screen reader would (Invoke pattern on its tree item).</summary>
    public void OpenArticle(string title) => TreeItem(title).Patterns.Invoke.Pattern.Invoke();

    /// <summary>Texts shown inside a tree item (name, unread count…).</summary>
    public static IReadOnlyList<string> TextsOf(AutomationElement element) =>
        element.FindAllChildren(cf => cf.ByControlType(ControlType.Text)).Select(t => t.Name).ToList();

    // ----- Waiting -----

    public static T WaitFor<T>(Func<T?> find, string what) where T : class =>
        Retry.WhileNull(find, Timeout, TimeSpan.FromMilliseconds(200)).Result
            ?? throw new TimeoutException($"Timed out waiting for {what}.");

    public static void WaitUntil(Func<bool> condition, string what)
    {
        if (!Retry.WhileFalse(condition, Timeout, TimeSpan.FromMilliseconds(200)).Result)
            throw new TimeoutException($"Timed out waiting until {what}.");
    }

    public void Dispose()
    {
        _app.Close(); // kills the process if closing fails
        _app.Dispose();
        Automation.Dispose();
        Feeds.Dispose();
        Ollama.Dispose();
        Catalog.Dispose();
        if (_ownsDatabase)
            DeleteDatabase(_databasePath);
    }

    public static string NewDatabasePath() => Path.Combine(Path.GetTempPath(), $"winnow-uitest-{Guid.NewGuid():N}.db");

    public static void DeleteDatabase(string path)
    {
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*"))
            File.Delete(file);
        if (Directory.Exists(path + ".themes"))
            Directory.Delete(path + ".themes", recursive: true);
    }

    /// <summary>Selects a combo box item by its value's automation name, through UI Automation only.</summary>
    public void SelectInCombo(string comboId, Func<string, bool> itemName)
    {
        var combo = ById(comboId).AsComboBox();
        combo.Expand();
        var item = WaitFor(() => combo.Items.FirstOrDefault(i => itemName(i.Name)), $"item in {comboId}");
        item.Patterns.SelectionItem.Pattern.Select();
        combo.Collapse();
    }

    public string SelectedInCombo(string comboId) => ById(comboId).AsComboBox().SelectedItem?.Name ?? "";

    private static string AppExecutable()
    {
        // tests/Winnow.App.UiTests/bin/<Config>/<tfm>/ -> src/Winnow.App/bin/<Config>/<tfm>/Winnow.App.exe
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var configuration = testOutput.Parent!.Name;
        var repoRoot = testOutput.Parent!.Parent!.Parent!.Parent!.Parent!.FullName;
        var exe = Path.Combine(repoRoot, "src", "Winnow.App", "bin", configuration, "net10.0-windows", "WinnowRSS.exe");
        return File.Exists(exe) ? exe : throw new FileNotFoundException("Build Winnow.App first.", exe);
    }
}
