using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace ClipBench.App;

/// <summary>
/// A small window that tells the user something serious at start-up, or asks one question. It exists because
/// start-up can fail or need a decision before the popup exists.
/// </summary>
public sealed partial class NoticeWindow : Window
{
    private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Creates the window, hidden.</summary>
    public NoticeWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(560, 280));
        Closed += (_, _) => _ = _answer.TrySetResult(false);
    }

    /// <summary>Shows the notice and waits for an answer.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="message">What happened and what the choices do.</param>
    /// <param name="primary">The label of the main choice.</param>
    /// <param name="secondary">The label of the other choice, or null to offer only one.</param>
    /// <returns>True when the main choice was picked; false for the other one or when the window was closed.</returns>
    public Task<bool> AskAsync(string title, string message, string primary, string? secondary)
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primary;
        SecondaryButton.Content = secondary;
        SecondaryButton.Visibility = secondary is null ? Visibility.Collapsed : Visibility.Visible;

        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(display.X + ((display.Width - size.Width) / 2), display.Y + ((display.Height - size.Height) / 2)));
        Activate();
        return _answer.Task;
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => _ = _answer.TrySetResult(true);

    private void OnSecondaryClick(object sender, RoutedEventArgs e) => _ = _answer.TrySetResult(false);
}
