using System.Buffers.Binary;
using System.IO.Compression;
using ClipBench.Core.Results;

namespace ClipBench.Platform.Windows.Formats;

/// <summary>A decoded picture: rows from the top, without padding or row filters.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Channels">3 for red, green, blue; 4 for red, green, blue, alpha (straight, not premultiplied).</param>
/// <param name="Pixels">Width x Height x Channels bytes.</param>
internal sealed record RawImage(int Width, int Height, int Channels, byte[] Pixels);

/// <summary>
/// A PNG writer and reader for the one kind of PNG ClipBench keeps: 8 bits per channel, RGB or RGBA, not
/// interlaced. The writer makes exactly that, and the reader accepts exactly that and refuses everything
/// else, so a PNG from outside is never interpreted by code that was written for something simpler.
/// </summary>
internal static class Png
{
    /// <summary>
    /// The most pixels in one picture: an 8K screen (33 million) fits. The decoded picture of that size is
    /// 144 MB, so the limit also bounds the memory a hostile file can make us use.
    /// </summary>
    public const long MaxPixels = 36_000_000;

    private const int MaxChunks = 100_000;
    private const int MaxDataChunkLength = 1 << 20;
    private const int HeaderLength = 13;
    private const int ChunkOverhead = 12;
    private const int MaxExpansion = 1032;   // the most a deflate stream can grow, so the stream length bounds the output

    private static readonly byte[] _signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Fills in one row of a picture.</summary>
    /// <param name="y">The row, counted from the top.</param>
    /// <param name="row">Where to put the row: width x channels bytes, red first.</param>
    public delegate void RowWriter(int y, Span<byte> row);

    /// <summary>
    /// Writes a picture. The rows are asked for one at a time and compressed as they come, so the picture
    /// is never held in memory twice. Every row uses the Paeth filter, which suits both photographs and
    /// flat screens.
    /// </summary>
    /// <param name="width">Width in pixels. At least 1.</param>
    /// <param name="height">Height in pixels. At least 1.</param>
    /// <param name="channels">3 for red, green, blue; 4 to add alpha.</param>
    /// <param name="rows">Supplies the pixels.</param>
    /// <returns>The PNG file.</returns>
    public static byte[] Encode(int width, int height, int channels, RowWriter rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((long)width * height, MaxPixels, nameof(width));
        if (channels is not (3 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "A picture has 3 or 4 channels.");
        }

        ArgumentNullException.ThrowIfNull(rows);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var rowBytes = width * channels;
            var current = new byte[rowBytes];
            var previous = new byte[rowBytes];
            var filtered = new byte[rowBytes + 1];
            filtered[0] = PaethFilter;
            for (var y = 0; y < height; y++)
            {
                rows(y, current);
                FilterPaeth(current, previous, filtered.AsSpan(1), channels);
                zlib.Write(filtered);
                (current, previous) = (previous, current);
            }
        }

        using var file = new MemoryStream();
        file.Write(_signature);

