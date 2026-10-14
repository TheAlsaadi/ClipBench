using ClipBench.Core.Screen;
using ClipBench.Presentation.Popup;

namespace ClipBench.Presentation.Tests.Popup;

public class PopupPlacementTests
{
    private static readonly PixelRect _screen = new(0, 0, 1920, 1040);

    private static ScreenAnchor Anchor(int x, int y, int height = 0, PixelRect? workArea = null, double scale = 1.0) =>
        new(new PixelPoint(x, y), height, workArea ?? _screen, scale);

    [Fact]
    public void The_popup_opens_below_the_caret_with_a_small_gap()
    {
        var placed = PopupPlacement.Compute(Anchor(500, 300, height: 20), 420, 520);

        placed.ShouldBe(new PixelRect(500, 324, 420, 520));
    }

    [Fact]
    public void The_popup_opens_just_below_the_pointer_when_there_is_no_caret()
    {
        var placed = PopupPlacement.Compute(Anchor(500, 300), 420, 520);

        placed.ShouldBe(new PixelRect(500, 304, 420, 520));
    }

    [Fact]
    public void Near_the_right_edge_the_popup_is_moved_left_to_fit()
    {
        var placed = PopupPlacement.Compute(Anchor(1800, 300, height: 20), 420, 520);

        placed.X.ShouldBe(1920 - 420);
        placed.Right.ShouldBe(1920);
    }

    [Fact]
    public void Near_the_bottom_the_popup_opens_above_the_caret()
    {
        var placed = PopupPlacement.Compute(Anchor(500, 800, height: 20), 420, 520);

        placed.Y.ShouldBe(800 - 4 - 520);
        placed.Bottom.ShouldBe(796);
    }

    [Fact]
    public void When_it_fits_neither_above_nor_below_it_sits_at_the_bottom_of_the_work_area()
    {
        var work = new PixelRect(0, 0, 1920, 600);

        var placed = PopupPlacement.Compute(Anchor(500, 100, height: 20, workArea: work), 420, 520);

        placed.ShouldBe(new PixelRect(500, 80, 420, 520));
    }

    [Fact]
    public void A_popup_taller_than_the_work_area_is_shrunk_to_fit()
    {
        var work = new PixelRect(0, 0, 1920, 400);

        var placed = PopupPlacement.Compute(Anchor(500, 100, height: 20, workArea: work), 420, 520);

        placed.Y.ShouldBe(0);
        placed.Height.ShouldBe(400);
    }

    [Fact]
    public void A_popup_wider_than_the_work_area_is_shrunk_to_fit()
    {
        var work = new PixelRect(0, 0, 300, 1040);

        var placed = PopupPlacement.Compute(Anchor(100, 100, workArea: work), 420, 520);

        placed.X.ShouldBe(0);
        placed.Width.ShouldBe(300);
    }

    [Fact]
    public void Sizes_grow_with_the_scale_of_the_monitor()
    {
        var work = new PixelRect(0, 0, 2880, 1680);

        var placed = PopupPlacement.Compute(Anchor(500, 300, height: 30, workArea: work, scale: 1.5), 420, 520);

        placed.Width.ShouldBe(630);
        placed.Height.ShouldBe(780);
        placed.Y.ShouldBe(300 + 30 + 4);
    }

    [Fact]
    public void A_monitor_left_of_the_main_one_has_negative_coordinates_and_the_popup_stays_on_it()
    {
        var left = new PixelRect(-1920, 0, 1920, 1040);

        var placed = PopupPlacement.Compute(Anchor(-100, 500, workArea: left), 420, 520);

        placed.X.ShouldBe(-420);
        placed.Right.ShouldBe(0);
        placed.Y.ShouldBe(504);
    }

    [Fact]
    public void A_monitor_above_the_main_one_has_negative_rows()
    {
        var above = new PixelRect(0, -1080, 1920, 1040);

        var placed = PopupPlacement.Compute(Anchor(500, -100, height: 20, workArea: above), 420, 520);

        placed.Bottom.ShouldBeLessThanOrEqualTo(above.Bottom);
        placed.Y.ShouldBeGreaterThanOrEqualTo(above.Y);
        placed.Y.ShouldBe(-100 - 4 - 520);
    }

    [Fact]
    public void An_anchor_left_of_the_work_area_is_pulled_in()
    {
        var placed = PopupPlacement.Compute(Anchor(-50, 300), 420, 520);

        placed.X.ShouldBe(0);
    }

    [Fact]
    public void An_anchor_above_the_work_area_is_pulled_in()
    {
        var placed = PopupPlacement.Compute(Anchor(500, -50), 420, 520);

        placed.Y.ShouldBe(0);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_scale_that_makes_no_sense_counts_as_one(double scale)
    {
        var placed = PopupPlacement.Compute(Anchor(500, 300, scale: scale), 420, 520);

        placed.Width.ShouldBe(420);
        placed.Height.ShouldBe(520);
    }

    [Fact]
    public void Sizes_are_rounded_to_whole_pixels()
    {
        var placed = PopupPlacement.Compute(Anchor(0, 0), 100.5, 200.4);

        placed.Width.ShouldBe(101);
        placed.Height.ShouldBe(200);
    }

    [Fact]
    public void The_rectangle_always_lies_inside_the_work_area()
    {
        var work = new PixelRect(-1920, -200, 1920, 1040);
        foreach (var x in new[] { -3000, -1920, -1500, -400, -1, 0, 500 })
        {
            foreach (var y in new[] { -500, -200, 0, 300, 700, 839, 840, 2000 })
            {
                foreach (var height in new[] { 0, 20 })
                {
                    var placed = PopupPlacement.Compute(Anchor(x, y, height, work), 420, 520);

                    placed.X.ShouldBeGreaterThanOrEqualTo(work.X, $"anchor ({x},{y},{height})");
                    placed.Y.ShouldBeGreaterThanOrEqualTo(work.Y, $"anchor ({x},{y},{height})");
                    placed.Right.ShouldBeLessThanOrEqualTo(work.Right, $"anchor ({x},{y},{height})");
                    placed.Bottom.ShouldBeLessThanOrEqualTo(work.Bottom, $"anchor ({x},{y},{height})");
                }
            }
        }
    }
}
