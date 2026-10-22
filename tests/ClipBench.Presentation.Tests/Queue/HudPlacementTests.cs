using ClipBench.Core.Screen;
using ClipBench.Presentation.Queue;

namespace ClipBench.Presentation.Tests.Queue;

public class HudPlacementTests
{
    private static ScreenAnchor Anchor(PixelRect workArea, double scale = 1.0) => new(new PixelPoint(10, 10), 0, workArea, scale);

    [Fact]
    public void The_window_sits_in_the_bottom_right_corner_of_the_work_area_with_a_margin()
    {
        var placed = HudPlacement.Compute(Anchor(new PixelRect(0, 0, 1920, 1040)), 260, 44);

        placed.ShouldBe(new PixelRect(1920 - 16 - 260, 1040 - 16 - 44, 260, 44));
    }

    [Fact]
    public void Sizes_and_margin_follow_the_monitor_scale()
    {
        var placed = HudPlacement.Compute(Anchor(new PixelRect(0, 0, 2560, 1400), scale: 1.25), 260, 44);

        placed.Width.ShouldBe(325);
        placed.Height.ShouldBe(55);
        placed.Right.ShouldBe(2560 - 20);
        placed.Bottom.ShouldBe(1400 - 20);
    }

    [Fact]
    public void A_monitor_left_of_or_above_the_main_one_works_with_negative_coordinates()
    {
        var placed = HudPlacement.Compute(Anchor(new PixelRect(-1920, -200, 1920, 1080)), 260, 44);

        placed.ShouldBe(new PixelRect(-16 - 260, 880 - 16 - 44, 260, 44));
    }

    [Fact]
    public void A_work_area_smaller_than_the_window_keeps_it_inside()
    {
        var placed = HudPlacement.Compute(Anchor(new PixelRect(100, 100, 200, 40)), 260, 44);

        placed.ShouldBe(new PixelRect(100, 100, 200, 40));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void A_scale_that_makes_no_sense_counts_as_one(double scale)
    {
        var placed = HudPlacement.Compute(Anchor(new PixelRect(0, 0, 1000, 800), scale), 260, 44);

        placed.Width.ShouldBe(260);
    }
}
