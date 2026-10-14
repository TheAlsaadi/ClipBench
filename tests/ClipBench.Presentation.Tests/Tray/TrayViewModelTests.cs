using ClipBench.Core.Capture;
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
        var tray = new TrayViewModel(_capture);

        tray.IsPaused.ShouldBeFalse();
        tray.PauseLabel.ShouldBe("Pause capturing");
        tray.ToolTip.ShouldBe("ClipBench · capturing");
    }

    [Fact]
    public void It_starts_paused_when_capture_already_is()
    {
        _capture.SetPaused(true);
        _capture.Calls.Clear();

        var tray = new TrayViewModel(_capture);

        tray.IsPaused.ShouldBeTrue();
        tray.PauseLabel.ShouldBe("Resume capturing");
        tray.ToolTip.ShouldBe("ClipBench · paused");
        _capture.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Choosing_pause_pauses_capture_and_updates_the_labels()
    {
        var tray = new TrayViewModel(_capture);

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
        var tray = new TrayViewModel(_capture);
        tray.TogglePauseCommand.Execute(null);

        tray.TogglePauseCommand.Execute(null);

        _capture.Calls.ShouldBe([true, false]);
        tray.IsPaused.ShouldBeFalse();
        tray.PauseLabel.ShouldBe("Pause capturing");
    }

    [Fact]
    public void Open_asks_for_the_popup()
    {
        var tray = new TrayViewModel(_capture);
        var opened = 0;
        tray.OpenRequested += (_, _) => opened++;

        tray.OpenCommand.Execute(null);

        opened.ShouldBe(1);
    }

    [Fact]
    public void Exit_asks_to_quit_once_however_often_it_is_chosen()
    {
        var tray = new TrayViewModel(_capture);
        var exits = 0;
        tray.ExitRequested += (_, _) => exits++;

        tray.ExitCommand.Execute(null);
        tray.ExitCommand.Execute(null);

        exits.ShouldBe(1);
    }
}
