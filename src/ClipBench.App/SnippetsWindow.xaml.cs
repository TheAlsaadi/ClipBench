using ClipBench.Presentation.Snippets;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace ClipBench.App;

/// <summary>
/// The window where snippets are written. It holds no logic of its own: keys and clicks are handed to
/// <see cref="SnippetEditorViewModel"/>, which the tests cover. Closing it only hides it, so the next open is quick.
/// </summary>
public sealed partial class SnippetsWindow : Window
{
    private const double WidthDip = 860;
    private const double HeightDip = 640;

    private bool _closing;
    private bool _sized;

    /// <summary>Creates the window, hidden.</summary>
    /// <param name="viewModel">What the window shows.</param>
    public SnippetsWindow(SnippetEditorViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
        AppWindow.Closing += OnClosing;
    }

    /// <summary>What the window shows.</summary>
    public SnippetEditorViewModel ViewModel { get; }

    /// <summary>The short explanation of the variables, shown under the text.</summary>
    public static string Help => SnippetEditorViewModel.HelpText;

    /// <summary>Shows the window with the first snippet chosen, or a new one when there are none.</summary>
    public void ShowEditor()
    {
        ViewModel.Reset();
        AppWindow.Show();
        Activate();
        if (!_sized)
        {
            // The scale of the screen is known once the window has content on screen.
            _sized = true;
            var scale = Content.XamlRoot?.RasterizationScale ?? 1.0;
            AppWindow.Resize(new SizeInt32((int)Math.Round(WidthDip * scale), (int)Math.Round(HeightDip * scale)));
        }

        _ = DispatcherQueue.TryEnqueue(() => _ = (ViewModel.IsNew ? (Control)NameBox : SnippetNames).Focus(FocusState.Programmatic));
    }

    /// <summary>Closes the window for good, as the app exits.</summary>
    public void Shutdown()
    {
        _closing = true;
        Close();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_closing)
        {
            // Hidden, not closed: unsaved changes are dropped the next time it is shown.
            args.Cancel = true;
            sender.Hide();
        }
    }

    private void Frame_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.S when control:
                ViewModel.SaveCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.N when control:
                ViewModel.NewCommand.Execute(null);
                _ = NameBox.Focus(FocusState.Programmatic);
                e.Handled = true;
                break;
            case VirtualKey.Delete when control:
                ViewModel.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                // Backs out of one thing at a time: the question about deleting, then the unsaved changes, then the window.
                if (ViewModel.IsConfirmingDelete)
                {
                    ViewModel.CancelDeleteCommand.Execute(null);
                }
                else if (ViewModel.IsDirty)
                {
                    ViewModel.DiscardCommand.Execute(null);
                }
                else
                {
                    AppWindow.Hide();
                }

                e.Handled = true;
                break;
            default:
                break;
        }
    }
}
