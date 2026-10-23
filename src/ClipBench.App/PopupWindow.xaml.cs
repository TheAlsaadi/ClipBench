using ClipBench.Core.Pasting;
using ClipBench.Core.Screen;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Rows;
using ClipBench.Presentation.Transforms;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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
    public const double WidthDip = 420;

    /// <summary>The height of the popup in device-independent pixels.</summary>
    public const double HeightDip = 520;

    private bool _customSeparatorChosen;

    /// <summary>Creates the window, hidden.</summary>
    /// <param name="viewModel">What the window shows.</param>
    public PopupWindow(PopupViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
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
        ViewModel.CloseRequested += (_, _) => AppWindow.Hide();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Transforms.PropertyChanged += OnTransformsPropertyChanged;
        BuildSeparatorMenu();
    }

    /// <summary>What the window shows.</summary>
    public PopupViewModel ViewModel { get; }

    /// <summary>Places the window, shows it and puts the cursor in the search box.</summary>
    /// <param name="bounds">Where the window goes, in device pixels.</param>
    public void ShowAt(PixelRect bounds)
    {
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        AppWindow.Show();
        Activate();
        _ = SearchBox.Focus(FocusState.Programmatic);
    }

    private void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        // Clicking anywhere else, or switching to another window, puts the popup away.
        if (args.WindowActivationState == WindowActivationState.Deactivated && ViewModel.IsOpen)
        {
            ViewModel.CloseNowCommand.Execute(null);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PopupViewModel.SelectedIndex) && ViewModel.Current is { } current)
        {
            ClipList.ScrollIntoView(current);
        }
        else if (e.PropertyName == nameof(PopupViewModel.IsTransformsOpen))
        {
            // The cursor goes into the picker when it opens and back to the search box when it closes. The focus is
            // moved after the layout has caught up with the panel being shown or hidden.
            var open = ViewModel.IsTransformsOpen;
            _ = DispatcherQueue.TryEnqueue(() => _ = (open ? TransformFilterBox : SearchBox).Focus(FocusState.Programmatic));
        }
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
            else
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
        flyout.Opened += (_, _) => _ = box.Focus(FocusState.Programmatic);
        flyout.Closed += (_, _) => _ = SearchBox.Focus(FocusState.Programmatic);
        flyout.ShowAt(SeparatorButton);
    }

    private void TableColumn_Click(object sender, RoutedEventArgs e) => ViewModel.PasteTableCommand.Execute(TableLayout.Column);

    private void TableRow_Click(object sender, RoutedEventArgs e) => ViewModel.PasteTableCommand.Execute(TableLayout.Row);

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

        // On a button, Enter and Space press the button, as they do everywhere else.
        if (e.Key is VirtualKey.Enter or VirtualKey.Space && FocusManager.GetFocusedElement(Content.XamlRoot) is ButtonBase)
        {
            return;
        }

        if (ViewModel.IsTransformsOpen)
        {
            HandleTransformKey(e, control);
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Enter when shift:
                ViewModel.OpenTransformsCommand.Execute(null);
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
