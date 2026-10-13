using ClipBench.App.Composition;
using ClipBench.Core.Hosting;
using ClipBench.Presentation.Hosting;
using ClipBench.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ClipBench.App;

/// <summary>The application: it starts the composition root and keeps it alive until the app exits.</summary>
public partial class App : Application
{
    private readonly StartupOptions _options;
    private readonly StoragePaths _paths;
    private readonly ISingleInstanceGuard _guard;
    private NoticeWindow? _notice;
    private AppRoot? _root;

    /// <summary>Creates the application.</summary>
    /// <param name="options">The command-line options.</param>
    /// <param name="paths">Where the history lives.</param>
    /// <param name="guard">The one-copy guard.</param>
    public App(StartupOptions options, StoragePaths paths, ISingleInstanceGuard guard)
    {
        _options = options;
        _paths = paths;
        _guard = guard;
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args) => _ = StartAsync();

    private async Task StartAsync()
    {
        StartOutcome outcome;
        try
        {
            outcome = await AppRoot.StartAsync(_options, _paths, _guard, DispatcherQueue.GetForCurrentThread(), AskStartFreshAsync, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Only the type is shown: a message can quote what was being read.
            outcome = new StartOutcome(null, $"ClipBench could not start ({exception.GetType().Name}). Please start it again.");
        }

        if (outcome.Root is { } root)
        {
            _root = root;
            root.ExitRequested += (_, _) => _ = ShutDownAsync();
            _notice?.Close();
            return;
        }

        if (outcome.Failure is { } failure)
        {
            _notice ??= new NoticeWindow();
            _ = await _notice.AskAsync("ClipBench cannot start", failure, "Close", secondary: null);
        }

        _notice?.Close();
        Exit();
    }

    private Task<bool> AskStartFreshAsync()
    {
        _notice ??= new NoticeWindow();
        return _notice.AskAsync(
            "ClipBench cannot read your saved history",
            "The saved history cannot be decrypted. That happens after a change to your Windows account or when the file is damaged. ClipBench can start with an empty history, which deletes the old one for good, or you can quit and leave the files untouched.",
            "Start with an empty history",
            "Quit");
    }

    /// <summary>Stops the app cleanly: services first, then the window that keeps it alive.</summary>
    /// <returns>A task that completes when the app is ready to exit.</returns>
    public async Task ShutDownAsync()
    {
        if (_root is { } root)
        {
            _root = null;
            await root.DisposeAsync();
        }

        Exit();
    }
}
