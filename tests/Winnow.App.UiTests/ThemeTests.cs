using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Winnow.App.UiTests.Support;

namespace Winnow.App.UiTests;

[Trait("Category", "UI")]
public class ThemeTests
{
    // Theme names follow the interface language, which depends on the machine running the tests.
    private static bool IsLight(string name) => name is "Light" or "Clair";
    private static bool IsDark(string name) => name is "Dark" or "Sombre";

    [Fact]
    public void A_VS_Code_theme_is_found_installed_used_remembered_and_deleted()
    {
        var database = AppSession.NewDatabasePath();
        try
        {
            using (var app = new AppSession(database))
            {
                app.Invoke("ManageThemes");
                var window = app.Dialog("ThemesWindow");
                AppSession.Find(window, "ThemeQuery").Patterns.Value.Pattern.SetValue("vampire");
                AppSession.Find(window, "SearchThemes").Patterns.Invoke.Pattern.Invoke();

                var install = AppSession.WaitFor(() => AppSession.Find(window, "ThemeResults").FindFirstDescendant(
                    cf => cf.ByControlType(ControlType.Button).And(cf.ByName(FakeOpenVsxServer.ExtensionName))), "the Install button");
                install.Patterns.Invoke.Pattern.Invoke();

                AppSession.WaitUntil(() => app.SelectedInCombo("Theme") == FakeOpenVsxServer.ThemeName, "the installed theme is in use");
                AppSession.Find(window, "CloseThemes").Patterns.Invoke.Pattern.Invoke();
            }

            using (var restarted = new AppSession(database))
            {
                AppSession.WaitUntil(() => restarted.SelectedInCombo("Theme") == FakeOpenVsxServer.ThemeName, "the theme is restored at startup");

                restarted.Invoke("ManageThemes");
                var window = restarted.Dialog("ThemesWindow");
                var delete = AppSession.WaitFor(() => AppSession.Find(window, "InstalledThemes").FindAllDescendants(
                    cf => cf.ByControlType(ControlType.Button)).LastOrDefault(), "the delete button");
                delete.Patterns.Invoke.Pattern.Invoke();

                AppSession.WaitUntil(() => IsSystem(restarted.SelectedInCombo("Theme")), "back to the system theme");
                Assert.DoesNotContain(restarted.ById("Theme").AsComboBox().Items, i => i.Name == FakeOpenVsxServer.ThemeName);
            }
        }
        finally
        {
            AppSession.DeleteDatabase(database);
        }
    }

    private static bool IsSystem(string name) => name is "System theme" or "Thème du système";

    [Fact]
    public void The_theme_switches_live_and_is_remembered()
    {
        var database = AppSession.NewDatabasePath();
        try
        {
            using (var app = new AppSession(database))
            {
                app.Expand(app.AddCategoryWithSampleFeed());
                app.OpenArticle("Firefox 157 passe en mode compact et change de look");

                app.SelectInCombo("Theme", IsLight);
                app.SelectInCombo("Theme", IsDark);

                // The reading pane is still there after switching (it is re-shown and reloaded).
                AppSession.WaitFor(() => app.Window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Document)), "the article page");
                Assert.True(IsDark(app.SelectedInCombo("Theme")));
            }

            using (var restarted = new AppSession(database))
                AppSession.WaitUntil(() => IsDark(restarted.SelectedInCombo("Theme")), "the dark theme is restored at startup");
        }
        finally
        {
            AppSession.DeleteDatabase(database);
        }
    }
}
