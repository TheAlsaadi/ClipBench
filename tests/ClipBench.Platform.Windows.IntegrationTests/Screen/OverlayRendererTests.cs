using ClipBench.Core.Screen;
using ClipBench.Platform.Windows.Screen;

namespace ClipBench.Platform.Windows.IntegrationTests.Screen;

public class OverlayRendererTests
{
    private const int Width = 100;
    private const int Height = 50;

    private static uint[] Draw(PixelRect? selection, int thickness = 2, int width = Width, int height = Height)
    {
        var pixels = new uint[width * height];
        OverlayRenderer.Draw(pixels, width, height, selection, thickness);
        return pixels;
    }

    private static uint At(uint[] pixels, int x, int y, int width = Width) => pixels[(y * width) + x];

    [Fact]
    public void With_nothing_selected_the_whole_monitor_is_dimmed()
    {
        Draw(null).ShouldAllBe(pixel => pixel == OverlayRenderer.Dim);
    }

    [Fact]
    public void The_dim_is_see_through_and_black_so_the_text_underneath_stays_readable()
    {
        // Premultiplied alpha: a black pixel with alpha 0x66 is stored as 0x66000000, and 0x66 is about 40 percent.
        OverlayRenderer.Dim.ShouldBe(0x66000000u);
        (OverlayRenderer.Dim >> 24).ShouldBeLessThan(0x80u);
    }

    [Fact]
    public void The_selected_area_is_left_clear_but_not_fully_transparent_so_the_mouse_still_reaches_the_window()
    {
        var pixels = Draw(new PixelRect(10, 10, 20, 10));

        for (var y = 10; y < 20; y++)
        {
            for (var x = 10; x < 30; x++)
            {
                At(pixels, x, y).ShouldBe(OverlayRenderer.Clear);
            }
        }

        (OverlayRenderer.Clear >> 24).ShouldBeGreaterThan(0u);
        (OverlayRenderer.Clear >> 24).ShouldBeLessThan(0x10u);
    }

    [Fact]
    public void A_border_of_the_given_thickness_is_drawn_just_outside_the_selected_area()
    {
        var pixels = Draw(new PixelRect(10, 10, 20, 10), thickness: 2);

        // Left and right columns, top and bottom rows of the ring, corners included.
        for (var y = 8; y < 22; y++)
        {
            At(pixels, 8, y).ShouldBe(OverlayRenderer.Border);
            At(pixels, 9, y).ShouldBe(OverlayRenderer.Border);
            At(pixels, 30, y).ShouldBe(OverlayRenderer.Border);
            At(pixels, 31, y).ShouldBe(OverlayRenderer.Border);
        }

        for (var x = 8; x < 32; x++)
        {
            At(pixels, x, 8).ShouldBe(OverlayRenderer.Border);
            At(pixels, x, 9).ShouldBe(OverlayRenderer.Border);
            At(pixels, x, 20).ShouldBe(OverlayRenderer.Border);
            At(pixels, x, 21).ShouldBe(OverlayRenderer.Border);
        }
    }

    [Fact]
    public void Everything_beyond_the_border_stays_dimmed()
    {
        var pixels = Draw(new PixelRect(10, 10, 20, 10), thickness: 2);

        At(pixels, 7, 15).ShouldBe(OverlayRenderer.Dim);
        At(pixels, 32, 15).ShouldBe(OverlayRenderer.Dim);
        At(pixels, 20, 7).ShouldBe(OverlayRenderer.Dim);
        At(pixels, 20, 22).ShouldBe(OverlayRenderer.Dim);
        At(pixels, 0, 0).ShouldBe(OverlayRenderer.Dim);
        At(pixels, 99, 49).ShouldBe(OverlayRenderer.Dim);
    }

    [Fact]
    public void A_thicker_border_is_drawn_for_a_larger_scale()
    {
        var pixels = Draw(new PixelRect(20, 20, 20, 10), thickness: 4);

        At(pixels, 16, 25).ShouldBe(OverlayRenderer.Border);
        At(pixels, 15, 25).ShouldBe(OverlayRenderer.Dim);
        At(pixels, 20, 16).ShouldBe(OverlayRenderer.Border);
    }

    [Fact]
    public void A_selection_at_the_edge_of_the_monitor_has_its_border_cut_off_and_nothing_is_written_outside_the_buffer()
    {
        var pixels = Draw(new PixelRect(0, 0, 20, 10), thickness: 2);

        At(pixels, 0, 0).ShouldBe(OverlayRenderer.Clear);
        At(pixels, 20, 5).ShouldBe(OverlayRenderer.Border);
        At(pixels, 5, 10).ShouldBe(OverlayRenderer.Border);
        pixels.Length.ShouldBe(Width * Height);
    }

    [Fact]
    public void A_selection_that_reaches_past_the_monitor_is_cut_at_its_edge()
    {
        var pixels = Draw(new PixelRect(80, 30, 100, 100));

        At(pixels, 99, 49).ShouldBe(OverlayRenderer.Clear);
        At(pixels, 80, 30).ShouldBe(OverlayRenderer.Clear);
        At(pixels, 79, 40).ShouldBe(OverlayRenderer.Border);
    }

    [Theory]
    [InlineData(500, 500, 10, 10)]
    [InlineData(-50, -50, 10, 10)]
    [InlineData(10, 10, 0, 10)]
    [InlineData(10, 10, 10, 0)]
    [InlineData(10, 10, -5, 10)]
    public void A_selection_with_no_area_or_wholly_outside_the_monitor_draws_nothing_but_the_dim(int x, int y, int width, int height)
    {
        Draw(new PixelRect(x, y, width, height)).ShouldAllBe(pixel => pixel == OverlayRenderer.Dim);
    }

    [Fact]
    public void Drawing_again_into_the_same_buffer_replaces_what_was_there()
    {
        var pixels = new uint[Width * Height];
        OverlayRenderer.Draw(pixels, Width, Height, new PixelRect(10, 10, 20, 10), 2);

        OverlayRenderer.Draw(pixels, Width, Height, null, 2);

        pixels.ShouldAllBe(pixel => pixel == OverlayRenderer.Dim);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_border_thinner_than_a_pixel_is_a_bug(int thickness)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => OverlayRenderer.Draw(new uint[Width * Height], Width, Height, null, thickness));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    public void A_buffer_with_no_size_is_a_bug(int width, int height)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => OverlayRenderer.Draw(new uint[10], width, height, null, 2));
    }

    [Fact]
    public void A_buffer_of_the_wrong_length_is_a_bug()
    {
        Should.Throw<ArgumentException>(() => OverlayRenderer.Draw(new uint[Width * Height - 1], Width, Height, null, 2));
    }
}
