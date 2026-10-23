using System.Diagnostics;
using ClipBench.Core.Clips;
using ClipBench.Platform.Windows;
using ClipBench.Storage;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace ClipBench.UiSmokeTests;

/// <summary>
/// The built app, started on a throwaway history with F24 as its popup shortcut and F23 as its collect shortcut. The
/// history is filled through the real storage before the app starts, so the tests never read the user's history. Nobody
/// may type or click while a test runs: the keys go to whichever window has focus.
/// </summary>
public sealed class AppUnderTest : IAsyncLifetime
{
    public const string Jwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    /// <summary>The clips saved before the app starts, newest first.</summary>
    public static readonly string[] SeededTexts =
    [
        "Invoice 2026-10 for ACME Corp",
        "Meeting notes: budget review and hiring plan",
        Jwt,
        "https://learn.microsoft.com/windows/apps/winui/",
        "Disposable clip to delete",
        "Pin me please",
    ];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clipbench-smoke-" + Guid.NewGuid().ToString("N"));
    private Process? _process;
    private UIA3Automation? _automation;
    private FlaUI.Core.Application? _application;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public string LogPath => Path.Combine(_folder, "logs", "clipbench.log");

    public Process Process => _process ?? throw new InvalidOperationException("The app was not started.");

    public async ValueTask InitializeAsync()
    {
        await SeedHistoryAsync();

        var start = new ProcessStartInfo(FindApp(), ["--data-dir", _folder, "--hotkey", "F24", "--collect-hotkey", "F23", "--allow-screen-capture"]) { UseShellExecute = false };
        _process = Process.Start(start) ?? throw new InvalidOperationException("The app did not start.");
        _automation = new UIA3Automation();
        _application = FlaUI.Core.Application.Attach(_process);

        WaitFor(() => File.Exists(LogPath) && ReadLog().Contains("HotkeyRegistered", StringComparison.Ordinal), 30_000)
            .ShouldBeTrue("the app never reported its shortcut. The log says: " + (File.Exists(LogPath) ? ReadLog() : "(no log)"));
    }

