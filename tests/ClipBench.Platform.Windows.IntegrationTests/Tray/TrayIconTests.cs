using System.Collections.Concurrent;
using System.Collections.Immutable;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using ClipBench.Core.Tray;
using ClipBench.Platform.Windows.IntegrationTests.Support;
using ClipBench.Platform.Windows.Tray;

namespace ClipBench.Platform.Windows.IntegrationTests.Tray;

public sealed class TrayIconTests : IDisposable
{
    private const nint Handle = 0x4242;
    private const string IconPath = @"C:\app\Assets\ClipBench.ico";
    private const int NinSelect = 0x400;
    private const int ContextMenu = 0x7B;

    private readonly FakeWindow _window = new();
    private readonly FakeTrayApi _api;
    private readonly FakeTrayMenu _menu;
    private readonly TrayIcon _icon;
    private readonly BlockingCollection<string> _events = [];
    private readonly BlockingCollection<int> _chosen = [];

    public TrayIconTests()
    {
        _api = new FakeTrayApi(_window);
        _menu = new FakeTrayMenu(_window);
        _icon = new TrayIcon(_window, _window, Handle, _api, _menu, IconPath);
        _icon.OpenRequested += (_, _) => _events.Add("open");
        _icon.MenuItemChosen += (_, args) => _chosen.Add(args.Id);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _icon.Dispose();
        _events.Dispose();
        _chosen.Dispose();
    }

    private static TrayModel Model(string toolTip = "ClipBench \u00B7 capturing") => new(
        toolTip,
        [new TrayMenuItem(1, "Open ClipBench", IsDefault: true), TrayMenuItem.Separator, new TrayMenuItem(2, "Pause capturing"), new TrayMenuItem(3, "Exit")]);

    private static nint Params(int notification, int iconId) => (nint)(((long)iconId << 16) | (uint)(notification & 0xFFFF));

    private static nint Point(int x, int y) => (nint)(((long)(short)y << 16) | (ushort)(short)x);

    private async Task ShowAsync(TrayModel? model = null) => (await _icon.ShowAsync(model ?? Model(), Ct)).IsSuccess.ShouldBeTrue();

    private bool NothingMoreArrives() => !_events.TryTake(out _, 300, Ct) && !_chosen.TryTake(out _, 1, Ct);

    // --- showing ---

    [Fact]
    public async Task Showing_adds_the_icon_with_the_tool_tip_the_callback_message_and_the_loaded_picture()
    {
        var result = await _icon.ShowAsync(Model("ClipBench \u00B7 paused"), Ct);

        result.IsSuccess.ShouldBeTrue();
        _api.LoadedFrom.ShouldBe([IconPath]);
        _api.Added.ShouldBe([(Handle, 1u, 0x8002u, (nint)0x77, "ClipBench \u00B7 paused")]);
    }

    [Fact]
    public async Task A_tool_tip_that_is_too_long_is_shortened_to_what_windows_keeps()
    {
        await ShowAsync(Model(new string('t', 400)));

        _api.Added.Single().ToolTip.Length.ShouldBe(127);
        _api.Added.Single().ToolTip.ShouldEndWith("\u2026");
    }

