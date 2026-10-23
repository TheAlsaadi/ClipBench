using ClipBench.Presentation.Boards;
using ClipBench.Presentation.Rows;
using ClipBench.Presentation.Snippets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace ClipBench.App;

/// <summary>
/// What the mouse can do in the popup: buttons that show on a row while the pointer is over it, and a menu on the right
/// button. Both do what the keys do, by choosing the row and running the same command, so the keys keep working as before.
/// </summary>
public sealed partial class PopupWindow
{
    private void Row_PointerEntered(object sender, PointerRoutedEventArgs e) => ShowRowActions(sender, show: true);

    private void Row_PointerExited(object sender, PointerRoutedEventArgs e) => ShowRowActions(sender, show: false);

    // The buttons take the place of the marks and icons of the row while the pointer is over it. The row is a grid of the
    // icon, the text and one cell that holds the marks and, over them, the buttons: names are not found inside a template
    // that is bound with x:Bind, so the parts are taken by their place.
    private static void ShowRowActions(object sender, bool show)
    {
        if (sender is Grid { Children: [_, _, Grid { Children: [UIElement status, UIElement actions] }] })
        {
            actions.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            status.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void RowMark_Click(object sender, RoutedEventArgs e)
    {
        if (ChooseRow(sender) is not null)
        {
            ViewModel.ToggleMarkCommand.Execute(null);
        }
    }

    private void RowPin_Click(object sender, RoutedEventArgs e)
    {
        if (ChooseRow(sender) is not null)
        {
            ViewModel.TogglePinCommand.Execute(null);
        }
    }

    private void RowDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ChooseRow(sender) is not null)
        {
            ViewModel.DeleteCommand.Execute(null);
        }
    }

    private void RowBoard_Click(object sender, RoutedEventArgs e)
    {
        if (ChooseRow(sender) is not null)
        {
            // Anchored to the row, not the button: the buttons go away when the pointer moves onto the menu.
            BoardMenu.ShowAt(RowContainerOf(sender));
        }
    }

    private void Row_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (ChooseRow(sender) is not { } row)
        {
            return;
        }

