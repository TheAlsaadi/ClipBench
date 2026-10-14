using ClipBench.Core.Pasting;
using ClipBench.Core.Screen;
using ClipBench.Platform.Windows.Screen;

namespace ClipBench.Platform.Windows.IntegrationTests.Screen;

public class ScreenLocatorTests
{
    private sealed class FakeScreen : IScreenApi
    {
        public CaretInfo? Caret { get; set; }

        public PixelPoint Cursor { get; set; } = new(700, 400);

        public MonitorInfo? Monitor { get; set; } = new(new PixelRect(0, 0, 1920, 1040), 1.25);

        public List<nint> CaretAskedFor { get; } = [];

        public List<PixelPoint> MonitorAskedFor { get; } = [];

        public CaretInfo? GetCaret(nint window)
        {
            CaretAskedFor.Add(window);
            return Caret;
        }

        public PixelPoint GetCursor() => Cursor;

        public MonitorInfo? GetMonitor(PixelPoint point)
        {
            MonitorAskedFor.Add(point);
            return Monitor;
        }
    }

    private readonly FakeScreen _screen = new();

    private ScreenLocator Locator => new(_screen);

    [Fact]
    public void A_window_that_shows_a_caret_anchors_the_popup_at_the_caret()
    {
        _screen.Caret = new CaretInfo(new PixelPoint(300, 200), 18);

        var anchor = Locator.Locate(new WindowHandle(0x55));

        anchor.Point.ShouldBe(new PixelPoint(300, 200));
        anchor.Height.ShouldBe(18);
        _screen.CaretAskedFor.ShouldBe([0x55]);
    }

    [Fact]
    public void A_window_without_a_caret_anchors_the_popup_at_the_pointer()
    {
        _screen.Caret = null;

        var anchor = Locator.Locate(new WindowHandle(0x55));

        anchor.Point.ShouldBe(new PixelPoint(700, 400));
        anchor.Height.ShouldBe(0);
    }

    [Fact]
    public void A_caret_with_no_height_is_not_a_caret()
    {
        _screen.Caret = new CaretInfo(new PixelPoint(0, 0), 0);

        var anchor = Locator.Locate(new WindowHandle(0x55));

        anchor.Point.ShouldBe(new PixelPoint(700, 400));
        anchor.Height.ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void With_no_window_on_record_the_caret_is_not_asked_for(long? window)
    {
        _screen.Caret = new CaretInfo(new PixelPoint(300, 200), 18);

        var anchor = Locator.Locate(window is null ? null : new WindowHandle(window.Value));

        anchor.Point.ShouldBe(new PixelPoint(700, 400));
        _screen.CaretAskedFor.ShouldBeEmpty();
    }

    [Fact]
    public void The_monitor_is_the_one_that_holds_the_anchor_point()
    {
        _screen.Caret = new CaretInfo(new PixelPoint(300, 200), 18);

        var anchor = Locator.Locate(new WindowHandle(0x55));

        _screen.MonitorAskedFor.ShouldBe([new PixelPoint(300, 200)]);
        anchor.WorkArea.ShouldBe(new PixelRect(0, 0, 1920, 1040));
        anchor.DpiScale.ShouldBe(1.25);
    }

    [Fact]
    public void A_monitor_that_cannot_be_found_falls_back_to_a_full_hd_area_at_normal_scale()
    {
        _screen.Monitor = null;

        var anchor = Locator.Locate(null);

        anchor.WorkArea.ShouldBe(new PixelRect(0, 0, 1920, 1080));
        anchor.DpiScale.ShouldBe(1.0);
    }

    [Theory]
    [InlineData(0, 1040, 1.0)]
    [InlineData(1920, 0, 1.0)]
    [InlineData(-5, 1040, 1.0)]
    public void A_work_area_with_no_size_counts_as_not_found(int width, int height, double scale)
    {
        _screen.Monitor = new MonitorInfo(new PixelRect(0, 0, width, height), scale);

        Locator.Locate(null).WorkArea.ShouldBe(new PixelRect(0, 0, 1920, 1080));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    [InlineData(double.NaN)]
    public void A_scale_that_makes_no_sense_counts_as_one(double scale)
    {
        _screen.Monitor = new MonitorInfo(new PixelRect(0, 0, 1920, 1040), scale);

        Locator.Locate(null).DpiScale.ShouldBe(1.0);
    }
}
