using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Drawing;
using ClipBench.Core.Results;
using ClipBench.Core.Tray;
using ClipBench.Platform.Windows.IntegrationTests.Support;
using ClipBench.Platform.Windows.Tray;
using ClipBench.Platform.Windows.Windowing;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace ClipBench.Platform.Windows.IntegrationTests.Tray;

/// <summary>
/// Adds a real icon to the real notification area for a moment. Nothing here clicks it or opens its menu, which is
/// modal and is checked by hand (docs/MANUAL_QA.md).
/// </summary>
[Trait("Category", "Integration")]
public sealed class RealTrayTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clipbench-traytests-" + Guid.NewGuid().ToString("N"));
    private readonly string _icon;

    public RealTrayTests()
    {
        Directory.CreateDirectory(_folder);
        _icon = Path.Combine(_folder, "test.ico");

        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.SteelBlue);
        }

        var handle = bitmap.GetHicon();
        using (var icon = Icon.FromHandle(handle))
        using (var stream = File.Create(_icon))
        {
            icon.Save(stream);
        }

        new Win32TrayApi().DestroyIcon(handle);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static TrayModel Model(string toolTip = "ClipBench test icon") => new(toolTip, [new TrayMenuItem(1, "Open"), new TrayMenuItem(2, "Exit")]);

    [Fact]
    public async Task The_platform_makes_an_icon_that_windows_accepts_and_that_can_be_changed()
    {
        var made = WindowsPlatform.CreateTray(_icon);
        made.IsSuccess.ShouldBeTrue();
        using var tray = made.Value;

        var shown = await tray.ShowAsync(Model(), Ct);

        shown.IsSuccess.ShouldBeTrue(shown.IsFailure ? shown.Error.Message : string.Empty);

        // Changing the tool tip only succeeds for an icon Windows really has.
        (await tray.UpdateAsync(Model("ClipBench test icon, changed"), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_picture_that_is_not_there_is_reported()
    {
        using var tray = WindowsPlatform.CreateTray(Path.Combine(_folder, "missing.ico")).Value;

        var shown = await tray.ShowAsync(Model(), Ct);

        shown.IsFailure.ShouldBeTrue();
        shown.Error.Code.ShouldBe(ErrorCode.PlatformFailure);
    }

    [Fact]
    public async Task A_click_message_sent_to_the_icons_window_asks_for_the_popup()
    {
        using var window = MessageWindow.Create(messageOnly: false).Value;
        using var tray = new TrayIcon(window, window, window.Handle, new Win32TrayApi(), new RefusingMenu(), _icon);
        using var opened = new BlockingCollection<int>();
        tray.OpenRequested += (_, _) => opened.Add(1);
        (await tray.ShowAsync(Model(), Ct)).IsSuccess.ShouldBeTrue();

        // What Windows sends for a click of an icon that asked for version 4 messages: the notification and the icon id.
        const int NinSelect = 0x400;
        bool posted = PInvoke.PostMessage(new HWND(window.Handle), TrayIcon.CallbackMessage, new WPARAM(0), new LPARAM((1 << 16) | NinSelect));

        posted.ShouldBeTrue();
        opened.TryTake(out _, 10_000, Ct).ShouldBeTrue();
    }

    [Fact]
    public async Task After_dispose_windows_no_longer_has_the_icon()
    {
        using var window = MessageWindow.Create(messageOnly: false).Value;
        var api = new Win32TrayApi();
        var tray = new TrayIcon(window, window, window.Handle, api, new RefusingMenu(), _icon);
        (await tray.ShowAsync(Model(), Ct)).IsSuccess.ShouldBeTrue();
        api.Modify(window.Handle, TrayIcon.IconId, "still here").ShouldBeTrue();

        tray.Dispose();

        api.Modify(window.Handle, TrayIcon.IconId, "gone").ShouldBeFalse();
    }

    private sealed class RefusingMenu : ITrayMenu
    {
        public int Show(nint window, Core.Screen.PixelPoint at, IReadOnlyList<TrayMenuItem> items) => 0;
    }
}
