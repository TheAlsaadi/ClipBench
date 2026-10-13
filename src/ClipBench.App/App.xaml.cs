using Microsoft.UI.Xaml;

namespace ClipBench.App;

/// <summary>Application entry point and, from Phase 4, the composition root.</summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>Initializes the application.</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
