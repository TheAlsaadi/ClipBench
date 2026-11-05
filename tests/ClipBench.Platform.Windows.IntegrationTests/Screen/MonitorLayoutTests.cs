using System.Collections.Immutable;
using ClipBench.Core.Screen;
using ClipBench.Platform.Windows.Screen;

namespace ClipBench.Platform.Windows.IntegrationTests.Screen;

public class MonitorLayoutTests
{
    private sealed class FakeScreen(ImmutableArray<MonitorBounds> monitors) : IScreenApi
    {
        public CaretInfo? GetCaret(nint window) => null;

        public PixelPoint GetCursor() => new(0, 0);

        public MonitorInfo? GetMonitor(PixelPoint point) => null;

        public ImmutableArray<MonitorBounds> GetMonitors() => monitors;
    }

    [Fact]
    public void The_layout_lists_every_monitor_with_its_place_and_scale()
    {
        MonitorBounds[] monitors =
        [
            new(new PixelRect(0, 0, 1920, 1080), 1.0),
            new(new PixelRect(-2560, -200, 2560, 1440), 1.5),
        ];

        var layout = new MonitorLayout(new FakeScreen([.. monitors]));

        layout.GetMonitors().ShouldBe(monitors);
    }

    [Fact]
    public void With_no_monitors_reported_the_layout_is_empty_and_the_overlay_can_say_so()
    {
        new MonitorLayout(new FakeScreen([])).GetMonitors().ShouldBeEmpty();
    }

    [Fact]
    public void A_missing_screen_api_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new MonitorLayout(null!));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void The_real_layout_has_at_least_the_main_monitor_with_a_usable_size_and_scale()
    {
        var monitors = new MonitorLayout(new Win32ScreenApi()).GetMonitors();

        monitors.ShouldNotBeEmpty();
        monitors.ShouldAllBe(monitor => monitor.Bounds.Width > 0 && monitor.Bounds.Height > 0 && monitor.DpiScale > 0);
        monitors.Count(monitor => monitor.Bounds.X == 0 && monitor.Bounds.Y == 0).ShouldBe(1, "exactly one monitor has its corner at the origin: the main one");
    }
}
