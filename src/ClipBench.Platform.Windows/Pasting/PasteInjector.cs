using ClipBench.Core.Capture;
using ClipBench.Core.Hotkeys;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Clipboard;

namespace ClipBench.Platform.Windows.Pasting;

/// <summary>
/// Pastes by putting the content on the clipboard and pressing Ctrl+V, then puts the user's own clipboard
/// back. The order is the point: look at the target first (and stop if Windows would drop the keys), keep a
/// copy of what the user had, write, bring the target to the front and check it really is, check nobody else
/// changed the clipboard in the meantime, wait for held keys, press, and restore. No key is sent unless the
/// target has focus, and a restore never overwrites something another program copied.
/// </summary>
internal sealed class PasteInjector : IPasteInjector
{
    private readonly IClipboardWriter _writer;
    private readonly IClipboardApi _clipboard;
    private readonly ITargetInspector _inspector;
    private readonly IWindowApi _windows;
    private readonly IKeyboardApi _keyboard;
    private readonly TimeProvider _time;
    private readonly PasteTiming _timing;
    private readonly ClipboardRetryPolicy _retry;
    private readonly uint _ownWriteFormat;

    /// <summary>Creates an injector.</summary>
    /// <param name="writer">Puts the content on the clipboard.</param>
    /// <param name="clipboard">The clipboard, for the sequence number and the restore.</param>
    /// <param name="inspector">Looks at the target window.</param>
    /// <param name="windows">The window calls.</param>
    /// <param name="keyboard">The keyboard.</param>
    /// <param name="time">The clock all the waits use.</param>
    /// <param name="timing">The waits and limits, or null for the defaults.</param>
    /// <param name="retry">How long to keep trying when another program holds the clipboard, or null for the default.</param>
    public PasteInjector(
        IClipboardWriter writer,
        IClipboardApi clipboard,
        ITargetInspector inspector,
        IWindowApi windows,
        IKeyboardApi keyboard,
        TimeProvider time,
        PasteTiming? timing = null,
        ClipboardRetryPolicy? retry = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(keyboard);
        ArgumentNullException.ThrowIfNull(time);
        _writer = writer;
        _clipboard = clipboard;
        _inspector = inspector;
        _windows = windows;
        _keyboard = keyboard;
        _time = time;
        _timing = timing ?? new PasteTiming();
        _retry = retry ?? ClipboardRetryPolicy.Default;
        _ownWriteFormat = clipboard.RegisterFormat(ClipboardFormatNames.OwnWrite);
    }

    /// <inheritdoc/>
    public async Task<Result> PasteAsync(PasteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Nothing has been touched yet, so a refusal here costs the user nothing.
        if (request.Target is { } target)
        {
            var described = _inspector.Describe(target);
            if (described.IsFailure)
            {
                return Result.Failure(described.Error);
            }

            if (described.Value.BlocksInputFromUs)
            {
                return Failure(ErrorCode.TargetElevated, "That program runs with more rights than ClipBench, so Windows would block the paste. ClipBench never runs as administrator.");
            }
        }

        var kept = request.RestorePreviousClipboard ? await KeepClipboardAsync().ConfigureAwait(false) : null;
        long? written = null;
        try
        {
            var write = await _writer.WriteAsync(request.Content, cancellationToken).ConfigureAwait(false);
            if (write.IsFailure)
            {
                return Result.Failure(write.Error);
            }

            written = write.Value;
            if (request.Target is { } window && !await GiveFocusAsync((nint)window.Value, cancellationToken).ConfigureAwait(false))
            {
                return Failure(ErrorCode.FocusNotRestored, "ClipBench could not switch back to the window to paste into. Nothing was pasted.");
            }

            // Another program may have copied while focus moved. Pasting now would paste their copy.
            if (_clipboard.SequenceNumber != written)
            {
                return Failure(ErrorCode.ClipboardChangedBeforePaste, "Something else was copied just before the paste, so the paste was cancelled.");
            }

            var held = await WaitForModifiersAsync(cancellationToken).ConfigureAwait(false);
            if (held is null)
            {
                return Failure(ErrorCode.InputBlocked, "Keys are still held down, so Ctrl+V would become another shortcut. Let go of them and try again.");
            }

            var keys = KeystrokePlan.CtrlV(held.Value);
            if (_keyboard.Send(keys) != keys.Length)
            {
                return Failure(ErrorCode.InputBlocked, "Windows did not accept the keystrokes.");
            }

            // The target reads the clipboard a moment after the keys. Putting it back sooner would paste the old content.
            // With nothing to put back there is nothing to wait for.
            if (kept is not null)
            {
                await WaitBeforeRestoreAsync(request.RestoreDelay, cancellationToken).ConfigureAwait(false);
            }

            return Result.Success();
        }
        finally
        {
            if (kept is not null)
            {
                await RestoreAsync(kept.Value.Snapshot, written, kept.Value.Sequence).ConfigureAwait(false);
                kept.Value.Snapshot.Dispose();
            }
        }
    }

