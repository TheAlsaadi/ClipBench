using System.Buffers.Binary;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Formats;
using ClipBench.Platform.Windows.IntegrationTests.Support;

namespace ClipBench.Platform.Windows.IntegrationTests.Formats;

public class PngTests
{
    private static byte[] Encode(int width, int height, int channels, byte[] pixels) =>
        Png.Encode(width, height, channels, (y, row) => pixels.AsSpan(y * width * channels, width * channels).CopyTo(row));

    private static RawImage Decode(byte[] png) => Png.Decode(png).Value;

    private static byte[] ToRgba(RawImage image)
    {
        if (image.Channels == 4)
        {
            return image.Pixels;
        }

        var rgba = new byte[image.Width * image.Height * 4];
        for (var i = 0; i < image.Width * image.Height; i++)
        {
            rgba[4 * i] = image.Pixels[3 * i];
            rgba[(4 * i) + 1] = image.Pixels[(3 * i) + 1];
            rgba[(4 * i) + 2] = image.Pixels[(3 * i) + 2];
            rgba[(4 * i) + 3] = 255;
        }

        return rgba;
    }

    // --- the checksum ---

    [Fact]
    public void The_checksum_matches_the_published_check_values()
    {
        Crc32.Compute("123456789"u8).ShouldBe(0xCBF43926u);
        Crc32.Compute("IEND"u8).ShouldBe(0xAE426082u, "the checksum every PNG file ends with");
        Crc32.Compute([]).ShouldBe(0u);
    }

    [Fact]
    public void A_checksum_built_in_pieces_matches_one_built_at_once()
    {
        byte[] data = [.. Enumerable.Range(0, 1000).Select(i => (byte)(i * 31))];

        var pieces = Crc32.Update(Crc32.Update(0, data.AsSpan(0, 333)), data.AsSpan(333));

        pieces.ShouldBe(Crc32.Compute(data));
    }

    // --- writing ---

    [Theory]
    [InlineData(1, 1, 3)]
    [InlineData(1, 1, 4)]
    [InlineData(2, 3, 3)]
    [InlineData(17, 5, 4)]
    [InlineData(300, 4, 3)]
    [InlineData(5, 200, 4)]
    public void What_is_written_is_read_back_exactly(int width, int height, int channels)
    {
        var pixels = PngBuilder.RandomPixels(width, height, channels, seed: width * height);

        var image = Decode(Encode(width, height, channels, pixels));

        image.Width.ShouldBe(width);
        image.Height.ShouldBe(height);
        image.Channels.ShouldBe(channels);
        image.Pixels.ShouldBe(pixels);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Windows_reads_what_is_written_as_the_same_picture(int channels)
    {
        var pixels = PngBuilder.RandomPixels(41, 23, channels, seed: 5);

        var (width, height, rgba) = await OsImaging.DecodeAsync(Encode(41, 23, channels, pixels));

        width.ShouldBe(41);
        height.ShouldBe(23);
        rgba.ShouldBe(ToRgba(new RawImage(41, 23, channels, pixels)));
    }

    [Fact]
    public async Task A_picture_written_by_Windows_is_read_back_exactly()
    {
        // Windows chooses its own row filters, so this covers whichever ones it uses.
        var rgba = PngBuilder.RandomPixels(64, 48, 4, seed: 9);
        for (var i = 3; i < rgba.Length; i += 4)
        {
            rgba[i] = 255;
        }

        var png = await OsImaging.EncodeAsync(64, 48, rgba);

        Decode(png).Pixels.ShouldBe(rgba);
    }

    [Fact]
    public async Task A_smooth_picture_is_much_smaller_than_its_raw_pixels_and_still_valid()
    {
        // A gradient is what filtering helps most. 1000 x 800 x 3 raw bytes is 2.4 MB.
        var width = 1000;
        var height = 800;
        var pixels = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 3;
                pixels[i] = (byte)(x / 4);
                pixels[i + 1] = (byte)(y / 4);
                pixels[i + 2] = (byte)((x + y) / 8);
            }
        }

