using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace Winnow.App.Controls;

/// <summary>
/// TreeView whose items can raise <see cref="LibraryTreeViewItem.InvokedEvent"/> through UI Automation's Invoke
/// pattern, the way files open in Explorer. Screen readers and UI tests can then open an article without a mouse.
/// </summary>
public class LibraryTreeView : TreeView
{
    protected override DependencyObject GetContainerForItemOverride() => new LibraryTreeViewItem();
    protected override bool IsItemItsOwnContainerOverride(object item) => item is LibraryTreeViewItem;
    protected override AutomationPeer OnCreateAutomationPeer() => new LibraryTreeViewAutomationPeer(this);

    /// <summary>Decides which data items expose the Invoke pattern.</summary>
    public Func<object, bool>? CanInvokeItem { get; set; }

    private sealed class LibraryTreeViewAutomationPeer(TreeView owner) : TreeViewAutomationPeer(owner)
    {
        protected override ItemAutomationPeer CreateItemAutomationPeer(object item) =>
            new InvokableItemAutomationPeer(item, this, null);
    }
}

public class LibraryTreeViewItem : TreeViewItem
{
    public static readonly RoutedEvent InvokedEvent = EventManager.RegisterRoutedEvent(
        "Invoked", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(LibraryTreeViewItem));

    // Attached-event accessors so XAML can handle it on the tree: controls:LibraryTreeViewItem.Invoked="...".
    public static void AddInvokedHandler(DependencyObject element, RoutedEventHandler handler) =>
        ((UIElement)element).AddHandler(InvokedEvent, handler);

    public static void RemoveInvokedHandler(DependencyObject element, RoutedEventHandler handler) =>
        ((UIElement)element).RemoveHandler(InvokedEvent, handler);

    protected override DependencyObject GetContainerForItemOverride() => new LibraryTreeViewItem();
    protected override bool IsItemItsOwnContainerOverride(object item) => item is LibraryTreeViewItem;
    protected override AutomationPeer OnCreateAutomationPeer() => new LibraryTreeViewItemAutomationPeer(this);

    internal void RaiseInvoked() => RaiseEvent(new RoutedEventArgs(InvokedEvent, this));

    private sealed class LibraryTreeViewItemAutomationPeer(TreeViewItem owner) : TreeViewItemAutomationPeer(owner)
    {
        protected override ItemAutomationPeer CreateItemAutomationPeer(object item) =>
            new InvokableItemAutomationPeer(item, this, EventsSource as TreeViewDataItemAutomationPeer);
    }
}

internal sealed class InvokableItemAutomationPeer(
    object item,
    ItemsControlAutomationPeer itemsControlPeer,
    TreeViewDataItemAutomationPeer? parent)
    : TreeViewDataItemAutomationPeer(item, itemsControlPeer, parent), IInvokeProvider
{
    public override object GetPattern(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Invoke && Tree?.CanInvokeItem?.Invoke(Item) == true)
            return this;
        return base.GetPattern(patternInterface);
    }

    public void Invoke()
    {
        var owner = (ItemsControl)ItemsControlAutomationPeer.Owner;
        (owner.ItemContainerGenerator.ContainerFromItem(Item) as LibraryTreeViewItem)?.RaiseInvoked();
    }

    private LibraryTreeView? Tree
    {
        get
        {
            DependencyObject? current = ItemsControlAutomationPeer.Owner;
            while (current is not null and not LibraryTreeView)
                current = ItemsControl.ItemsControlFromItemContainer(current);
            return current as LibraryTreeView;
        }
    }
}
