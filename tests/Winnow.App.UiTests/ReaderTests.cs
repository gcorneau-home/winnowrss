using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Winnow.App.UiTests.Support;

// Each test drives a real window; running them one at a time keeps them independent.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Winnow.App.UiTests;

[Trait("Category", "UI")]
public class ReaderTests
{
    private const string Article = "Firefox 157 passe en mode compact et change de look";

    [Fact]
    public void Adding_a_feed_shows_it_with_its_unread_count()
    {
        using var app = new AppSession();

        var feed = app.AddCategoryWithSampleFeed();

        AppSession.WaitUntil(() => AppSession.TextsOf(feed).Contains("3"), "the feed shows 3 unread articles");
    }

    [Fact]
    public void Opening_an_article_shows_it_in_a_tab_and_marks_it_read()
    {
        using var app = new AppSession();
        var feed = app.AddCategoryWithSampleFeed();
        app.Expand(feed);

        app.OpenArticle(Article);

        var tab = AppSession.WaitFor(() => app.ArticleTabs.TabItems.FirstOrDefault(), "an article tab");
        Assert.Equal(Article, tab.Name);
        AppSession.WaitFor(() => app.Window.FindFirstDescendant(cf => cf.ByName(Article).And(cf.ByControlType(ControlType.Text))),
            "the article title in the header");
        AppSession.WaitUntil(() => AppSession.TextsOf(feed).Contains("2"), "the unread count drops to 2");

        // Opening it again switches to the same tab instead of adding one.
        app.OpenArticle(Article);
        Assert.Single(app.ArticleTabs.TabItems);
    }

    [Fact]
    public void Opening_the_same_article_twice_quickly_gives_a_single_tab()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithSampleFeed());
        var item = app.TreeItem(Article);

        item.Patterns.Invoke.Pattern.Invoke();
        item.Patterns.Invoke.Pattern.Invoke();
        item.Patterns.Invoke.Pattern.Invoke();

        AppSession.WaitUntil(() => app.OpenTabCount > 0, "a tab opens");
        Thread.Sleep(1000); // give any duplicate open time to land
        Assert.Equal(1, app.OpenTabCount);
    }

    [Fact]
    public void The_article_content_is_rendered()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithSampleFeed());

        app.OpenArticle(Article);

        // WebView2 exposes the page through UI Automation too.
        AppSession.WaitFor(() => app.Window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Document)),
            "the article document");
        AppSession.WaitUntil(() => app.Window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .Any(t => t.Name.Contains("Firefox", StringComparison.Ordinal) && t.Name.Length > Article.Length),
            "article body text is visible");
    }

    [Fact]
    public void In_page_anchor_links_scroll_the_article_instead_of_leaving_it()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithFeed(app.Feeds.AnchorsUrl, "Anchors"));
        app.OpenArticle("Long article with a table of contents");

        var end = AppSession.WaitFor(() => app.Window.FindFirstDescendant(cf => cf.ByName("End of the article")), "the end marker");
        Assert.True(end.IsOffscreen);

        var link = AppSession.WaitFor(() => app.Window.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.Hyperlink).And(cf.ByName("Jump to the end"))), "the anchor link");
        link.Patterns.Invoke.Pattern.Invoke();

        AppSession.WaitUntil(() => !end.IsOffscreen, "the page scrolled to the end marker");
    }

    [Fact]
    public void Pin_and_thumbs_toggle_and_thumbs_are_exclusive()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithSampleFeed());
        app.OpenArticle(Article);

        var pin = app.ById("Pin").AsToggleButton();
        var up = app.ById("ThumbUp").AsToggleButton();
        var down = app.ById("ThumbDown").AsToggleButton();

        pin.Toggle();
        AppSession.WaitUntil(() => pin.ToggleState == ToggleState.On, "pinned");

        up.Toggle();
        AppSession.WaitUntil(() => up.ToggleState == ToggleState.On, "thumb up");
        down.Toggle();
        AppSession.WaitUntil(() => down.ToggleState == ToggleState.On && up.ToggleState == ToggleState.Off,
            "thumb down replaces thumb up");
        down.Toggle();
        AppSession.WaitUntil(() => down.ToggleState == ToggleState.Off, "same thumb again clears it");
    }

    [Fact]
    public void Deleting_an_article_closes_its_tab_and_moves_it_to_the_trash()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithSampleFeed());
        app.OpenArticle(Article);
        AppSession.WaitFor(() => app.ArticleTabs.TabItems.FirstOrDefault(), "an article tab");

        app.Invoke("Delete");

        AppSession.WaitUntil(() => app.OpenTabCount == 0, "the tab is closed");
        AppSession.WaitUntil(() => app.TryTreeItem(Article) is null, "the article leaves the feed");
        app.Expand(AppSession.WaitFor(() => app.TryTreeItem("Trash") ?? app.TryTreeItem("Corbeille"), "the trash"));
        app.TreeItem(Article);
    }

    [Fact]
    public void Closing_a_tab_with_its_close_button()
    {
        using var app = new AppSession();
        app.Expand(app.AddCategoryWithSampleFeed());
        app.OpenArticle(Article);
        AppSession.WaitFor(() => app.ArticleTabs.TabItems.FirstOrDefault(), "an article tab");

        app.Invoke("CloseTab");

        AppSession.WaitUntil(() => app.OpenTabCount == 0, "the tab is closed");
    }

    [Fact]
    public void Switching_language_retranslates_the_interface_live()
    {
        using var app = new AppSession();
        var before = app.ById("AddCategory").Name;

        SelectOtherLanguage(app);
        AppSession.WaitUntil(() => app.ById("AddCategory").Name != before, "the toolbar is retranslated");

        SelectOtherLanguage(app);
        AppSession.WaitUntil(() => app.ById("AddCategory").Name == before, "the toolbar is back to the first language");
    }

    [Fact]
    public void The_interface_is_available_in_spanish()
    {
        using var app = new AppSession();
        var combo = app.ById("Language").AsComboBox();
        combo.Expand();
        AppSession.WaitFor(() => combo.Items.FirstOrDefault(i => i.Name == "Español"), "Español in the list")
            .Patterns.SelectionItem.Pattern.Select();
        combo.Collapse();

        AppSession.WaitUntil(() => app.ById("AddCategory").Name == "Añadir una categoría", "the toolbar is in Spanish");
    }

    private static void SelectOtherLanguage(AppSession app)
    {
        var combo = app.ById("Language").AsComboBox();
        var current = combo.SelectedItem?.Name;
        combo.Expand();
        var other = AppSession.WaitFor(() => combo.Items.FirstOrDefault(i => i.Name != current), "another language");
        other.Patterns.SelectionItem.Pattern.Select();
        combo.Collapse();
    }
}
