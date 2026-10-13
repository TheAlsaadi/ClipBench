using System.Collections.Concurrent;
using ClipBench.Core.Hotkeys;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Windowing;
using Windows.Win32;

namespace ClipBench.Platform.Windows.Hotkeys;

/// <summary>
/// Global keyboard shortcuts through RegisterHotKey. Claiming and releasing happen on the window thread,
/// because Windows ties a shortcut to the window that receives its presses; the presses come back as messages
/// and are passed on in order, off that thread. No key hook is installed.
/// </summary>
internal sealed class HotkeyService : IHotkeyService
{
    private const int MaxId = 0xBFFF;                   // Windows allows 0 to 0xBFFF for a program's own shortcuts
    private const uint NoRepeat = 0x4000;               // MOD_NOREPEAT: holding the keys down presses once
    private const uint HotkeyMessage = PInvoke.WM_HOTKEY;
    private const int AlreadyRegistered = 1409;         // ERROR_HOTKEY_ALREADY_REGISTERED
    private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(5);

    private readonly IThreadInvoker _invoker;
    private readonly IHotkeyApi _api;
    private readonly ConcurrentDictionary<int, Hotkey> _registered = new();
    private readonly EventPump<HotkeyId> _pump;
    private int _lastId;
    private int _disposed;

    /// <summary>Creates a hotkey service.</summary>
    /// <param name="source">Where the shortcut messages arrive.</param>
    /// <param name="invoker">Runs the calls Windows wants made on the window's thread.</param>
    /// <param name="api">Claims and releases shortcuts with Windows.</param>
    /// <param name="firstId">The first number to register under. A parameter only so a test can reach the limit.</param>
    public HotkeyService(IMessageSource source, IThreadInvoker invoker, IHotkeyApi api, int firstId = 1)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentOutOfRangeException.ThrowIfLessThan(firstId, 1);
        _invoker = invoker;
        _api = api;
        _lastId = firstId - 1;
        _pump = new EventPump<HotkeyId>(id => Subscribers.Raise(Pressed, this, new HotkeyPressedEventArgs(id)));
        source.AddHandler(HotkeyMessage, (wParam, _) =>
        {
            var id = (int)wParam;
            if (!_registered.ContainsKey(id))
            {
                return false;
            }

            _pump.Post(new HotkeyId(id));
            return true;
        });
    }

    /// <inheritdoc/>
    public event EventHandler<HotkeyPressedEventArgs>? Pressed;

    /// <inheritdoc/>
    public async Task<Result<HotkeyId>> RegisterAsync(Hotkey hotkey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var id = Interlocked.Increment(ref _lastId);
        if (id > MaxId)
        {
            return Result.Failure<HotkeyId>(new ResultError(ErrorCode.PlatformFailure, "ClipBench has used up the shortcut numbers Windows allows."));
        }

        // The shortcut is remembered on the window thread, before any press of it can be handled.
        var error = await _invoker.InvokeAsync(
            () =>
            {
                if (_api.Register(id, ToWindowsModifiers(hotkey.Modifiers) | NoRepeat, (uint)hotkey.VirtualKey, out var failure))
                {
                    _registered[id] = hotkey;
                    return 0;
                }

                return failure;
            },
            cancellationToken).ConfigureAwait(false);

        return error switch
        {
            0 => Result.Success(new HotkeyId(id)),
            AlreadyRegistered => Result.Failure<HotkeyId>(new ResultError(
                ErrorCode.HotkeyUnavailable,
                $"{hotkey} is already used by another program or by Windows. Choose a different shortcut.")),
            _ => Result.Failure<HotkeyId>(new ResultError(ErrorCode.PlatformFailure, $"Windows would not register {hotkey} (error {error}).")),
        };
    }

    /// <inheritdoc/>
    public async Task<Result> UnregisterAsync(HotkeyId id, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        return await _invoker.InvokeAsync(
            () =>
            {
                if (!_registered.ContainsKey(id.Value))
                {
                    return Result.Failure(new ResultError(ErrorCode.HotkeyNotRegistered, "That shortcut is not registered."));
                }

                if (!_api.Unregister(id.Value))
                {
                    return Result.Failure(new ResultError(ErrorCode.PlatformFailure, "Windows would not release the shortcut."));
                }

                _registered.TryRemove(id.Value, out _);
                return Result.Success();
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _invoker.InvokeAsync(
                () =>
                {
                    foreach (var id in _registered.Keys)
                    {
                        _api.Unregister(id);
                    }

                    _registered.Clear();
                    return true;
                },
                CancellationToken.None).Wait(_stopTimeout);
        }
        catch (AggregateException exception) when (exception.InnerException is ObjectDisposedException)
        {
            // The window is already gone, and Windows releases its shortcuts with it.
        }

        _pump.Dispose();
    }

    // The flag values are Windows' own (MOD_ALT 1, MOD_CONTROL 2, MOD_SHIFT 4, MOD_WIN 8); Core's enum uses the same numbers on purpose.
    private static uint ToWindowsModifiers(HotkeyModifiers modifiers) => (uint)modifiers;
}
