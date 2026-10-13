using System.Diagnostics;
using ClipBench.Core.Hotkeys;
using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.Hotkeys;
using ClipBench.Platform.Windows.IntegrationTests.Support;
using ClipBench.Platform.Windows.Windowing;

namespace ClipBench.Platform.Windows.IntegrationTests.Windowing;

// A subscriber that disposes a service from inside an event handler can end up on the window thread. The service has to
// do its clean-up there and then: queueing it behind the running work and waiting would freeze every shortcut for the wait.
public class DisposeOnWindowThreadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_hotkey_service_gives_its_shortcuts_back_at_once_when_disposed_on_the_window_thread()
    {
        using var window = MessageWindow.Create().Value;
        var api = new RecordingHotkeyApi();
        var service = new HotkeyService(window, window, api);
        var id = (await service.RegisterAsync(Hotkey.Parse("Ctrl+K").Value, Ct)).Value;

        var elapsed = await window.InvokeAsync(
            () =>
            {
                var clock = Stopwatch.StartNew();
                service.Dispose();
                return clock.Elapsed;
            },
            Ct);

        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2), "it must not wait for work queued behind itself");
        api.Released.ShouldBe([id.Value]);
    }

    [Fact]
    public async Task The_clipboard_listener_stops_listening_at_once_when_disposed_on_the_window_thread()
    {
        using var window = MessageWindow.Create().Value;
        var notifications = new RecordingNotifications();
        var listener = new ClipboardListener(window, window, notifications, new FakeClipboardApi());
        (await listener.StartAsync(Ct)).IsSuccess.ShouldBeTrue();

        var elapsed = await window.InvokeAsync(
            () =>
            {
                var clock = Stopwatch.StartNew();
                listener.Dispose();
                return clock.Elapsed;
            },
            Ct);

        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        notifications.Unsubscriptions.ShouldBe(1);
    }

    [Fact]
    public async Task Disposing_from_another_thread_still_waits_for_the_window_thread_to_do_it()
    {
        using var window = MessageWindow.Create().Value;
        var api = new RecordingHotkeyApi();
        var service = new HotkeyService(window, window, api);
        var id = (await service.RegisterAsync(Hotkey.Parse("Ctrl+K").Value, Ct)).Value;

        service.Dispose();

        api.Released.ShouldBe([id.Value], "by the time Dispose returns, the shortcut is back with Windows");
    }

    private sealed class RecordingHotkeyApi : IHotkeyApi
    {
        public List<int> Released { get; } = [];

        public bool Register(int id, uint modifiers, uint virtualKey, out int error)
        {
            error = 0;
            return true;
        }

        public bool Unregister(int id)
        {
            lock (Released)
            {
                Released.Add(id);
            }

            return true;
        }
    }

    private sealed class RecordingNotifications : IClipboardNotifications
    {
        public int Unsubscriptions { get; private set; }

        public bool Subscribe() => true;

        public void Unsubscribe() => Unsubscriptions++;
    }
}
