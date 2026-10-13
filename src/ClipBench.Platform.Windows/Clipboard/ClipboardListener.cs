using ClipBench.Core.Capture;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Windowing;

namespace ClipBench.Platform.Windows.Clipboard;

/// <summary>
/// Turns Windows' clipboard-change messages into one event per change. Windows can send the message twice for
/// one change, so changes are told apart by the clipboard sequence number; ClipBench's own writes are
/// recognised by their marker and dropped here, before anything is read.
/// </summary>
internal sealed class ClipboardListener : IClipboardListener
{
    private const uint ClipboardUpdateMessage = 0x031D;   // WM_CLIPBOARDUPDATE
    private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(5);

    private readonly IThreadInvoker _invoker;
    private readonly IClipboardNotifications _notifications;
    private readonly IClipboardApi _api;
    private readonly uint _ownWriteFormat;
    private readonly EventPump<long> _pump;
    private long _lastSequence = -1;
    private volatile bool _started;
    private int _disposed;

    /// <summary>Creates a listener. Nothing is reported until <see cref="StartAsync"/>.</summary>
    /// <param name="source">Where the clipboard-change messages arrive.</param>
    /// <param name="invoker">Runs the calls Windows wants made on the window's thread.</param>
    /// <param name="notifications">Asks Windows for the messages.</param>
    /// <param name="api">The clipboard, for the sequence number and the marker check.</param>
    public ClipboardListener(IMessageSource source, IThreadInvoker invoker, IClipboardNotifications notifications, IClipboardApi api)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(api);
        _invoker = invoker;
        _notifications = notifications;
        _api = api;
        _ownWriteFormat = api.RegisterFormat(ClipboardFormatNames.OwnWrite);
        _pump = new EventPump<long>(sequence => Subscribers.Raise(Changed, this, new ClipboardChangedEventArgs(sequence)));
        source.AddHandler(ClipboardUpdateMessage, (_, _) =>
        {
            OnUpdate();
            return true;
        });
    }

    /// <inheritdoc/>
    public event EventHandler<ClipboardChangedEventArgs>? Changed;

    /// <inheritdoc/>
    public async Task<Result> StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        // The flag is set on the window thread, before the message Windows sends on registration is handled.
        var subscribed = await _invoker.InvokeAsync(
            () =>
            {
                if (_started)
                {
                    return true;
                }

                if (!_notifications.Subscribe())
                {
                    return false;
                }

                _started = true;
                return true;
            },
            cancellationToken).ConfigureAwait(false);

        return subscribed
            ? Result.Success()
            : Result.Failure(new ResultError(ErrorCode.PlatformFailure, "Windows would not tell ClipBench when the clipboard changes."));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_started)
        {
            _started = false;
            try
            {
                _invoker.InvokeAsync(
                    () =>
                    {
                        _notifications.Unsubscribe();
                        return true;
                    },
                    CancellationToken.None).Wait(_stopTimeout);
            }
            catch (AggregateException exception) when (exception.InnerException is ObjectDisposedException)
            {
                // The window is already gone, and its registrations with it.
            }
        }

        _pump.Dispose();
    }

    private void OnUpdate()
    {
        if (!_started)
        {
            return;
        }

        var sequence = _api.SequenceNumber;
        if (Interlocked.Exchange(ref _lastSequence, sequence) == sequence)
        {
            return;
        }

        if (_api.IsFormatAvailable(_ownWriteFormat))
        {
            return;
        }

        _pump.Post(sequence);
    }
}
