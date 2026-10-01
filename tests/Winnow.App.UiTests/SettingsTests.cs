using FlaUI.Core.AutomationElements;
using Winnow.App.UiTests.Support;

namespace Winnow.App.UiTests;

[Trait("Category", "UI")]
public class SettingsTests
{
    private const string Battery = "Test de la batterie externe VoltaFlow Pro et de son refroidissement liquide apparent";
    private const string Ps5 = "Rebound, le jailbreak qui débloque presque toutes les PS5";
    private const string Firefox = "Firefox 157 passe en mode compact et change de look";

    [Fact]
    public void The_tab_limit_closes_the_least_recently_viewed_article()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithSampleFeed());
        Configure(app, window => AppSession.Find(window, "MaxOpenTabs").Patterns.Value.Pattern.SetValue("2"));

        app.OpenArticle(Battery);
        AppSession.WaitUntil(() => app.OpenTabCount == 1, "first tab");
        app.OpenArticle(Ps5);
        AppSession.WaitUntil(() => app.OpenTabCount == 2, "second tab");
        app.ArticleTabs.TabItems[0].Select(); // the battery tab becomes the most recently viewed
        app.OpenArticle(Firefox);

        AppSession.WaitUntil(() => app.OpenTabCount == 2, "still two tabs");
        AppSession.WaitFor(() => app.Window.FindFirstDescendant(cf => cf.ByName(Firefox).And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))), "Firefox is open");
        app.ArticleTabs.TabItems[0].Select();
        AppSession.WaitFor(() => app.Window.FindFirstDescendant(cf => cf.ByName(Battery).And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))), "the battery article is still open");
    }

    [Fact]
    public void Settings_are_saved_and_restored()
    {
        var database = AppSession.NewDatabasePath();
        try
        {
            using (var app = new AppSession(database))
            {
                Configure(app, window =>
                {
                    AppSession.Find(window, "OpenOnSingleClick").AsRadioButton().IsChecked = true;
                    AppSession.Find(window, "MaxOpenTabs").Patterns.Value.Pattern.SetValue("7");
                    AppSession.Find(window, "DimRead").AsCheckBox().IsChecked = true;
                    AppSession.Find(window, "RejectedStrikethrough").AsCheckBox().IsChecked = true;
                    AppSession.Find(window, "UnreadBold").AsCheckBox().IsChecked = false;
                    var zoom = AppSession.Find(window, "ArticleZoom").AsComboBox();
                    zoom.Select(8); // 50, 67, 75, 80, 90, 100, 110, 125, 150
                    AppSession.WaitUntil(() => zoom.SelectedItem?.Name == "150", "150 % is selected");
                });
            }

            using var restarted = new AppSession(database);
            restarted.Invoke("Settings");
            var settings = restarted.Dialog("SettingsWindow");
            Assert.True(AppSession.Find(settings, "OpenOnSingleClick").AsRadioButton().IsChecked);
            Assert.Equal("7", AppSession.Find(settings, "MaxOpenTabs").Patterns.Value.Pattern.Value.Value);
            Assert.True(AppSession.Find(settings, "DimRead").AsCheckBox().IsChecked);
            Assert.True(AppSession.Find(settings, "RejectedStrikethrough").AsCheckBox().IsChecked);
            Assert.False(AppSession.Find(settings, "UnreadBold").AsCheckBox().IsChecked);
            Assert.True(AppSession.Find(settings, "UnreadDot").AsCheckBox().IsChecked); // untouched default
            Assert.Equal("150", AppSession.Find(settings, "ArticleZoom").AsComboBox().SelectedItem?.Name);
        }
        finally
        {
            AppSession.DeleteDatabase(database);
        }
    }

    [Fact]
    public void Read_articles_can_be_hidden_and_shown_again()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithSampleFeed());
        app.OpenArticle(Battery);
        AppSession.WaitUntil(() => app.OpenTabCount == 1, "the article is open, hence read");

        var toggle = app.ById("ShowRead").AsToggleButton();
        toggle.Toggle();
        app.Expand(app.TreeItem("Le Fil Bidouille - flux complet"));
        AppSession.WaitUntil(() => app.TryTreeItem(Battery) is null, "the read article is hidden");
        Assert.NotNull(app.TryTreeItem(Ps5));

        toggle.Toggle();
        app.Expand(app.TreeItem("Le Fil Bidouille - flux complet"));
        AppSession.WaitUntil(() => app.TryTreeItem(Battery) is not null, "the read article is back");
    }

    [Fact]
    public void The_about_box_opens_from_the_settings_and_shows_the_version()
    {
        using var app = new AppSession();
        app.Invoke("Settings");
        var settings = app.Dialog("SettingsWindow");
        AppSession.Find(settings, "About").Patterns.Invoke.Pattern.Invoke();

        var about = AppSession.WaitFor(() => settings.FindFirstChild(cf => cf.ByAutomationId("AboutWindow"))
            ?? app.Window.FindFirstDescendant(cf => cf.ByAutomationId("AboutWindow")), "the about box");
        Assert.Matches(@"\d+\.\d+\.\d+", AppSession.Find(about, "AboutVersion").Name);
        Assert.NotNull(about.FindFirstDescendant(cf => cf.ByName("Claude Code", FlaUI.Core.Definitions.PropertyConditionFlags.MatchSubstring)));

        AppSession.Find(about, "CloseAbout").Patterns.Invoke.Pattern.Invoke();
        AppSession.WaitUntil(() => settings.FindFirstChild(cf => cf.ByAutomationId("AboutWindow")) is null, "the about box closes");
    }

    private static void Configure(AppSession app, Action<AutomationElement> change)
    {
        app.Invoke("Settings");
        var window = app.Dialog("SettingsWindow");
        change(window);
        AppSession.Find(window, "SaveSettings").Patterns.Invoke.Pattern.Invoke();
        AppSession.WaitUntil(() => app.Window.FindFirstChild(cf => cf.ByAutomationId("SettingsWindow")) is null, "the window closes");
    }
}
