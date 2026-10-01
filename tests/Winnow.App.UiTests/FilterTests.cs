using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Winnow.App.UiTests.Support;

namespace Winnow.App.UiTests;

/// <summary>The interest filter, against a fake Ollama that rejects PS5 articles and one-word exclusions.</summary>
[Trait("Category", "UI")]
public class FilterTests
{
    private const string Ps5 = "Rebound, le jailbreak qui débloque presque toutes les PS5";
    private const string Battery = "Test de la batterie externe VoltaFlow Pro et de son refroidissement liquide apparent";
    private const string Firefox = "Firefox 157 passe en mode compact et change de look";

    [Fact]
    public void Filtered_articles_stay_visible_but_do_not_count_as_unread()
    {
        using var app = new AppSession();
        var feed = app.AddCategoryWithSampleFeed();
        app.Expand(feed);
        AppSession.WaitUntil(() => AppSession.TextsOf(feed).Contains("3"), "3 unread before filtering");

        app.EnableFilter(app.Ollama.Endpoint);

        AppSession.WaitUntil(() => AppSession.ItemStatus(app.TreeItem(Ps5)).Contains(FakeOllamaServer.ConsolesExclusion),
            "the PS5 article is filtered out, with the exclusion that rejected it");
        AppSession.WaitUntil(() => AppSession.ItemStatus(app.TreeItem(Firefox)).Contains("Nouvelles applications"),
            "the Firefox article is kept, with the interest that kept it");
        AppSession.WaitUntil(() => AppSession.TextsOf(app.TreeItem("Le Fil Bidouille - flux complet")).Contains("2"),
            "the filtered article no longer counts as unread");

        // Still clickable: it opens, and its tab explains the verdict.
        app.OpenArticle(Ps5);
        AppSession.WaitUntil(() => app.ById("Verdict").Name.Contains("Il est question de PS5."), "the verdict in the article tab");

        // Reading it does not change the badge: it was never counted.
        Thread.Sleep(500);
        Assert.Contains("2", AppSession.TextsOf(app.TreeItem("Le Fil Bidouille - flux complet")));
    }

    [Fact]
    public void Unread_counts_follow_the_filter_even_for_a_collapsed_feed()
    {
        using var app = new AppSession();
        var feed = app.AddCategoryWithSampleFeed(); // left collapsed: its articles have no tree items yet
        AppSession.WaitUntil(() => AppSession.TextsOf(feed).Contains("3"), "3 unread before filtering");

        app.EnableFilter(app.Ollama.Endpoint);

        AppSession.WaitUntil(() => AppSession.TextsOf(app.TreeItem("Le Fil Bidouille - flux complet")).Contains("2"),
            "the feed badge drops to 2 once the PS5 article is filtered out");
        AppSession.WaitUntil(() => AppSession.TextsOf(app.TreeItem("Tech")).Contains("2"), "and so does the category badge");
    }

    [Fact]
    public void Filtered_articles_can_be_hidden_and_shown_again()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithSampleFeed());
        app.EnableFilter(app.Ollama.Endpoint);
        AppSession.WaitUntil(() => AppSession.ItemStatus(app.TreeItem(Ps5)).Contains(FakeOllamaServer.ConsolesExclusion), "filtered");

        var toggle = app.ById("ShowRejected").AsToggleButton();
        toggle.Toggle();
        AppSession.WaitUntil(() => app.TryTreeItem(Ps5) is null, "the filtered article is hidden");
        Assert.NotNull(app.TryTreeItem(Firefox));

        toggle.Toggle();
        AppSession.WaitUntil(() => app.TryTreeItem(Ps5) is not null, "the filtered article is back");
    }

    [Fact]
    public void A_new_exclusion_filters_unread_articles_again()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithSampleFeed());
        app.EnableFilter(app.Ollama.Endpoint);
        AppSession.WaitUntil(() => AppSession.ItemStatus(app.TreeItem(Battery)).Contains("Nouvelles applications"), "first pass keeps it");

        app.Invoke("FilterSettings");
        var window = app.Dialog("FilterSettingsWindow");
        AppSession.Find(window, "AddExclusion").Patterns.Invoke.Pattern.Invoke();
        var exclusions = AppSession.Find(window, "Exclusions");
        var newRow = AppSession.WaitFor(() => exclusions.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
            .FirstOrDefault(e => e.Patterns.Value.Pattern.Value.Value == ""), "the new exclusion row");
        newRow.Patterns.Value.Pattern.SetValue("batterie");
        Assert.True(AppSession.Find(window, "RefilterAfterSave").AsCheckBox().IsChecked);
        AppSession.Find(window, "SaveFilter").Patterns.Invoke.Pattern.Invoke();

        AppSession.WaitUntil(() => AppSession.ItemStatus(app.TreeItem(Battery)).Contains("batterie"),
            "the battery article is filtered out by the new exclusion");
    }

    [Fact]
    public void A_keyword_filters_out_matching_titles_without_the_model()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithSampleFeed());

        app.EnableFilter(app.Ollama.Endpoint, window =>
        {
            AppSession.Find(window, "AddKeyword").Patterns.Invoke.Pattern.Invoke();
            var keywords = AppSession.Find(window, "Keywords");
            AppSession.WaitFor(() => keywords.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit)), "the keyword row")
                .Patterns.Value.Pattern.SetValue("Firefox");
        });

        // The fake model keeps Firefox; the keyword rule filters it out first.
        AppSession.WaitUntil(() => AppSession.ItemStatus(app.TreeItem(Firefox)).Contains("Firefox"), "filtered by the keyword");
        Assert.DoesNotContain("Nouvelles applications", AppSession.ItemStatus(app.TreeItem(Firefox)));
    }

    [Fact]
    public void A_trusted_feed_is_never_filtered()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithSampleFeed());

        app.EnableFilter(app.Ollama.Endpoint, window =>
            AppSession.WaitFor(() => AppSession.Find(window, "TrustedFeeds").FindFirstDescendant(
                cf => cf.ByControlType(ControlType.CheckBox).And(cf.ByName("Le Fil Bidouille - flux complet"))), "the feed's box")
                .AsCheckBox().IsChecked = true);

        // The fake model would reject the PS5 article; the trusted feed keeps it.
        AppSession.WaitUntil(() => AppSession.ItemStatus(app.TreeItem(Ps5)).Contains("Le Fil Bidouille - flux complet"), "kept as a trusted feed");
        Assert.DoesNotContain(FakeOllamaServer.ConsolesExclusion, AppSession.ItemStatus(app.TreeItem(Ps5)));
    }

    [Fact]
    public void Articles_wait_when_Ollama_is_unreachable()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithSampleFeed());

        app.EnableFilter($"http://localhost:{FeedServer.FreePort()}/"); // nothing listens there

        AppSession.WaitUntil(() => app.StatusText.Contains("Ollama"), "the status bar says the filter is unavailable");
        Assert.NotEqual("", AppSession.ItemStatus(app.TreeItem(Firefox))); // still pending
        Assert.DoesNotContain("Nouvelles applications", AppSession.ItemStatus(app.TreeItem(Firefox)));
    }
}
