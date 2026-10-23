using ClipBench.Core.Boards;
using ClipBench.Core.Capture;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.History;
using ClipBench.Core.Hosting;
using ClipBench.Core.Hotkeys;
using ClipBench.Core.Notifications;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Core.Snippets;
using ClipBench.Core.Transforms;
using ClipBench.Core.Tray;
using ClipBench.Platform.Windows;
using ClipBench.Platform.Windows.Diagnostics;
using ClipBench.Presentation.Boards;
using ClipBench.Presentation.Collect;
using ClipBench.Presentation.Hosting;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Queue;
using ClipBench.Presentation.Snippets;
using ClipBench.Presentation.Transforms;
using ClipBench.Presentation.Tray;
using ClipBench.Storage;
using ClipBench.Storage.Boards;
using ClipBench.Storage.Clips;
using ClipBench.Storage.Settings;
using ClipBench.Storage.Snippets;
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
    private readonly QueueHudWindow _hud;
    private readonly PopupViewModel _popupViewModel;
    private readonly TrayViewModel _trayViewModel;
    private readonly ITrayIcon? _tray;
    private readonly IActivityLog _log;
    private readonly ISingleInstanceGuard _guard;
    private readonly IUiDispatcher _dispatcher;
    private readonly StartupOptions _options;
    private SnippetsWindow? _snippetsWindow;
    private HotkeyId? _hotkey;
    private HotkeyId? _collectHotkey;
    private bool _disposed;

    private AppRoot(
        ServiceProvider services,
        WindowsPlatform platform,
        ClipStorage storage,
        PopupWindow popup,
        QueueHudWindow hud,
        PopupViewModel popupViewModel,
        TrayViewModel trayViewModel,
        ITrayIcon? tray,
        IActivityLog log,
        ISingleInstanceGuard guard,
        IUiDispatcher dispatcher,
        StartupOptions options)
    {
        _options = options;
        _services = services;
        _platform = platform;
        _storage = storage;
        _popup = popup;
        _hud = hud;
        _popupViewModel = popupViewModel;
        _trayViewModel = trayViewModel;
        _tray = tray;
        _log = log;
        _guard = guard;
        _dispatcher = dispatcher;
    }

    /// <summary>Raised on the UI thread when the user chooses Exit in the tray menu.</summary>
    public event EventHandler? ExitRequested;

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

        // The tray icon is also how the app tells the user things, so it comes before the services that need to.
        var trayCreated = WindowsPlatform.CreateTray(Path.Combine(AppContext.BaseDirectory, "Assets", "ClipBench.ico"));
        var tray = trayCreated.IsSuccess ? trayCreated.Value : null;
        if (trayCreated.IsFailure)
        {
            log.Write(ActivityLevel.Error, ActivityEvent.TrayFailed, new ActivityDetails { Error = trayCreated.Error.Code });
        }

        var services = BuildServices(platform, storage, paths, log, dispatcher, time, tray);
        PopupWindow? popup = null;
        QueueHudWindow? hud = null;
        try
        {
            // Settings that cannot be read give the defaults; the app runs either way.
            var settingsLoad = await services.GetRequiredService<SettingsService>().LoadAsync(cancellationToken);
            if (settingsLoad is SettingsLoadStatus.Damaged or SettingsLoadStatus.Unreadable)
            {
                log.Write(
                    ActivityLevel.Warning,
                    settingsLoad == SettingsLoadStatus.Damaged ? ActivityEvent.SettingsDamaged : ActivityEvent.SettingsUnreadable);
            }

            var library = services.GetRequiredService<ClipLibrary>();
            var loaded = await library.LoadAsync(cancellationToken);
            if (loaded.IsFailure)
            {
                await ReleaseAsync(services, storage, platform, tray, popup, hud);
                return new StartOutcome(null, "ClipBench could not read its saved history. Check that the folder is readable, then start ClipBench again.");
            }

            log.Write(ActivityLevel.Information, ActivityEvent.HistoryLoaded, new ActivityDetails { Count = loaded.Value.Loaded });
            if (loaded.Value.Unreadable > 0)
            {
                log.Write(ActivityLevel.Warning, ActivityEvent.HistorySkipped, new ActivityDetails { Count = loaded.Value.Unreadable });
            }

            // The boards come after the clips, because loading them takes clips off boards that no longer exist. Without
            // them the app still pastes what is saved.
            var boardsLoad = await services.GetRequiredService<BoardCatalog>().LoadAsync(cancellationToken);
            if (boardsLoad.IsFailure)
            {
                log.Write(ActivityLevel.Warning, ActivityEvent.StoreFailed, new ActivityDetails { Error = boardsLoad.Error.Code });
            }

            var snippetsLoad = await services.GetRequiredService<SnippetCatalog>().LoadAsync(cancellationToken);
            if (snippetsLoad.IsFailure)
            {
                log.Write(ActivityLevel.Warning, ActivityEvent.StoreFailed, new ActivityDetails { Error = snippetsLoad.Error.Code });
            }

            // The capture service reports its own failure to the log; the app is still useful without it, for pasting what is saved.
            _ = await services.GetRequiredService<CaptureService>().StartAsync(cancellationToken);

            var popupViewModel = services.GetRequiredService<PopupViewModel>();
            popup = new PopupWindow(popupViewModel);
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(popup);
            if (!options.AllowScreenCapture)
            {
                _ = platform.WindowEffects.ExcludeFromCapture(new Core.Pasting.WindowHandle(handle));
            }

            _ = platform.WindowEffects.UseRoundedCorners(new Core.Pasting.WindowHandle(handle));

            hud = CreateQueueWindow(services, platform, options);
            var root = new AppRoot(services, platform, storage, popup, hud, popupViewModel, services.GetRequiredService<TrayViewModel>(), tray, log, guard, dispatcher, options);
            popupViewModel.EditSnippetsRequested += (_, _) => root.OpenSnippetEditor();
            await root.ShowTrayAsync(cancellationToken);
            root._hotkey = await root.RegisterShortcutAsync(
                options.Hotkey ?? Hotkey.Default,
                "You can still open ClipBench from its tray icon.",
                cancellationToken);
            root._collectHotkey = await root.RegisterShortcutAsync(
                options.CollectHotkey ?? CollectToggle.DefaultShortcut,
                "You can still start and finish collecting from the tray menu.",
                cancellationToken);
            platform.Hotkeys.Pressed += root.OnHotkeyPressed;
            guard.ShowRequested += root.OnShowRequested;
            return new StartOutcome(root, null);
        }
        catch
        {
            // Something failed half way: let go of what was already created, so no icon or connection is left behind.
            await ReleaseAsync(services, storage, platform, tray, popup, hud);
            throw;
        }
    }

    private static async Task ReleaseAsync(ServiceProvider services, ClipStorage storage, WindowsPlatform platform, ITrayIcon? tray, PopupWindow? popup, QueueHudWindow? hud)
    {
        tray?.Dispose();
        popup?.Close();
        hud?.Close();
        await DisposeAllAsync(services, storage, platform);
    }

    /// <summary>Shows the popup, or hides it when it is already open.</summary>
    /// <param name="fromTray">
    /// True when the tray icon was used: the window that has focus is then the taskbar, which is no place to paste, so
    /// the paste goes to whatever has focus once the popup is hidden.
    /// </param>
    public void TogglePopup(bool fromTray = false)
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

        var focused = fromTray ? default : _platform.ForegroundTracker.Capture();
        var anchor = _platform.ScreenLocator.Locate(focused.IsSuccess ? focused.Value.Window : null);
        var bounds = PopupPlacement.Compute(anchor, _popup.CurrentWidthDip, PopupWindow.HeightDip);

        // Opening records the window that has focus now, so it has to come before the popup takes focus.
        _popupViewModel.Open(pasteIntoPreviousWindow: !fromTray);
        _popup.ShowAt(bounds);
    }

    /// <summary>Shows the snippet editor, making the window the first time.</summary>
    public void OpenSnippetEditor()
    {
        if (_disposed)
        {
            return;
        }

        if (_snippetsWindow is null)
        {
            _snippetsWindow = new SnippetsWindow(_services.GetRequiredService<SnippetEditorViewModel>());

            // It shows what the user wrote in their snippets, so it is kept out of screen captures like the popup.
            if (!_options.AllowScreenCapture)
            {
                var handle = WinRT.Interop.WindowNative.GetWindowHandle(_snippetsWindow);
                _ = _platform.WindowEffects.ExcludeFromCapture(new Core.Pasting.WindowHandle(handle));
            }
        }

        _snippetsWindow.ShowEditor();
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
        _tray?.Dispose();
        _popupViewModel.Close();
        _popup.Close();
        _hud.Close();
        _snippetsWindow?.Shutdown();
        await DisposeAllAsync(_services, _storage, _platform);
    }

    private static async Task DisposeAllAsync(ServiceProvider services, ClipStorage storage, WindowsPlatform platform)
    {
        // The container disposes what it created, newest first: the popup, the capture service, the library.
        await services.DisposeAsync();
        await storage.DisposeAsync();
        platform.Dispose();
    }

    // The queue window goes to the corner of the monitor the user is working on, and is kept out of screen captures like the popup.
    private static QueueHudWindow CreateQueueWindow(ServiceProvider services, WindowsPlatform platform, StartupOptions options)
    {
        var hud = new QueueHudWindow(services.GetRequiredService<QueueHudViewModel>(), () =>
        {
            var focused = platform.ForegroundTracker.Capture();
            var anchor = platform.ScreenLocator.Locate(focused.IsSuccess ? focused.Value.Window : null);
            return HudPlacement.Compute(anchor, QueueHudWindow.WidthDip, QueueHudWindow.HeightDip);
        });

        var handle = new WindowHandle(WinRT.Interop.WindowNative.GetWindowHandle(hud));
        _ = platform.WindowEffects.UseRoundedCorners(handle);
        if (!options.AllowScreenCapture)
        {
            _ = platform.WindowEffects.ExcludeFromCapture(handle);
        }

        return hud;
    }

    private static ServiceProvider BuildServices(WindowsPlatform platform, ClipStorage storage, StoragePaths paths, IActivityLog log, IUiDispatcher dispatcher, TimeProvider time, ITrayIcon? tray)
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
        _ = services.AddSingleton(platform.Hotkeys);
        _ = tray is null ? services.AddSingleton<IUserNotifier, NullNotifier>() : services.AddSingleton<IUserNotifier>(tray);
        _ = services.AddSingleton<IClipStore>(new ClipStore(storage.Repository));
        _ = services.AddSingleton<ISettingsStore>(new JsonSettingsStore(paths.SettingsPath));
        _ = services.AddSingleton<SettingsService>();
        _ = services.AddSingleton(new HistoryOptions());
        _ = services.AddSingleton<ClipHistory>();
        _ = services.AddSingleton<ClipLibrary>();
        _ = services.AddSingleton<IBoardStore>(new BoardStore(storage.Repository));
        _ = services.AddSingleton<BoardCatalog>();
        _ = services.AddSingleton<ISnippetStore>(new SnippetStore(storage.Repository));
        _ = services.AddSingleton<SnippetCatalog>();
        _ = services.AddSingleton<SnippetEngine>();
        _ = services.AddSingleton<SnippetsViewModel>();
        _ = services.AddSingleton<SnippetEditorViewModel>();
        _ = services.AddSingleton<SidebarViewModel>();
        _ = services.AddSingleton<CollectController>();
        _ = services.AddSingleton<ICollectControl>(provider => provider.GetRequiredService<CollectController>());
        _ = services.AddSingleton<ICaptureRouter>(provider => provider.GetRequiredService<CollectController>());
        _ = services.AddSingleton<CaptureService>();
        _ = services.AddSingleton<ICaptureControl>(provider => provider.GetRequiredService<CaptureService>());
        _ = services.AddSingleton<IPasteFlow, PasteFlow>();
        _ = services.AddSingleton<QueueRunner>();
        _ = services.AddSingleton<IQueueControl>(provider => provider.GetRequiredService<QueueRunner>());
        _ = services.AddSingleton(TransformRegistry.Default);
        _ = services.AddSingleton(new TransformPipeline());
        _ = services.AddSingleton<TransformPickerViewModel>();
        _ = services.AddSingleton<PopupViewModel>();
        _ = services.AddSingleton<QueueHudViewModel>();
        _ = services.AddSingleton<CollectToggle>();
        _ = services.AddSingleton<TrayViewModel>();
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

    private async Task ShowTrayAsync(CancellationToken cancellationToken)
    {
        if (_tray is null)
        {
            return;
        }

        _tray.OpenRequested += (_, _) => _dispatcher.Post(() => TogglePopup(fromTray: true));
        _tray.MenuItemChosen += (_, e) => _dispatcher.Post(() => TrayMenu.Execute(_trayViewModel, e.Id));
        _trayViewModel.OpenRequested += (_, _) => TogglePopup(fromTray: true);
        _trayViewModel.EditSnippetsRequested += (_, _) => OpenSnippetEditor();
        _trayViewModel.ExitRequested += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        // Pausing, collecting and a queue change the tool tip and the wording of their items, so the tool tip changes exactly
        // when the menu does; the restore tick is the one item it does not show. Answering these two updates the icon once
        // per change, not once per property.
        _trayViewModel.PropertyChanged += (_, e) =>
        {
            // Changes come from the UI thread, the queue and the capture thread. The menu is built and sent from the UI
            // thread, one change after another, so an older menu can never arrive after a newer one.
            if (e.PropertyName is nameof(TrayViewModel.ToolTip) or nameof(TrayViewModel.RestoreClipboard))
            {
                _dispatcher.Post(() =>
                {
                    if (!_disposed)
                    {
                        _ = _tray.UpdateAsync(TrayMenu.Build(_trayViewModel), CancellationToken.None);
                    }
                });
            }
        };

        var shown = await _tray.ShowAsync(TrayMenu.Build(_trayViewModel), cancellationToken);
        if (shown.IsFailure)
        {
            _log.Write(ActivityLevel.Error, ActivityEvent.TrayFailed, new ActivityDetails { Error = shown.Error.Code });
        }
    }

    // Returns the registration, or null when another program holds the shortcut; the user is then told what still works.
    private async Task<HotkeyId?> RegisterShortcutAsync(Hotkey hotkey, string fallback, CancellationToken cancellationToken)
    {
        var registered = await _platform.Hotkeys.RegisterAsync(hotkey, cancellationToken);
        if (registered.IsSuccess)
        {
            _log.Write(ActivityLevel.Information, ActivityEvent.HotkeyRegistered);
            return registered.Value;
        }

        _log.Write(ActivityLevel.Warning, ActivityEvent.HotkeyFailed, new ActivityDetails { Error = registered.Error.Code });
        _services.GetRequiredService<IUserNotifier>().Show(
            "ClipBench",
            $"ClipBench could not claim the shortcut {hotkey} because another program uses it. {fallback}");
        return null;
    }

    // Runs on a pool thread.
    private void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        if (_hotkey is { } own && e.Id == own)
        {
            _dispatcher.Post(() => TogglePopup());
        }
        else if (_collectHotkey is { } collect && e.Id == collect)
        {
            _dispatcher.Post(() =>
            {
                // A press that arrives while the app shuts down finds the services gone.
                if (!_disposed)
                {
                    _ = _services.GetRequiredService<CollectToggle>().ToggleAsync(CancellationToken.None);
                }
            });
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
