using ClipBench.Core.Pasting;
using ClipBench.Core.Screen;
using ClipBench.Presentation.Boards;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Rows;
using ClipBench.Presentation.Snippets;
using ClipBench.Presentation.Transforms;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace ClipBench.App;

/// <summary>
/// The popup: a borderless, topmost window with a search box and the list of clips. It holds no logic of its own:
/// keys and clicks are handed to <see cref="PopupViewModel"/>, which the tests cover.
/// </summary>
public sealed partial class PopupWindow : Window
{
    /// <summary>The width of the popup in device-independent pixels.</summary>
    public const double WidthDip = 440;

    /// <summary>The height of the popup in device-independent pixels.</summary>
    public const double HeightDip = 520;

    /// <summary>How much wider the popup is while the sidebar is shown, in device-independent pixels.</summary>
    public const double SidebarWidthDip = 168;

    private readonly IWindowEffects _effects;
    private readonly IForegroundTracker _tracker;
    private readonly IScreenLocator _screens;
    private bool _customSeparatorChosen;
    private (PixelPoint Cursor, PointInt32 Window)? _dragStart;

    /// <summary>Creates the window, hidden.</summary>
    /// <param name="viewModel">What the window shows.</param>
    /// <param name="effects">What Windows does with the window from outside: what a click on it does.</param>
    /// <param name="tracker">Tells which window has focus.</param>
    /// <param name="screens">Tells where the mouse pointer is, for moving the window.</param>
    public PopupWindow(PopupViewModel viewModel, IWindowEffects effects, IForegroundTracker tracker, IScreenLocator screens)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(screens);
        ViewModel = viewModel;
        _effects = effects;
        _tracker = tracker;
        _screens = screens;
        InitializeComponent();

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        Activated += OnActivated;

