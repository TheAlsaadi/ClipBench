using ClipBench.App.Composition;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.Hosting;
using ClipBench.Core.Notifications;
using ClipBench.Platform.Windows.Diagnostics;
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
    private readonly FileActivityLog _log;
    private readonly CrashNotice _crashNotice = new();
    private readonly CrashHandler _crashHandler;
    private readonly ActivityTimer _startup;

    // How long closing after a crash waits for the services to stop, and for the user to read the notice.
    private static readonly TimeSpan _stopAfterCrashLimit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _noticeAfterCrashLimit = TimeSpan.FromMinutes(2);
    private AppRoot? _root;
    private int _closingAfterCrash;

    /// <summary>Creates the application.</summary>
    /// <param name="options">The command-line options.</param>
    /// <param name="paths">Where the history lives.</param>
    /// <param name="guard">The one-copy guard.</param>
    /// <param name="startup">Started on the first line of the program; stopped once the app is ready.</param>
    public App(StartupOptions options, StoragePaths paths, ISingleInstanceGuard guard, ActivityTimer startup)
    {
        _options = options;
        _paths = paths;
        _guard = guard;
        _startup = startup;

        // The log and the crash handler come first, so an exception anywhere after this line is recorded by type.
        _log = new FileActivityLog(AppRoot.LogFolder(paths), TimeProvider.System);
        _crashHandler = new CrashHandler(_log, _crashNotice);
        UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnProcessCrashing;

        InitializeComponent();

        // ClipBench lives in the tray, and every way out calls Exit itself. By default the message loop ends when the last
        // window closes, which happens half way through shutting down (the popup and the queue window are closed first),
        // so the history would never be closed and the shortcuts never given back.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args) => _ = StartAsync();

    private async Task StartAsync()
    {
        var running = false;
        try
        {
            StartOutcome outcome;
            try
            {
                outcome = await AppRoot.StartAsync(_options, _paths, _guard, DispatcherQueue.GetForCurrentThread(), AskStartFreshAsync, _log, _startup, CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Only the type is shown and logged: a message can quote what was being read.
                _log.Write(ActivityLevel.Error, ActivityEvent.AppCrashed, new ActivityDetails { ExceptionType = exception.GetType(), Source = CrashSource.UiThread });
                outcome = new StartOutcome(null, $"ClipBench could not start ({exception.GetType().Name}). Please start it again.");
            }

            if (outcome.Root is { } root)
            {
                // A crash while the services were starting is already closing the app: what has just started is let go
                // again, so the history is closed and the shortcuts and tray icon are given back.
                if (Volatile.Read(ref _closingAfterCrash) != 0)
                {
                    await root.DisposeAsync();
                    return;
                }

                _root = root;
                root.ExitRequested += (_, _) => _ = ShutDownAsync();
                running = true;
                return;
            }

            if (outcome.Failure is { } failure)
            {
                _ = await AskInNewWindowAsync("ClipBench cannot start", failure, "Close", secondary: null);
            }
        }
        finally
        {
            // Every way out of a start that did not leave the app running ends the process: with no window and no tray
            // icon, a process left behind would only stop the next start from working.
            if (!running)
            {
                Exit();
            }
        }
    }

    // A crash that is closing the app answers for the user: Quit, which leaves the history files untouched.
    private Task<bool> AskStartFreshAsync() =>
        Volatile.Read(ref _closingAfterCrash) != 0
            ? Task.FromResult(false)
            : AskInNewWindowAsync(
                "ClipBench cannot read your saved history",
                "The saved history cannot be decrypted. That happens after a change to your Windows account or when the file is damaged. ClipBench can start with an empty history, which deletes the old one for good, or you can quit and leave the files untouched.",
                "Start with an empty history",
                "Quit");

    // Each question gets a window of its own. A notice window answers once, so sharing one would let the answer to one
    // question (Close on a crash notice) count as the answer to another (start with an empty history).
    private static async Task<bool> AskInNewWindowAsync(string title, string message, string primary, string? secondary)
    {
        var notice = new NoticeWindow();
        try
        {
            return await notice.AskAsync(title, message, primary, secondary);
        }
        finally
        {
            notice.Close();
        }
    }

    /// <summary>Stops the app cleanly: services first, then the window that keeps it alive.</summary>
    /// <returns>A task that completes when the app is ready to exit.</returns>
    public async Task ShutDownAsync()
    {
        try
        {
            await StopRootAsync();
        }
        finally
        {
            // Whatever went wrong while stopping, the app still has to end: nothing is left running to clean up.
            Exit();
        }
    }

    private async Task StopRootAsync()
    {
        if (_root is { } root)
        {
            _root = null;
            await root.DisposeAsync();
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Handled, so WinUI does not end the process before the history is closed; the app closes itself below.
        e.Handled = true;
        if (_crashHandler.Handle(e.Exception, CrashSource.UiThread) == CrashResponse.CloseApp
            && Interlocked.Exchange(ref _closingAfterCrash, 1) == 0)
        {
            _ = CloseAfterCrashAsync();
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _ = _crashHandler.Handle(e.Exception, CrashSource.BackgroundTask);
        e.SetObserved();
    }

    private void OnProcessCrashing(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _ = _crashHandler.Handle(exception, CrashSource.Process);
        }
    }

    // Stops every service first, so nothing keeps capturing or holding decrypted clips in a state that can no longer be
    // trusted, then says why the app is closing, then exits. Neither step may keep the process alive: a service that
    // never stops, or a notice nobody can see after a broken window, would leave a process with no tray icon that stops
    // the next start from working. Each step therefore has a time limit.
    private async Task CloseAfterCrashAsync()
    {
        try
        {
            _ = await Task.WhenAny(StopRootAsync(), Task.Delay(_stopAfterCrashLimit));
            if (_crashNotice.Text is { } text)
            {
                _ = await Task.WhenAny(
                    AskInNewWindowAsync(_crashNotice.Title ?? CrashHandler.Title, text, "Close", secondary: null),
                    Task.Delay(_noticeAfterCrashLimit));
            }
        }
#pragma warning disable CA1031 // The app is closing after a crash: a second failure here must not stop it from exiting.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
        finally
        {
            Exit();
        }
    }

    // Keeps the notice the crash handler asks for, so it can be shown in a window once the services are stopped. The
    // tray's own notices would disappear with the tray icon as the app exits.
    private sealed class CrashNotice : IUserNotifier
    {
        public string? Title { get; private set; }

        public string? Text { get; private set; }

        public void Show(string title, string message)
        {
            Title = title;
            Text = message;
        }
    }
}
