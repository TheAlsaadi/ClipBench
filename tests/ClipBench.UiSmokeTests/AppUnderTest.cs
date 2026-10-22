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
        // misses windows on a second monitor with another scale.
        _ = Directory.CreateDirectory(folder);
        var bounds = window.BoundingRectangle;
        using var image = new System.Drawing.Bitmap(Math.Max(bounds.Width, 1), Math.Max(bounds.Height, 1));
        using (var graphics = System.Drawing.Graphics.FromImage(image))
        {
            var context = graphics.GetHdc();
            _ = PrintWindow(window.Properties.NativeWindowHandle.ValueOrDefault, context, PrintFullContent);
            graphics.ReleaseHdc(context);
        }

        image.Save(Path.Combine(folder, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    private const uint PrintFullContent = 2;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern bool PrintWindow(nint window, nint context, uint flags);

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
        if (FindPopup() is not null)
        {
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            WaitFor(() => FindPopup() is null, 10_000).ShouldBeTrue("Escape did not close the popup");
        }
    }

    public static AutomationElement SearchBox(Window popup) => popup.FindFirstDescendant(cf => cf.ByAutomationId("SearchBox"))
        ?? throw new InvalidOperationException("The popup has no search box.");

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
