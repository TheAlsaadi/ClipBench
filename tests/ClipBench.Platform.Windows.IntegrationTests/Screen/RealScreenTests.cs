using ClipBench.Core.Pasting;
using ClipBench.Core.Screen;
using ClipBench.Platform.Windows.Pasting;
using ClipBench.Platform.Windows.Screen;

namespace ClipBench.Platform.Windows.IntegrationTests.Screen;

/// <summary>Asks Windows about the real screen. Only things that hold on any machine are checked: nothing here depends on where the pointer is.</summary>
[Trait("Category", "Integration")]
public class RealScreenTests
{
    private readonly Win32ScreenApi _api = new();

    [Fact]
    public void The_pointer_is_on_a_monitor_with_a_usable_area_and_a_sane_scale()
    {
        var monitor = _api.GetMonitor(_api.GetCursor());

        monitor.ShouldNotBeNull();
        monitor.Value.WorkArea.Width.ShouldBeGreaterThan(0);
        monitor.Value.WorkArea.Height.ShouldBeGreaterThan(0);
        monitor.Value.DpiScale.ShouldBeInRange(1.0, 6.0);
    }

    [Fact]
    public void A_point_far_outside_every_monitor_is_given_the_nearest_one()
    {
        var monitor = _api.GetMonitor(new PixelPoint(-200_000, -200_000));

        monitor.ShouldNotBeNull();
        monitor.Value.WorkArea.Width.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_window_that_does_not_exist_has_no_caret()
    {
        _api.GetCaret(0).ShouldBeNull();
        _api.GetCaret(0x7FFF_FFFF).ShouldBeNull();
    }

    [Fact]
    public void Asking_the_window_that_has_focus_for_a_caret_never_throws()
    {
        var foreground = new Win32WindowApi().GetForegroundWindow();

        Should.NotThrow(() => _api.GetCaret(foreground));
    }

    [Fact]
    public void The_locator_reports_the_pointer_where_the_screen_api_does()
    {
        var locator = new ScreenLocator(_api);
        var before = _api.GetCursor();

        var reported = locator.CursorPosition();

        // The pointer may move between the two reads; it is only never far from where it was.
        Math.Abs(reported.X - before.X).ShouldBeLessThan(2_000);
        Math.Abs(reported.Y - before.Y).ShouldBeLessThan(2_000);
    }

    [Fact]
    public void The_locator_gives_a_placeable_anchor_on_the_real_screen()
    {
        var locator = new ScreenLocator(_api);

        var anchor = locator.Locate(new WindowHandle(new Win32WindowApi().GetForegroundWindow()));

        anchor.WorkArea.Width.ShouldBeGreaterThan(0);
        anchor.DpiScale.ShouldBeGreaterThanOrEqualTo(1.0);
    }
}
