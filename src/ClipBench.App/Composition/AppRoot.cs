using ClipBench.Core.Capture;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.History;
using ClipBench.Core.Hosting;
using ClipBench.Core.Hotkeys;
using ClipBench.Core.Notifications;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows;
using ClipBench.Platform.Windows.Diagnostics;
using ClipBench.Presentation.Hosting;
using ClipBench.Presentation.Popup;
using ClipBench.Storage;
using ClipBench.Storage.Clips;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;

namespace ClipBench.App.Composition;

/// <summary>What starting the app produced: the running app, or the plain-words reason it cannot run.</summary>
/// <param name="Root">The running app, or null.</param>
/// <param name="Failure">What went wrong, for the user, or null.</param>
internal readonly record struct StartOutcome(AppRoot? Root, string? Failure);

/// <summary>
/// The composition root: it opens the history, creates the services, wires them together, registers the popup
/// shortcut and owns everything until the app exits. It runs on the UI thread.
/// </summary>
internal sealed class AppRoot : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly WindowsPlatform _platform;
    private readonly ClipStorage _storage;
    private readonly PopupWindow _popup;
    private readonly PopupViewModel _popupViewModel;
    private readonly IActivityLog _log;
    private readonly ISingleInstanceGuard _guard;
    private readonly IUiDispatcher _dispatcher;
    private HotkeyId? _hotkey;
    private bool _disposed;

    private AppRoot(
        ServiceProvider services,
        WindowsPlatform platform,
        ClipStorage storage,
        PopupWindow popup,
        PopupViewModel popupViewModel,
        IActivityLog log,
        ISingleInstanceGuard guard,
        IUiDispatcher dispatcher)
    {
        _services = services;
        _platform = platform;
        _storage = storage;
        _popup = popup;
        _popupViewModel = popupViewModel;
        _log = log;
        _guard = guard;
        _dispatcher = dispatcher;
    }

    /// <summary>Opens the history, creates and wires the services, and registers the popup shortcut.</summary>
    /// <param name="options">The command-line options.</param>
    /// <param name="paths">Where the history, its key and the log live.</param>
    /// <param name="guard">The one-copy guard; a second launch asks it to open the popup.</param>
    /// <param name="queue">The UI thread's dispatcher queue.</param>
    /// <param name="confirmStartFresh">Asks the user whether to throw away a history that cannot be read.</param>
    /// <param name="cancellationToken">Cancels start-up.</param>
    /// <returns>The running app, or the reason it cannot run.</returns>
    public static async Task<StartOutcome> StartAsync(
        StartupOptions options,
        StoragePaths paths,
        ISingleInstanceGuard guard,
        DispatcherQueue queue,
        Func<Task<bool>> confirmStartFresh,
        CancellationToken cancellationToken)
    {
        var time = TimeProvider.System;
        var log = new FileActivityLog(Path.Combine(paths.Root, "logs"), time);
        log.Write(ActivityLevel.Information, ActivityEvent.AppStarted);

        var created = WindowsPlatform.Create(time);
        if (created.IsFailure)
        {
            return new StartOutcome(null, "Windows would not let ClipBench set up its background window, so it cannot watch the clipboard. Try starting ClipBench again.");
        }

        var platform = created.Value;
        var opened = await OpenStorageAsync(paths, time, log, confirmStartFresh, cancellationToken);
        if (opened.Failure is not null || opened.Storage is not { } storage)
        {
            platform.Dispose();
            return new StartOutcome(null, opened.Failure);
        }

        var dispatcher = new DispatcherQueueDispatcher(queue);
        var services = BuildServices(platform, storage, log, dispatcher, time);

        var library = services.GetRequiredService<ClipLibrary>();
        var loaded = await library.LoadAsync(cancellationToken);
        if (loaded.IsFailure)
        {
            await DisposeAllAsync(services, storage, platform);
            return new StartOutcome(null, "ClipBench could not read its saved history. Check that the folder is readable, then start ClipBench again.");
        }

        log.Write(ActivityLevel.Information, ActivityEvent.HistoryLoaded, new ActivityDetails { Count = loaded.Value.Loaded });
        if (loaded.Value.Unreadable > 0)
        {
            log.Write(ActivityLevel.Warning, ActivityEvent.HistorySkipped, new ActivityDetails { Count = loaded.Value.Unreadable });
        }

        // The capture service reports its own failure to the log; the app is still useful without it, for pasting what is saved.
        _ = await services.GetRequiredService<CaptureService>().StartAsync(cancellationToken);

        var popupViewModel = services.GetRequiredService<PopupViewModel>();
        var popup = new PopupWindow(popupViewModel);
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(popup);
        if (!options.AllowScreenCapture)
        {
            _ = platform.WindowEffects.ExcludeFromCapture(new Core.Pasting.WindowHandle(handle));
        }

        _ = platform.WindowEffects.UseRoundedCorners(new Core.Pasting.WindowHandle(handle));

        var root = new AppRoot(services, platform, storage, popup, popupViewModel, log, guard, dispatcher);
        await root.RegisterShortcutAsync(options.Hotkey ?? Hotkey.Default, cancellationToken);
        platform.Hotkeys.Pressed += root.OnHotkeyPressed;
        guard.ShowRequested += root.OnShowRequested;
        return new StartOutcome(root, null);
    }

    /// <summary>Shows the popup, or hides it when it is already open.</summary>
    public void TogglePopup()
    {
        if (_disposed)
        {
            return;
        }

        if (_popupViewModel.IsOpen)
        {
            _popupViewModel.Close();
            return;
        }

        var focused = _platform.ForegroundTracker.Capture();
        var anchor = _platform.ScreenLocator.Locate(focused.IsSuccess ? focused.Value.Window : null);
        var bounds = PopupPlacement.Compute(anchor, PopupWindow.WidthDip, PopupWindow.HeightDip);

        // Opening records the window that has focus now, so it has to come before the popup takes focus.
        _popupViewModel.Open();
        _popup.ShowAt(bounds);
    }

    /// <summary>Stops everything in the order that keeps data safe: no new clips, then the history, then Windows.</summary>
    /// <returns>A task that completes when everything is released.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _log.Write(ActivityLevel.Information, ActivityEvent.AppStopping);
        _guard.ShowRequested -= OnShowRequested;
        _platform.Hotkeys.Pressed -= OnHotkeyPressed;
        _popupViewModel.Close();
        _popup.Close();
        await DisposeAllAsync(_services, _storage, _platform);
    }

    private static async Task DisposeAllAsync(ServiceProvider services, ClipStorage storage, WindowsPlatform platform)
    {
        // The container disposes what it created, newest first: the popup, the capture service, the library.
        await services.DisposeAsync();
        await storage.DisposeAsync();
        platform.Dispose();
    }

    private static ServiceProvider BuildServices(WindowsPlatform platform, ClipStorage storage, IActivityLog log, IUiDispatcher dispatcher, TimeProvider time)
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton(time);
        _ = services.AddSingleton(log);
        _ = services.AddSingleton(dispatcher);
        _ = services.AddSingleton(platform.ClipboardListener);
        _ = services.AddSingleton(platform.ClipboardReader);
        _ = services.AddSingleton(platform.ClipboardWriter);
        _ = services.AddSingleton(platform.ForegroundTracker);
        _ = services.AddSingleton(platform.PasteInjector);
        _ = services.AddSingleton<IUserNotifier, NullNotifier>();
        _ = services.AddSingleton<IClipStore>(new ClipStore(storage.Repository));
        _ = services.AddSingleton(new HistoryOptions());
        _ = services.AddSingleton<ClipHistory>();
        _ = services.AddSingleton<ClipLibrary>();
        _ = services.AddSingleton<CaptureService>();
        _ = services.AddSingleton<IPasteFlow, PasteFlow>();
        _ = services.AddSingleton<PopupViewModel>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static async Task<(ClipStorage? Storage, string? Failure)> OpenStorageAsync(
        StoragePaths paths,
        TimeProvider time,
        FileActivityLog log,
        Func<Task<bool>> confirmStartFresh,
        CancellationToken cancellationToken)
    {
        var protector = WindowsPlatform.CreateKeyProtector();
        var opened = await ClipStorage.OpenAsync(paths, protector, time, cancellationToken);
        if (opened.IsSuccess)
        {
            return (opened.Value, null);
        }

        switch (opened.Error.Code)
        {
            case ErrorCode.HistoryUnreadable:
                if (!await confirmStartFresh())
                {
                    return (null, null);
                }

                var fresh = await ClipStorage.StartFreshAsync(paths, protector, time, cancellationToken);
                if (fresh.IsFailure)
                {
                    return (null, "ClipBench could not remove the old history to start again. Close anything that may be using the ClipBench folder and try once more.");
                }

                log.Write(ActivityLevel.Warning, ActivityEvent.HistoryReset);
                return (fresh.Value, null);
            case ErrorCode.HistoryTooNew:
                return (null, "Your saved history was written by a newer version of ClipBench, so this version will not touch it. Install the newer version of ClipBench.");
            case ErrorCode.KeyProtectionFailed:
                return (null, "Windows would not protect ClipBench's encryption key for your account, so ClipBench cannot save your history safely.");
            default:
                return (null, "ClipBench could not open its history database. Check that its folder can be written to and that the disk has room, then try again.");
        }
    }

    private async Task RegisterShortcutAsync(Hotkey hotkey, CancellationToken cancellationToken)
    {
        var registered = await _platform.Hotkeys.RegisterAsync(hotkey, cancellationToken);
        if (registered.IsSuccess)
        {
            _hotkey = registered.Value;
            _log.Write(ActivityLevel.Information, ActivityEvent.HotkeyRegistered);
            return;
        }

        _log.Write(ActivityLevel.Warning, ActivityEvent.HotkeyFailed, new ActivityDetails { Error = registered.Error.Code });
        _services.GetRequiredService<IUserNotifier>().Show(
            "ClipBench",
            $"ClipBench could not claim the shortcut {hotkey} because another program uses it. You can still open ClipBench from its tray icon.");
    }

    // Runs on a pool thread.
    private void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        if (_hotkey is { } own && e.Id == own)
        {
            _dispatcher.Post(TogglePopup);
        }
    }

    // Runs on a pool thread: a second launch asked for the popup.
    private void OnShowRequested(object? sender, EventArgs e) => _dispatcher.Post(() =>
    {
        if (!_popupViewModel.IsOpen)
        {
            TogglePopup();
        }
    });
}
