using System.Collections.Immutable;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;

namespace ClipBench.Core.Tests.Screen;

public class RegionMathTests
{
    // A main monitor, one to its left (negative coordinates) at another scale, and one above.
    private static readonly MonitorBounds _main = new(new PixelRect(0, 0, 1920, 1080), 1.0);
    private static readonly MonitorBounds _left = new(new PixelRect(-2560, -200, 2560, 1440), 1.5);
    private static readonly MonitorBounds _above = new(new PixelRect(100, -1080, 1920, 1080), 1.25);
    private static readonly ImmutableArray<MonitorBounds> _all = [_main, _left, _above];

    // --- turning a drag into a region ---

    [Fact]
    public void A_drag_down_and_to_the_right_is_the_rectangle_between_its_two_points()
    {
        var region = RegionMath.FromDrag(new PixelPoint(100, 200), new PixelPoint(400, 350), _main.Bounds);

        region.IsSuccess.ShouldBeTrue();
        region.Value.ShouldBe(new PixelRect(100, 200, 300, 150));
    }

    [Theory]
    [InlineData(400, 350, 100, 200)]
    [InlineData(100, 350, 400, 200)]
    [InlineData(400, 200, 100, 350)]
    public void A_drag_in_any_direction_gives_the_same_rectangle(int startX, int startY, int endX, int endY)
    {
        RegionMath.FromDrag(new PixelPoint(startX, startY), new PixelPoint(endX, endY), _main.Bounds).Value
            .ShouldBe(new PixelRect(100, 200, 300, 150));
    }

    [Fact]
    public void On_a_monitor_left_of_the_main_one_the_region_keeps_its_negative_coordinates()
    {
        var region = RegionMath.FromDrag(new PixelPoint(-2000, 100), new PixelPoint(-1500, 400), _left.Bounds);

        region.Value.ShouldBe(new PixelRect(-2000, 100, 500, 300));
    }

    [Fact]
    public void A_drag_that_leaves_the_monitor_is_cut_at_its_edge()
    {
        var region = RegionMath.FromDrag(new PixelPoint(1800, 1000), new PixelPoint(2500, 1500), _main.Bounds);

        region.Value.ShouldBe(new PixelRect(1800, 1000, 120, 80));
    }

    [Fact]
    public void A_drag_that_starts_outside_the_monitor_is_cut_at_the_other_edge()
    {
        var region = RegionMath.FromDrag(new PixelPoint(-300, -300), new PixelPoint(200, 150), _main.Bounds);

        region.Value.ShouldBe(new PixelRect(0, 0, 200, 150));
    }

    [Fact]
    public void A_drag_on_a_monitor_above_the_main_one_works_with_negative_rows()
    {
        var region = RegionMath.FromDrag(new PixelPoint(150, -900), new PixelPoint(450, -700), _above.Bounds);

        region.Value.ShouldBe(new PixelRect(150, -900, 300, 200));
    }

    [Theory]
    [InlineData(100, 100, 100, 100)]
    [InlineData(100, 100, 107, 400)]
    [InlineData(100, 100, 400, 107)]
    public void A_region_smaller_than_the_least_that_can_hold_text_is_refused(int startX, int startY, int endX, int endY)
    {
        var region = RegionMath.FromDrag(new PixelPoint(startX, startY), new PixelPoint(endX, endY), _main.Bounds);

        region.IsFailure.ShouldBeTrue();
        region.Error.Code.ShouldBe(ErrorCode.RegionTooSmall);
        region.Error.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_region_exactly_the_least_size_is_accepted()
    {
        var region = RegionMath.FromDrag(new PixelPoint(10, 10), new PixelPoint(10 + RegionMath.MinSide, 10 + RegionMath.MinSide), _main.Bounds);

        region.IsSuccess.ShouldBeTrue();
        region.Value.Width.ShouldBe(RegionMath.MinSide);
    }

    [Fact]
    public void A_drag_wholly_outside_the_monitor_is_refused()
    {
        var region = RegionMath.FromDrag(new PixelPoint(3000, 100), new PixelPoint(3400, 300), _main.Bounds);

        region.IsFailure.ShouldBeTrue();
        region.Error.Code.ShouldBe(ErrorCode.RegionTooSmall);
    }

    [Fact]
    public void A_region_larger_than_the_most_the_recognizer_reads_is_refused()
    {
        var huge = new PixelRect(0, 0, 20_000, 20_000);

        var region = RegionMath.FromDrag(new PixelPoint(0, 0), new PixelPoint(15_000, 300), huge);

        region.IsFailure.ShouldBeTrue();
        region.Error.Code.ShouldBe(ErrorCode.RegionTooLarge);
    }

    [Fact]
    public void A_region_exactly_the_most_is_accepted()
    {
        var huge = new PixelRect(0, 0, 20_000, 20_000);

        var region = RegionMath.FromDrag(new PixelPoint(0, 0), new PixelPoint(RegionMath.MaxSide, 300), huge);

        region.IsSuccess.ShouldBeTrue();
        region.Value.Width.ShouldBe(RegionMath.MaxSide);
    }

    // --- from window units to device pixels ---

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(10, 20, 10, 20)]
    [InlineData(99.5, 0.5, 100, 1)]
    public void On_a_monitor_at_100_percent_a_point_in_window_units_is_the_same_pixel_offset_from_the_monitors_corner(double x, double y, int pixelX, int pixelY)
    {
        RegionMath.ToPixels(x, y, _main).ShouldBe(new PixelPoint(pixelX, pixelY));
    }

