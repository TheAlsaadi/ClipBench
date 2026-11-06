using ClipBench.Core.Results;

namespace ClipBench.Core.Hotkeys;

/// <summary>What a global shortcut does.</summary>
public enum HotkeyAction
{
    /// <summary>Opens or closes the popup.</summary>
    OpenPopup,

    /// <summary>Starts or finishes collecting.</summary>
    ToggleCollect,

    /// <summary>Lets the user choose a part of the screen and copies the text in it.</summary>
    CaptureText,
}

/// <summary>Tells which action's shortcut was pressed.</summary>
/// <param name="action">The action.</param>
public sealed class HotkeyActionEventArgs(HotkeyAction action) : EventArgs
{
    /// <summary>The action whose shortcut was pressed.</summary>
    public HotkeyAction Action { get; } = action;
}

/// <summary>
/// Which shortcut does what. A shortcut can be changed while the app runs: the new one is claimed before the old one is
/// given back, so a refused change leaves the old shortcut working. One shortcut never does two things.
/// </summary>
public sealed class HotkeyBindings : IDisposable
{
    private readonly IHotkeyService _service;
    private readonly Dictionary<HotkeyAction, (Hotkey Hotkey, HotkeyId Id)> _bound = [];
    private readonly SemaphoreSlim _changes = new(1, 1);
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <summary>Starts listening for presses. Nothing is bound yet.</summary>
    /// <param name="service">Claims the shortcuts from Windows.</param>
    public HotkeyBindings(IHotkeyService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _service.Pressed += OnPressed;
    }

    /// <summary>Raised on a thread-pool thread when a bound shortcut is pressed. A subscriber that throws is skipped.</summary>
    public event EventHandler<HotkeyActionEventArgs>? Pressed;

    /// <summary>The shortcut an action has now.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The shortcut, or null when the action has none.</returns>
    public Hotkey? Current(HotkeyAction action)
    {
        lock (_gate)
        {
            return _bound.TryGetValue(action, out var entry) ? entry.Hotkey : null;
        }
    }

    /// <summary>Gives an action a shortcut, replacing the one it had.</summary>
    /// <param name="action">The action.</param>
    /// <param name="hotkey">The shortcut.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>
    /// Success, <see cref="ErrorCode.HotkeyInvalid"/> when another action already uses the shortcut, or
    /// <see cref="ErrorCode.HotkeyUnavailable"/> when another program has it. A failure leaves the old shortcut in place.
    /// </returns>
    public async Task<Result> BindAsync(HotkeyAction action, Hotkey hotkey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hotkey);

        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            (Hotkey Hotkey, HotkeyId Id)? old;
            lock (_gate)
            {
                if (_bound.Any(pair => pair.Key != action && pair.Value.Hotkey == hotkey))
                {
                    return Result.Failure(new ResultError(ErrorCode.HotkeyInvalid, "That shortcut is already used for something else in ClipBench."));
                }

                old = _bound.TryGetValue(action, out var entry) ? entry : null;
            }

            if (old is { } current && current.Hotkey == hotkey)
            {
                return Result.Success();
            }

            var claimed = await _service.RegisterAsync(hotkey, cancellationToken).ConfigureAwait(false);
            if (claimed.IsFailure)
            {
                return Result.Failure(claimed.Error);
            }

            lock (_gate)
            {
                _bound[action] = (hotkey, claimed.Value);
            }

            if (old is { } replaced)
            {
                // Giving back the old one can only fail when it is already gone, which is what was wanted.
                _ = await _service.UnregisterAsync(replaced.Id, cancellationToken).ConfigureAwait(false);
            }

            return Result.Success();
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Stops listening. The shortcuts themselves belong to the hotkey service, which gives them back when it ends.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        // The semaphore is not disposed: a change in flight may still be about to release it, and it holds no handle.
        _service.Pressed -= OnPressed;
    }

    // Runs on a pool thread.
    private void OnPressed(object? sender, HotkeyPressedEventArgs e)
    {
        HotkeyAction? action;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            action = _bound.Where(pair => pair.Value.Id == e.Id).Select(pair => (HotkeyAction?)pair.Key).FirstOrDefault();
        }

        if (action is { } pressed)
        {
            Pressed.RaiseEach(this, new HotkeyActionEventArgs(pressed));
        }
    }
}
