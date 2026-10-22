using ClipBench.Core.Screen;
using ClipBench.Presentation.Queue;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Windows.Graphics;

namespace ClipBench.App;

/// <summary>
/// The small window that shows a running paste queue. It shows and hides itself as <see cref="QueueHudViewModel"/>
/// says, and it does not keep focus: focus has to stay in the window the user is filling in.
/// </summary>
public sealed partial class QueueHudWindow : Window
{
    /// <summary>The width of the window in device-independent pixels.</summary>
    public const double WidthDip = 240;

    /// <summary>The height of the window in device-independent pixels.</summary>
    public const double HeightDip = 44;

    private readonly Func<PixelRect> _place;

    /// <summary>Creates the window, hidden.</summary>
    /// <param name="viewModel">What the window shows.</param>
    /// <param name="place">Works out where the window goes, in device pixels, each time it is shown.</param>
    public QueueHudWindow(QueueHudViewModel viewModel, Func<PixelRect> place)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(place);
        ViewModel = viewModel;
        _place = place;
        InitializeComponent();

        // A tool window has no taskbar button and is left out of Alt+Tab.
        var presenter = OverlappedPresenter.CreateForToolWindow();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        // WinUI loads a window's content, and starts its bindings, only when the window is first activated, and a window
        // shown without activation stays blank. So it is activated once now, off screen, and hidden again at once. This
        // is at start-up, before any queue runs; from then on it is only ever shown without focus.
        AppWindow.Move(new PointInt32(-32000, -32000));
        Activate();
        AppWindow.Hide();

        Activated += OnActivated;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>What the window shows.</summary>
    public QueueHudViewModel ViewModel { get; }

    // The window is shown without focus, but a click can still give it focus. It then steps aside, so Windows hands
    // focus back to the window the user was in, and comes back without taking focus. Only a click counts: WinUI also
    // reports an activation by code when the window is first shown, and answering that would hide it again at once.
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.PointerActivated || !ViewModel.IsVisible)
        {
            return;
        }

        AppWindow.Hide();
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel.IsVisible)
            {
                AppWindow.Show(activateWindow: false);
            }
        });
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // The window never has focus, so a screen reader hears about it only through its live region.
        if (e.PropertyName == nameof(QueueHudViewModel.AutomationText) && ViewModel.IsVisible)
        {
            FrameworkElementAutomationPeer.CreatePeerForElement(Root).RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            return;
        }

        if (e.PropertyName != nameof(QueueHudViewModel.IsVisible))
        {
            return;
        }

        if (ViewModel.IsVisible)
        {
            var bounds = _place();
            AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));

            // Shown without being activated, so the next Ctrl+V still goes to the window the user is in.
            AppWindow.Show(activateWindow: false);
        }
        else
        {
            AppWindow.Hide();
        }
    }
}