    [Fact]
    public void On_a_monitor_at_150_percent_window_units_are_scaled_and_placed_from_the_monitors_corner()
    {
        // 100 units across and 40 down at 150% is 150 and 60 pixels, from the corner at (-2560, -200).
        RegionMath.ToPixels(100, 40, _left).ShouldBe(new PixelPoint(-2560 + 150, -200 + 60));
    }

    [Fact]
    public void On_a_monitor_at_125_percent_the_result_is_rounded_to_the_nearest_pixel()
    {
        // 33 x 1.25 = 41.25 and 7 x 1.25 = 8.75.
        RegionMath.ToPixels(33, 7, _above).ShouldBe(new PixelPoint(100 + 41, -1080 + 9));
    }

    [Fact]
    public void The_same_physical_spot_gives_the_same_pixel_whichever_way_it_is_reached_on_one_monitor()
    {
        var corner = RegionMath.ToPixels(0, 0, _left);

        corner.ShouldBe(new PixelPoint(_left.Bounds.X, _left.Bounds.Y));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 0)]
    public void A_point_that_is_not_a_finite_number_is_a_bug(double x, double y)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RegionMath.ToPixels(x, y, _main));
    }

    [Fact]
    public void A_monitor_with_a_scale_that_is_not_positive_is_a_bug()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new MonitorBounds(new PixelRect(0, 0, 100, 100), 0));
        Should.Throw<ArgumentOutOfRangeException>(() => new MonitorBounds(new PixelRect(0, 0, 100, 100), -1));
        Should.Throw<ArgumentOutOfRangeException>(() => new MonitorBounds(new PixelRect(0, 0, 100, 100), double.NaN));
    }

    [Fact]
    public void A_monitor_with_no_area_is_a_bug()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new MonitorBounds(new PixelRect(0, 0, 0, 100), 1));
        Should.Throw<ArgumentOutOfRangeException>(() => new MonitorBounds(new PixelRect(0, 0, 100, -5), 1));
    }

    // --- which monitor a point is on ---

    [Theory]
    [InlineData(10, 10, 0)]
    [InlineData(1919, 1079, 0)]
    [InlineData(-1, 0, 1)]
    [InlineData(-2560, -200, 1)]
    [InlineData(100, -1, 2)]
    [InlineData(2019, -1080, 2)]
    public void A_point_is_on_the_monitor_whose_rectangle_holds_it(int x, int y, int expected)
    {
        RegionMath.MonitorAt(_all, new PixelPoint(x, y)).ShouldBe(_all[expected]);
    }

    [Theory]
    [InlineData(1920, 0)]
    [InlineData(0, 1080)]
    [InlineData(-2561, 0)]
    [InlineData(5000, 5000)]
    public void A_point_the_edge_just_outside_a_monitor_or_in_no_monitor_is_on_none(int x, int y)
    {
        RegionMath.MonitorAt(_all, new PixelPoint(x, y)).ShouldBeNull();
    }

    [Fact]
    public void With_no_monitors_a_point_is_on_none()
    {
        RegionMath.MonitorAt([], new PixelPoint(0, 0)).ShouldBeNull();
    }

    // --- the whole walk: a drag in window units on a second monitor ---

    [Fact]
    public void A_drag_in_window_units_on_a_scaled_second_monitor_becomes_the_pixels_to_capture()
    {
        var start = RegionMath.ToPixels(200, 100, _left);
        var end = RegionMath.ToPixels(600, 300, _left);

        var region = RegionMath.FromDrag(start, end, _left.Bounds);

        // 400 x 200 units at 150% is 600 x 300 pixels, starting 300 and 150 pixels in from the monitor's corner.
        region.Value.ShouldBe(new PixelRect(-2560 + 300, -200 + 150, 600, 300));
    }
}
