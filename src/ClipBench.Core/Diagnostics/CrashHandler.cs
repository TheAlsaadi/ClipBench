using ClipBench.Core.Notifications;

namespace ClipBench.Core.Diagnostics;

/// <summary>Where an unexpected exception surfaced.</summary>
public enum CrashSource
{
    /// <summary>Not given.</summary>
    Unspecified,

    /// <summary>The UI thread: a window, a binding or an event handler threw and nothing caught it.</summary>
    UiThread,

    /// <summary>A background task faulted and nothing observed it.</summary>
    BackgroundTask,

    /// <summary>The runtime is ending the process.</summary>
    Process,
}

/// <summary>What the app does after an unexpected exception.</summary>
public enum CrashResponse
{
    /// <summary>Carry on: the fault stayed inside one background task.</summary>
    KeepRunning,

    /// <summary>Close cleanly: the app's state can no longer be trusted.</summary>
    CloseApp,
}

/// <summary>
/// Decides what happens after an exception that nothing caught, and records it by type only: the message can quote what
/// was being read, so it never reaches the log or the user. Handling a crash never throws.
/// </summary>
public sealed class CrashHandler
{
    /// <summary>The heading of the notice.</summary>
    public const string Title = "ClipBench";

    /// <summary>The notice shown once before the app closes.</summary>
    public const string ClosingNotice =
        "ClipBench ran into a problem and has to close. Start it again from the Start menu. The log records only the kind of problem, never what you copied.";

    private readonly IActivityLog _log;
    private readonly IUserNotifier _notifier;
    private int _notified;

    /// <summary>Creates the handler.</summary>
    /// <param name="log">Where the crash is recorded.</param>
    /// <param name="notifier">Tells the user the app has to close.</param>
    public CrashHandler(IActivityLog log, IUserNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(notifier);
        _log = log;
        _notifier = notifier;
    }

    /// <summary>Records an unexpected exception and says what to do next.</summary>
    /// <param name="exception">The exception. Only its type is kept.</param>
    /// <param name="source">Where it surfaced.</param>
    /// <returns>Whether the app keeps running or closes.</returns>
    public CrashResponse Handle(Exception exception, CrashSource source)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var keepRunning = source == CrashSource.BackgroundTask;
        var type = exception is AggregateException { InnerExceptions: [var first, ..] } ? first.GetType() : exception.GetType();
        Try(() => _log.Write(
            keepRunning ? ActivityLevel.Warning : ActivityLevel.Error,
            keepRunning ? ActivityEvent.BackgroundFaulted : ActivityEvent.AppCrashed,
            new ActivityDetails { ExceptionType = type, Source = source }));

        if (keepRunning)
        {
            return CrashResponse.KeepRunning;
        }

        // The process is already ending when the runtime reports it: there is no window left to show a notice in.
        if (source != CrashSource.Process && Interlocked.Exchange(ref _notified, 1) == 0)
        {
            Try(() => _notifier.Show(Title, ClosingNotice));
        }

        return CrashResponse.CloseApp;
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031 // Handling a crash must not raise another one: whatever the log or the notice throws is dropped.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
