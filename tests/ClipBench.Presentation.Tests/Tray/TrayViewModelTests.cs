using ClipBench.Core.Capture;
using ClipBench.Core.Pasting;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Collect;
using ClipBench.Presentation.Tests.Support;
using ClipBench.Presentation.Tray;

namespace ClipBench.Presentation.Tests.Tray;

public sealed class TrayViewModelTests : IDisposable
{
    private sealed class FakeCapture : ICaptureControl
    {
        public bool IsPaused { get; private set; }

        public List<bool> Calls { get; } = [];

        public void SetPaused(bool paused)
        {
            IsPaused = paused;
            Calls.Add(paused);
        }
    }

    private readonly FakeCapture _capture = new();
    private readonly FakeQueueControl _queue = new();
    private readonly FakeCollectControl _collect = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeSettingsStore _settingsStore = new();
    private readonly SettingsService _settings;

    public TrayViewModelTests() => _settings = new SettingsService(_settingsStore);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _settings.Dispose();

    private TrayViewModel Create() => new(_capture, _queue, new CollectToggle(_collect, _notifier), _settings);

    private static string[] ChangedProperties(TrayViewModel tray, Action act)
    {
        List<string> names = [];
        tray.PropertyChanged += (_, args) => names.Add(args.PropertyName!);
        act();
        return [.. names];
    }

    [Fact]
    public void It_starts_capturing_with_labels_that_say_what_the_menu_will_do()
    {
        var tray = Create();

        tray.IsPaused.ShouldBeFalse();
        tray.PauseLabel.ShouldBe("Pause capturing");
        tray.ToolTip.ShouldBe("ClipBench · capturing");
    }