        var png = Encode(width, height, 3, pixels);

        png.Length.ShouldBeLessThan(pixels.Length / 10);
        (await OsImaging.DecodeAsync(png)).Rgba.ShouldBe(ToRgba(new RawImage(width, height, 3, pixels)));
    }

    [Fact]
    public void The_written_file_has_the_standard_structure()
    {
        var png = Encode(3, 2, 4, PngBuilder.RandomPixels(3, 2, 4, 1));

        png.AsSpan(0, 8).ToArray().ShouldBe(PngBuilder.Signature);
        BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(8, 4)).ShouldBe(13u);
        System.Text.Encoding.ASCII.GetString(png, 12, 4).ShouldBe("IHDR");
        png[24].ShouldBe((byte)8, "bit depth");
        png[25].ShouldBe((byte)6, "colour type: RGB with alpha");
        png[28].ShouldBe((byte)0, "no interlacing");
        png.AsSpan(^12).ToArray().ShouldBe(PngBuilder.End());
    }

    [Fact]
    public void Every_chunk_checksum_in_a_written_file_is_correct()
    {
        var png = Encode(50, 50, 3, PngBuilder.RandomPixels(50, 50, 3, 2));

        var position = 8;
        while (position < png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position, 4));
            var stored = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position + 8 + length, 4));
            Crc32.Compute(png.AsSpan(position + 4, 4 + length)).ShouldBe(stored);
            position += 12 + length;
        }

        position.ShouldBe(png.Length, "the chunks end exactly at the end of the file");
    }

    [Fact]
    public void A_large_picture_is_split_into_several_data_chunks()
    {
        // Random pixels do not compress, so 700 x 700 x 4 is about 2 MB of compressed data.
        var png = Encode(700, 700, 4, PngBuilder.RandomPixels(700, 700, 4, 3));

        var idat = 0;
        var position = 8;
        while (position < png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position, 4));
            length.ShouldBeLessThanOrEqualTo(1 << 20);
            idat += System.Text.Encoding.ASCII.GetString(png, position + 4, 4) == "IDAT" ? 1 : 0;
            position += 12 + length;
        }

        idat.ShouldBeGreaterThan(1);
        Decode(png).Pixels.Length.ShouldBe(700 * 700 * 4);
    }

    [Theory]
    [InlineData(0, 1, 3)]
    [InlineData(1, 0, 3)]
    [InlineData(-1, 1, 3)]
    [InlineData(1, 1, 2)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 5)]
    public void Writing_a_picture_with_impossible_dimensions_is_a_bug(int width, int height, int channels)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Png.Encode(width, height, channels, (_, _) => { }));
    }

    [Fact]
    public void Writing_a_picture_over_the_pixel_cap_is_a_bug()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Png.Encode(6001, 6000, 3, (_, _) => { }));
    }

    // --- reading, every kind of row filter ---

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(0, 4)]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    public void Each_kind_of_row_filter_is_undone_correctly(byte filterType, int channels)
    {
        var pixels = PngBuilder.RandomPixels(13, 9, channels, seed: filterType + 10);

        Decode(PngBuilder.Build(13, 9, channels, pixels, filterType)).Pixels.ShouldBe(pixels);
    }

    [Fact]
    public void A_chunk_the_decoder_does_not_know_is_skipped_when_it_is_optional()
    {
        var pixels = PngBuilder.RandomPixels(2, 2, 4, 4);
        var png = PngBuilder.Concat(
            PngBuilder.Signature,
            PngBuilder.Chunk("IHDR", PngBuilder.Header(2, 2)),
            PngBuilder.Chunk("tEXt", "Comment\0hello"u8),
            PngBuilder.Chunk("pHYs", new byte[9]),
            PngBuilder.Chunk("IDAT", PngBuilder.Deflate(PngBuilder.FilterRows(2, 2, 4, pixels, 0))),
            PngBuilder.End());

        Decode(png).Pixels.ShouldBe(pixels);
    }

    [Fact]
    public void The_data_may_be_split_over_several_data_chunks()
    {
        var pixels = PngBuilder.RandomPixels(8, 8, 3, 6);
        var compressed = PngBuilder.Deflate(PngBuilder.FilterRows(8, 8, 3, pixels, 0));
        var png = PngBuilder.Concat(
            PngBuilder.Signature,
            PngBuilder.Chunk("IHDR", PngBuilder.Header(8, 8, colorType: 2)),
            PngBuilder.Chunk("IDAT", compressed.AsSpan(0, 10)),
            PngBuilder.Chunk("IDAT", compressed.AsSpan(10)),
            PngBuilder.End());

        Decode(png).Pixels.ShouldBe(pixels);
    }

    // --- reading, damaged or unsupported files are refused ---

    private static byte[] Valid(int width = 4, int height = 4, int channels = 4) =>
        PngBuilder.Build(width, height, channels, PngBuilder.RandomPixels(width, height, channels, 1));

    private static byte[] WithHeader(byte[] header, int width = 4, int height = 4, int channels = 4) =>
        PngBuilder.Concat(
            PngBuilder.Signature,
            PngBuilder.Chunk("IHDR", header),
            PngBuilder.Chunk("IDAT", PngBuilder.Deflate(PngBuilder.FilterRows(width, height, channels, PngBuilder.RandomPixels(width, height, channels, 1), 0))),
            PngBuilder.End());

    public static TheoryData<string, byte[]> Refused => new()
    {
        { "empty", [] },
        { "shorter than the signature", PngBuilder.Signature[..5] },
        { "only the signature", PngBuilder.Signature },
        { "a wrong signature", [.. new byte[] { 0x88 }, .. Valid()[1..]] },
        { "a damaged header checksum", PngBuilder.Concat(PngBuilder.Signature, PngBuilder.Chunk("IHDR", PngBuilder.Header(4, 4), badCrc: true), PngBuilder.End()) },
        { "a damaged data checksum", PngBuilder.Concat(PngBuilder.Signature, PngBuilder.Chunk("IHDR", PngBuilder.Header(4, 4)), PngBuilder.Chunk("IDAT", PngBuilder.Deflate(new byte[68]), badCrc: true), PngBuilder.End()) },
        { "a data chunk before the header", PngBuilder.Concat(PngBuilder.Signature, PngBuilder.Chunk("IDAT", PngBuilder.Deflate(new byte[68])), PngBuilder.Chunk("IHDR", PngBuilder.Header(4, 4)), PngBuilder.End()) },
        { "two headers", PngBuilder.Concat(PngBuilder.Signature, PngBuilder.Chunk("IHDR", PngBuilder.Header(4, 4)), PngBuilder.Chunk("IHDR", PngBuilder.Header(4, 4)), PngBuilder.End()) },
        { "a header of the wrong length", PngBuilder.Concat(PngBuilder.Signature, PngBuilder.Chunk("IHDR", new byte[12]), PngBuilder.End()) },
        { "no data chunk", PngBuilder.Concat(PngBuilder.Signature, PngBuilder.Chunk("IHDR", PngBuilder.Header(4, 4)), PngBuilder.End()) },
        { "no end chunk", Valid()[..^12] },
        { "bytes after the end chunk", [.. Valid(), 0x00] },
        { "a critical chunk it does not know", PngBuilder.Concat(PngBuilder.Signature, PngBuilder.Chunk("IHDR", PngBuilder.Header(4, 4)), PngBuilder.Chunk("XXXX", [1]), PngBuilder.Chunk("IDAT", PngBuilder.Deflate(new byte[68])), PngBuilder.End()) },
        { "a chunk longer than the file", [.. PngBuilder.Signature, 0x7F, 0xFF, 0xFF, 0xFF, .. "IHDR"u8.ToArray()] },
        { "a chunk length of four billion", [.. PngBuilder.Signature, 0xFF, 0xFF, 0xFF, 0xFF, .. "IDAT"u8.ToArray(), 0, 0, 0, 0] },
        { "a chunk name that is not letters", PngBuilder.Concat(PngBuilder.Signature, PngBuilder.Chunk("IHDR", PngBuilder.Header(4, 4)), PngBuilder.Chunk("te\0t", [1]), PngBuilder.Chunk("IDAT", PngBuilder.Deflate(new byte[68])), PngBuilder.End()) },
        { "a chunk length one more than the data that is left", [.. Valid()[..^12], .. new byte[] { 0, 0, 0, 1 }, .. "IEND"u8.ToArray(), 0xAE, 0x42, 0x60, 0x82] },
        { "a width of zero", WithHeader(PngBuilder.Header(0, 4), 1, 4) },
        { "a height of zero", WithHeader(PngBuilder.Header(4, 0), 4, 1) },
        { "16 bits per channel", WithHeader(PngBuilder.Header(4, 4, depth: 16)) },
        { "1 bit per channel", WithHeader(PngBuilder.Header(4, 4, depth: 1, colorType: 0)) },
        { "a palette picture", WithHeader(PngBuilder.Header(4, 4, colorType: 3)) },
        { "a grey picture", WithHeader(PngBuilder.Header(4, 4, colorType: 0)) },
        { "a grey picture with alpha", WithHeader(PngBuilder.Header(4, 4, colorType: 4)) },
        { "an unknown colour type", WithHeader(PngBuilder.Header(4, 4, colorType: 5)) },
        { "an unknown compression method", WithHeader(PngBuilder.Header(4, 4, compression: 1)) },
        { "an unknown filter method", WithHeader(PngBuilder.Header(4, 4, filter: 1)) },
        { "an interlaced picture", WithHeader(PngBuilder.Header(4, 4, interlace: 1)) },
        { "data that is not compressed data", PngBuilder.Concat(PngBuilder.Signature, PngBuilder.Chunk("IHDR", PngBuilder.Header(4, 4)), PngBuilder.Chunk("IDAT", [1, 2, 3, 4, 5, 6, 7, 8]), PngBuilder.End()) },
        { "too few pixels", PngBuilder.Concat(PngBuilder.Signature, PngBuilder.Chunk("IHDR", PngBuilder.Header(4, 4)), PngBuilder.Chunk("IDAT", PngBuilder.Deflate(new byte[67])), PngBuilder.End()) },
        { "too many pixels", PngBuilder.Concat(PngBuilder.Signature, PngBuilder.Chunk("IHDR", PngBuilder.Header(4, 4)), PngBuilder.Chunk("IDAT", PngBuilder.Deflate(new byte[69])), PngBuilder.End()) },
        { "a row filter that does not exist", PngBuilder.Concat(PngBuilder.Signature, PngBuilder.Chunk("IHDR", PngBuilder.Header(1, 1)), PngBuilder.Chunk("IDAT", PngBuilder.Deflate([5, 0, 0, 0, 0])), PngBuilder.End()) },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_file_that_is_damaged_or_not_supported_is_a_failure_and_not_a_crash(string description, byte[] png)
    {
        var result = Png.Decode(png);

        result.IsFailure.ShouldBeTrue(description);
        result.Error.Code.ShouldBe(ErrorCode.ClipboardUnreadable);
    }

    [Fact]
    public void The_valid_file_the_refusals_are_built_from_is_accepted()
    {
        // The refused cases above change one thing each. This proves the rest of each is fine.
        Png.Decode(Valid()).IsSuccess.ShouldBeTrue();
        Png.Decode(WithHeader(PngBuilder.Header(4, 4))).IsSuccess.ShouldBeTrue();
        Png.Decode(PngBuilder.Concat(PngBuilder.Signature, PngBuilder.Chunk("IHDR", PngBuilder.Header(4, 4)), PngBuilder.Chunk("IDAT", PngBuilder.Deflate(new byte[68])), PngBuilder.End())).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Dimensions_over_the_pixel_cap_are_refused_before_anything_is_unpacked()
    {
        var png = PngBuilder.Concat(
            PngBuilder.Signature,
            PngBuilder.Chunk("IHDR", PngBuilder.Header(6001, 6000)),
            PngBuilder.Chunk("IDAT", PngBuilder.Deflate(new byte[10])),
            PngBuilder.End());

        var result = Png.Decode(png);

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("too large");
    }

    [Fact]
    public void Dimensions_at_the_pixel_cap_are_not_refused_for_their_size_and_do_not_allocate_the_picture_before_the_data_is_there()
    {
        var png = PngBuilder.Concat(
            PngBuilder.Signature,
            PngBuilder.Chunk("IHDR", PngBuilder.Header(6000, 6000)),
            PngBuilder.Chunk("IDAT", PngBuilder.Deflate(new byte[10])),
            PngBuilder.End());
        var before = GC.GetAllocatedBytesForCurrentThread();

        var result = Png.Decode(png);

        result.IsFailure.ShouldBeTrue("the data is far too short");
        result.Error.Message.ShouldNotContain("too large");
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBeLessThan(16L * 1024 * 1024, "the 144 MB picture must not be allocated for 10 bytes of data");
    }

    [Fact]
    public void A_file_with_more_chunks_than_any_picture_needs_is_refused()
    {
        // The header, the data, the end and 99,997 comments make exactly 100,000 chunks: allowed. One more is not.
        byte[] File(int comments) => PngBuilder.Concat(
            PngBuilder.Signature,
            PngBuilder.Chunk("IHDR", PngBuilder.Header(1, 1)),
            PngBuilder.Concat([.. Enumerable.Range(0, comments).Select(_ => PngBuilder.Chunk("tEXt", []))]),
            PngBuilder.Chunk("IDAT", PngBuilder.Deflate(new byte[5])),
            PngBuilder.End());

        Png.Decode(File(99_997)).IsSuccess.ShouldBeTrue();
        Png.Decode(File(99_998)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_tiny_file_that_unpacks_to_gigabytes_is_stopped_at_the_size_the_header_promises()
    {
        // A zlib stream of 256 MB of zeros is about 250 KB. The header promises a 4 x 4 picture, so no more
        // than that many bytes may be unpacked.
        using var bomb = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(bomb, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            var zeros = new byte[1 << 20];
            for (var i = 0; i < 256; i++)
            {
                zlib.Write(zeros);
            }
        }

        var png = PngBuilder.Concat(
            PngBuilder.Signature,
            PngBuilder.Chunk("IHDR", PngBuilder.Header(4, 4)),
            PngBuilder.Chunk("IDAT", bomb.ToArray()),
            PngBuilder.End());
        // This thread's allocations only: other tests run in parallel and allocate on their own threads.
        var before = GC.GetAllocatedBytesForCurrentThread();

        var result = Png.Decode(png);

        result.IsFailure.ShouldBeTrue();
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBeLessThan(16L * 1024 * 1024, "nothing close to 256 MB may be allocated");
    }

    [Fact]
    public void Random_bytes_never_throw()
    {
        var random = new Random(31);
        for (var round = 0; round < 300; round++)
        {
            var data = new byte[random.Next(0, 300)];
            random.NextBytes(data);

            Should.NotThrow(() => Png.Decode(data));
        }
    }

    [Fact]
    public void Random_changes_to_a_valid_file_never_throw_and_never_give_wrong_sized_pixels()
    {
        var random = new Random(32);
        var valid = Valid(8, 8, 3);
        for (var round = 0; round < 600; round++)
        {
            var data = (byte[])valid.Clone();
            data[random.Next(data.Length)] = (byte)random.Next(256);

            var result = Png.Decode(data);

            if (result.IsSuccess)
            {
                result.Value.Pixels.Length.ShouldBe(result.Value.Width * result.Value.Height * result.Value.Channels);
            }
        }
    }
}