    [Fact]
    public async Task A_picture_that_cannot_be_loaded_is_reported_and_no_icon_is_added()
    {
        _api.IconHandle = 0;

        var result = await _icon.ShowAsync(Model(), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.PlatformFailure);
        _api.Added.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_icon_windows_refuses_is_reported_and_its_picture_is_freed()
    {
        _api.AddSucceeds = false;

        var result = await _icon.ShowAsync(Model(), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.PlatformFailure);
        _api.Destroyed.ShouldBe([(nint)0x77]);
    }

    [Fact]
    public async Task Windows_is_asked_for_version_4_messages_right_after_the_icon_is_added_and_again_when_it_is_added_again()
    {
        await ShowAsync();
        _api.VersionRequests.ShouldBe([1u]);

        _window.Send(_api.TaskbarCreatedMessage);

        _api.Added.Count.ShouldBe(2);
        _api.VersionRequests.ShouldBe([1u, 1u]);
    }

    [Fact]
    public async Task An_icon_whose_version_windows_refuses_is_taken_away_again_and_reported()
    {
        // Windows accepted the icon but not the message version: the icon must not stay behind with a picture that is freed.
        _api.VersionSucceeds = false;

        var result = await _icon.ShowAsync(Model(), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.PlatformFailure);
        _api.Deleted.ShouldBe([(Handle, 1u)]);
        _api.Destroyed.ShouldBe([(nint)0x77]);
    }

    [Fact]
    public async Task Showing_twice_adds_the_icon_once()
    {
        await ShowAsync();
        await ShowAsync(Model("changed"));

        _api.Added.Count.ShouldBe(1);
        _api.Modified.ShouldBe(["changed"]);
    }

    // --- clicks ---

    [Fact]
    public async Task A_click_on_the_icon_asks_for_the_popup_off_the_window_thread()
    {
        await ShowAsync();
        using var deliveredOn = new BlockingCollection<int>();
        _icon.OpenRequested += (_, _) => deliveredOn.Add(Environment.CurrentManagedThreadId);

        // The fake window runs its work on the calling thread, so the calling thread stands in for the window thread.
        // It stays blocked below until the event arrives, so the pump cannot be delivering on it.
        var windowThread = Environment.CurrentManagedThreadId;
        var handled = _window.Send(TrayIcon.CallbackMessage, Point(10, 10), Params(NinSelect, 1));

        handled.ShouldBeTrue();
        deliveredOn.TryTake(out var thread, 10_000, Ct).ShouldBeTrue();
        thread.ShouldNotBe(windowThread);
    }

    [Fact]
    public async Task A_click_before_the_icon_is_shown_or_for_another_icon_is_ignored()
    {
        _window.Send(TrayIcon.CallbackMessage, Point(1, 1), Params(NinSelect, 1));
        await ShowAsync();

        _window.Send(TrayIcon.CallbackMessage, Point(1, 1), Params(NinSelect, 2));

        NothingMoreArrives().ShouldBeTrue();
    }

    [Fact]
    public async Task Other_mouse_messages_are_ignored()
    {
        await ShowAsync();

        _window.Send(TrayIcon.CallbackMessage, Point(1, 1), Params(0x200, 1));
        _window.Send(TrayIcon.CallbackMessage, Point(1, 1), Params(0x201, 1));

        NothingMoreArrives().ShouldBeTrue();
        _menu.Shown.ShouldBeEmpty();
    }

    // --- the menu ---

    [Fact]
    public async Task The_context_menu_opens_at_the_pointer_with_the_items_and_reports_the_choice()
    {
        await ShowAsync();
        _menu.Choice = 3;

        var handled = _window.Send(TrayIcon.CallbackMessage, Point(1500, 1010), Params(ContextMenu, 1));

        handled.ShouldBeTrue();
        _chosen.TryTake(out var id, 10_000, Ct).ShouldBeTrue();
        id.ShouldBe(3);
        var shown = _menu.Shown.Single();
        shown.At.ShouldBe(new PixelPoint(1500, 1010));
        shown.Items.Select(item => item.Text).ShouldBe(["Open ClipBench", string.Empty, "Pause capturing", "Exit"]);
    }

    [Fact]
    public async Task Closing_the_menu_without_a_choice_reports_nothing()
    {
        await ShowAsync();
        _menu.Choice = 0;

        _window.Send(TrayIcon.CallbackMessage, Point(5, 5), Params(ContextMenu, 1));

        _menu.Shown.Count.ShouldBe(1);
        NothingMoreArrives().ShouldBeTrue();
    }

    [Fact]
    public async Task The_menu_that_opens_is_the_latest_one_given()
    {
        await ShowAsync();
        ImmutableArray<TrayMenuItem> menu = [new TrayMenuItem(1, "Open ClipBench"), new TrayMenuItem(2, "Resume capturing")];

        (await _icon.UpdateAsync(new TrayModel("ClipBench \u00B7 paused", menu), Ct)).IsSuccess.ShouldBeTrue();
        _window.Send(TrayIcon.CallbackMessage, Point(5, 5), Params(ContextMenu, 1));

        _menu.Shown.Single().Items.Select(item => item.Text).ShouldBe(["Open ClipBench", "Resume capturing"]);
    }

    // --- updating ---

    [Fact]
    public async Task Updating_changes_the_tool_tip_and_shortens_it_when_needed()
    {
        await ShowAsync();

        (await _icon.UpdateAsync(Model(new string('u', 300)), Ct)).IsSuccess.ShouldBeTrue();

        _api.Modified.Single().Length.ShouldBe(127);
    }

    [Fact]
    public async Task An_update_windows_refuses_is_reported_but_the_menu_still_changes()
    {
        await ShowAsync();
        _api.ModifySucceeds = false;

        var result = await _icon.UpdateAsync(new TrayModel("x", [new TrayMenuItem(9, "New item")]), Ct);
        _window.Send(TrayIcon.CallbackMessage, Point(1, 1), Params(ContextMenu, 1));

        result.IsFailure.ShouldBeTrue();
        _menu.Shown.Single().Items.Single().Text.ShouldBe("New item");
    }

    [Fact]
    public async Task Updating_before_showing_only_remembers_the_model()
    {
        var result = await _icon.UpdateAsync(Model("later"), Ct);

        result.IsSuccess.ShouldBeTrue();
        _api.Modified.ShouldBeEmpty();

        await ShowAsync(Model("shown"));
        _api.Added.Single().ToolTip.ShouldBe("shown");
    }

    // --- the taskbar coming back ---

    [Fact]
    public async Task When_the_taskbar_is_created_again_the_icon_is_added_again_with_the_current_tool_tip()
    {
        await ShowAsync();
        await _icon.UpdateAsync(Model("current"), Ct);

        var handled = _window.Send(_api.TaskbarCreatedMessage);

        handled.ShouldBeTrue();
        _api.Added.Count.ShouldBe(2);
        _api.Added[1].ToolTip.ShouldBe("current");
        _api.Added[1].Icon.ShouldBe(0x77);
    }

    [Fact]
    public void A_taskbar_that_is_created_before_the_icon_was_shown_adds_nothing()
    {
        _window.Send(_api.TaskbarCreatedMessage);

        _api.Added.ShouldBeEmpty();
    }

    // --- notices ---

    [Fact]
    public async Task A_notice_is_shown_next_to_the_icon_with_a_shortened_title_and_text()
    {
        await ShowAsync();

        _icon.Show(new string('T', 100), new string('M', 400));

        var notice = _api.Notices.Single();
        notice.Title.Length.ShouldBe(63);
        notice.Message.Length.ShouldBe(255);
    }

    [Fact]
    public async Task A_short_notice_is_shown_as_it_is()
    {
        await ShowAsync();

        _icon.Show("ClipBench", "The paste did not work.");

        _api.Notices.ShouldBe([("ClipBench", "The paste did not work.")]);
    }

    [Fact]
    public void A_notice_before_the_icon_is_shown_is_dropped()
    {
        _icon.Show("ClipBench", "Nobody can see this.");

        _api.Notices.ShouldBeEmpty();
    }

    // --- stopping ---

    [Fact]
    public async Task Disposing_takes_the_icon_away_and_frees_the_picture_once()
    {
        await ShowAsync();

        _icon.Dispose();
        _icon.Dispose();

        _api.Deleted.ShouldBe([(Handle, 1u)]);
        _api.Destroyed.ShouldBe([(nint)0x77]);
    }

    private sealed class Owned : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    [Fact]
    public async Task Disposing_also_disposes_the_window_the_icon_owns_once_the_icon_is_gone()
    {
        var owned = new Owned();
        using var icon = new TrayIcon(_window, _window, Handle, _api, _menu, IconPath, owned);
        (await icon.ShowAsync(Model(), Ct)).IsSuccess.ShouldBeTrue();

        icon.Dispose();
        icon.Dispose();

        owned.Disposals.ShouldBe(1);
        _api.Deleted.Count.ShouldBe(1);
    }

    [Fact]
    public void Disposing_an_icon_that_was_never_shown_removes_nothing()
    {
        _icon.Dispose();

        _api.Deleted.ShouldBeEmpty();
        _api.Destroyed.ShouldBeEmpty();
    }

    [Fact]
    public async Task After_dispose_clicks_and_notices_do_nothing()
    {
        await ShowAsync();
        _icon.Dispose();

        _window.Send(TrayIcon.CallbackMessage, Point(1, 1), Params(NinSelect, 1));
        _icon.Show("ClipBench", "late");

        NothingMoreArrives().ShouldBeTrue();
        _api.Notices.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_subscriber_that_throws_does_not_stop_the_icon()
    {
        await ShowAsync();
        _icon.OpenRequested += (_, _) => throw new InvalidOperationException("a subscriber bug");

        _window.Send(TrayIcon.CallbackMessage, Point(1, 1), Params(NinSelect, 1));
        _window.Send(TrayIcon.CallbackMessage, Point(1, 1), Params(NinSelect, 1));

        _events.TryTake(out _, 10_000, Ct).ShouldBeTrue();
        _events.TryTake(out _, 10_000, Ct).ShouldBeTrue();
    }
}
