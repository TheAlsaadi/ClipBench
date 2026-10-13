using ClipBench.Core.Results;
using ClipBench.Core.Tray;
using ClipBench.Platform.Windows.Windowing;

namespace ClipBench.Platform.Windows.Tray;

/// <summary>
/// The icon in the notification area. Everything Windows ties to the icon's window happens on that window's
/// thread; clicks and menu choices reach subscribers on another thread, so a slow subscriber cannot freeze the menu.
/// </summary>
internal sealed class TrayIcon : ITrayIcon
{
    /// <summary>The callback message the icon asks Windows to send to its window.</summary>
    public const uint CallbackMessage = 0x8000 + 2;

    /// <summary>The number of the one icon this window has.</summary>
    public const uint IconId = 1;

    private const int OpenSignal = 0;
    private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(5);

    private readonly IThreadInvoker _invoker;
    private readonly ITrayApi _api;
    private readonly ITrayMenu _menu;
    private readonly nint _window;
    private readonly string _iconPath;
    private readonly EventPump<int> _pump;
    private readonly IDisposable? _ownedWindow;

    // These three belong to the window thread.
    private TrayModel _model = new("ClipBench", []);
    private nint _icon;
    private bool _shown;

    private int _disposed;

    /// <summary>Creates the icon. Nothing shows until <see cref="ShowAsync"/>.</summary>
    /// <param name="source">Where the callback and taskbar messages arrive.</param>
    /// <param name="invoker">Runs the calls Windows wants made on the window's thread.</param>
    /// <param name="window">The window that owns the icon.</param>
    /// <param name="api">The notification-area calls.</param>
    /// <param name="menu">Shows the menu.</param>
    /// <param name="iconPath">The .ico file that holds the picture.</param>
    /// <param name="ownedWindow">The window thread that runs the icon, when the icon owns it: it is disposed after the icon is gone.</param>
    public TrayIcon(IMessageSource source, IThreadInvoker invoker, nint window, ITrayApi api, ITrayMenu menu, string iconPath, IDisposable? ownedWindow = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconPath);
        _invoker = invoker;
        _window = window;
        _api = api;
        _menu = menu;
        _iconPath = iconPath;
        _ownedWindow = ownedWindow;
        _pump = new EventPump<int>(Deliver);

        source.AddHandler(CallbackMessage, OnCallback);
        if (api.TaskbarCreatedMessage != 0)
        {
            source.AddHandler(api.TaskbarCreatedMessage, OnTaskbarCreated);
        }
    }

    /// <inheritdoc/>
    public event EventHandler? OpenRequested;

    /// <inheritdoc/>
    public event EventHandler<TrayMenuItemEventArgs>? MenuItemChosen;

    /// <inheritdoc/>
    public Task<Result> ShowAsync(TrayModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _invoker.InvokeAsync(() => ShowOnWindowThread(model), cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result> UpdateAsync(TrayModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _invoker.InvokeAsync(() => UpdateOnWindowThread(model), cancellationToken);
    }

    /// <inheritdoc/>
    public void Show(string title, string message)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        // A notice must never make the caller wait for the window thread, which may be inside the menu.
        var task = _invoker.InvokeAsync(
            () =>
            {
                if (_shown)
                {
                    _ = _api.Notify(_window, IconId, TrayText.Fit(title, TrayText.MaxTitle), TrayText.Fit(message, TrayText.MaxMessage));
                }

                return true;
            },
            CancellationToken.None);
        _ = task.ContinueWith(static finished => _ = finished.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _invoker.RunAndWait(
            () =>
            {
                if (_shown)
                {
                    _ = _api.Delete(_window, IconId);
                    _api.DestroyIcon(_icon);
                    _shown = false;
                }
            },
            _stopTimeout);
        _pump.Dispose();
        _ownedWindow?.Dispose();
    }

    private Result ShowOnWindowThread(TrayModel model)
    {
        if (_shown)
        {
            return UpdateOnWindowThread(model);
        }

        _model = model;
        var icon = _api.LoadIcon(_iconPath);
        if (icon == 0)
        {
            return Failure("ClipBench's icon picture could not be loaded.");
        }

        if (!AddToNotificationArea(icon, model.ToolTip))
        {
            _api.DestroyIcon(icon);
            return Failure("Windows would not add ClipBench's icon to the notification area.");
        }

        _icon = icon;
        _shown = true;
        return Result.Success();
    }

    private Result UpdateOnWindowThread(TrayModel model)
    {
        _model = model;
        if (!_shown)
        {
            return Result.Success();
        }

        return _api.Modify(_window, IconId, TrayText.Fit(model.ToolTip, TrayText.MaxToolTip))
            ? Result.Success()
            : Failure("Windows would not change the icon's tool tip.");
    }

    // Runs on the window thread.
    private bool OnCallback(nint wParam, nint lParam)
    {
        var message = TrayMessage.Decode(wParam, lParam);
        if (!_shown || message.IconId != IconId)
        {
            return false;
        }

        switch (message.Event)
        {
            case TrayEvent.Select:
                _pump.Post(OpenSignal);
                return true;
            case TrayEvent.ContextMenu:
                var chosen = _menu.Show(_window, message.Anchor, _model.Menu);
                if (chosen > 0)
                {
                    _pump.Post(chosen);
                }

                return true;
            default:
                return false;
        }
    }

    // Runs on the window thread. Explorer started again, and its notification area is empty.
    private bool OnTaskbarCreated(nint wParam, nint lParam)
    {
        if (!_shown)
        {
            return false;
        }

        _ = AddToNotificationArea(_icon, _model.ToolTip);
        return true;
    }

    // Adds the icon and asks for version 4 messages. An icon Windows would not give those is taken away again rather than left behind.
    private bool AddToNotificationArea(nint icon, string toolTip)
    {
        if (!_api.Add(_window, IconId, CallbackMessage, icon, TrayText.Fit(toolTip, TrayText.MaxToolTip)))
        {
            return false;
        }

        if (_api.UseVersion4(_window, IconId))
        {
            return true;
        }

        _ = _api.Delete(_window, IconId);
        return false;
    }

    // Runs on a pool thread.
    private void Deliver(int signal)
    {
        if (signal == OpenSignal)
        {
            RaiseOpenRequested();
        }
        else
        {
            Subscribers.Raise(MenuItemChosen, this, new TrayMenuItemEventArgs(signal));
        }
    }

    private void RaiseOpenRequested()
    {
        if (OpenRequested is not { } handlers)
        {
            return;
        }

        foreach (var subscriber in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                subscriber(this, EventArgs.Empty);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A subscriber's bug must not take the others down.
            }
        }
    }

    private static Result Failure(string message) => Result.Failure(new ResultError(ErrorCode.PlatformFailure, message));
}
