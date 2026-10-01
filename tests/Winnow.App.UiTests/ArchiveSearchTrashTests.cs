using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Winnow.App.UiTests.Support;

namespace Winnow.App.UiTests;

[Trait("Category", "UI")]
public class ArchiveSearchTrashTests
{
    private const string Battery = "Test de la batterie externe VoltaFlow Pro et de son refroidissement liquide apparent";
    private const string Picture = "Article with a picture";

    [Fact]
    public void An_archived_article_shows_its_images_without_the_network()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithFeed(app.Feeds.ImagesUrl, "Images"));
        app.OpenArticle(Picture);
        AppSession.WaitUntil(() => app.Feeds.ImageRequests >= 1, "the page loads its image from the site");

        app.Invoke("Archive");
        AppSession.WaitUntil(() => app.Feeds.ImageRequests >= 2, "archiving downloads the image");
        AppSession.WaitUntil(() => app.TryTreeItem(Picture) is null || ArchiveNode(app).IsOffscreen is false, "the article moves to the archive");

        // The site goes away; reading the archived copy must not touch the network.
        app.Feeds.ImagesOffline = true;
        app.Invoke("CloseTab");
        AppSession.WaitUntil(() => app.OpenTabCount == 0, "the tab is closed");
        var requestsBefore = app.Feeds.ImageRequests;

        app.Expand(ArchiveNode(app));
        app.OpenArticle(Picture);
        AppSession.WaitFor(() => app.Window.FindFirstDescendant(cf => cf.ByName("Look at this picture.")), "the archived page");
        Thread.Sleep(1500); // leave time for any image request to reach the server

        Assert.Equal(requestsBefore, app.Feeds.ImageRequests);
    }

    [Fact]
    public void Search_finds_articles_and_opens_them()
    {
        using var app = new AppSession();
        app.AddCategoryWithSampleFeed();

        app.ById("GlobalSearch").Patterns.Value.Pattern.SetValue("refroidissement gouttes");
        app.Invoke("SearchButton");

        var result = AppSession.WaitFor(() => app.Window.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.Button).And(cf.ByName(Battery))), "the search result");
        Assert.Equal(1, app.OpenTabCount);

        result.Patterns.Invoke.Pattern.Invoke();

        AppSession.WaitUntil(() => app.OpenTabCount == 2, "the article opens in its own tab");
        AppSession.WaitFor(() => app.Window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text).And(cf.ByName(Battery))),
            "the article header");
    }

    [Fact]
    public void Switching_between_search_and_article_tabs_keeps_the_article()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithSampleFeed());
        app.OpenArticle(Battery);
        AppSession.WaitFor(() => app.Window.FindFirstDescendant(cf => cf.ByName("Pourquoi ?", FlaUI.Core.Definitions.PropertyConditionFlags.MatchSubstring)),
            "the article body");

        app.ById("GlobalSearch").Patterns.Value.Pattern.SetValue("jailbreak");
        app.Invoke("SearchButton");
        AppSession.WaitFor(() => app.Window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button)
            .And(cf.ByName("Rebound, le jailbreak qui débloque presque toutes les PS5"))), "the search result");

        app.ArticleTabs.TabItems.Single(t => t.Name == Battery).Select();

        AppSession.WaitFor(() => app.Window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text).And(cf.ByName(Battery))),
            "the article header again");
        AppSession.WaitFor(() => app.Window.FindFirstDescendant(cf => cf.ByName("Pourquoi ?", FlaUI.Core.Definitions.PropertyConditionFlags.MatchSubstring)),
            "the article body again");
    }

    [Fact]
    public void An_article_can_be_restored_from_the_trash()
    {
        using var app = new AppSession();
        var feed = app.AddCategoryWithSampleFeed();
        app.Expand(feed);
        app.OpenArticle(Battery);
        app.Invoke("Delete");
        AppSession.WaitUntil(() => app.OpenTabCount == 0, "the tab closes");

        var trash = TrashNode(app);
        app.Expand(trash);
        app.OpenArticle(Battery);
        app.Invoke("Restore");

        AppSession.WaitUntil(() => TrashNode(app).FindAllChildren(cf => cf.ByControlType(ControlType.TreeItem)).Length == 0,
            "the trash is empty again");
        AppSession.WaitUntil(() => app.TryTreeItem(Battery) is not null, "the article is back in its feed");
        AppSession.WaitUntil(() => app.Window.FindFirstDescendant(cf => cf.ByAutomationId("Delete")) is { IsOffscreen: false },
            "the open tab offers Delete again instead of Restore");
    }

    // Node names follow the interface language, which depends on the machine running the tests.
    private static TreeItem ArchiveNode(AppSession app) =>
        AppSession.WaitFor(() => app.TryTreeItem("Archive") ?? app.TryTreeItem("Archives"), "the archive node");

    private static TreeItem TrashNode(AppSession app) =>
        AppSession.WaitFor(() => app.TryTreeItem("Trash") ?? app.TryTreeItem("Corbeille"), "the trash node");
}
