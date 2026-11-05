using ClipBench.Core.Detection;

namespace ClipBench.Core.Tests.Detection;

public class ColorDetectorTests
{
    [Theory]
    [InlineData("#f00", 255, 0, 0, 255)]
    [InlineData("#FF0000", 255, 0, 0, 255)]
    [InlineData("#ff0000", 255, 0, 0, 255)]
    [InlineData("#1a2B3c", 0x1A, 0x2B, 0x3C, 255)]
    [InlineData("#0f08", 0, 255, 0, 0x88)]
    [InlineData("#ff000080", 255, 0, 0, 0x80)]
    [InlineData("  #00f  ", 0, 0, 255, 255)]
    public void A_hex_color_is_read_in_every_length(string text, int red, int green, int blue, int alpha)
    {
        ColorDetector.TryParse(text).ShouldBe(new ColorSwatch((byte)red, (byte)green, (byte)blue, (byte)alpha));
    }

    [Theory]
    [InlineData("rgb(255, 0, 0)", 255, 0, 0, 255)]
    [InlineData("RGB(12,34,56)", 12, 34, 56, 255)]
    [InlineData("rgb(255 128 0)", 255, 128, 0, 255)]
    [InlineData("rgba(0, 0, 0, 0.5)", 0, 0, 0, 128)]
    [InlineData("rgba(10, 20, 30, 1)", 10, 20, 30, 255)]
    [InlineData("rgba(10, 20, 30, 0)", 10, 20, 30, 0)]
    [InlineData("rgb(255 0 0 / 50%)", 255, 0, 0, 128)]
    [InlineData("rgb(100%, 0%, 0%)", 255, 0, 0, 255)]
    [InlineData("rgb(50%, 50%, 50%)", 128, 128, 128, 255)]
    public void An_rgb_color_is_read_with_commas_or_spaces_and_a_fraction_or_percent_alpha(string text, int red, int green, int blue, int alpha)
    {
        ColorDetector.TryParse(text).ShouldBe(new ColorSwatch((byte)red, (byte)green, (byte)blue, (byte)alpha));
    }

    [Theory]
    [InlineData("hsl(0, 100%, 50%)", 255, 0, 0)]
    [InlineData("hsl(120, 100%, 50%)", 0, 255, 0)]
    [InlineData("hsl(240, 100%, 50%)", 0, 0, 255)]
    [InlineData("hsl(0, 0%, 50%)", 128, 128, 128)]
    [InlineData("hsl(0, 0%, 100%)", 255, 255, 255)]
    [InlineData("hsl(0, 0%, 0%)", 0, 0, 0)]
    [InlineData("hsl(210, 50%, 40%)", 51, 102, 153)]
    [InlineData("hsl(210deg 50% 40%)", 51, 102, 153)]
    [InlineData("HSL(570, 50%, 40%)", 51, 102, 153)]
    [InlineData("hsl(-150, 50%, 40%)", 51, 102, 153)]
    public void An_hsl_color_is_turned_into_rgb_and_the_hue_wraps_around(string text, int red, int green, int blue)
    {
        var swatch = ColorDetector.TryParse(text);

        swatch.ShouldBe(new ColorSwatch((byte)red, (byte)green, (byte)blue, 255));
    }

    [Fact]
    public void An_hsla_color_keeps_its_alpha()
    {
        ColorDetector.TryParse("hsla(0, 100%, 50%, 0.25)").ShouldBe(new ColorSwatch(255, 0, 0, 64));
    }

    [Theory]
    [InlineData("#ff")]
    [InlineData("#fffff")]
    [InlineData("#fffffff")]
    [InlineData("#fffffffff")]
    [InlineData("#ggg")]
    [InlineData("ff0000")]
    [InlineData("#")]
    [InlineData("##f00")]
    [InlineData("#f00 and more")]
    [InlineData("color: #f00")]
    [InlineData("rgb(256, 0, 0)")]
    [InlineData("rgb(-1, 0, 0)")]
    [InlineData("rgb(1, 2)")]
    [InlineData("rgb(1, 2, 3, 4, 5)")]
    [InlineData("rgb(a, b, c)")]
    [InlineData("rgb(1.5, 2, 3)")]
    [InlineData("rgb(1, 2, 3")]
    [InlineData("rgb 1, 2, 3)")]
    [InlineData("rgba(1, 2, 3, 2)")]
    [InlineData("rgba(1, 2, 3, -0.1)")]
    [InlineData("rgb(101%, 0%, 0%)")]
    [InlineData("hsl(0, 100, 50)")]
    [InlineData("hsl(0, 101%, 50%)")]
    [InlineData("hsl(0, 100%, -1%)")]
    [InlineData("hsl(x, 100%, 50%)")]
    [InlineData("hsl(0, 100%)")]
    [InlineData("hsl(NaN, 100%, 50%)")]
    [InlineData("hsl(1e999, 100%, 50%)")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("red")]
    public void Text_that_is_not_exactly_one_color_is_not_a_color(string text)
    {
        ColorDetector.TryParse(text).ShouldBeNull();
    }

    [Fact]
    public void Text_that_is_far_too_long_is_not_looked_at_for_a_color()
    {
        ColorDetector.TryParse("#" + new string('f', 10_000)).ShouldBeNull();
        ColorDetector.TryParse("rgb(" + new string(' ', 10_000) + "1,2,3)").ShouldBeNull();
    }

    [Theory]
    [InlineData(255, 0, 0, 255, "#FF0000")]
    [InlineData(0, 0, 0, 255, "#000000")]
    [InlineData(1, 2, 3, 255, "#010203")]
    [InlineData(255, 0, 0, 128, "#FF000080")]
    public void A_swatch_names_its_color_as_hex_with_the_alpha_only_when_it_is_not_opaque(int red, int green, int blue, int alpha, string hex)
    {
        new ColorSwatch((byte)red, (byte)green, (byte)blue, (byte)alpha).Hex.ShouldBe(hex);
    }

    [Fact]
    public void A_swatch_says_whether_it_is_opaque()
    {
        new ColorSwatch(1, 2, 3, 255).IsOpaque.ShouldBeTrue();
        new ColorSwatch(1, 2, 3, 254).IsOpaque.ShouldBeFalse();
    }

    [Fact]
    public void Missing_text_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => ColorDetector.TryParse(null!));
    }
}
