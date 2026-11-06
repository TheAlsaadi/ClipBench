using ClipBench.Core.Screen;

namespace ClipBench.Core.Tests.Screen;

public class ImageScalerTests
{
    private static ScreenImage Solid(int width, int height, byte blue, byte green, byte red)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = blue;
            pixels[i + 1] = green;
            pixels[i + 2] = red;
            pixels[i + 3] = 255;
        }

        return new ScreenImage(width, height, pixels);
    }

    private static (byte B, byte G, byte R, byte A) At(ScreenImage image, int x, int y)
    {
        var index = ((y * image.Width) + x) * 4;
        return (image.Pixels[index], image.Pixels[index + 1], image.Pixels[index + 2], image.Pixels[index + 3]);
    }

    // --- choosing how much to enlarge ---

    [Theory]
    [InlineData(300, 40)]
    [InlineData(1000, 1000)]
    [InlineData(1333, 1000)]
    [InlineData(3333, 10)]
    public void A_picture_whose_threefold_enlargement_fits_is_enlarged_threefold(int width, int height)
    {
        ImageScaler.ChooseFactor(width, height).ShouldBe(3);
    }

    [Theory]
    [InlineData(1334, 1000)]
    [InlineData(1920, 1080)]
    [InlineData(2000, 1500)]
    [InlineData(3000, 1000)]
    [InlineData(3334, 10)]
    public void A_picture_too_large_for_threefold_but_not_twofold_is_enlarged_twofold(int width, int height)
    {
        ImageScaler.ChooseFactor(width, height).ShouldBe(2);
    }

    [Theory]
    [InlineData(3000, 1001)]
    [InlineData(3840, 2160)]
    [InlineData(6000, 3000)]
    [InlineData(5001, 10)]
    [InlineData(10, 5001)]
    public void A_picture_too_large_to_enlarge_is_read_as_it_is(int width, int height)
    {
        ImageScaler.ChooseFactor(width, height).ShouldBe(1);
    }

    [Fact]
    public void The_limits_are_twelve_million_pixels_and_ten_thousand_on_a_side()
    {
        ImageScaler.MaxOutputPixels.ShouldBe(12_000_000);
        ImageScaler.MaxSide.ShouldBe(10_000);
        ImageScaler.ChooseFactor(5000, 10).ShouldBe(2);
        ImageScaler.ChooseFactor(3333, 10).ShouldBe(3);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    public void A_size_with_no_area_is_a_bug(int width, int height)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ImageScaler.ChooseFactor(width, height));
    }

    // --- enlarging ---

    [Fact]
    public void The_result_is_the_size_asked_for_and_the_original_is_not_changed()
    {
        using var source = Solid(3, 2, 10, 20, 30);
        var before = source.Pixels.ToArray();

        using var scaled = ImageScaler.Scale(source, 2);

        scaled.Width.ShouldBe(6);
        scaled.Height.ShouldBe(4);
        scaled.Pixels.Length.ShouldBe(6 * 4 * 4);
        source.Pixels.ShouldBe(before);
        scaled.Pixels.ShouldNotBeSameAs(source.Pixels);
    }

    [Fact]
    public void A_picture_of_one_color_stays_that_color_at_every_pixel()
    {
        using var source = Solid(5, 4, 11, 22, 33);

        using var scaled = ImageScaler.Scale(source, 2);

        for (var y = 0; y < scaled.Height; y++)
        {
            for (var x = 0; x < scaled.Width; x++)
            {
                At(scaled, x, y).ShouldBe(((byte)11, (byte)22, (byte)33, (byte)255));
            }
        }
    }

    [Fact]
    public void The_edges_of_the_picture_keep_their_colors_and_the_inside_is_blended_smoothly()
    {
        // Two pixels side by side: black then white.
        var pixels = new byte[]
        {
            0, 0, 0, 255,
            255, 255, 255, 255,
        };
        using var source = new ScreenImage(2, 1, pixels);

        using var scaled = ImageScaler.Scale(source, 2);

        scaled.Width.ShouldBe(4);
        At(scaled, 0, 0).B.ShouldBe((byte)0);
        At(scaled, 3, 0).B.ShouldBe((byte)255);

        // Smooth: never going down from left to right, and the middle ones are neither black nor white.
        var values = Enumerable.Range(0, 4).Select(x => At(scaled, x, 0).B).ToArray();
        values.ShouldBe([.. values.Order()]);
        values[1].ShouldBeGreaterThan((byte)0);
        values[1].ShouldBeLessThan((byte)255);
        values[2].ShouldBeGreaterThan((byte)0);
        values[2].ShouldBeLessThan((byte)255);
    }

    [Fact]
    public void Each_channel_is_blended_on_its_own_and_alpha_stays_opaque()
    {
        // Blue then red.
        using var source = new ScreenImage(2, 1, [255, 0, 0, 255, 0, 0, 255, 255]);

        using var scaled = ImageScaler.Scale(source, 2);

        At(scaled, 0, 0).ShouldBe(((byte)255, (byte)0, (byte)0, (byte)255));
        At(scaled, 3, 0).ShouldBe(((byte)0, (byte)0, (byte)255, (byte)255));
        for (var x = 0; x < 4; x++)
        {
            At(scaled, x, 0).G.ShouldBe((byte)0);
            At(scaled, x, 0).A.ShouldBe((byte)255);
        }
    }

    [Fact]
    public void A_single_pixel_can_be_enlarged()
    {
        using var source = Solid(1, 1, 5, 6, 7);

        using var scaled = ImageScaler.Scale(source, 3);

        scaled.Width.ShouldBe(3);
        scaled.Height.ShouldBe(3);
        scaled.Pixels.Where((_, i) => i % 4 == 0).ShouldAllBe(value => value == 5);
    }

    [Fact]
    public void Enlarging_by_one_gives_a_separate_copy()
    {
        using var source = Solid(2, 2, 1, 2, 3);

        using var copy = ImageScaler.Scale(source, 1);

        copy.Pixels.ShouldBe(source.Pixels);
        copy.Pixels.ShouldNotBeSameAs(source.Pixels);
    }

    [Fact]
    public void Letting_go_of_the_result_zeroes_it_and_leaves_the_original_alone()
    {
        using var source = Solid(2, 2, 200, 200, 200);
        var scaled = ImageScaler.Scale(source, 2);
        var buffer = scaled.Pixels;

        scaled.Dispose();

        buffer.ShouldAllBe(value => value == 0);
        source.Pixels.Any(value => value != 0).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(9)]
    public void A_factor_outside_one_to_eight_is_a_bug(int factor)
    {
        using var source = Solid(2, 2, 0, 0, 0);

        Should.Throw<ArgumentOutOfRangeException>(() => ImageScaler.Scale(source, factor));
    }

    [Fact]
    public void A_picture_that_has_been_let_go_of_or_is_missing_is_a_bug()
    {
        var source = Solid(2, 2, 0, 0, 0);
        source.Dispose();

        Should.Throw<ObjectDisposedException>(() => ImageScaler.Scale(source, 2));
        Should.Throw<ArgumentNullException>(() => ImageScaler.Scale(null!, 2));
    }

    [Fact]
    public void A_result_that_would_be_too_large_is_a_bug()
    {
        using var source = Solid(4000, 4000, 0, 0, 0);

        Should.Throw<ArgumentOutOfRangeException>(() => ImageScaler.Scale(source, 8));
    }

    // --- the margin ---

    private static ScreenImage Frame(int width, int height, (byte B, byte G, byte R) edge, (byte B, byte G, byte R) inside)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var onEdge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                var color = onEdge ? edge : inside;
                var i = ((y * width) + x) * 4;
                pixels[i] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = 255;
            }
        }

        return new ScreenImage(width, height, pixels);
    }

    [Fact]
    public void A_margin_in_the_color_of_the_edge_is_added_on_every_side_and_the_picture_sits_in_the_middle()
    {
        using var source = Frame(5, 4, (200, 100, 50), (1, 2, 3));

        using var padded = ImageScaler.Pad(source, 3);

        padded.Width.ShouldBe(11);
        padded.Height.ShouldBe(10);
        At(padded, 0, 0).ShouldBe(((byte)200, (byte)100, (byte)50, (byte)255));
        At(padded, 10, 9).ShouldBe(((byte)200, (byte)100, (byte)50, (byte)255));
        At(padded, 5, 1).ShouldBe(((byte)200, (byte)100, (byte)50, (byte)255));
        At(padded, 3, 3).ShouldBe(((byte)200, (byte)100, (byte)50, (byte)255)); // the source's own edge pixel
        At(padded, 4, 4).ShouldBe(((byte)1, (byte)2, (byte)3, (byte)255));
        At(padded, 6, 5).ShouldBe(((byte)1, (byte)2, (byte)3, (byte)255));
        source.Pixels.Length.ShouldBe(5 * 4 * 4);
    }

    [Fact]
    public void Text_touching_a_few_edge_pixels_does_not_change_the_margin_color()
    {
        // A dark terminal background with two light pixels of a letter on the top edge.
        var pixels = new byte[20 * 6 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 12;
            pixels[i + 1] = 12;
            pixels[i + 2] = 12;
            pixels[i + 3] = 255;
        }

        for (var x = 4; x < 6; x++)
        {
            pixels[x * 4] = 230;
            pixels[(x * 4) + 1] = 230;
            pixels[(x * 4) + 2] = 230;
        }

        using var source = new ScreenImage(20, 6, pixels);

        using var padded = ImageScaler.Pad(source, 4);

        At(padded, 0, 0).ShouldBe(((byte)12, (byte)12, (byte)12, (byte)255));
        At(padded, 9, 1).ShouldBe(((byte)12, (byte)12, (byte)12, (byte)255));
    }

    [Fact]
    public void The_margin_is_zeroed_with_the_picture_and_the_original_is_not_changed()
    {
        using var source = Frame(3, 3, (9, 9, 9), (200, 200, 200));
        var before = source.Pixels.ToArray();
        var padded = ImageScaler.Pad(source, 2);
        var buffer = padded.Pixels;

        padded.Dispose();

        buffer.ShouldAllBe(value => value == 0);
        source.Pixels.ShouldBe(before);
    }

    [Fact]
    public void A_margin_that_is_not_positive_or_a_missing_or_released_picture_is_a_bug()
    {
        using var source = Solid(2, 2, 0, 0, 0);
        Should.Throw<ArgumentOutOfRangeException>(() => ImageScaler.Pad(source, 0));
        Should.Throw<ArgumentNullException>(() => ImageScaler.Pad(null!, 2));

        var released = Solid(2, 2, 0, 0, 0);
        released.Dispose();
        Should.Throw<ObjectDisposedException>(() => ImageScaler.Pad(released, 2));
    }

    // --- preparing a picture to be read ---

    [Fact]
    public void A_picture_is_given_a_margin_and_enlarged_threefold_in_one_step()
    {
        using var source = Frame(300, 40, (255, 255, 255), (0, 0, 0));

        using var prepared = ImageScaler.Prepare(source)!;

        prepared.Width.ShouldBe((300 + (2 * ImageScaler.Margin)) * 3);
        prepared.Height.ShouldBe((40 + (2 * ImageScaler.Margin)) * 3);
        At(prepared, 0, 0).ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255));
        source.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public void A_picture_too_large_to_enlarge_still_gets_its_margin()
    {
        using var source = Solid(3840, 2160, 255, 255, 255);

        using var prepared = ImageScaler.Prepare(source)!;

        prepared.Width.ShouldBe(3840 + (2 * ImageScaler.Margin));
        prepared.Height.ShouldBe(2160 + (2 * ImageScaler.Margin));
    }

    [Fact]
    public void A_picture_so_wide_that_a_margin_would_pass_the_limit_is_left_as_it_is()
    {
        using var source = Solid(ImageScaler.MaxSide - 1, 4, 0, 0, 0);

        ImageScaler.Prepare(source).ShouldBeNull();
    }

    [Fact]
    public void Preparing_zeroes_the_picture_with_the_margin_that_is_replaced_by_the_enlargement()
    {
        // Nothing can observe the in-between picture directly; the result holds the only copy and is zeroed with the others.
        using var source = Solid(10, 10, 5, 5, 5);
        var prepared = ImageScaler.Prepare(source)!;
        var buffer = prepared.Pixels;

        prepared.Dispose();

        buffer.ShouldAllBe(value => value == 0);
    }

    [Fact]
    public void A_missing_or_released_picture_cannot_be_prepared()
    {
        Should.Throw<ArgumentNullException>(() => ImageScaler.Prepare(null!));
        var released = Solid(2, 2, 0, 0, 0);
        released.Dispose();
        Should.Throw<ObjectDisposedException>(() => ImageScaler.Prepare(released));
    }

    [Fact]
    public void A_cap_on_the_factor_gives_a_smaller_enlargement_and_the_factor_can_be_told_from_the_result()
    {
        using var source = new ScreenImage(100, 20, new byte[100 * 20 * 4]);

        using var twofold = ImageScaler.Prepare(source, 2)!;
        using var threefold = ImageScaler.Prepare(source)!;

        (twofold.Width, twofold.Height).ShouldBe((232, 72));
        ImageScaler.FactorOf(source, twofold).ShouldBe(2);
        ImageScaler.FactorOf(source, threefold).ShouldBe(3);
        ImageScaler.FactorOf(source, null).ShouldBe(1);
    }

    [Fact]
    public void A_cap_outside_one_to_the_largest_factor_is_a_bug_and_a_missing_picture_too()
    {
        using var source = new ScreenImage(10, 10, new byte[400]);

        Should.Throw<ArgumentOutOfRangeException>(() => ImageScaler.Prepare(source, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => ImageScaler.Prepare(source, ImageScaler.MaxFactor + 1));
        Should.Throw<ArgumentNullException>(() => ImageScaler.FactorOf(null!, null));
    }
}
