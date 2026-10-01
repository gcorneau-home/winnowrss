using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Winnow.App.Controls;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;

namespace Winnow.App;

/// <summary>View plumbing only: tree selection (incl. Ctrl/Shift multi-selection), shortcuts and feed drag &amp; drop.</summary>
public partial class MainWindow : Window
{
    private Point _dragStart;
    private FeedNodeViewModel? _dragCandidate;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = ViewModel = viewModel;
        Tree.CanInvokeItem = item => item is ArticleNodeViewModel;
    }

    public MainViewModel ViewModel { get; }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        ViewModel.SelectedNode = e.NewValue as TreeNodeViewModel;

    // Select the item under the cursor so its context menu acts on it.
    private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ContainerAt(e.OriginalSource) is { } item)
        {
            item.IsSelected = true;
            item.Focus();
            if (item.DataContext is ArticleNodeViewModel { IsMultiSelected: false } article)
                ViewModel.SelectArticle(article, ArticleSelectMode.Single);
        }
    }

    // ----- Opening articles: double-click or Enter -----

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ContainerAt(e.OriginalSource)?.DataContext is ArticleNodeViewModel article)
        {
            ViewModel.OpenArticleNodeCommand.Execute(article);
            e.Handled = true;
        }
    }

    // Optional single-click opening (Settings): a plain click, not a Ctrl/Shift selection or the end of a drag.
    private void Tree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!DisplaySettings.Instance.OpenOnSingleClick || Keyboard.Modifiers != ModifierKeys.None)
            return;
        var offset = e.GetPosition(Tree) - _dragStart;
        if (Math.Abs(offset.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(offset.Y) > SystemParameters.MinimumVerticalDragDistance)
            return;
        if (ContainerAt(e.OriginalSource)?.DataContext is ArticleNodeViewModel article)
            ViewModel.OpenArticleNodeCommand.Execute(article);
    }

    // UI Automation "Invoke" (screen readers, UI tests) opens an article like a double-click.
    private void Tree_ItemInvoked(object sender, RoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ArticleNodeViewModel article)
            ViewModel.OpenArticleNodeCommand.Execute(article);
    }

    private void Tree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel.SelectedNode is ArticleNodeViewModel article)
        {
            ViewModel.OpenArticleNodeCommand.Execute(article);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && ViewModel.SelectedNode is ArticleNodeViewModel { IsInTrash: false } selected)
        {
            ViewModel.TrashArticlesCommand.Execute(selected);
            e.Handled = true;
        }
    }

    // Ctrl+F goes to the open article's search box, wherever the focus is.
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && FindDescendant<ArticleView>(ArticleTabs) is { } view)
        {
            view.FocusFind();
            e.Handled = true;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if ((child as T ?? FindDescendant<T>(child)) is { } found)
                return found;
        }
        return null;
    }

    // ----- Drag & drop: a feed can be dropped on a category (or anything inside it). -----

    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(Tree);
        var clicked = ContainerAt(e.OriginalSource)?.DataContext;
        _dragCandidate = clicked as FeedNodeViewModel;

        if (clicked is ArticleNodeViewModel article)
            ViewModel.SelectArticle(article, Keyboard.Modifiers switch
            {
                ModifierKeys.Control => ArticleSelectMode.Toggle,
                ModifierKeys.Shift => ArticleSelectMode.Range,
                ModifierKeys.Control | ModifierKeys.Shift => ArticleSelectMode.AddRange,
                _ => ArticleSelectMode.Single,
            });
        else if (clicked is not null)
            ViewModel.ClearArticleSelection();
    }

    private void Tree_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var offset = e.GetPosition(Tree) - _dragStart;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var feed = _dragCandidate;
        _dragCandidate = null;
        DragDrop.DoDragDrop(Tree, new DataObject(typeof(FeedNodeViewModel), feed), DragDropEffects.Move);
    }

    private void Tree_DragOver(object sender, DragEventArgs e)
    {
        var (feed, target) = DropInfo(e);
        e.Effects = feed is not null && target is not null && feed.Category != target
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Tree_Drop(object sender, DragEventArgs e)
    {
        var (feed, target) = DropInfo(e);
        if (feed is not null && target is not null)
            await ViewModel.MoveFeedAsync(feed, target);
    }

    private static (FeedNodeViewModel? Feed, CategoryNodeViewModel? Target) DropInfo(DragEventArgs e) =>
    (
        e.Data.GetData(typeof(FeedNodeViewModel)) as FeedNodeViewModel,
        (ContainerAt(e.OriginalSource)?.DataContext as TreeNodeViewModel)?.Category
    );

    private static TreeViewItem? ContainerAt(object source)
    {
        var current = source as DependencyObject;
        while (current is not null and not TreeViewItem)
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        return current as TreeViewItem;
    }
}