        // The text boxes handle a press themselves, so the frame listens to it even when it is said to be done.
        DragHandle.PointerPressed += DragHandle_PointerPressed;
        DragHandle.PointerMoved += DragHandle_PointerMoved;
        DragHandle.PointerReleased += DragHandle_DragEnded;
        DragHandle.PointerCanceled += DragHandle_DragEnded;
        DragHandle.PointerCaptureLost += DragHandle_DragEnded;
        Frame.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Frame_PointerPressed), handledEventsToo: true);
        ViewModel.CloseRequested += (_, _) => AppWindow.Hide();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Transforms.PropertyChanged += OnTransformsPropertyChanged;
        ViewModel.Sidebar.PropertyChanged += OnSidebarPropertyChanged;
        BuildSeparatorMenu();
    }

    /// <summary>What the window shows.</summary>
    public PopupViewModel ViewModel { get; }

    /// <summary>The width the popup has now, in device-independent pixels: wider while the sidebar is shown.</summary>
    public double CurrentWidthDip => WidthDip + (ViewModel.Sidebar.IsOpen ? SidebarWidthDip : 0);

    /// <summary>Places the window, shows it and puts the cursor in the search box.</summary>
    /// <param name="bounds">Where the window goes, in device pixels.</param>
    public void ShowAt(PixelRect bounds)
    {
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        AppWindow.Show();
        TakeForeground();
        _ = SearchBox.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// True while the popup is the window with focus. Windows is asked, because the window does not always report that it has
    /// been put behind another one, for example when the shortcut has just given the focus back to a program.
    /// </summary>
    public bool IsActive => _tracker.IsForeground(new WindowHandle(WinRT.Interop.WindowNative.GetWindowHandle(this)));

    /// <summary>
    /// Takes up a locked popup that is on screen: the program in front is the one to paste into, and the cursor goes to the
    /// search box. A popup that is not locked is opened with <see cref="ShowAt"/> instead.
    /// </summary>
    /// <param name="pasteIntoPreviousWindow">False when the tray icon was used and the window in front is the taskbar.</param>
    public void TakeFocus(bool pasteIntoPreviousWindow)
    {
        ViewModel.Refocus(pasteIntoPreviousWindow);
        KeepOnScreen();
        TakeForeground();
        _ = SearchBox.Focus(FocusState.Programmatic);
    }

    // A popup that was let go over the edge of a screen, or was left on a monitor that is gone, is moved back into the work area.
    // One that is inside it is not touched.
    private void KeepOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        var x = Math.Clamp(position.X, area.X, Math.Max(area.X, area.X + area.Width - size.Width));
        var y = Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - size.Height));
        if (x != position.X || y != position.Y)
        {
            AppWindow.Move(new PointInt32(x, y));
        }
    }

    // A window that is on screen already is not always given the focus by Activate: Windows only allows that to a program that has
    // just had the user's input. The request is then made the way the task switcher makes it.
    private void TakeForeground()
    {
        // The request lifts the style that keeps a locked popup from taking the focus; it is not lifted by Activate.
        _ = _effects.BringToFront(new WindowHandle(WinRT.Interop.WindowNative.GetWindowHandle(this)));
        Activate();
    }

    private void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        // Clicking anywhere else, or switching to another window, puts the popup away. A locked popup stays.
        if (args.WindowActivationState == WindowActivationState.Deactivated && ViewModel.IsOpen && !ViewModel.IsLocked)
        {
            ViewModel.CloseNowCommand.Execute(null);
        }
    }

    // A locked popup does not take the focus when it is clicked, so that a click on a clip pastes into the program the user
    // was in. Keys and typing need the focus, so it is taken when something that is typed in is used.
    private void NeedKeyboard()
    {
        if (ViewModel.IsOpen && ViewModel.IsLocked && !IsActive)
        {
            TakeFocus(pasteIntoPreviousWindow: true);
        }
    }

    private void Frame_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is not null && !ReferenceEquals(element, Frame); element = VisualTreeHelper.GetParent(element))
        {
            if (element is TextBox box)
            {
                // Only a locked popup that is not in front needs this: any other text box takes the focus by itself.
                if (ViewModel.IsOpen && ViewModel.IsLocked && !IsActive)
                {
                    NeedKeyboard();
                    _ = DispatcherQueue.TryEnqueue(() => _ = box.Focus(FocusState.Pointer));
                }

                return;
            }
        }
    }

    // The popup is moved by the pointer events of the handle, which is captured for as long as the button is down. The pointer is
    // read on the whole desktop and the window is put where it was plus how far the pointer has gone, so a fast mouse cannot get
    // ahead of the window: measuring the pointer inside a window that is still moving counts the same distance twice.
    private void DragHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(DragHandle).Properties.IsLeftButtonPressed && DragHandle.CapturePointer(e.Pointer))
        {
            _dragStart = (_screens.CursorPosition(), AppWindow.Position);
        }
    }

    private void DragHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragStart is not var (cursor, origin) || !e.GetCurrentPoint(DragHandle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var now = _screens.CursorPosition();
        AppWindow.Move(new PointInt32(origin.X + (now.X - cursor.X), origin.Y + (now.Y - cursor.Y)));
    }

    private void DragHandle_DragEnded(object sender, PointerRoutedEventArgs e)
    {
        if (_dragStart is null)
        {
            return;
        }

        _dragStart = null;
        DragHandle.ReleasePointerCaptures();
        KeepOnScreen();

        // Pressing something that cannot take the focus leaves it nowhere, and the keys would go nowhere. It is put back once
        // the pointer events are over, or the press that is ending would take it again. A locked popup that is not in front
        // keeps the focus out of the way of the program the user is in.
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (ViewModel.IsOpen && IsActive)
            {
                _ = SearchBox.Focus(FocusState.Programmatic);
            }
        });
    }

    // Whether a click makes the popup the window with focus follows the lock. Letting go of the lock makes it an ordinary popup
    // again, which has the focus, so that clicking elsewhere puts it away.
    private void ApplyLock()
    {
        _ = _effects.SetActivatesOnClick(new WindowHandle(WinRT.Interop.WindowNative.GetWindowHandle(this)), activates: !ViewModel.IsLocked);
        if (!ViewModel.IsLocked && ViewModel.IsOpen && !IsActive)
        {
            TakeForeground();
            _ = SearchBox.Focus(FocusState.Programmatic);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PopupViewModel.IsLocked))
        {
            ApplyLock();
        }
        else if (e.PropertyName == nameof(PopupViewModel.SelectedIndex) && ViewModel.Current is { } current)
        {
            ClipList.ScrollIntoView(current);
        }
        else if (e.PropertyName == nameof(PopupViewModel.SelectedIndex) && ViewModel.CurrentSnippet is { } snippet)
        {
            SnippetList.ScrollIntoView(snippet);
        }
        else if (e.PropertyName == nameof(PopupViewModel.IsPromptingSnippet))
        {
            // The first value is focused by the field itself when it appears; going back puts the cursor in the search box.
            // Not once the popup is hidden, which is what a paste does right after the prompt closes: focusing a control in a
            // hidden window would take the focus back from the program the paste is going to.
            if (ViewModel.IsPromptingSnippet)
            {
                NeedKeyboard();
            }
            else
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (ViewModel.IsOpen)
                    {
                        _ = SearchBox.Focus(FocusState.Programmatic);
                    }
                });
            }
        }
        else if (e.PropertyName == nameof(PopupViewModel.IsTransformsOpen))
        {
            // The cursor goes into the picker when it opens and back to the search box when it closes. The focus is
            // moved after the layout has caught up with the panel being shown or hidden.
            // A hidden popup is left alone, as with the snippet prompt.
            var open = ViewModel.IsTransformsOpen;
            if (open)
            {
                NeedKeyboard();
            }

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (ViewModel.IsOpen)
                {
                    _ = (open ? TransformFilterBox : SearchBox).Focus(FocusState.Programmatic);
                }
            });
        }
    }

    private void OnSidebarPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!ViewModel.IsOpen)
        {
            return;
        }

        if (e.PropertyName == nameof(SidebarViewModel.IsOpen))
        {
            ResizeForSidebar();
        }
        else if (e.PropertyName == nameof(SidebarViewModel.IsEditing))
        {
            // The cursor goes into the name box while a name is typed and back to the search box afterwards.
            var editing = ViewModel.Sidebar.IsEditing;
            if (editing)
            {
                NeedKeyboard();
            }

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (ViewModel.IsOpen)
                {
                    _ = (editing ? BoardNameBox : SearchBox).Focus(FocusState.Programmatic);
                }
            });
        }
        else if (e.PropertyName == nameof(SidebarViewModel.SelectedIndex) && SidebarList.Items.Count > 0)
        {
            SidebarList.ScrollIntoView(ViewModel.Sidebar.Current);
        }
    }

    // The window grows to the right to make room for the sidebar, and is moved left if that would take it off the screen.
    private void ResizeForSidebar()
    {
        var scale = Content.XamlRoot?.RasterizationScale ?? 1.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var width = Math.Min((int)Math.Round(CurrentWidthDip * scale), area.Width);
        var position = AppWindow.Position;
        var x = Math.Max(area.X, Math.Min(position.X, area.X + area.Width - width));
        AppWindow.MoveAndResize(new RectInt32(x, position.Y, width, AppWindow.Size.Height));
    }

    // The menu is made fresh each time it opens, so it lists the boards as they are now.
    private void BoardMenu_Opening(object? sender, object e)
    {
        BoardMenu.Items.Clear();
        var boards = ViewModel.Sidebar.BoardEntries;
        foreach (var entry in boards)
        {
            var item = new MenuFlyoutItem { Text = entry.Name };
            var id = entry.Board!.Value;
            item.Click += (_, _) => ViewModel.AddToBoardCommand.Execute(id);
            BoardMenu.Items.Add(item);
        }

        if (boards.Count > 0)
        {
            BoardMenu.Items.Add(new MenuFlyoutSeparator());
        }

        var create = new MenuFlyoutItem { Text = "New board\u2026" };
        create.Click += (_, _) => StartNewBoard();
        BoardMenu.Items.Add(create);
    }

    // Opened from the keyboard, the menu takes the keys: the arrows choose a board and Enter adds the clip to it.
    private void BoardMenu_Opened(object? sender, object e)
    {
        if (BoardMenu.Items.FirstOrDefault() is MenuFlyoutItem first)
        {
            _ = first.Focus(FocusState.Keyboard);
        }
    }

    // The cursor goes back to the search box, unless the menu's New board item has started a name.
    private void BoardMenu_Closed(object? sender, object e)
    {
        if (ViewModel.IsOpen && !ViewModel.Sidebar.IsEditing)
        {
            _ = SearchBox.Focus(FocusState.Programmatic);
        }
    }

    private void ShowBoardMenu() => BoardMenu.ShowAt(BoardButton);

    private void StartNewBoard()
    {
        ViewModel.Sidebar.IsOpen = true;
        ViewModel.Sidebar.StartNewCommand.Execute(null);
    }

    private void OnTransformsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var picker = ViewModel.Transforms;
        if (e.PropertyName == nameof(TransformPickerViewModel.SelectedIndex) && picker.Current is { } current)
        {
            TransformList.ScrollIntoView(current);
        }
        else if (e.PropertyName == nameof(TransformPickerViewModel.IsAskingFindReplace) && picker.IsOpen)
        {
            var asking = picker.IsAskingFindReplace;
            _ = DispatcherQueue.TryEnqueue(() => _ = (asking ? FindBox : TransformFilterBox).Focus(FocusState.Programmatic));
        }
    }

    private void TransformList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TransformItemViewModel item)
        {
            ViewModel.Transforms.SelectedIndex = ViewModel.Transforms.Items.IndexOf(item);
            ViewModel.ConfirmTransformCommand.Execute(null);
        }
    }

    private void TransformBack_Click(object sender, RoutedEventArgs e) => ViewModel.EscapeCommand.Execute(null);

    private void SnippetList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SnippetRowViewModel row)
        {
            ViewModel.SelectedIndex = ViewModel.Snippets.Rows.IndexOf(row);
            ViewModel.PasteCommand.Execute(null);
        }
    }

    private void SnippetPromptCancel_Click(object sender, RoutedEventArgs e) => ViewModel.EscapeCommand.Execute(null);

    // The first field of the prompt takes the cursor as it appears, so the user can start typing at once.
    private void SnippetField_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && ReferenceEquals(box.DataContext, ViewModel.Snippets.Fields.FirstOrDefault()))
        {
            _ = box.Focus(FocusState.Programmatic);
        }
    }

    private void ClipList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ClipRowViewModel row)
        {
            ViewModel.SelectedIndex = ViewModel.Rows.IndexOf(row);
            ViewModel.PasteCommand.Execute(null);
        }
    }

    // The presets, then a way to type a custom separator. Choosing one puts the cursor back in the search box.
    private void BuildSeparatorMenu()
    {
        foreach (var option in PopupViewModel.SeparatorOptions)
        {
            var item = new MenuFlyoutItem { Text = option.Label };
            item.Click += (_, _) => ViewModel.ChooseSeparatorCommand.Execute(option.Choice);
            SeparatorMenu.Items.Add(item);
        }

        SeparatorMenu.Items.Add(new MenuFlyoutSeparator());
        var custom = new MenuFlyoutItem { Text = "Custom…" };
        custom.Click += (_, _) => _customSeparatorChosen = true;
        SeparatorMenu.Items.Add(custom);

        // The box for a custom separator opens once the menu has closed, so the menu does not take the focus away from it.
        SeparatorMenu.Closed += (_, _) =>
        {
            if (_customSeparatorChosen)
            {
                _customSeparatorChosen = false;
                ShowCustomSeparatorBox();
            }
            else if (ViewModel.IsOpen)
            {
                _ = SearchBox.Focus(FocusState.Programmatic);
            }
        };
    }

    private void ShowCustomSeparatorBox()
    {
        var box = new TextBox
        {
            Width = 200,
            PlaceholderText = @"For example  |  or \t",
            Header = @"Separator (\n, \r, \t and \\ work)",
        };
        AutomationProperties.SetName(box, "Custom separator");
        var flyout = new Flyout { Content = box, Placement = FlyoutPlacementMode.Top };
        box.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                ViewModel.SetCustomSeparatorCommand.Execute(box.Text);
                flyout.Hide();
                args.Handled = true;
            }
        };
        flyout.Opened += (_, _) =>
        {
            NeedKeyboard();
            _ = box.Focus(FocusState.Programmatic);
        };
        flyout.Closed += (_, _) =>
        {
            if (ViewModel.IsOpen)
            {
                _ = SearchBox.Focus(FocusState.Programmatic);
            }
        };
        flyout.ShowAt(SeparatorButton);
    }

    private void TableColumn_Click(object sender, RoutedEventArgs e) => ViewModel.PasteTableCommand.Execute(TableLayout.Column);

    private void TableRow_Click(object sender, RoutedEventArgs e) => ViewModel.PasteTableCommand.Execute(TableLayout.Row);

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

        // On a button, Enter and Space press the button, as they do everywhere else. In a menu or a flyout every key is its
        // own: its content sits outside the popup's, yet the keys still tunnel through here first.
        var focused = FocusManager.GetFocusedElement(Content.XamlRoot);
        if ((focused is DependencyObject element && !IsInsideFrame(element)) || (e.Key is VirtualKey.Enter or VirtualKey.Space && focused is ButtonBase))
        {
            return;
        }

        if (ViewModel.IsTransformsOpen)
        {
            HandleTransformKey(e, control);
            return;
        }

        if (ViewModel.IsPromptingSnippet)
        {
            HandleSnippetPromptKey(e);
            return;
        }

        if (ViewModel.Sidebar.IsEditing)
        {
            HandleBoardNameKey(e);
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Application:
            case VirtualKey.F10 when shift:
                ShowMenuForCurrent();
                e.Handled = true;
                break;
            case VirtualKey.Enter when shift:
                ViewModel.OpenTransformsCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.K when control:
                ViewModel.ToggleLockCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.B when control:
                ViewModel.Sidebar.ToggleCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.E when control:
                ViewModel.EditSnippetsCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.N when control:
                StartNewBoard();
                e.Handled = true;
                break;
            case VirtualKey.F2:
                ViewModel.Sidebar.StartRenameCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Delete when control && shift:
                ViewModel.Sidebar.DeleteBoardCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.D when control && shift:
                ViewModel.RemoveFromBoardCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.D when control:
                ShowBoardMenu();
                e.Handled = true;
                break;
            case VirtualKey.Up when control:
                ViewModel.MoveUpInBoardCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Down when control:
                ViewModel.MoveDownInBoardCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.PageDown when control:
                ViewModel.Sidebar.NextCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.PageUp when control:
                ViewModel.Sidebar.PreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case >= VirtualKey.Number1 and <= VirtualKey.Number9 when control:
                ViewModel.Sidebar.SelectEntryCommand.Execute(e.Key - VirtualKey.Number1);
                e.Handled = true;
                break;
            case VirtualKey.Down:
                ViewModel.MoveDownCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                ViewModel.MoveUpCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                ViewModel.PasteCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                ViewModel.EscapeCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Delete when SearchBox.SelectionStart >= SearchBox.Text.Length:
                // At the end of the text there is nothing to delete forward, so the key removes the clip instead.
                ViewModel.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Space when control || SearchBox.Text.Length == 0:
                // With nothing typed a space cannot start a search, so it marks the clip. Ctrl+Space marks while searching.
                ViewModel.ToggleMarkCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.P when control:
                ViewModel.TogglePinCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.R when control:
                ViewModel.RevealCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.T when control:
                ViewModel.PasteTableCommand.Execute(shift ? TableLayout.Row : TableLayout.Column);
                e.Handled = true;
                break;
            case VirtualKey.L when control:
                ViewModel.ToggleCollectCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Q when control:
                ViewModel.QueueCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.S when control:
                ViewModel.NextSeparatorCommand.Execute(null);
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    private bool IsInsideFrame(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, Frame))
            {
                return true;
            }
        }

        return false;
    }

    // While a snippet asks for values, Enter goes to the next value and pastes after the last. Tab walks the values too.
    private void HandleSnippetPromptKey(KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                var fields = ViewModel.Snippets.Fields;
                var onLast = FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox { DataContext: SnippetFieldViewModel field } && ReferenceEquals(field, fields.LastOrDefault());
                if (onLast)
                {
                    ViewModel.SubmitSnippetPromptCommand.Execute(null);
                }
                else
                {
                    _ = FocusManager.TryMoveFocus(FocusNavigationDirection.Next);
                }

                e.Handled = true;
                break;
            case VirtualKey.Escape:
                ViewModel.EscapeCommand.Execute(null);
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    // While a board name is typed, Enter saves it and Esc gives it up. Everything else is typing.
    private void HandleBoardNameKey(KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                ViewModel.Sidebar.SubmitEditCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                ViewModel.EscapeCommand.Execute(null);
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    // While the picker is open the keys belong to it. Tab is left alone so the find-and-replace fields can be walked.
    private void HandleTransformKey(KeyRoutedEventArgs e, bool control)
    {
        var picker = ViewModel.Transforms;
        var choosing = picker.IsChoosing;
        switch (e.Key)
        {
            case VirtualKey.Down when choosing:
                picker.MoveDownCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Up when choosing:
                picker.MoveUpCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                (choosing ? ViewModel.ConfirmTransformCommand : ViewModel.SubmitFindReplaceCommand).Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                ViewModel.EscapeCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Space when control && choosing:
                picker.AddToChainCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Back when choosing && TransformFilterBox.Text.Length == 0:
                picker.RemoveLastFromChainCommand.Execute(null);
                e.Handled = true;
                break;
            default:
                break;
        }
    }
}