        Span<byte> header = stackalloc byte[HeaderLength];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), (uint)height);
        header[8] = 8;                                  // bits per channel
        header[9] = channels == 4 ? ColorTypeRgba : ColorTypeRgb;
        header[10] = 0;                                 // deflate
        header[11] = 0;                                 // adaptive filtering
        header[12] = 0;                                 // not interlaced
        WriteChunk(file, "IHDR"u8, header);

        var data = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);
        for (var offset = 0; offset < data.Length; offset += MaxDataChunkLength)
        {
            WriteChunk(file, "IDAT"u8, data.Slice(offset, Math.Min(MaxDataChunkLength, data.Length - offset)));
        }

        WriteChunk(file, "IEND"u8, []);
        return file.ToArray();
    }

    /// <summary>Reads a PNG of the kind <see cref="Encode"/> writes.</summary>
    /// <param name="png">The file.</param>
    /// <returns>The picture, or <see cref="ErrorCode.ClipboardUnreadable"/> for a damaged file or one of another kind.</returns>
    public static Result<RawImage> Decode(ReadOnlySpan<byte> png)
    {
        if (png.Length < _signature.Length + ChunkOverhead || !png[.._signature.Length].SequenceEqual(_signature))
        {
            return Unreadable("That is not a PNG picture.");
        }

        var walk = Walk(png);
        if (walk.Failure is { } failure)
        {
            return Result.Failure<RawImage>(failure);
        }

        var header = walk.Header!;
        var rowBytes = (long)header.Width * header.Channels;
        var expected = (rowBytes + 1) * header.Height;
        var raw = Inflate(walk.Data!, expected);
        if (raw is null)
        {
            return Unreadable("The picture data is damaged.");
        }

        var pixels = new byte[rowBytes * header.Height];
        return Unfilter(raw, pixels, header.Width, header.Height, header.Channels)
            ? Result.Success(new RawImage(header.Width, header.Height, header.Channels, pixels))
            : Unreadable("The picture data is damaged.");
    }

    private const byte ColorTypeRgb = 2;
    private const byte ColorTypeRgba = 6;
    private const byte PaethFilter = 4;

    private sealed record HeaderInfo(int Width, int Height, int Channels);

    private sealed record WalkResult(HeaderInfo? Header, byte[]? Data, ResultError? Failure);

    // Goes through the chunks once: checks every checksum, reads the header, and gathers the picture data.
    private static WalkResult Walk(ReadOnlySpan<byte> png)
    {
        HeaderInfo? header = null;
        using var data = new MemoryStream();
        var position = _signature.Length;
        var chunks = 0;
        var ended = false;

        while (position < png.Length && !ended)
        {
            if (++chunks > MaxChunks || png.Length - position < ChunkOverhead)
            {
                return Fail("The PNG picture is damaged.");
            }

            var length = BinaryPrimitives.ReadUInt32BigEndian(png[position..]);
            if (length > (uint)(png.Length - position - ChunkOverhead))
            {
                return Fail("The PNG picture is damaged.");
            }

            var type = png.Slice(position + 4, 4);
            var body = png.Slice(position + 8, (int)length);
            var stored = BinaryPrimitives.ReadUInt32BigEndian(png[(position + 8 + (int)length)..]);
            if (!IsLetters(type) || Crc32.Compute(png.Slice(position + 4, 4 + (int)length)) != stored)
            {
                return Fail("The PNG picture is damaged.");
            }

            position += ChunkOverhead + (int)length;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (chunks != 1 || length != HeaderLength)
                {
                    return Fail("The PNG picture is damaged.");
                }

                var read = ReadHeader(body);
                if (read.Failure is not null)
                {
                    return read;
                }

                header = read.Header;
            }
            else if (header is null)
            {
                return Fail("The PNG picture is damaged.");
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                data.Write(body);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                ended = true;
            }
            else if (IsCritical(type) && !type.SequenceEqual("PLTE"u8))
            {
                return Fail("The PNG picture needs a feature ClipBench does not read.");
            }
        }

        return !ended || position != png.Length || header is null || data.Length == 0
            ? Fail("The PNG picture is damaged.")
            : new WalkResult(header, data.ToArray(), null);

        static WalkResult Fail(string message) => new(null, null, new ResultError(ErrorCode.ClipboardUnreadable, message));
    }

    private static WalkResult ReadHeader(ReadOnlySpan<byte> body)
    {
        var width = BinaryPrimitives.ReadUInt32BigEndian(body[..4]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(body.Slice(4, 4));
        if (width == 0 || height == 0)
        {
            return Fail("The PNG picture is damaged.");
        }

        // Checked before anything is unpacked: the header alone decides how much memory a file may use.
        if ((long)width * height > MaxPixels)
        {
            return Fail("The picture is too large to keep.");
        }

        var depth = body[8];
        var colorType = body[9];
        var compression = body[10];
        var filter = body[11];
        var interlace = body[12];
        if (depth != 8 || colorType is not (ColorTypeRgb or ColorTypeRgba) || compression != 0 || filter != 0 || interlace != 0)
        {
            return Fail("That kind of PNG picture is not supported.");
        }

        return new WalkResult(new HeaderInfo((int)width, (int)height, colorType == ColorTypeRgba ? 4 : 3), null, null);

        static WalkResult Fail(string message) => new(null, null, new ResultError(ErrorCode.ClipboardUnreadable, message));
    }

    // Unpacks exactly the number of bytes the header promises, or fails. The output grows with the bytes
    // that really come out, so a tiny file that claims a huge picture allocates almost nothing.
    private static byte[]? Inflate(byte[] compressed, long expected)
    {
        try
        {
            using var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream((int)Math.Min(expected, (compressed.LongLength * MaxExpansion) + 1024));
            var block = new byte[64 * 1024];
            while (output.Length <= expected)
            {
                var read = zlib.Read(block, 0, (int)Math.Min(block.Length, expected + 1 - output.Length));
                if (read == 0)
                {
                    break;
                }

                output.Write(block, 0, read);
            }

            // Too little is damage; so is a single byte more than promised. Reaching the end of the stream is
            // also what makes the decompressor check the stream's own checksum.
            return output.Length == expected ? output.ToArray() : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static bool Unfilter(byte[] raw, byte[] pixels, int width, int height, int bytesPerPixel)
    {
        var rowBytes = width * bytesPerPixel;
        var previous = new byte[rowBytes];
        for (var y = 0; y < height; y++)
        {
            var source = raw.AsSpan(y * (rowBytes + 1), rowBytes + 1);
            var current = pixels.AsSpan(y * rowBytes, rowBytes);
            var filter = source[0];
            var data = source[1..];

            for (var x = 0; x < rowBytes; x++)
            {
                int left = x >= bytesPerPixel ? current[x - bytesPerPixel] : 0;
                int up = previous[x];
                int upLeft = x >= bytesPerPixel ? previous[x - bytesPerPixel] : 0;
                int predicted;
                switch (filter)
                {
                    case 0:
                        predicted = 0;
                        break;
                    case 1:
                        predicted = left;
                        break;
                    case 2:
                        predicted = up;
                        break;
                    case 3:
                        predicted = (left + up) / 2;
                        break;
                    case PaethFilter:
                        predicted = PaethPredictor(left, up, upLeft);
                        break;
                    default:
                        return false;
                }

                current[x] = (byte)(data[x] + predicted);
            }

            current.CopyTo(previous);
        }

        return true;
    }

    private static void FilterPaeth(ReadOnlySpan<byte> current, ReadOnlySpan<byte> previous, Span<byte> output, int bytesPerPixel)
    {
        for (var x = 0; x < current.Length; x++)
        {
            int left = x >= bytesPerPixel ? current[x - bytesPerPixel] : 0;
            int upLeft = x >= bytesPerPixel ? previous[x - bytesPerPixel] : 0;
            output[x] = (byte)(current[x] - PaethPredictor(left, previous[x], upLeft));
        }
    }

    // The predictor as the PNG specification words it: the neighbour closest to left + up - upLeft.
    private static int PaethPredictor(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceUp = Math.Abs(estimate - up);
        var distanceUpLeft = Math.Abs(estimate - upLeft);
        return distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft ? left : (distanceUp <= distanceUpLeft ? up : upLeft);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32.Update(Crc32.Compute(type), data));
        output.Write(number);
    }

    private static bool IsLetters(ReadOnlySpan<byte> type)
    {
        foreach (var character in type)
        {
            if (!(character is >= (byte)'A' and <= (byte)'Z' || character is >= (byte)'a' and <= (byte)'z'))
            {
                return false;
            }
        }

        return true;
    }

    // An upper-case first letter marks a chunk a reader must understand to show the picture correctly.
    private static bool IsCritical(ReadOnlySpan<byte> type) => type[0] is >= (byte)'A' and <= (byte)'Z';

    private static Result<RawImage> Unreadable(string message) =>
        Result.Failure<RawImage>(new ResultError(ErrorCode.ClipboardUnreadable, message));
}
