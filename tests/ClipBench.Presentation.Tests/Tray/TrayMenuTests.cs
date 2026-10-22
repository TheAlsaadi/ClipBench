using ClipBench.Core.Capture;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Collect;
using ClipBench.Presentation.Tests.Support;
using ClipBench.Presentation.Tray;

namespace ClipBench.Presentation.Tests.Tray;

public sealed class TrayMenuTests : IDisposable
{
    private sealed class FakeCapture : ICaptureControl
    {
        public bool IsPaused { get; private set; }

        public void SetPaused(bool paused) => IsPaused = paused;
    }

    private readonly FakeCapture _capture = new();
    private readonly FakeQueueControl _queue = new();
    private readonly FakeCollectControl _collect = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeSettingsStore _settingsStore = new();
    private readonly SettingsService _settings;

    public TrayMenuTests() => _settings = new SettingsService(_settingsStore);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _settings.Dispose();

    private TrayViewModel Create() => new(_capture, _queue, new CollectToggle(_collect, _notifier), _settings);

    [Fact]
    public void The_menu_offers_open_collect_pause_and_exit_with_open_as_the_default()
    {
        var model = TrayMenu.Build(Create());

        model.Menu.Select(item => (item.Id, item.Text, item.IsDefault)).ShouldBe(
        [
            (1, "Open ClipBench", true),
            (0, string.Empty, false),
            (5, "Start collecting", false),
            (7, "Restore clipboard after paste", false),
            (2, "Pause capturing", false),
            (3, "Exit", false),
        ]);
    }

    [Fact]
    public void The_tool_tip_and_the_pause_item_follow_the_state()
    {
        var tray = Create();
        tray.TogglePauseCommand.Execute(null);

        var model = TrayMenu.Build(tray);

        model.ToolTip.ShouldBe("ClipBench · paused");
        model.Menu.Single(item => item.Id == TrayMenu.PauseId).Text.ShouldBe("Resume capturing");
    }

    [Fact]
    public void Choosing_each_item_runs_its_command()
    {
        var tray = Create();
        var opened = 0;
        var exited = 0;
        tray.OpenRequested += (_, _) => opened++;
        tray.ExitRequested += (_, _) => exited++;

        TrayMenu.Execute(tray, TrayMenu.OpenId);
        TrayMenu.Execute(tray, TrayMenu.PauseId);
        TrayMenu.Execute(tray, TrayMenu.ExitId);

        opened.ShouldBe(1);
        exited.ShouldBe(1);
        _capture.IsPaused.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void An_id_that_is_not_ours_does_nothing(int id)
    {
        var tray = Create();
        var raised = 0;
        tray.OpenRequested += (_, _) => raised++;
        tray.ExitRequested += (_, _) => raised++;

        TrayMenu.Execute(tray, id);

        raised.ShouldBe(0);
        _capture.IsPaused.ShouldBeFalse();
        _queue.Stops.ShouldBe(0);
    }

    [Fact]
    public void While_a_queue_runs_the_menu_offers_to_stop_it()
    {
        var tray = Create();
        _queue.Report(Core.Pasting.QueueState.Armed, 1, 4);

        var model = TrayMenu.Build(tray);

        model.Menu.Select(item => (item.Id, item.Text)).ShouldBe(
        [
            (TrayMenu.OpenId, "Open ClipBench"),
            (0, string.Empty),
            (TrayMenu.StopQueueId, "Stop paste queue"),
            (TrayMenu.CollectId, "Start collecting"),
            (TrayMenu.RestoreId, "Restore clipboard after paste"),
            (TrayMenu.PauseId, "Pause capturing"),
            (TrayMenu.ExitId, "Exit"),
        ]);
    }

    [Fact]
    public void Choosing_stop_paste_queue_stops_it()
    {
        var tray = Create();
        _queue.Report(Core.Pasting.QueueState.Armed, 0, 2);

        TrayMenu.Execute(tray, TrayMenu.StopQueueId);

        _queue.Stops.ShouldBe(1);
    }

    [Fact]
    public void While_collecting_the_menu_offers_to_finish_with_the_count_or_to_cancel()
    {
        var tray = Create();
        _collect.Start();
        _collect.Collected();
        _collect.Collected();

        var model = TrayMenu.Build(tray);

        model.Menu.Select(item => (item.Id, item.Text)).ShouldBe(
        [
            (TrayMenu.OpenId, "Open ClipBench"),
            (0, string.Empty),
            (TrayMenu.CollectId, "Finish collecting (2)"),
            (TrayMenu.CancelCollectId, "Cancel collecting"),
            (TrayMenu.RestoreId, "Restore clipboard after paste"),
            (TrayMenu.PauseId, "Pause capturing"),
            (TrayMenu.ExitId, "Exit"),
        ]);
    }

    [Fact]
    public async Task The_restore_item_is_ticked_while_the_clipboard_is_put_back_after_a_paste()
    {
        var tray = Create();
        TrayMenu.Build(tray).Menu.Single(item => item.Id == TrayMenu.RestoreId).IsChecked.ShouldBeTrue();

        await _settings.UpdateAsync(settings => settings with { RestorePreviousClipboard = false }, Ct);

        TrayMenu.Build(tray).Menu.Single(item => item.Id == TrayMenu.RestoreId).IsChecked.ShouldBeFalse();
        TrayMenu.Build(tray).Menu.Where(item => item.Id != TrayMenu.RestoreId).ShouldAllBe(item => !item.IsChecked);
    }

    [Fact]
    public void Choosing_the_restore_item_switches_it()
    {
        var tray = Create();

        TrayMenu.Execute(tray, TrayMenu.RestoreId);

        _settings.Current.RestorePreviousClipboard.ShouldBeFalse();
    }

    [Fact]
    public void Choosing_collect_starts_and_then_finishes_and_cancel_cancels()
    {
        var tray = Create();

        TrayMenu.Execute(tray, TrayMenu.CollectId);
        _collect.IsActive.ShouldBeTrue();
        TrayMenu.Execute(tray, TrayMenu.CollectId);
        _collect.Finishes.ShouldBe(1);
        TrayMenu.Execute(tray, TrayMenu.CollectId);
        TrayMenu.Execute(tray, TrayMenu.CancelCollectId);

        _collect.Cancels.ShouldBe(1);
        _collect.IsActive.ShouldBeFalse();
    }
}
