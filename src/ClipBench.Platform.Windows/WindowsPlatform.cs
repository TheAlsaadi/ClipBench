using System.Security.Cryptography;
using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Core.Hosting;
using ClipBench.Core.Hotkeys;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using ClipBench.Core.Security;
using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.Hosting;
using ClipBench.Platform.Windows.Hotkeys;
using ClipBench.Platform.Windows.Pasting;
using ClipBench.Platform.Windows.Screen;
using ClipBench.Platform.Windows.Security;
using ClipBench.Platform.Windows.Windowing;

namespace ClipBench.Platform.Windows;

/// <summary>
/// The Windows implementations of the Core interfaces, wired together over one background window and handed out
/// as the interfaces. The app creates one at start-up and disposes it at exit; nothing else needs to know which
/// classes sit behind them.
/// </summary>
public sealed class WindowsPlatform : IDisposable
{
    private readonly MessageWindow _window;
    private readonly ClipboardListener _listener;
    private readonly HotkeyService _hotkeys;
    private int _disposed;

    private WindowsPlatform(
        MessageWindow window,
        ClipboardListener listener,
        ClipboardReader reader,
        ClipboardWriter writer,
        HotkeyService hotkeys,
        ForegroundTracker tracker,
        PasteInjector injector)
    {
        _window = window;
        _listener = listener;
        _hotkeys = hotkeys;
        ClipboardReader = reader;
        ClipboardWriter = writer;
        ForegroundTracker = tracker;
        PasteInjector = injector;
    }

    /// <summary>Tells when the clipboard changes.</summary>
    public IClipboardListener ClipboardListener => _listener;

    /// <summary>Reads the clipboard through the capture filter.</summary>
    public IClipboardReader ClipboardReader { get; }

    /// <summary>Puts content on the clipboard.</summary>
    public IClipboardWriter ClipboardWriter { get; }

    /// <summary>Global keyboard shortcuts.</summary>
    public IHotkeyService Hotkeys => _hotkeys;

    /// <summary>Remembers which window had focus.</summary>
    public IForegroundTracker ForegroundTracker { get; }

    /// <summary>Pastes into another window.</summary>
    public IPasteInjector PasteInjector { get; }

    /// <summary>Finds where to put the popup.</summary>
    public IScreenLocator ScreenLocator { get; } = new ScreenLocator(new Win32ScreenApi());

    /// <summary>Hides windows from screen capture and rounds their corners.</summary>
    public IWindowEffects WindowEffects { get; } = new Win32WindowEffects();

    /// <summary>Creates the platform: the background window and everything that runs on it.</summary>
    /// <param name="timeProvider">The clock for every wait.</param>
    /// <returns>The platform, or <see cref="ErrorCode.PlatformFailure"/> when Windows would not create the background window.</returns>
    public static Result<WindowsPlatform> Create(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var created = MessageWindow.Create();
        if (created.IsFailure)
        {
            return Result.Failure<WindowsPlatform>(created.Error);
        }

        var window = created.Value;
        try
        {
            var clipboard = new Win32ClipboardApi(window.Handle);
            var windows = new Win32WindowApi();
            var writer = new ClipboardWriter(clipboard, timeProvider);
            var tracker = new ForegroundTracker(windows, clipboard, new Win32ProcessIntegrity());

            return Result.Success(new WindowsPlatform(
                window,
                new ClipboardListener(window, window, new Win32ClipboardNotifications(window.Handle), clipboard),
                new ClipboardReader(clipboard, timeProvider),
                writer,
                new HotkeyService(window, window, new Win32HotkeyApi(window.Handle)),
                tracker,
                new PasteInjector(writer, clipboard, tracker, windows, new Win32KeyboardApi(), timeProvider)));
        }
        catch
        {
            window.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Makes sure only one copy runs for the signed-in user and a data folder. Copies that use different folders (a
    /// test run with its own folder) do not get in each other's way, and a second copy for the same folder never opens
    /// the same history twice.
    /// </summary>
    /// <param name="dataDirectory">The full path of the folder that holds the history. Case and a trailing separator do not matter.</param>
    /// <returns>The guard. When it is not the first instance, signal the first one and exit.</returns>
    public static ISingleInstanceGuard AcquireSingleInstance(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Path.IsPathFullyQualified(dataDirectory))
        {
            throw new ArgumentException("The data folder must be a full path.", nameof(dataDirectory));
        }

        var normal = Path.GetFullPath(dataDirectory).TrimEnd('\\', '/').ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normal)))[..16];
        return SingleInstanceGuard.Acquire($"{SingleInstanceGuard.AppName}.{hash}");
    }

    /// <summary>The key protector that wraps the history key with the current user's Windows account.</summary>
    /// <returns>The protector.</returns>
    public static IKeyProtector CreateKeyProtector() => DpapiKeyProtector.ForClipBench();

    /// <summary>Stops every service and destroys the background window.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _hotkeys.Dispose();
        _listener.Dispose();
        _window.Dispose();
    }
}
