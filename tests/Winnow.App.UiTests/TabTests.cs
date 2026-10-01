using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Winnow.App.UiTests.Support;

namespace Winnow.App.UiTests;

[Trait("Category", "UI")]
public class TabTests
{
    [Fact]
    public void A_long_tab_title_keeps_its_close_button_visible()
    {
        using var app = new AppSession();
        app.AddCategoryWithSampleFeed();

        app.ById("GlobalSearch").Patterns.Value.Pattern.SetValue("refroidissement liquide batterie externe voltaflow pro apparent");
        app.Invoke("SearchButton");

        var tab = AppSession.WaitFor(() => app.ArticleTabs.TabItems.FirstOrDefault(), "the search tab");
        var close = AppSession.WaitFor(() => tab.FindFirstDescendant(cf => cf.ByAutomationId("CloseTab")), "the close button");
        Assert.False(close.IsOffscreen);
        Assert.True(tab.BoundingRectangle.Contains(close.BoundingRectangle),
            $"close button {close.BoundingRectangle} is outside its tab {tab.BoundingRectangle}");
    }
}
