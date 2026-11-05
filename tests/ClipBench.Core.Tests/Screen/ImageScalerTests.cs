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
    [InlineData(1920, 1080)]
    [InlineData(2000, 1500)]
    public void A_picture_that_fits_is_enlarged_twofold(int width, int height)
    {
        ImageScaler.ChooseFactor(width, height).ShouldBe(2);
    }

    [Fact]
    public void The_largest_picture_that_is_enlarged_is_the_one_whose_result_holds_twelve_million_pixels()
    {
        ImageScaler.ChooseFactor(3000, 1000).ShouldBe(2);
        ImageScaler.ChooseFactor(3000, 1001).ShouldBe(1);
        ImageScaler.MaxOutputPixels.ShouldBe(12_000_000);
    }

    [Theory]
    [InlineData(3840, 2160)]
    [InlineData(6000, 3000)]
    public void A_picture_too_large_to_enlarge_is_read_as_it_is(int width, int height)
    {
        ImageScaler.ChooseFactor(width, height).ShouldBe(1);
    }

    [Fact]
    public void A_picture_whose_enlargement_would_be_longer_than_the_recognizer_reads_is_left_alone()
    {
        ImageScaler.ChooseFactor(5001, 10).ShouldBe(1);
        ImageScaler.ChooseFactor(5000, 10).ShouldBe(2);
        ImageScaler.ChooseFactor(10, 5001).ShouldBe(1);
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
}
