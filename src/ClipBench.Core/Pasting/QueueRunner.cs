using System.Diagnostics.CodeAnalysis;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.Hotkeys;
using ClipBench.Core.Notifications;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;

namespace ClipBench.Core.Pasting;

/// <summary>Where a running queue is: its state, how many clips were pasted and how many there are.</summary>
/// <param name="State">The state of the queue.</param>
/// <param name="Pasted">How many clips were pasted.</param>
/// <param name="Count">How many clips the queue holds.</param>
public readonly record struct QueueProgress(QueueState State, int Pasted, int Count)
{
    /// <summary>True while the queue waits for Ctrl+V or is pasting.</summary>
    public bool IsRunning => State is QueueState.Armed or QueueState.Pasting;
}

/// <summary>
/// Queue paste: while a queue runs, each Ctrl+V the user presses pastes the next clip, and Esc stops it. Both keys
/// are claimed with RegisterHotKey through <see cref="IHotkeyService"/>, never with a keyboard hook, and only while a
/// queue runs (ADR 0015). For each paste Ctrl+V is released, so the keys ClipBench sends reach the program, and claimed
/// again afterwards. Every way a queue ends gives both keys back: the last clip, Esc, <see cref="StopAsync"/>, a paste
/// that fails, a new queue and disposal. Steps run one at a time; Esc takes effect at once and a paste that is under
/// way finishes first.
/// </summary>
public sealed class QueueRunner : IQueueControl, IAsyncDisposable
{
    private const string Title = "ClipBench";

    private readonly IHotkeyService _hotkeys;
    private readonly IPasteInjector _injector;
    private readonly IForegroundTracker _tracker;
    private readonly SettingsService _settings;
    private readonly IUserNotifier _notifier;
    private readonly IActivityLog _log;
    private readonly PasteQueue _queue = new();

