using ClipBench.Core.Capture;
using ClipBench.Core.Pasting;
using ClipBench.Presentation.Tests.Support;
using ClipBench.Presentation.Tray;

namespace ClipBench.Presentation.Tests.Tray;

public class TrayViewModelTests
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

    private TrayViewModel Create() => new(_capture, _queue);

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

    [Fact]
    public void Missing_services_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new TrayViewModel(null!, _queue));
        Should.Throw<ArgumentNullException>(() => new TrayViewModel(_capture, null!));
    }
}
