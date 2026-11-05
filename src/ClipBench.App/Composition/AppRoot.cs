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
using ClipBench.Platform.Windows.Hosting;
using ClipBench.Presentation.Boards;
using ClipBench.Presentation.Collect;
using ClipBench.Presentation.Hosting;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Preferences;
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
    private SettingsWindow? _settingsWindow;
    private EventHandler? _settingsChanged;
    private bool _windowsHidden;
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
        var trayCreated = WindowsPlatform.CreateTray(
            Path.Combine(AppContext.BaseDirectory, "Assets", "ClipBench.ico"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "ClipBench-incognito.ico"));
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
            // The applier comes first so that the loaded settings reach the history and the capture as soon as they are read.
            _ = services.GetRequiredService<SettingsApplier>();

            // Settings that cannot be read give the defaults; the app runs either way.
            var settingsLoad = await services.GetRequiredService<SettingsService>().LoadAsync(cancellationToken);
            if (settingsLoad is SettingsLoadStatus.Damaged or SettingsLoadStatus.Unreadable)
            {
                log.Write(
                    ActivityLevel.Warning,
                    settingsLoad == SettingsLoadStatus.Damaged ? ActivityEvent.SettingsDamaged : ActivityEvent.SettingsUnreadable);
            }

            // A start-with-Windows entry that went missing or points to an old folder is put right.
            if (services.GetService<StartWithWindowsService>() is { } startup && startup.Reconcile() is { IsFailure: true } reconciled)
            {
                log.Write(ActivityLevel.Warning, ActivityEvent.StartupRegistrationFailed, new ActivityDetails { Error = reconciled.Error.Code });
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

            // Secrets that expired while the app was closed go now, and later ones as their time comes.
            services.GetRequiredService<ExpirySweeper>().Start();

            // The capture service reports its own failure to the log; the app is still useful without it, for pasting what is saved.
            _ = await services.GetRequiredService<CaptureService>().StartAsync(cancellationToken);

            var popupViewModel = services.GetRequiredService<PopupViewModel>();
            popup = new PopupWindow(popupViewModel, platform.WindowEffects, platform.ForegroundTracker, platform.ScreenLocator);
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(popup);
            if (HideFromCapture(options, services))
            {
                _ = platform.WindowEffects.ExcludeFromCapture(new Core.Pasting.WindowHandle(handle));
            }

            _ = platform.WindowEffects.RemoveFrame(new Core.Pasting.WindowHandle(handle));
            _ = platform.WindowEffects.UseRoundedCorners(new Core.Pasting.WindowHandle(handle));
            _ = platform.WindowEffects.HideSystemBorder(new Core.Pasting.WindowHandle(handle));

            hud = CreateQueueWindow(services, platform, options);
            var root = new AppRoot(services, platform, storage, popup, hud, popupViewModel, services.GetRequiredService<TrayViewModel>(), tray, log, guard, dispatcher, options);
            popupViewModel.EditSnippetsRequested += (_, _) => root.OpenSettings(SettingsSection.Snippets);
            await root.ShowTrayAsync(cancellationToken);

            // A shortcut on the command line is a developer's override; otherwise the user's saved shortcut is used.
            var saved = services.GetRequiredService<SettingsService>().Current;
            await root.BindShortcutAsync(
                HotkeyAction.OpenPopup,
                options.Hotkey ?? saved.PopupHotkey,
                "You can still open ClipBench from its tray icon.",
                cancellationToken);
            await root.BindShortcutAsync(
                HotkeyAction.ToggleCollect,
                options.CollectHotkey ?? saved.CollectHotkey,
                "You can still start and finish collecting from the tray menu.",
                cancellationToken);
            services.GetRequiredService<HotkeyBindings>().Pressed += root.OnHotkeyPressed;

            // The windows follow the user's choice about screen capture as soon as it changes.
            root._windowsHidden = HideFromCapture(options, services);
            root._settingsChanged = (_, _) => dispatcher.Post(root.ApplyCaptureProtection);
            services.GetRequiredService<SettingsService>().Changed += root._settingsChanged;

            // The first time ClipBench runs, the Settings window opens on the question about starting with Windows.
            if (!saved.FirstRunCompleted)
            {
                root.OpenSettings(SettingsSection.General);
            }

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
            // A locked popup is not put away by its shortcut: from another program it takes the focus, and from the popup the
            // focus goes back to the program.
            if (_popupViewModel.IsLocked)
            {
                if (_popup.IsActive)
                {
                    _popupViewModel.ReturnToProgram();
                }
                else
                {
                    _popup.TakeFocus(pasteIntoPreviousWindow: !fromTray);
                }

                return;
            }

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

    // Puts every window in or out of screenshots and screen recordings to match the setting. Runs on the UI thread.
    private void ApplyCaptureProtection()
    {
        var hide = HideFromCapture(_options, _services);
        if (_disposed || hide == _windowsHidden)
        {
            return;
        }

        _windowsHidden = hide;
        List<object> windows = [_popup, _hud];
        if (_settingsWindow is not null)
        {
            windows.Add(_settingsWindow);
        }

        foreach (var window in windows)
        {
            var handle = new Core.Pasting.WindowHandle(WinRT.Interop.WindowNative.GetWindowHandle(window));
            _ = hide ? _platform.WindowEffects.ExcludeFromCapture(handle) : _platform.WindowEffects.AllowCapture(handle);
        }
    }

    /// <summary>Shows the Settings window with a section open, making the window the first time.</summary>
    /// <param name="section">The section to open.</param>
    public void OpenSettings(SettingsSection section = SettingsSection.General)
    {
        if (_disposed)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_services.GetRequiredService<SettingsViewModel>());

            // It shows the user's snippets and excluded programs, so it is kept out of screen captures like the popup.
            if (HideFromCapture(_options, _services))
            {
                var handle = WinRT.Interop.WindowNative.GetWindowHandle(_settingsWindow);
                _ = _platform.WindowEffects.ExcludeFromCapture(new Core.Pasting.WindowHandle(handle));
            }
        }

        _settingsWindow.ShowSection(section);
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
        _services.GetRequiredService<HotkeyBindings>().Pressed -= OnHotkeyPressed;
        if (_settingsChanged is { } changed)
        {
            _services.GetRequiredService<SettingsService>().Changed -= changed;
        }

        _tray?.Dispose();
        _popupViewModel.Close();
        _popup.Close();
        _hud.Close();
        _settingsWindow?.Shutdown();
        await _services.GetRequiredService<ExpirySweeper>().StopAsync();
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
        if (HideFromCapture(options, services))
        {
            _ = platform.WindowEffects.ExcludeFromCapture(handle);
        }

        return hud;
    }

    // The version the build was stamped with, without the source revision that follows a plus sign.
    private static string ProgramVersion()
    {
        var stamped = typeof(AppRoot).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Select(attribute => attribute.InformationalVersion)
            .FirstOrDefault();
        var version = stamped is { Length: > 0 } ? stamped : typeof(AppRoot).Assembly.GetName().Version?.ToString(3);
        var plus = version?.IndexOf('+', StringComparison.Ordinal) ?? -1;
        return plus > 0 ? version![..plus] : version ?? "development build";
    }

    // The command-line switch is a developer's override; otherwise the user's setting decides.
    private static bool HideFromCapture(StartupOptions options, IServiceProvider services) =>
        !options.AllowScreenCapture && services.GetRequiredService<SettingsService>().Current.HideFromScreenCapture;

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
        var clipStore = new ClipStore(storage.Repository);
        _ = services.AddSingleton<IClipStore>(clipStore);
        _ = services.AddSingleton<IStoreWipe>(clipStore);
        _ = services.AddSingleton<ISettingsStore>(new JsonSettingsStore(paths.SettingsPath));
        _ = services.AddSingleton<SettingsService>();
        _ = services.AddSingleton<IStartupRegistration>(new RegistryStartupRegistration());
        if (Environment.ProcessPath is { } processPath && Path.IsPathFullyQualified(processPath) && !processPath.Contains('"', StringComparison.Ordinal))
        {
            _ = services.AddSingleton(provider => new StartWithWindowsService(provider.GetRequiredService<IStartupRegistration>(), provider.GetRequiredService<SettingsService>(), processPath));
        }

        _ = services.AddSingleton(new HistoryOptions());
        _ = services.AddSingleton<ClipHistory>();
        _ = services.AddSingleton<ClipLibrary>();
        _ = services.AddSingleton(provider => new ExpirySweeper(provider.GetRequiredService<ClipLibrary>(), provider.GetRequiredService<IActivityLog>(), TimeSpan.FromSeconds(5)));
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
        _ = services.AddSingleton<SettingsApplier>();
        _ = services.AddSingleton<HotkeyBindings>();
        _ = services.AddSingleton<WipeService>();
        _ = services.AddSingleton<IWipeAll>(provider => provider.GetRequiredService<WipeService>());
        _ = services.AddSingleton(provider => new GeneralSettingsViewModel(
            provider.GetRequiredService<SettingsService>(),
            provider.GetService<StartWithWindowsService>(),
            provider.GetRequiredService<IUiDispatcher>()));
        _ = services.AddSingleton<HotkeysSettingsViewModel>();
        _ = services.AddSingleton<HistorySettingsViewModel>();
        _ = services.AddSingleton<PrivacySettingsViewModel>();
        _ = services.AddSingleton<PasteSettingsViewModel>();
        _ = services.AddSingleton(new AboutViewModel(ProgramVersion()));
        _ = services.AddSingleton<SettingsViewModel>();
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
        _trayViewModel.SettingsRequested += (_, _) => OpenSettings();
        _trayViewModel.ExitRequested += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        // Pausing, collecting and a queue change the tool tip and the wording of their items, so the tool tip changes exactly
        // when the menu does; the restore tick is the one item it does not show. Answering these two updates the icon once
        // per change, not once per property.
        _trayViewModel.PropertyChanged += (_, e) =>
        {
            // Changes come from the UI thread, the queue and the capture thread. The menu is built and sent from the UI
            // thread, one change after another, so an older menu can never arrive after a newer one.
            if (e.PropertyName is nameof(TrayViewModel.ToolTip))
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

    // When another program holds the shortcut the user is told what still works.
    private async Task BindShortcutAsync(HotkeyAction action, Hotkey hotkey, string fallback, CancellationToken cancellationToken)
    {
        var bound = await _services.GetRequiredService<HotkeyBindings>().BindAsync(action, hotkey, cancellationToken);
        if (bound.IsSuccess)
        {
            _log.Write(ActivityLevel.Information, ActivityEvent.HotkeyRegistered);
            return;
        }

        _log.Write(ActivityLevel.Warning, ActivityEvent.HotkeyFailed, new ActivityDetails { Error = bound.Error.Code });
        _services.GetRequiredService<IUserNotifier>().Show(
            "ClipBench",
            $"ClipBench could not claim the shortcut {hotkey} because another program uses it. {fallback}");
    }

    // Runs on a pool thread.
    private void OnHotkeyPressed(object? sender, HotkeyActionEventArgs e)
    {
        if (e.Action == HotkeyAction.OpenPopup)
        {
            _dispatcher.Post(() => TogglePopup());
        }
        else if (e.Action == HotkeyAction.ToggleCollect)
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
