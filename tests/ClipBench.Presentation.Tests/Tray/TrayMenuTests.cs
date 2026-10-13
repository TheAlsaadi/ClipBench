using ClipBench.Core.Capture;
using ClipBench.Presentation.Tray;

namespace ClipBench.Presentation.Tests.Tray;

public class TrayMenuTests
{
    private sealed class FakeCapture : ICaptureControl
    {
        public bool IsPaused { get; private set; }

        public void SetPaused(bool paused) => IsPaused = paused;
    }

    private readonly FakeCapture _capture = new();

    [Fact]
    public void The_menu_offers_open_pause_and_exit_with_open_as_the_default()
    {
        var model = TrayMenu.Build(new TrayViewModel(_capture));

        model.Menu.Select(item => (item.Id, item.Text, item.IsDefault)).ShouldBe(
        [
            (1, "Open ClipBench", true),
            (0, string.Empty, false),
            (2, "Pause capturing", false),
            (3, "Exit", false),
        ]);
    }

    [Fact]
    public void The_tool_tip_and_the_pause_item_follow_the_state()
    {
        var tray = new TrayViewModel(_capture);
        tray.TogglePauseCommand.Execute(null);

        var model = TrayMenu.Build(tray);

        model.ToolTip.ShouldBe("ClipBench · paused");
        model.Menu.Single(item => item.Id == TrayMenu.PauseId).Text.ShouldBe("Resume capturing");
    }

    [Fact]
    public void Choosing_each_item_runs_its_command()
    {
        var tray = new TrayViewModel(_capture);
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
    [InlineData(4)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void An_id_that_is_not_ours_does_nothing(int id)
    {
        var tray = new TrayViewModel(_capture);
        var raised = 0;
        tray.OpenRequested += (_, _) => raised++;
        tray.ExitRequested += (_, _) => raised++;

        TrayMenu.Execute(tray, id);

        raised.ShouldBe(0);
        _capture.IsPaused.ShouldBeFalse();
    }
}