    // Not disposed on purpose: a press reported late may still wait on it after disposal, and it holds no handle.
    [SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed", Justification = "See the comment above.")]
    private readonly SemaphoreSlim _steps = new(1, 1);
    private readonly Lock _keysGate = new();
    private HotkeyId? _pasteKey;
    private HotkeyId? _stopKey;
    private int _disposed;

    /// <summary>Creates the runner. Nothing is claimed until a queue starts.</summary>
    /// <param name="hotkeys">Claims and releases the queue's keys.</param>
    /// <param name="injector">Pastes each clip.</param>
    /// <param name="tracker">Finds the window that has focus when Ctrl+V is pressed.</param>
    /// <param name="settings">Says whether to put the clipboard back after each paste.</param>
    /// <param name="notifier">Tells the user why a queue stopped early.</param>
    /// <param name="log">Records what happened, without content.</param>
    public QueueRunner(IHotkeyService hotkeys, IPasteInjector injector, IForegroundTracker tracker, SettingsService settings, IUserNotifier notifier, IActivityLog log)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);
        ArgumentNullException.ThrowIfNull(injector);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(log);
        _hotkeys = hotkeys;
        _injector = injector;
        _tracker = tracker;
        _settings = settings;
        _notifier = notifier;
        _log = log;
        _hotkeys.Pressed += OnPressed;
    }

    /// <summary>
    /// Raised after every step: a start, a paste, the end. It runs on the thread that took the step, often a pool
    /// thread. A subscriber that throws is skipped.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>The key that pastes the next clip: Ctrl+V.</summary>
    public static Hotkey PasteKey { get; } = new(HotkeyModifiers.Control, 'V');

    /// <summary>The key that stops the queue: Esc on its own.</summary>
    public static Hotkey StopKey { get; } = new(HotkeyModifiers.None, 0x1B);

    /// <summary>Where the queue is.</summary>
    public QueueProgress Progress => _queue.Progress;

    /// <summary>
    /// Starts a queue: claims Ctrl+V and Esc and waits for the first Ctrl+V. A queue that is running is stopped first.
    /// </summary>
    /// <param name="clips">The clips, in the order they are to be pasted.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>
    /// Success, <see cref="ErrorCode.NothingSelected"/>, or <see cref="ErrorCode.HotkeyUnavailable"/> when another program
    /// holds one of the keys. A queue that did not start holds no key.
    /// </returns>
    public async Task<Result> StartAsync(IReadOnlyList<Clip> clips, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clips);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _steps.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Progress.IsRunning)
            {
                _queue.Cancel();
                await ReleaseKeysAsync().ConfigureAwait(false);
            }

            var armed = _queue.Arm(clips);
            if (armed.IsFailure)
            {
                return armed;
            }

            var paste = await _hotkeys.RegisterAsync(PasteKey, cancellationToken).ConfigureAwait(false);
            if (paste.IsFailure)
            {
                _queue.Cancel();
                return KeyTaken("Another program is using Ctrl+V, so the queue cannot start.");
            }

            SetKeys(paste.Value, null);
            var stop = await _hotkeys.RegisterAsync(StopKey, cancellationToken).ConfigureAwait(false);
            if (stop.IsFailure)
            {
                _queue.Cancel();
                await ReleaseKeysAsync().ConfigureAwait(false);
                return KeyTaken("Another program is using Esc, so the queue cannot start.");
            }

            SetKeys(paste.Value, stop.Value);
            _log.Write(ActivityLevel.Information, ActivityEvent.QueueStarted, new ActivityDetails { Count = clips.Count });
        }
        finally
        {
            _ = _steps.Release();
        }

        RaiseChanged();
        return Result.Success();
    }

    /// <summary>Stops the queue at once and gives both keys back. A paste that is under way finishes first.</summary>
    /// <param name="cancellationToken">Cancels the wait for a paste under way; the queue is stopped either way.</param>
    /// <returns>A task that completes when the keys are given back.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var wasRunning = Progress.IsRunning;
        _queue.Cancel();

        await _steps.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReleaseKeysAsync().ConfigureAwait(false);
        }
        finally
        {
            _ = _steps.Release();
        }

        if (wasRunning)
        {
            _log.Write(ActivityLevel.Information, ActivityEvent.QueueStopped);
            RaiseChanged();
        }
    }

    /// <summary>Stops the queue, gives both keys back and stops listening for them.</summary>
    /// <returns>A task that completes when the keys are given back.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _hotkeys.Pressed -= OnPressed;
        _queue.Cancel();
        await _steps.WaitAsync().ConfigureAwait(false);
        try
        {
            await ReleaseKeysAsync().ConfigureAwait(false);
        }
        finally
        {
            _ = _steps.Release();
        }
    }

    private static Result KeyTaken(string message) => Result.Failure(new ResultError(ErrorCode.HotkeyUnavailable, message));

    // Runs on a pool thread, one call per press.
    private void OnPressed(object? sender, HotkeyPressedEventArgs e) => _ = HandlePressAsync(e.Id);

    private async Task HandlePressAsync(HotkeyId id)
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            HotkeyId? pasteKey, stopKey;
            lock (_keysGate)
            {
                (pasteKey, stopKey) = (_pasteKey, _stopKey);
            }

            if (id == stopKey)
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            else if (id == pasteKey)
            {
                await PasteNextAsync(id).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Only the type is recorded: an exception message can quote what was being pasted.
            _log.Write(ActivityLevel.Error, ActivityEvent.QueueFaulted, new ActivityDetails { ExceptionType = exception.GetType() });
        }
    }

    private async Task PasteNextAsync(HotkeyId pressed)
    {
        await _steps.WaitAsync().ConfigureAwait(false);
        try
        {
            // A press reported after Ctrl+V was let go, or after the queue ended, is stale.
            lock (_keysGate)
            {
                if (pressed != _pasteKey)
                {
                    return;
                }
            }

            var next = _queue.Next();
            if (next.IsFailure)
            {
                return;
            }

            // Let go of Ctrl+V, or the V that ClipBench presses would come back here instead of reaching the program.
            await ReleasePasteKeyAsync().ConfigureAwait(false);
            if (!await PasteAsync(next.Value).ConfigureAwait(false))
            {
                return;
            }

            if (_queue.Complete().IsFailure)
            {
                // Esc stopped the queue while the clip was being pasted. The keys go back now rather than when the
                // stop gets its turn, which a waiting step only gets after this one has ended.
                await ReleaseKeysAsync().ConfigureAwait(false);
                return;
            }

            if (_queue.State == QueueState.Exhausted)
            {
                await ReleaseKeysAsync().ConfigureAwait(false);
                _log.Write(ActivityLevel.Information, ActivityEvent.QueueFinished);
            }
            else
            {
                var again = await _hotkeys.RegisterAsync(PasteKey, CancellationToken.None).ConfigureAwait(false);
                if (again.IsFailure)
                {
                    await FailAsync(again.Error.Code, "The queue stopped because ClipBench could not take Ctrl+V back from another program.").ConfigureAwait(false);
                    return;
                }

                lock (_keysGate)
                {
                    _pasteKey = again.Value;
                }
            }
        }
        finally
        {
            _ = _steps.Release();
        }

        RaiseChanged();
    }

    // Returns false when the paste did not work; the queue is then stopped and the user told why.
    private async Task<bool> PasteAsync(Clip clip)
    {
        var focused = _tracker.Capture();
        var request = new PasteRequest
        {
            Content = clip.Content,
            Target = focused.IsSuccess ? focused.Value.Window : null,
            RestorePreviousClipboard = _settings.Current.RestorePreviousClipboard,
        };

        Result pasted;
        try
        {
            pasted = await _injector.PasteAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _log.Write(ActivityLevel.Error, ActivityEvent.QueueFaulted, new ActivityDetails { ExceptionType = exception.GetType() });
            await StopForFailureAsync("The queue stopped because the paste did not work.").ConfigureAwait(false);
            return false;
        }

        if (pasted.IsFailure)
        {
            var message = pasted.Error.Code == ErrorCode.TargetElevated
                ? "The queue stopped: Windows does not let ClipBench paste into a window that runs as administrator."
                : "The queue stopped because the paste did not work.";
            await FailAsync(pasted.Error.Code, message).ConfigureAwait(false);
            return false;
        }

        _log.Write(
            ActivityLevel.Information,
            ActivityEvent.QueuePasted,
            new ActivityDetails { Format = clip.Content.Format, Bytes = clip.Content.SizeInBytes });
        return true;
    }

    private async Task FailAsync(ErrorCode code, string message)
    {
        _log.Write(ActivityLevel.Warning, ActivityEvent.QueueFailed, new ActivityDetails { Error = code });
        await StopForFailureAsync(message).ConfigureAwait(false);
    }

    private async Task StopForFailureAsync(string message)
    {
        _queue.Cancel();
        await ReleaseKeysAsync().ConfigureAwait(false);
        _notifier.Show(Title, message);
        RaiseChanged();
    }

    private void SetKeys(HotkeyId? pasteKey, HotkeyId? stopKey)
    {
        lock (_keysGate)
        {
            _pasteKey = pasteKey;
            _stopKey = stopKey;
        }
    }

    private async Task ReleasePasteKeyAsync()
    {
        HotkeyId? pasteKey;
        lock (_keysGate)
        {
            pasteKey = _pasteKey;
            _pasteKey = null;
        }

        if (pasteKey is { } id)
        {
            _ = await _hotkeys.UnregisterAsync(id, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task ReleaseKeysAsync()
    {
        await ReleasePasteKeyAsync().ConfigureAwait(false);

        HotkeyId? stopKey;
        lock (_keysGate)
        {
            stopKey = _stopKey;
            _stopKey = null;
        }

        if (stopKey is { } id)
        {
            _ = await _hotkeys.UnregisterAsync(id, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void RaiseChanged()
    {
        if (Changed is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A subscriber's bug must not stop the queue.
            }
        }
    }
}
