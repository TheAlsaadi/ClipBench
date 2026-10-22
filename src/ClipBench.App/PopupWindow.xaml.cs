using ClipBench.Core.Pasting;
using ClipBench.Core.Screen;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Rows;
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
        custom.Click += (_, _) => ShowCustomSeparatorBox();
        SeparatorMenu.Items.Add(custom);
        SeparatorMenu.Closed += (_, _) => _ = SearchBox.Focus(FocusState.Programmatic);
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
        flyout.Closed += (_, _) => _ = SearchBox.Focus(FocusState.Programmatic);
        flyout.ShowAt(SeparatorButton);
    }

    private void TableColumn_Click(object sender, RoutedEventArgs e) => ViewModel.PasteTableCommand.Execute(TableLayout.Column);

    private void TableRow_Click(object sender, RoutedEventArgs e) => ViewModel.PasteTableCommand.Execute(TableLayout.Row);

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);

        // On a footer button, Enter and Space press the button, as they do everywhere else.
        if (e.Key is VirtualKey.Enter or VirtualKey.Space && FocusManager.GetFocusedElement(Content.XamlRoot) is ButtonBase)
        {
            return;
        }

        switch (e.Key)
        {
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
                var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
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
}
