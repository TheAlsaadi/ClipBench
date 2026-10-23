using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.Notifications;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;

namespace ClipBench.Presentation.Popup;

/// <summary>How a paste is carried out.</summary>
public sealed record PasteFlowOptions
{
    /// <summary>How long to wait after Ctrl+V before the clipboard is put back.</summary>
    public TimeSpan RestoreDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The id of this program's process, so its own windows are never taken for a place to paste into.</summary>
    public int OwnProcessId { get; init; } = Environment.ProcessId;
}

/// <summary>
/// Remembers the window that had focus when the popup opened, pastes the chosen clip back into it, and tells
/// the user in plain words when that did not work.
/// </summary>
public sealed class PasteFlow : IPasteFlow
{
    private const string Title = "ClipBench";

    private readonly IForegroundTracker _tracker;
    private readonly IPasteInjector _injector;
    private readonly IClipboardWriter _writer;
    private readonly IUserNotifier _notifier;
    private readonly IActivityLog _log;
    private readonly SettingsService _settings;
    private readonly IQueueControl _queue;
    private readonly PasteFlowOptions _options;
    private readonly Lock _gate = new();
    private WindowHandle? _target;
    private bool _held;

    /// <summary>Creates the flow.</summary>
    /// <param name="tracker">Finds the window that has focus.</param>
    /// <param name="injector">Does the paste.</param>
    /// <param name="writer">Puts a clip on the clipboard when pasting is not possible, so the user can paste by hand.</param>
    /// <param name="notifier">Tells the user when a paste did not work.</param>
    /// <param name="log">Records what happened, without content.</param>
    /// <param name="settings">Says whether to put the clipboard back after the paste.</param>
    /// <param name="queue">The paste queue, which a paste ends first.</param>
    /// <param name="options">How to paste, or null for the defaults.</param>
    public PasteFlow(
        IForegroundTracker tracker,
        IPasteInjector injector,
        IClipboardWriter writer,
        IUserNotifier notifier,
        IActivityLog log,
        SettingsService settings,
        IQueueControl queue,
        PasteFlowOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(injector);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(log);
        _tracker = tracker;
        _injector = injector;
        _writer = writer;
        _notifier = notifier;
        _log = log;
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(queue);
        _settings = settings;
        _queue = queue;
        _options = options ?? new PasteFlowOptions();
    }

    /// <inheritdoc/>
    public void Begin(bool recordTarget = true)
    {
        var focused = recordTarget ? _tracker.Capture() : default;
        lock (_gate)
        {
            _target = recordTarget && focused.IsSuccess ? focused.Value.Window : null;
        }
    }

    /// <inheritdoc/>
    public void Hold(bool hold)
    {
        lock (_gate)
        {
            _held = hold;
        }
    }

    /// <inheritdoc/>
    public void ReturnToTarget()
    {
        WindowHandle? target;
        lock (_gate)
        {
            target = _target;
        }

        if (target is { } window)
        {
            _ = _tracker.Restore(window);
        }
    }

    /// <inheritdoc/>
    public Task PasteAsync(Clip clip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return PasteContentAsync(clip.Content, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task PasteContentAsync(ClipContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        // A window on record is good for one paste: the next popup records its own. A held flow belongs to a popup that stays
        // on screen, so it keeps the window, and it follows the user to another program: the popup does not take the focus
        // when it is clicked, so the program in front is the one the user is working in.
        WindowHandle? target;
        bool held;
        lock (_gate)
        {
            held = _held;
            target = _target;
            if (!held)
            {
                _target = null;
            }
        }

        if (held && _tracker.Capture() is { IsSuccess: true, Value: var front } && front.ProcessId != _options.OwnProcessId)
        {
            target = front.Window;
            lock (_gate)
            {
                _target = target;
            }
        }

        var request = new PasteRequest
        {
            Content = content,
            Target = target,
            RestorePreviousClipboard = _settings.Current.RestorePreviousClipboard,
            RestoreDelay = _options.RestoreDelay,
        };

        try
        {
            // The paste presses Ctrl+V. A running queue holds Ctrl+V and would take the press for its own, so it ends first.
            if (_queue.Progress.IsRunning)
            {
                await _queue.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            var result = await _injector.PasteAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                _log.Write(
                    ActivityLevel.Information,
                    ActivityEvent.PasteSucceeded,
                    new ActivityDetails { Format = content.Format, Bytes = content.SizeInBytes });
                return;
            }

            _log.Write(ActivityLevel.Warning, ActivityEvent.PasteFailed, new ActivityDetails { Error = result.Error.Code });
            await TellAboutAsync(result.Error.Code, content, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user gave up before any key was sent.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Only the type is recorded: an exception message can quote what was being pasted.
            _log.Write(ActivityLevel.Error, ActivityEvent.PasteFaulted, new ActivityDetails { ExceptionType = exception.GetType() });
            _notifier.Show(Title, "The paste did not work.");
        }
    }

    private async Task TellAboutAsync(ErrorCode code, ClipContent content, CancellationToken cancellationToken)
    {
        switch (code)
        {
            case ErrorCode.TargetElevated:
                await LeaveOnClipboardAsync(
                    content,
                    "Windows does not let ClipBench paste into a window that runs as administrator.",
                    cancellationToken).ConfigureAwait(false);
                break;
            case ErrorCode.InputBlocked:
                await LeaveOnClipboardAsync(content, "Windows blocked the paste.", cancellationToken).ConfigureAwait(false);
                break;
            case ErrorCode.TargetNotFound:
                _notifier.Show(Title, "The window you were in has closed, so nothing was pasted.");
                break;
            case ErrorCode.FocusNotRestored:
                _notifier.Show(Title, "ClipBench could not return to the window you were in, so nothing was pasted.");
                break;
            case ErrorCode.ClipboardChangedBeforePaste:
                _notifier.Show(Title, "Another program changed the clipboard during the paste, so nothing was pasted.");
                break;
            case ErrorCode.ClipboardBusy:
                _notifier.Show(Title, "Another program is using the clipboard. Try again in a moment.");
                break;
            case ErrorCode.ClipboardWriteFailed:
                _notifier.Show(Title, "ClipBench could not put the clip on the clipboard.");
                break;
            default:
                _notifier.Show(Title, "The paste did not work.");
                break;
        }
    }

    // When a window cannot be pasted into, the next best thing is the clip on the clipboard, so the user can paste it themselves.
    private async Task LeaveOnClipboardAsync(ClipContent content, string reason, CancellationToken cancellationToken)
    {
        var written = await _writer.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        _notifier.Show(Title, written.IsSuccess ? reason + " The clip is on your clipboard: press Ctrl+V." : reason);
    }
}
