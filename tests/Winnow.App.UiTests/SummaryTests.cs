using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Winnow.App.UiTests.Support;

namespace Winnow.App.UiTests;

[Trait("Category", "UI")]
public class SummaryTests
{
    private const string Battery = "Test de la batterie externe VoltaFlow Pro et de son refroidissement liquide apparent";

    [Fact]
    public void A_summary_is_written_shown_and_found_again_after_a_restart()
    {
        var database = AppSession.NewDatabasePath();
        try
        {
            using (var app = new AppSession(database))
            {
                app.Expand(app.AddCategoryWithSampleFeed());
                app.EnableFilter(app.Ollama.Endpoint); // the summary uses the filter's Ollama
                app.OpenArticle(Battery);

                app.Invoke("Summarize"); // in the interface language, which follows Windows on a new database
                var text = SummaryText(app, "Summary in ");
                Assert.Contains(Battery, text);
                Assert.Contains("•  First point", text); // the model's "- " points are shown as bullets

                // Another language from the list adds a second summary; the first one stays saved.
                app.ById("SummaryLanguages").AsToggleButton().Toggle();
                LanguageButton(app, "Español").Patterns.Invoke.Pattern.Invoke();
                SummaryText(app, "Summary in Spanish");
            }

            using var restarted = new AppSession(database);
            restarted.Expand(restarted.TreeItem("Tech"));
            restarted.Expand(restarted.TreeItem("Le Fil Bidouille - flux complet"));
            restarted.OpenArticle(Battery);
            SummaryText(restarted, "Summary in Spanish"); // the last language asked for

            // The fake Ollama of this session listens on another port.
            restarted.Invoke("FilterSettings");
            var filter = restarted.Dialog("FilterSettingsWindow");
            AppSession.Find(filter, "FilterEndpoint").Patterns.Value.Pattern.SetValue(restarted.Ollama.Endpoint);
            AppSession.Find(filter, "SaveFilter").Patterns.Invoke.Pattern.Invoke();

            restarted.ById("SummaryLanguages").AsToggleButton().Toggle();
            LanguageButton(restarted, "English").Patterns.Invoke.Pattern.Invoke();
            SummaryText(restarted, "Summary in English");

            // The main part of the button now writes in English, the last language asked for.
            restarted.Invoke("Summarize");
            SummaryText(restarted, "Summary in English");
        }
        finally
        {
            AppSession.DeleteDatabase(database);
        }
    }

    [Fact]
    public void Without_Ollama_the_card_explains_why()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithSampleFeed());
        app.EnableFilter("http://localhost:9/"); // nothing listens there
        app.OpenArticle(Battery);

        app.Invoke("Summarize");

        var error = AppSession.WaitFor(() => app.Window.FindFirstDescendant(cf => cf.ByAutomationId("SummaryError"))
            is { IsOffscreen: false } e && e.Name.Length > 0 ? e : null, "the error in the card");
        Assert.Contains("Ollama", error.Name);
    }

    private static string SummaryText(AppSession app, string start)
    {
        var shown = "";
        try
        {
            return AppSession.WaitFor(() => app.Window.FindFirstDescendant(cf => cf.ByAutomationId("SummaryText"))
                is { } t && (shown = t.Name).StartsWith(start) && t.Name.Contains("Second point") ? t : null, start).Name;
        }
        catch (TimeoutException ex)
        {
            var error = app.Window.FindFirstDescendant(cf => cf.ByAutomationId("SummaryError"))?.Name;
            throw new TimeoutException($"{ex.Message} Shown: \"{shown}\"; error: \"{error}\"", ex);
        }
    }

    // The list is a popup: it may appear outside the main window in the automation tree.
    private static AutomationElement LanguageButton(AppSession app, string name) =>
        AppSession.WaitFor(() =>
            (app.Window.FindFirstDescendant(cf => cf.ByAutomationId("SummaryLanguageList"))
                ?? app.Window.Automation.GetDesktop().FindFirstDescendant(cf => cf.ByAutomationId("SummaryLanguageList")))
            ?.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name))),
            $"{name} in the language list");
}
