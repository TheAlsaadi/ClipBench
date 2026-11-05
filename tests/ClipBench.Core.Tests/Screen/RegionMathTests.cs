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

    // --- sizes that follow the scale of a monitor ---

    [Theory]
    [InlineData(2, 1.0, 2)]
    [InlineData(2, 1.25, 3)]
    [InlineData(2, 1.5, 3)]
    [InlineData(2, 2.0, 4)]
    [InlineData(1, 1.25, 1)]
    [InlineData(1, 0.5, 1)]
    [InlineData(0, 2.0, 1)]
    public void A_size_in_units_is_scaled_to_pixels_and_is_never_less_than_one_pixel(int units, double scale, int pixels)
    {
        RegionMath.Scaled(units, scale).ShouldBe(pixels);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_scale_that_is_not_a_positive_number_is_a_bug(double scale)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RegionMath.Scaled(2, scale));
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

    // --- where a selection falls inside a monitor's own window ---

    [Fact]
    public void A_selection_inside_a_monitor_is_given_relative_to_the_monitors_corner()
    {
        RegionMath.ToLocal(new PixelRect(300, 200, 100, 50), new PixelRect(0, 0, 1920, 1080)).ShouldBe(new PixelRect(300, 200, 100, 50));
    }

    [Fact]
    public void On_a_monitor_with_negative_coordinates_the_offset_is_taken_from_its_corner()
    {
        RegionMath.ToLocal(new PixelRect(-2000, 100, 500, 300), _left.Bounds).ShouldBe(new PixelRect(560, 300, 500, 300));
    }

    [Fact]
    public void A_selection_that_reaches_past_the_monitor_is_cut_at_its_edge()
    {
        RegionMath.ToLocal(new PixelRect(1800, 1000, 400, 400), _main.Bounds).ShouldBe(new PixelRect(1800, 1000, 120, 80));
        RegionMath.ToLocal(new PixelRect(-50, -50, 100, 100), _main.Bounds).ShouldBe(new PixelRect(0, 0, 50, 50));
    }

    [Theory]
    [InlineData(2000, 100, 50, 50)]
    [InlineData(-200, 100, 50, 50)]
    [InlineData(100, 1080, 50, 50)]
    [InlineData(100, 100, 0, 50)]
    public void A_selection_on_another_monitor_or_with_no_area_is_not_on_this_one(int x, int y, int width, int height)
    {
        RegionMath.ToLocal(new PixelRect(x, y, width, height), _main.Bounds).ShouldBeNull();
    }

    // --- the whole walk: a drag across the pixels of a second monitor ---

    [Fact]
    public void A_drag_on_a_scaled_second_monitor_gives_the_pixels_to_capture_and_the_part_to_draw()
    {
        var start = new PixelPoint(-2260, -50);
        var end = new PixelPoint(-1660, 250);

        var region = RegionMath.FromDrag(start, end, _left.Bounds);

        region.Value.ShouldBe(new PixelRect(-2260, -50, 600, 300));
        RegionMath.ToLocal(region.Value, _left.Bounds).ShouldBe(new PixelRect(300, 150, 600, 300));
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
}