    [Fact]
    public void It_starts_paused_when_capture_already_is()
    {
        _capture.SetPaused(true);
        _capture.Calls.Clear();

        var tray = Create();

        tray.IsPaused.ShouldBeTrue();
        tray.PauseLabel.ShouldBe("Resume capturing");
        tray.ToolTip.ShouldBe("ClipBench · paused");
        _capture.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Choosing_pause_pauses_capture_and_updates_the_labels()
    {
        var tray = Create();

        var changed = ChangedProperties(tray, () => tray.TogglePauseCommand.Execute(null));

        _capture.IsPaused.ShouldBeTrue();
        tray.IsPaused.ShouldBeTrue();
        tray.PauseLabel.ShouldBe("Resume capturing");
        tray.ToolTip.ShouldBe("ClipBench · paused");
        changed.ShouldContain(nameof(TrayViewModel.PauseLabel));
        changed.ShouldContain(nameof(TrayViewModel.ToolTip));
    }

    [Fact]
    public void Choosing_it_again_resumes_capture()
    {
        var tray = Create();
        tray.TogglePauseCommand.Execute(null);

        tray.TogglePauseCommand.Execute(null);

        _capture.Calls.ShouldBe([true, false]);
        tray.IsPaused.ShouldBeFalse();
        tray.PauseLabel.ShouldBe("Pause capturing");
    }

    [Fact]
    public void Open_asks_for_the_popup()
    {
        var tray = Create();
        var opened = 0;
        tray.OpenRequested += (_, _) => opened++;

        tray.OpenCommand.Execute(null);

        opened.ShouldBe(1);
    }

    [Fact]
    public void Exit_asks_to_quit_once_however_often_it_is_chosen()
    {
        var tray = Create();
        var exits = 0;
        tray.ExitRequested += (_, _) => exits++;

        tray.ExitCommand.Execute(null);
        tray.ExitCommand.Execute(null);

        exits.ShouldBe(1);
    }

    // --- the paste queue ---

    [Fact]
    public void While_a_queue_runs_the_tool_tip_says_so()
    {
        var tray = Create();

        var changed = ChangedProperties(tray, () => _queue.Report(QueueState.Armed, 0, 3));

        tray.IsQueueRunning.ShouldBeTrue();
        tray.ToolTip.ShouldBe("ClipBench · capturing · paste queue running");
        changed.ShouldContain(nameof(TrayViewModel.IsQueueRunning));
        changed.ShouldContain(nameof(TrayViewModel.ToolTip));
    }

    [Fact]
    public void When_the_queue_ends_the_tool_tip_goes_back()
    {
        var tray = Create();
        _queue.Report(QueueState.Armed, 0, 1);

        _queue.Report(QueueState.Exhausted, 1, 1);

        tray.IsQueueRunning.ShouldBeFalse();
        tray.ToolTip.ShouldBe("ClipBench · capturing");
    }

    [Fact]
    public async Task Stop_queue_stops_it()
    {
        var tray = Create();
        _queue.Report(QueueState.Armed, 0, 1);

        await tray.StopQueueCommand.ExecuteAsync(null);

        _queue.Stops.ShouldBe(1);
        tray.IsQueueRunning.ShouldBeFalse();
    }

    [Fact]
    public void Disposing_stops_following_the_queue()
    {
        var tray = Create();

        tray.Dispose();

        _queue.SubscriberCount.ShouldBe(0);
    }

    // --- collecting ---

    [Fact]
    public void Collecting_shows_in_the_label_and_the_tool_tip_with_the_count()
    {
        var tray = Create();

        var changed = ChangedProperties(tray, () =>
        {
            _collect.Start();
            _collect.Collected();
        });

        tray.IsCollecting.ShouldBeTrue();
        tray.CollectLabel.ShouldBe("Finish collecting (1)");
        tray.ToolTip.ShouldBe("ClipBench · capturing · collecting 1");
        changed.ShouldContain(nameof(TrayViewModel.CollectLabel));
        changed.ShouldContain(nameof(TrayViewModel.ToolTip));
    }

    [Fact]
    public void Without_a_session_the_label_offers_to_start()
    {
        var tray = Create();

        tray.IsCollecting.ShouldBeFalse();
        tray.CollectLabel.ShouldBe("Start collecting");
    }

    [Fact]
    public void Collecting_and_a_queue_both_show_in_the_tool_tip()
    {
        var tray = Create();
        _collect.Start();
        _queue.Report(QueueState.Armed, 0, 2);

        tray.ToolTip.ShouldBe("ClipBench · capturing · collecting 0 · paste queue running");
    }

    [Fact]
    public async Task The_collect_command_starts_and_finishes_and_cancel_cancels()
    {
        var tray = Create();

        await tray.ToggleCollectCommand.ExecuteAsync(null);
        _collect.IsActive.ShouldBeTrue();
        await tray.ToggleCollectCommand.ExecuteAsync(null);
        _collect.Finishes.ShouldBe(1);
        await tray.ToggleCollectCommand.ExecuteAsync(null);
        tray.CancelCollectCommand.Execute(null);

        _collect.Cancels.ShouldBe(1);
    }

    [Fact]
    public void Disposing_stops_following_the_collect_session()
    {
        var tray = Create();

        tray.Dispose();

        _collect.SubscriberCount.ShouldBe(0);
    }

    // --- restoring the clipboard ---

    [Fact]
    public void Restoring_the_clipboard_follows_the_settings()
    {
        var tray = Create();

        tray.RestoreClipboard.ShouldBeTrue();
    }

    [Fact]
    public async Task The_restore_command_switches_the_setting_and_saves_it()
    {
        var tray = Create();

        await tray.ToggleRestoreCommand.ExecuteAsync(null);

        tray.RestoreClipboard.ShouldBeFalse();
        _settingsStore.Saved.Single().RestorePreviousClipboard.ShouldBeFalse();
        await tray.ToggleRestoreCommand.ExecuteAsync(null);
        tray.RestoreClipboard.ShouldBeTrue();
    }

    [Fact]
    public async Task A_change_of_the_setting_made_elsewhere_is_announced()
    {
        var tray = Create();
        List<string> changed = [];
        tray.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);

        await _settings.UpdateAsync(settings => settings with { RestorePreviousClipboard = false }, Ct);

        changed.ShouldContain(nameof(TrayViewModel.RestoreClipboard));
    }

    [Fact]
    public async Task A_settings_change_that_leaves_the_restore_switch_alone_is_not_announced_as_one()
    {
        var tray = Create();
        List<string> changed = [];
        tray.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);

        await _settings.UpdateAsync(settings => settings with { Separator = SeparatorChoice.Tab }, Ct);

        changed.ShouldNotContain(nameof(TrayViewModel.RestoreClipboard));
    }

    [Fact]
    public void Disposing_stops_following_the_settings()
    {
        var tray = Create();
        var raised = 0;
        tray.PropertyChanged += (_, _) => raised++;

        tray.Dispose();
        _ = _settings.UpdateAsync(settings => settings with { RestorePreviousClipboard = false }, Ct);

        raised.ShouldBe(0);
    }

    [Fact]
    public void Missing_services_are_a_bug()
    {
        var toggle = new CollectToggle(_collect, _notifier);
        Should.Throw<ArgumentNullException>(() => new TrayViewModel(null!, _queue, toggle, _settings));
        Should.Throw<ArgumentNullException>(() => new TrayViewModel(_capture, null!, toggle, _settings));
        Should.Throw<ArgumentNullException>(() => new TrayViewModel(_capture, _queue, null!, _settings));
        Should.Throw<ArgumentNullException>(() => new TrayViewModel(_capture, _queue, toggle, null!));
    }
}
