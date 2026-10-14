using ClipBench.Platform.Windows;
using ClipBench.Presentation.Hosting;
using ClipBench.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ClipBench.App;

/// <summary>
/// The entry point. It is written by hand, not generated, because a second launch must be sent to the running copy
/// before any window or XAML exists.
/// </summary>
public static class Program
{
    /// <summary>Starts the app, or hands over to the copy that is already running.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var options = StartupOptions.Parse(args);
        var paths = options.DataDirectory is { } folder ? new StoragePaths(folder) : StoragePaths.ForCurrentUser();

        using var guard = WindowsPlatform.AcquireSingleInstance(paths.Root);
        if (!guard.IsFirstInstance)
        {
            _ = guard.SignalFirstInstance();
            return 0;
        }

        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(options, paths, guard);
        });

        return 0;
    }
}