        args.Handled = true;
        ShowRowMenu(row, RowContainerOf(sender), args.TryGetPosition(sender, out var position) ? position : null);
    }

    private void SnippetRow_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: SnippetRowViewModel row })
        {
            return;
        }

        var index = ViewModel.Snippets.Rows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        ViewModel.SelectedIndex = index;
        args.Handled = true;
        ShowSnippetMenu(RowContainerOf(sender), args.TryGetPosition(sender, out var position) ? position : null);
    }

    private void SidebarEntry_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: SidebarEntryViewModel entry })
        {
            return;
        }

        var index = ViewModel.Sidebar.Entries.IndexOf(entry);
        if (index < 0)
        {
            return;
        }

        ViewModel.Sidebar.SelectEntryCommand.Execute(index);
        args.Handled = true;

        var menu = new MenuFlyout();
        if (entry.IsBoard)
        {
            AddItem(menu.Items, "Rename", "F2", () => ViewModel.Sidebar.StartRenameCommand.Execute(null));
            AddItem(menu.Items, "Delete the board", "Ctrl+Shift+Delete", () => ViewModel.Sidebar.DeleteBoardCommand.Execute(null));
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        if (entry.Scope == PopupScope.Snippets)
        {
            AddItem(menu.Items, "Edit the snippets…", "Ctrl+E", () => ViewModel.EditSnippetsCommand.Execute(null));
        }

        AddItem(menu.Items, "New board…", "Ctrl+N", StartNewBoard);
        ShowMenu(menu, (FrameworkElement)sender, args.TryGetPosition(sender, out var position) ? position : null);
    }

    private void KeepBoard_Click(object sender, RoutedEventArgs e) => _ = ViewModel.Sidebar.Cancel();

    // The Menu key or Shift+F10: the same menu as the right button, for the row that is highlighted.
    private void ShowMenuForCurrent()
    {
        if (ViewModel.IsSnippetsView)
        {
            if (ViewModel.CurrentSnippet is not null && SnippetList.ContainerFromIndex(ViewModel.SelectedIndex) is FrameworkElement snippetRow)
            {
                ShowSnippetMenu(snippetRow, null);
            }

            return;
        }

        if (ViewModel.Current is { } row && ClipList.ContainerFromIndex(ViewModel.SelectedIndex) is FrameworkElement container)
        {
            ShowRowMenu(row, container, null);
        }
    }

    private void ShowRowMenu(ClipRowViewModel row, FrameworkElement anchor, Point? position)
    {
        var menu = new MenuFlyout();
        AddItem(menu.Items, "Paste", "Enter", () => ViewModel.PasteCommand.Execute(null));
        AddItem(menu.Items, "Change the text, then paste…", "Shift+Enter", () => ViewModel.OpenTransformsCommand.Execute(null));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddItem(menu.Items, row.IsMarked ? "Unselect" : "Select, to paste several clips", "Space", () => ViewModel.ToggleMarkCommand.Execute(null));
        AddItem(menu.Items, row.IsPinned ? "Unpin" : "Pin", "Ctrl+P", () => ViewModel.TogglePinCommand.Execute(null));

        var boards = new MenuFlyoutSubItem { Text = "Add to a board" };
        foreach (var entry in ViewModel.Sidebar.BoardEntries)
        {
            var id = entry.Board!.Value;
            AddItem(boards.Items, entry.Name, null, () => ViewModel.AddToBoardCommand.Execute(id));
        }

        if (boards.Items.Count > 0)
        {
            boards.Items.Add(new MenuFlyoutSeparator());
        }

        AddItem(boards.Items, "New board…", null, StartNewBoard);
        menu.Items.Add(boards);

        if (row.IsOnBoard)
        {
            AddItem(menu.Items, "Take off its board", "Ctrl+Shift+D", () => ViewModel.RemoveFromBoardCommand.Execute(null));
        }

        if (ViewModel.Sidebar.Scope == PopupScope.Board && string.IsNullOrEmpty(ViewModel.SearchText))
        {
            AddItem(menu.Items, "Move up", "Ctrl+Up", () => ViewModel.MoveUpInBoardCommand.Execute(null));
            AddItem(menu.Items, "Move down", "Ctrl+Down", () => ViewModel.MoveDownInBoardCommand.Execute(null));
        }

        if (row.IsSensitive)
        {
            AddItem(menu.Items, row.IsRevealed ? "Hide" : "Show", "Ctrl+R", () => ViewModel.RevealCommand.Execute(null));
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        AddItem(menu.Items, "Delete", "Del", () => ViewModel.DeleteCommand.Execute(null));
        ShowMenu(menu, anchor, position);
    }

    private void ShowSnippetMenu(FrameworkElement anchor, Point? position)
    {
        var menu = new MenuFlyout();
        AddItem(menu.Items, "Paste", "Enter", () => ViewModel.PasteCommand.Execute(null));
        AddItem(menu.Items, "Edit the snippets…", "Ctrl+E", () => ViewModel.EditSnippetsCommand.Execute(null));
        ShowMenu(menu, anchor, position);
    }

    // The cursor goes back to the search box when a menu closes, unless the popup was hidden or a name is being typed.
    private void ShowMenu(MenuFlyout menu, FrameworkElement anchor, Point? position)
    {
        menu.Closed += (_, _) =>
        {
            if (ViewModel.IsOpen && !ViewModel.Sidebar.IsEditing)
            {
                _ = SearchBox.Focus(FocusState.Programmatic);
            }
        };
        menu.ShowAt(anchor, position is { } at ? new FlyoutShowOptions { Position = at } : null);
    }

    private static void AddItem(IList<MenuFlyoutItemBase> items, string text, string? shortcut, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        if (shortcut is not null)
        {
            item.KeyboardAcceleratorTextOverride = shortcut;
        }

        item.Click += (_, _) => action();
        items.Add(item);
    }

    // Makes the row the one the keys would act on, so the command that runs is the one for this row.
    private ClipRowViewModel? ChooseRow(object sender)
    {
        if ((sender as FrameworkElement)?.DataContext is not ClipRowViewModel row)
        {
            return null;
        }

        var index = ViewModel.Rows.IndexOf(row);
        if (index < 0)
        {
            return null;
        }

        ViewModel.SelectedIndex = index;
        return row;
    }

    // The list item that holds a row: it stays on screen when the buttons inside it are hidden.
    private static FrameworkElement RowContainerOf(object sender)
    {
        var element = (FrameworkElement)sender;
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ListViewItem item)
            {
                return item;
            }
        }

        return element;
    }
}