    public ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false } running)
        {
            running.Kill(entireProcessTree: true);
            running.WaitForExit(10_000);
        }

        _process?.Dispose();
        _automation?.Dispose();
        if (Directory.Exists(_folder))
        {
            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (IOException)
            {
                // The app may still be letting go of a file. The folder is in the temp folder and holds only test data.
            }
        }

        return ValueTask.CompletedTask;
    }

    public string ReadLog()
    {
        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The popup when it is on screen, or null.</summary>
    public Window? FindPopup() => _application!
        .GetAllTopLevelWindows(_automation!)
        .FirstOrDefault(window => window.Title == "ClipBench" && !window.Properties.IsOffscreen.ValueOrDefault);

    /// <summary>Another window of the app (the snippets window, say) when it is on screen, or null.</summary>
    /// <param name="title">The title of the window.</param>
    public Window? FindWindow(string title) => _application!
        .GetAllTopLevelWindows(_automation!)
        .FirstOrDefault(window => window.Title == title && !window.Properties.IsOffscreen.ValueOrDefault);

    /// <summary>
    /// A menu item of the app, or null. A menu that does not fit inside the popup opens in a window of its own, so every top-level
    /// element of the app's process is searched, and nothing else: a search of the whole desktop takes seconds.
    /// </summary>
    /// <param name="name">The text of the item.</param>
    public AutomationElement? FindMenuItem(string name) => _automation!
        .GetDesktop()
        .FindAllChildren(cf => cf.ByProcessId(Process.Id))
        .Select(window => window.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(name))))
        .FirstOrDefault(item => item is not null);

    /// <summary>What is on screen for the app, for the message of a test that fails: the pointer, the windows, the menu items.</summary>
    public string Describe()
    {
        var top = _automation!.GetDesktop().FindAllChildren(cf => cf.ByProcessId(Process.Id));
        var items = top.SelectMany(window => window.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))).Select(item => item.Name);
        return $"pointer {Mouse.Position}; app windows: {string.Join(" | ", top.Select(window => window.Name + "@" + window.BoundingRectangle))}; menu items: {string.Join(", ", items)}; focused: {_automation.FocusedElement()?.Name}";
    }

    /// <summary>The queue window when it is on screen, or null.</summary>
    public Window? FindQueueWindow() => _application!
        .GetAllTopLevelWindows(_automation!)
        .FirstOrDefault(window => window.Title == "ClipBench paste queue" && !window.Properties.IsOffscreen.ValueOrDefault);

    /// <summary>What the queue window says, or null when it is not on screen.</summary>
    public string? QueueText() => FindQueueWindow()?.FindFirstDescendant(cf => cf.ByAutomationId("QueueHudText"))?.Properties.Name.ValueOrDefault;

    /// <summary>Stops a queue that an earlier test left running, so its Ctrl+V cannot take this test's pastes.</summary>
    public void EndQueue()
    {
        if (FindQueueWindow() is not null)
        {
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            WaitFor(() => FindQueueWindow() is null, 10_000).ShouldBeTrue("Esc did not stop the queue left running");
        }
    }

    /// <summary>
    /// Saves a picture of an element when CLIPBENCH_SMOKE_SCREENSHOTS names a folder, so its looks can be checked by eye.
    /// The app runs with --allow-screen-capture for this; it hides from captures otherwise.
    /// </summary>
    public static void SaveScreenshot(Window window, string name)
    {
        var folder = Environment.GetEnvironmentVariable("CLIPBENCH_SMOKE_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        // The window draws itself into the picture, which works on any monitor at any scale; a copy of the screen
        // misses windows on a second monitor with another scale. The size is asked of Windows in real pixels: the
        // rectangle that UI Automation gives is scaled for a program that does not know about the display scale, and the
        // picture would show only the top left of a window on a monitor above 100%.
        _ = Directory.CreateDirectory(folder);
        var handle = window.Properties.NativeWindowHandle.ValueOrDefault;
        var previous = SetThreadDpiAwarenessContext(_perMonitorAwareV2);
        try
        {
            _ = GetWindowRect(handle, out var rect);
            using var image = new System.Drawing.Bitmap(Math.Max(rect.Right - rect.Left, 1), Math.Max(rect.Bottom - rect.Top, 1));
            using (var graphics = System.Drawing.Graphics.FromImage(image))
            {
                var context = graphics.GetHdc();
                _ = PrintWindow(handle, context, PrintFullContent);
                graphics.ReleaseHdc(context);
            }

            image.Save(Path.Combine(folder, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        finally
        {
            _ = SetThreadDpiAwarenessContext(previous);
        }
    }

    private const uint PrintFullContent = 2;
    private static readonly nint _perMonitorAwareV2 = -4;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private readonly record struct NativeRect(int Left, int Top, int Right, int Bottom);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern bool PrintWindow(nint window, nint context, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    public Window OpenPopup()
    {
        if (FindPopup() is { } open)
        {
            return open;
        }

        Keyboard.Type(VirtualKeyShort.F24);
        WaitFor(() => FindPopup() is not null, 10_000).ShouldBeTrue("the shortcut did not open the popup");
        var popup = FindPopup()!;
        WaitFor(() => SearchBox(popup).Properties.HasKeyboardFocus.ValueOrDefault, 10_000).ShouldBeTrue("the search box did not get the focus");
        return popup;
    }

    /// <summary>Closes the popup if it is open, so the next test starts from a known place.</summary>
    public void ClosePopup()
    {
        if (FindPopup() is { } popup)
        {
            // The close button is pressed through automation, not with a key: a key goes to the window in front, which is not always
            // the popup (a locked one stays behind other windows), and Esc is also what interrupts the person running the tests.
            popup.FindFirstDescendant(cf => cf.ByAutomationId("CloseButton"))!.Patterns.Invoke.Pattern.Invoke();

            WaitFor(() => FindPopup() is null, 10_000).ShouldBeTrue("the popup did not close. " + Describe());
        }
    }

    /// <summary>
    /// Presses the mouse button on a point, drags the pointer by a distance in small steps and lets go, the way a hand moves a
    /// window. Each step is put there and then nudged with real mouse input, because a move that is only a new position sends no
    /// mouse message and the system's move loop would not see it.
    /// </summary>
    /// <param name="from">Where to press.</param>
    /// <param name="dx">How far to the right to drag; negative for the left.</param>
    /// <param name="dy">How far down to drag; negative for up.</param>
    /// <param name="fast">True to drag in a few big steps with no wait between them, the way a quick flick of the hand does.</param>
    public static void DragBy(System.Drawing.Point from, int dx, int dy, bool fast = false)
    {
        PointAt(from);
        Mouse.Down(MouseButton.Left);
        Thread.Sleep(200);
        var steps = fast ? 6 : 20;
        for (var step = 1; step <= steps; step++)
        {
            Mouse.MoveTo(new System.Drawing.Point(from.X + (dx * step / steps), from.Y + (dy * step / steps)));
            MouseEvent(MouseEventMove, 1, 0, 0, 0);
            if (!fast)
            {
                Thread.Sleep(30);
            }

            MouseEvent(MouseEventMove, -1, 0, 0, 0);
            if (!fast)
            {
                Thread.Sleep(30);
            }
        }

        Thread.Sleep(200);
        Mouse.Up(MouseButton.Left);
        Thread.Sleep(300);
    }

    /// <summary>
    /// Tells whether the popup is the window in front. A test that types must check this first: when the popup has not taken the
    /// focus, the keys go to whatever is in front, which can be the window the person running the tests is working in.
    /// </summary>
    public static bool IsInFront(Window popup) => GetForegroundWindow() == popup.Properties.NativeWindowHandle.ValueOrDefault;

    /// <summary>The window in front, for the message of a test that fails.</summary>
    public static string ForegroundDescription()
    {
        var window = GetForegroundWindow();
        var text = new char[256];
        var length = GetWindowText(window, text, text.Length);
        return $"in front: 0x{window:X} \"{new string(text, 0, Math.Max(0, length))}\"";
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern int GetWindowText(nint window, char[] text, int count);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern nint GetForegroundWindow();

    public static AutomationElement SearchBox(Window popup) => popup.FindFirstDescendant(cf => cf.ByAutomationId("SearchBox"))
        ?? throw new InvalidOperationException("The popup has no search box.");

    /// <summary>The entries of the sidebar, or none while it is closed.</summary>
    public static AutomationElement[] SidebarEntries(Window popup) =>
        popup.FindFirstDescendant(cf => cf.ByAutomationId("SidebarList"))?.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)) ?? [];

    /// <summary>Opens the sidebar with its shortcut when it is closed: it stays open or closed as it was left.</summary>
    public static void EnsureSidebar(Window popup)
    {
        if (SidebarEntries(popup).Length == 0)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_B);
        }

        WaitFor(() => SidebarEntries(popup).Length > 0, 10_000).ShouldBeTrue("the sidebar did not appear");
    }

    /// <summary>
    /// Moves the mouse pointer onto a point the way a hand does: the pointer is put there, and then moved a little with real
    /// mouse input. Setting the position alone sends the app no pointer event, so nothing under it would react to the
    /// pointer, and a click could land on nothing.
    /// </summary>
    /// <param name="point">Where to put the pointer.</param>
    public static void PointAt(System.Drawing.Point point)
    {
        Mouse.MoveTo(point);
        Thread.Sleep(100);
        MouseEvent(MouseEventMove, 3, 0, 0, 0);
        Thread.Sleep(100);
        MouseEvent(MouseEventMove, -3, 0, 0, 0);
        Thread.Sleep(250);
    }

    private const uint MouseEventMove = 0x0001;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "mouse_event")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern void MouseEvent(uint flags, int dx, int dy, uint data, nuint extraInfo);

    public static AutomationElement[] Rows(Window popup) =>
        popup.FindFirstDescendant(cf => cf.ByAutomationId("ClipList"))?.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem))
        ?? [];

    public static string[] RowNames(Window popup) => [.. Rows(popup).Select(row => row.Name)];

    /// <summary>Polls until the condition holds. These tests drive a real desktop, so they wait in real time.</summary>
    public static bool WaitFor(Func<bool> condition, int milliseconds)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(milliseconds / 1000.0 * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            try
            {
                if (condition())
                {
                    return true;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or FlaUI.Core.Exceptions.ElementNotAvailableException or NullReferenceException)
            {
                // The element was replaced while it was being read. Try again.
            }

            Thread.Sleep(50);
        }

        return false;
    }

    private async Task SeedHistoryAsync()
    {
        var paths = new StoragePaths(_folder);
        var opened = await ClipStorage.OpenAsync(paths, WindowsPlatform.CreateKeyProtector(), TimeProvider.System, Ct);
        opened.IsSuccess.ShouldBeTrue(opened.IsFailure ? opened.Error.Message : string.Empty);
        await using var storage = opened.Value;

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < SeededTexts.Length; i++)
        {
            var clip = new Clip(ClipId.New(), new TextContent(SeededTexts[i]), now.AddMinutes(-(i + 1)), "notepad.exe", IsPinned: false);
            (await storage.Repository.SaveAsync(clip, Ct)).IsSuccess.ShouldBeTrue();
        }
    }

    // The tests run from tests/ClipBench.UiSmokeTests/bin/<config>/<tfm>, the app from src/ClipBench.App/bin/<platform>/<config>/<tfm>/win-<platform>.
    private static string FindApp()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ClipBench.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("the solution folder was not found above the test program");
        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? "Debug" : "Release";
        var platform = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "ARM64" : "x64";
        var runtime = platform == "ARM64" ? "win-arm64" : "win-x64";
        var path = Path.Combine(directory.FullName, "src", "ClipBench.App", "bin", platform, configuration, "net10.0-windows10.0.19041.0", runtime, "ClipBench.App.exe");
        File.Exists(path).ShouldBeTrue($"the app is not built at {path}. Build the solution first.");
        return path;
    }
}