    // Copies what the user has on the clipboard. Null when the clipboard is busy or too large: the paste goes ahead
    // and the old content is simply not put back.
    private async Task<(ClipboardSnapshot Snapshot, long Sequence)?> KeepClipboardAsync()
    {
        using var session = await ClipboardOpener.OpenAsync(_clipboard, _retry, _time, CancellationToken.None).ConfigureAwait(false);
        if (session is null)
        {
            return null;
        }

        var sequence = _clipboard.SequenceNumber;
        var snapshot = ClipboardSnapshot.Take(session, _timing.SnapshotLimit);
        return snapshot is null ? null : (snapshot, sequence);
    }

    // Puts the user's clipboard back, but only if it is still what ClipBench left: after a successful write that is the
    // number the write returned; after a failed write it is anything that differs from the start.
    private async Task RestoreAsync(ClipboardSnapshot snapshot, long? written, long sequenceAtStart)
    {
        var now = _clipboard.SequenceNumber;
        if (written is { } receipt ? now != receipt : now == sequenceAtStart)
        {
            return;
        }

        using var session = await ClipboardOpener.OpenAsync(_clipboard, _retry, _time, CancellationToken.None).ConfigureAwait(false);
        if (session is not null && _clipboard.SequenceNumber == now)
        {
            // The restored content is marked as ClipBench's own, so it is not recorded again as a new copy.
            snapshot.Restore(session, _ownWriteFormat);
        }
    }

    private async Task<bool> GiveFocusAsync(nint target, CancellationToken cancellationToken)
    {
        if (_windows.GetForegroundWindow() == target)
        {
            return true;
        }

        // Windows may say no, or yes without doing it. Only the foreground window itself is believed.
        _windows.SetForegroundWindow(target);
        var waited = TimeSpan.Zero;
        while (_windows.GetForegroundWindow() != target)
        {
            if (waited >= _timing.FocusTimeout)
            {
                return false;
            }

            await Task.Delay(_timing.FocusPollInterval, _time, cancellationToken).ConfigureAwait(false);
            waited += _timing.FocusPollInterval;
        }

        await Task.Delay(_timing.FocusSettle, _time, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // Alt, Shift and Windows held down would turn Ctrl+V into a different shortcut. Control is fine: it is what is wanted.
    private async Task<HotkeyModifiers?> WaitForModifiersAsync(CancellationToken cancellationToken)
    {
        var waited = TimeSpan.Zero;
        while (true)
        {
            var held = _keyboard.GetHeldModifiers();
            if ((held & (HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Windows)) == 0)
            {
                return held;
            }

            if (waited >= _timing.ModifierTimeout)
            {
                return null;
            }

            await Task.Delay(_timing.ModifierPollInterval, _time, cancellationToken).ConfigureAwait(false);
            waited += _timing.ModifierPollInterval;
        }
    }

    // The paste has happened; cancelling now only skips the rest of the wait, and the restore follows straight away.
    private async Task WaitBeforeRestoreAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Restore now.
        }
    }

    private static Result Failure(ErrorCode code, string message) => Result.Failure(new ResultError(code, message));
}
