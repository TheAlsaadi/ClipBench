using System.Buffers.Binary;
using System.Diagnostics;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Formats;
using ClipBench.Platform.Windows.IntegrationTests.Support;

namespace ClipBench.Platform.Windows.IntegrationTests.Formats;

public class DibTests
{
    private const uint StandardRed = 0x00FF0000;
    private const uint StandardGreen = 0x0000FF00;
    private const uint StandardBlue = 0x000000FF;
    private const uint StandardAlpha = 0xFF000000;

    private static RawImage ToImage(byte[] dib)
    {
        var png = Dib.ToPng(dib);
        png.IsSuccess.ShouldBeTrue(png.IsFailure ? png.Error.Message : string.Empty);
        return Png.Decode(png.Value).Value;
    }

    private static byte[] Row(params byte[] bytes) => bytes;

    // A 2 x 2 picture used by several tests: red and green on top, blue and white below.
    private static readonly byte[] _redGreenBlueWhite = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255];

    // --- 24 bits ---

    [Fact]
    public void A_24_bit_bitmap_becomes_a_picture_with_red_and_blue_the_right_way_round()
    {
        var dib = DibBuilder.Join(
            DibBuilder.InfoHeader(2, 2, 24),
            DibBuilder.Rows([Row(0, 0, 255, 0, 255, 0), Row(255, 0, 0, 255, 255, 255)]));   // stored blue, green, red

        var image = ToImage(dib);

        image.Width.ShouldBe(2);
        image.Height.ShouldBe(2);
        image.Channels.ShouldBe(3);
        image.Pixels.ShouldBe(_redGreenBlueWhite);
    }

    [Fact]
    public void A_top_down_bitmap_is_read_from_the_top()
    {
        var dib = DibBuilder.Join(
            DibBuilder.InfoHeader(2, -2, 24),
            DibBuilder.Rows([Row(0, 0, 255, 0, 255, 0), Row(255, 0, 0, 255, 255, 255)], bottomUp: false));

        ToImage(dib).Pixels.ShouldBe(_redGreenBlueWhite);
    }

    [Fact]
    public void The_padding_at_the_end_of_each_row_is_skipped()
    {
        // 3 pixels of 3 bytes is 9 bytes, padded to 12.
        var dib = DibBuilder.Join(
            DibBuilder.InfoHeader(3, 2, 24),
            DibBuilder.Rows([Row(0, 0, 1, 0, 0, 2, 0, 0, 3), Row(0, 0, 4, 0, 0, 5, 0, 0, 6)]));

        var image = ToImage(dib);

        image.Pixels.ShouldBe([1, 0, 0, 2, 0, 0, 3, 0, 0, 4, 0, 0, 5, 0, 0, 6, 0, 0]);
    }

    // --- 32 bits and transparency ---

    [Theory]
    [InlineData(0)]
    [InlineData(0x7F)]
    public void In_a_plain_32_bit_bitmap_the_fourth_byte_is_unused_and_the_picture_is_opaque(byte unused)
    {
        // Screenshots are stored this way with the fourth byte zero, which must not make them invisible.
        var dib = DibBuilder.Join(
            DibBuilder.InfoHeader(2, 1, 32),
            DibBuilder.Rows([Row(10, 20, 30, unused, 40, 50, 60, unused)]));

        var image = ToImage(dib);

        image.Channels.ShouldBe(3);
        image.Pixels.ShouldBe([30, 20, 10, 60, 50, 40]);
    }

    [Fact]
    public void A_32_bit_bitmap_with_an_alpha_mask_keeps_its_transparency()
    {
        var dib = DibBuilder.Join(
            DibBuilder.ExtendedHeader(124, 2, 1, 32, StandardRed, StandardGreen, StandardBlue, StandardAlpha),
            DibBuilder.Rows([Row(10, 20, 30, 128, 40, 50, 60, 255)]));

        var image = ToImage(dib);

        image.Channels.ShouldBe(4);
        image.Pixels.ShouldBe([30, 20, 10, 128, 60, 50, 40, 255]);
    }

    [Fact]
    public void An_alpha_mask_whose_values_are_all_zero_means_the_alpha_is_unused()
    {
        var dib = DibBuilder.Join(
            DibBuilder.ExtendedHeader(124, 2, 1, 32, StandardRed, StandardGreen, StandardBlue, StandardAlpha),
            DibBuilder.Rows([Row(10, 20, 30, 0, 40, 50, 60, 0)]));

        var image = ToImage(dib);

        image.Channels.ShouldBe(3);
        image.Pixels.ShouldBe([30, 20, 10, 60, 50, 40]);
    }

    [Fact]
    public void A_picture_that_is_transparent_in_only_some_places_keeps_its_alpha()
    {
        var dib = DibBuilder.Join(
            DibBuilder.ExtendedHeader(108, 2, 1, 32, StandardRed, StandardGreen, StandardBlue, StandardAlpha),
            DibBuilder.Rows([Row(1, 2, 3, 0, 4, 5, 6, 200)]));

        var image = ToImage(dib);

        image.Channels.ShouldBe(4);
        image.Pixels.ShouldBe([3, 2, 1, 0, 6, 5, 4, 200]);
    }

    [Fact]
    public void Masks_after_a_40_byte_header_are_honoured_and_give_an_opaque_picture()
    {
        // The red channel in the top byte instead of the usual third.
        var dib = DibBuilder.Join(
            DibBuilder.InfoHeader(1, 1, 32, DibBuilder.BiBitfields),
            DibBuilder.Masks(0xFF000000, 0x00FF0000, 0x0000FF00),
            Row(7, 8, 9, 10));   // bytes in memory: 7 = bits 0-7 (unused), 8 = blue, 9 = green, 10 = red

        var image = ToImage(dib);

        image.Channels.ShouldBe(3);
        image.Pixels.ShouldBe([10, 9, 8]);
    }

    [Fact]
    public void Masks_that_put_the_alpha_in_the_low_byte_are_honoured()
    {
        var dib = DibBuilder.Join(
            DibBuilder.ExtendedHeader(124, 1, 1, 32, 0xFF000000, 0x00FF0000, 0x0000FF00, 0x000000FF),
            Row(77, 30, 20, 10));   // alpha, blue, green, red

        var image = ToImage(dib);

        image.Channels.ShouldBe(4);
        image.Pixels.ShouldBe([10, 20, 30, 77]);
    }

    // --- 16 bits ---

    [Fact]
    public void A_plain_16_bit_bitmap_uses_five_bits_for_each_colour()
    {
        // 0x7C00 is full red, 0x03E0 full green, 0x001F full blue, 0x0000 black, 0x7FFF white.
        var dib = DibBuilder.Join(
            DibBuilder.InfoHeader(5, 1, 16),
            DibBuilder.Rows([Row(0x00, 0x7C, 0xE0, 0x03, 0x1F, 0x00, 0x00, 0x00, 0xFF, 0x7F)]));

        ToImage(dib).Pixels.ShouldBe([255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255]);
    }

    [Fact]
    public void A_16_bit_bitmap_with_565_masks_uses_six_bits_for_green()
    {
        var dib = DibBuilder.Join(
            DibBuilder.InfoHeader(3, 1, 16, DibBuilder.BiBitfields),
            DibBuilder.Masks(0xF800, 0x07E0, 0x001F),
            DibBuilder.Rows([Row(0x00, 0xF8, 0xE0, 0x07, 0x10, 0x00)]));   // full red, full green, half blue

        var pixels = ToImage(dib).Pixels;

        pixels.AsSpan(0, 6).ToArray().ShouldBe([255, 0, 0, 0, 255, 0]);
        pixels[8].ShouldBe((byte)((16 * 255 + 15) / 31), "half of five bits, rounded to the nearest of 256 levels");
    }

    // --- palettes ---

    [Fact]
    public void An_8_bit_bitmap_looks_its_colours_up_in_the_palette()
    {
        var dib = DibBuilder.Join(
            DibBuilder.InfoHeader(3, 1, 8, clrUsed: 3),
            DibBuilder.Palette((255, 0, 0), (0, 255, 0), (0, 0, 255)),
            DibBuilder.Rows([Row(2, 0, 1)]));

        ToImage(dib).Pixels.ShouldBe([0, 0, 255, 255, 0, 0, 0, 255, 0]);
    }

    [Fact]
    public void A_colour_number_past_the_end_of_the_palette_is_black_and_not_an_error()
    {
        var dib = DibBuilder.Join(
            DibBuilder.InfoHeader(2, 1, 8, clrUsed: 2),
            DibBuilder.Palette((255, 255, 255), (1, 2, 3)),
            DibBuilder.Rows([Row(1, 200)]));

        ToImage(dib).Pixels.ShouldBe([1, 2, 3, 0, 0, 0]);
    }

    [Fact]
    public void A_4_bit_bitmap_holds_two_pixels_in_each_byte_high_half_first()
    {
        var dib = DibBuilder.Join(
            DibBuilder.InfoHeader(3, 1, 4),   // no colour count: all 16 entries are present
            DibBuilder.Palette([.. Enumerable.Range(0, 16).Select(i => ((byte)(i * 10), (byte)(i * 10 + 1), (byte)(i * 10 + 2)))]),
            DibBuilder.Rows([Row(0x12, 0x30)]));   // pixels 1, 2, 3

        ToImage(dib).Pixels.ShouldBe([10, 11, 12, 20, 21, 22, 30, 31, 32]);
    }

    [Fact]
    public void A_1_bit_bitmap_holds_eight_pixels_in_each_byte_leftmost_first()
    {
        var dib = DibBuilder.Join(
            DibBuilder.InfoHeader(9, 1, 1),
            DibBuilder.Palette((0, 0, 0), (255, 255, 255)),
            DibBuilder.Rows([Row(0b1010_0000, 0b1000_0000)]));

        var pixels = ToImage(dib).Pixels;

        pixels.Where((_, index) => index % 3 == 0).ToArray().ShouldBe([255, 0, 255, 0, 0, 0, 0, 0, 255]);
    }

    [Fact]
    public void A_row_of_33_one_bit_pixels_takes_eight_bytes_because_rows_are_padded_to_four()
    {
        // 33 bits is one bit more than a 4-byte group holds, so each row is 5 bytes of pixels and 3 of padding.
        var top = new byte[5];
        var bottom = new byte[5];
        top[0] = 0x80;       // first pixel white
        bottom[4] = 0x80;    // 33rd pixel white
        var dib = DibBuilder.Join(
            DibBuilder.InfoHeader(33, 2, 1),
            DibBuilder.Palette((0, 0, 0), (255, 255, 255)),
            DibBuilder.Rows([top, bottom]));

        var reds = ToImage(dib).Pixels.Where((_, index) => index % 3 == 0).ToArray();

        reds.Take(33).ShouldBe([255, .. new byte[32]]);
        reds.Skip(33).ShouldBe([.. new byte[32], 255]);
    }

    // --- the three header sizes ---

    [Theory]
    [InlineData(108)]
    [InlineData(124)]
    public void The_larger_headers_are_read_like_the_small_one(int headerSize)
    {
        var dib = DibBuilder.Join(
            DibBuilder.ExtendedHeader(headerSize, 2, 2, 24, 0, 0, 0, 0, compression: DibBuilder.BiRgb),
            DibBuilder.Rows([Row(0, 0, 255, 0, 255, 0), Row(255, 0, 0, 255, 255, 255)]));

        ToImage(dib).Pixels.ShouldBe(_redGreenBlueWhite);
    }

    // --- refusing what is damaged ---

    private static byte[] WithHeaderField(byte[] dib, int offset, int value, int size = 4)
    {
        var copy = (byte[])dib.Clone();
        if (size == 4)
        {
            BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(offset, 4), value);
        }
        else
        {
            BinaryPrimitives.WriteInt16LittleEndian(copy.AsSpan(offset, 2), (short)value);
        }

        return copy;
    }

    public static TheoryData<string, byte[]> Refused => new()
    {
        { "empty", [] },
        { "shorter than a header", new byte[39] },
        { "an old OS/2 header", WithHeaderField(DibBuilder.Good24(), 0, 12) },
        { "a header size nobody uses", WithHeaderField(DibBuilder.Good24(), 0, 41) },
        { "a header size of zero", WithHeaderField(DibBuilder.Good24(), 0, 0) },
        { "a header larger than the data", WithHeaderField(DibBuilder.Good24(), 0, 124) },
        { "a version 5 header cut off before its masks end", DibBuilder.ExtendedHeader(124, 1, 1, 32, StandardRed, StandardGreen, StandardBlue, StandardAlpha)[..50] },
        { "a width of zero", WithHeaderField(DibBuilder.Good24(), 4, 0) },
        { "a negative width", WithHeaderField(DibBuilder.Good24(), 4, -2) },
        { "a height of zero", WithHeaderField(DibBuilder.Good24(), 8, 0) },
        { "the most negative height", WithHeaderField(DibBuilder.Good24(), 8, int.MinValue) },
        { "two colour planes", WithHeaderField(DibBuilder.Good24(), 12, 2, size: 2) },
        { "zero bits per pixel", WithHeaderField(DibBuilder.Good24(), 14, 0, size: 2) },
        { "seven bits per pixel", WithHeaderField(DibBuilder.Good24(), 14, 7, size: 2) },
        { "48 bits per pixel", WithHeaderField(DibBuilder.Good24(), 14, 48, size: 2) },
        { "run-length compression (8 bit)", WithHeaderField(DibBuilder.Good24(), 16, 1) },
        { "run-length compression (4 bit)", WithHeaderField(DibBuilder.Good24(), 16, 2) },
        { "an embedded JPEG", WithHeaderField(DibBuilder.Good24(), 16, 4) },
        { "an embedded PNG", WithHeaderField(DibBuilder.Good24(), 16, 5) },
        { "bit fields with a separate alpha mask", WithHeaderField(DibBuilder.Good24(), 16, 6) },
        { "less pixel data than the header promises", DibBuilder.Good24(2, 2)[..^3] },
        { "a width that needs more data than there is", WithHeaderField(DibBuilder.Good24(2, 2), 4, 3) },
        { "no palette data for a palette bitmap", DibBuilder.Join(DibBuilder.InfoHeader(2, 1, 8, clrUsed: 4), DibBuilder.Rows([Row(0, 1)])) },
        { "more colours than the bit depth allows", DibBuilder.Join(DibBuilder.InfoHeader(2, 1, 1, clrUsed: 3), DibBuilder.Palette((0, 0, 0), (1, 1, 1), (2, 2, 2)), DibBuilder.Rows([Row(0)])) },
        { "a negative colour count", DibBuilder.Join(DibBuilder.InfoHeader(2, 1, 8, clrUsed: -1), DibBuilder.Rows([Row(0, 1)])) },
        { "a red mask of zero", DibBuilder.Join(DibBuilder.InfoHeader(1, 1, 32, DibBuilder.BiBitfields), DibBuilder.Masks(0, StandardGreen, StandardBlue), Row(1, 2, 3, 4)) },
        { "a mask that is not one run of bits", DibBuilder.Join(DibBuilder.InfoHeader(1, 1, 32, DibBuilder.BiBitfields), DibBuilder.Masks(0x00FF00FF, StandardGreen, StandardBlue), Row(1, 2, 3, 4)) },
        { "a 16 bit mask wider than 16 bits", DibBuilder.Join(DibBuilder.InfoHeader(1, 1, 16, DibBuilder.BiBitfields), DibBuilder.Masks(0x1F0000, 0x07E0, 0x001F), Row(1, 2, 3, 4)) },
        { "bit fields on a 24 bit bitmap", DibBuilder.Join(DibBuilder.InfoHeader(1, 1, 24, DibBuilder.BiBitfields), DibBuilder.Masks(StandardRed, StandardGreen, StandardBlue), Row(1, 2, 3, 0)) },
        { "bit fields with no masks", DibBuilder.Join(DibBuilder.InfoHeader(1, 1, 32, DibBuilder.BiBitfields), Row(1, 2, 3, 4)) },
        { "a width and height over the pixel cap", DibBuilder.Join(DibBuilder.InfoHeader(6001, 6000, 24)) },
        { "a width and height too large to multiply", DibBuilder.Join(DibBuilder.InfoHeader(int.MaxValue, int.MaxValue, 32)) },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_bitmap_that_is_damaged_or_not_supported_is_a_failure_and_not_a_crash(string description, byte[] dib)
    {
        var result = Dib.ToPng(dib);

        result.IsFailure.ShouldBeTrue(description);
        result.Error.Code.ShouldBe(ErrorCode.ClipboardUnreadable);
    }

    [Fact]
    public void The_starting_bitmap_of_the_refusals_is_valid()
    {
        Dib.ToPng(DibBuilder.Good24()).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Padding_missing_after_the_last_row_is_tolerated_but_missing_pixels_are_not()
    {
        // A 2 x 2 bitmap of 24 bits: each row is 6 bytes of pixels and 2 of padding.
        var full = DibBuilder.Good24(2, 2);

        Dib.ToPng(full.AsMemory(0, full.Length - 2)).IsSuccess.ShouldBeTrue("only the padding of the last row is missing");
        Dib.ToPng(full.AsMemory(0, full.Length - 3)).IsFailure.ShouldBeTrue("a pixel is missing");
    }

    [Fact]
    public void A_bitmap_at_the_pixel_cap_is_not_refused_for_its_size_but_one_over_is()
    {
        var atCap = Dib.ToPng(DibBuilder.Join(DibBuilder.InfoHeader(6000, 6000, 24)));
        var overCap = Dib.ToPng(DibBuilder.Join(DibBuilder.InfoHeader(6001, 6000, 24)));

        atCap.IsFailure.ShouldBeTrue("there is no pixel data");
        atCap.Error.Message.ShouldNotContain("too large");
        overCap.Error.Message.ShouldContain("too large");
    }

    [Fact]
    public void Random_bytes_never_throw()
    {
        var random = new Random(41);
        for (var round = 0; round < 400; round++)
        {
            var data = new byte[random.Next(0, 300)];
            random.NextBytes(data);

            Should.NotThrow(() => Dib.ToPng(data));
        }
    }

    [Fact]
    public void Random_changes_to_valid_bitmaps_never_throw()
    {
        var random = new Random(42);
        byte[][] valid =
        [
            DibBuilder.Join(DibBuilder.InfoHeader(3, 2, 24), DibBuilder.Rows([Row(1, 2, 3, 4, 5, 6, 7, 8, 9), Row(9, 8, 7, 6, 5, 4, 3, 2, 1)])),
            DibBuilder.Join(DibBuilder.InfoHeader(3, 1, 8, clrUsed: 3), DibBuilder.Palette((1, 2, 3), (4, 5, 6), (7, 8, 9)), DibBuilder.Rows([Row(0, 1, 2)])),
            DibBuilder.Join(DibBuilder.ExtendedHeader(124, 2, 1, 32, StandardRed, StandardGreen, StandardBlue, StandardAlpha), DibBuilder.Rows([Row(1, 2, 3, 4, 5, 6, 7, 8)])),
            DibBuilder.Join(DibBuilder.InfoHeader(3, 1, 16, DibBuilder.BiBitfields), DibBuilder.Masks(0xF800, 0x07E0, 0x001F), DibBuilder.Rows([Row(1, 2, 3, 4, 5, 6)])),
        ];

        foreach (var original in valid)
        {
            for (var round = 0; round < 400; round++)
            {
                var data = (byte[])original.Clone();
                data[random.Next(data.Length)] = (byte)random.Next(256);

                var result = Dib.ToPng(data);

                if (result.IsSuccess)
                {
                    Png.Decode(result.Value).IsSuccess.ShouldBeTrue("every picture that is written must be readable");
                }
            }
        }
    }

    // --- Windows agrees ---

    [Fact]
    public async Task Windows_reads_a_converted_picture_as_the_same_colours()
    {
        var dib = DibBuilder.Join(
            DibBuilder.ExtendedHeader(124, 3, 2, 32, StandardRed, StandardGreen, StandardBlue, StandardAlpha),
            DibBuilder.Rows([Row(1, 2, 3, 255, 4, 5, 6, 128, 7, 8, 9, 0), Row(10, 11, 12, 255, 13, 14, 15, 255, 16, 17, 18, 64)]));

        var (width, height, rgba) = await OsImaging.DecodeAsync(Dib.ToPng(dib).Value);

        width.ShouldBe(3);
        height.ShouldBe(2);
        rgba.ShouldBe([3, 2, 1, 255, 6, 5, 4, 128, 9, 8, 7, 0, 12, 11, 10, 255, 15, 14, 13, 255, 18, 17, 16, 64]);
    }

    [Fact]
    public async Task A_full_screen_screenshot_converts_quickly_into_a_valid_smaller_picture()
    {
        // 1920 x 1080 at 32 bits with a flat background and a gradient strip, like a desktop.
        const int width = 1920;
        const int height = 1080;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                pixels[i] = y < 100 ? (byte)(x / 8) : (byte)200;
                pixels[i + 1] = y < 100 ? (byte)(y * 2) : (byte)200;
                pixels[i + 2] = 200;
            }
        }

        var dib = DibBuilder.Join(DibBuilder.InfoHeader(width, -height, 32), pixels);
        var stopwatch = Stopwatch.StartNew();

        var png = Dib.ToPng(dib).Value;

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        png.Length.ShouldBeLessThan(pixels.Length / 20);
        var (decodedWidth, decodedHeight, _) = await OsImaging.DecodeAsync(png);
        decodedWidth.ShouldBe(width);
        decodedHeight.ShouldBe(height);
    }

    // --- going back to a bitmap ---

    [Fact]
    public void A_picture_becomes_a_bottom_up_32_bit_bitmap_with_a_version_5_header()
    {
        var png = Png.Encode(2, 2, 4, (y, row) => new byte[][] { [1, 2, 3, 4, 5, 6, 7, 8], [9, 10, 11, 12, 13, 14, 15, 16] }[y].CopyTo(row));

        var dib = Dib.FromPng(png).Value;

        BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(0, 4)).ShouldBe(124, "header size");
        BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4, 4)).ShouldBe(2, "width");
        BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8, 4)).ShouldBe(2, "a positive height means the rows are stored bottom first");
        BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(12, 2)).ShouldBe((short)1, "planes");
        BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(14, 2)).ShouldBe((short)32, "bits per pixel");
        BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(16, 4)).ShouldBe(3, "bit fields");
        BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(20, 4)).ShouldBe(16, "size of the pixel data");
        BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(40, 4)).ShouldBe(StandardRed);
        BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(44, 4)).ShouldBe(StandardGreen);
        BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(48, 4)).ShouldBe(StandardBlue);
        BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(52, 4)).ShouldBe(StandardAlpha);
        BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(56, 4)).ShouldBe(0x73524742u, "the colour space is sRGB");
        dib.Length.ShouldBe(124 + 16);
        // Bottom row first, each pixel as blue, green, red, alpha.
        dib.AsSpan(124).ToArray().ShouldBe([11, 10, 9, 12, 15, 14, 13, 16, 3, 2, 1, 4, 7, 6, 5, 8]);
    }

    [Fact]
    public void A_picture_without_alpha_becomes_fully_opaque()
    {
        var png = Png.Encode(1, 1, 3, (_, row) => new byte[] { 10, 20, 30 }.CopyTo(row));

        var dib = Dib.FromPng(png).Value;

        dib.AsSpan(124).ToArray().ShouldBe([30, 20, 10, 255]);
    }

    [Fact]
    public void Converting_to_a_bitmap_and_back_gives_the_same_picture()
    {
        var pixels = PngBuilder.RandomPixels(13, 7, 4, seed: 3);
        var png = Png.Encode(13, 7, 4, (y, row) => pixels.AsSpan(y * 13 * 4, 13 * 4).CopyTo(row));

        var back = ToImage(Dib.FromPng(png).Value);

        back.Channels.ShouldBe(4);
        back.Pixels.ShouldBe(pixels);
    }

    [Fact]
    public void An_opaque_picture_comes_back_with_the_same_colours_and_full_alpha()
    {
        var pixels = PngBuilder.RandomPixels(5, 5, 3, seed: 4);
        var png = Png.Encode(5, 5, 3, (y, row) => pixels.AsSpan(y * 5 * 3, 5 * 3).CopyTo(row));

        var back = ToImage(Dib.FromPng(png).Value);

        back.Channels.ShouldBe(4);
        for (var i = 0; i < 25; i++)
        {
            back.Pixels.AsSpan(4 * i, 3).ToArray().ShouldBe(pixels.AsSpan(3 * i, 3).ToArray());
            back.Pixels[(4 * i) + 3].ShouldBe((byte)255);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(0)]
    public void A_picture_that_is_not_the_kind_ClipBench_writes_cannot_be_put_on_the_clipboard(byte colorType)
    {
        var png = PngBuilder.Concat(
            PngBuilder.Signature,
            PngBuilder.Chunk("IHDR", PngBuilder.Header(2, 2, colorType: colorType)),
            PngBuilder.Chunk("IDAT", PngBuilder.Deflate(new byte[6])),
            PngBuilder.End());

        var result = Dib.FromPng(png);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ClipboardWriteFailed);
    }

    [Fact]
    public void Bytes_that_are_not_a_picture_cannot_be_put_on_the_clipboard()
    {
        var result = Dib.FromPng(new byte[] { 1, 2, 3 });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ClipboardWriteFailed);
    }
}
