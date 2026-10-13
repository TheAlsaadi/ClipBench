using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using ClipBench.Platform.Windows.Formats;

namespace ClipBench.Platform.Windows.IntegrationTests.Support;

/// <summary>
/// Builds PNG files byte by byte, with the filters written out here and not borrowed from the code
/// under test, so a decoder bug cannot hide behind an encoder that makes the same mistake.
/// </summary>
internal static class PngBuilder
{
    public static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Chunk(string type, ReadOnlySpan<byte> data, bool badCrc = false)
    {
        var bytes = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)data.Length);
        Encoding.ASCII.GetBytes(type, bytes.AsSpan(4, 4));
        data.CopyTo(bytes.AsSpan(8));
        var crc = Crc32.Compute(bytes.AsSpan(4, 4 + data.Length));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8 + data.Length), badCrc ? crc ^ 1 : crc);
        return bytes;
    }

    public static byte[] Header(int width, int height, byte depth = 8, byte colorType = 6, byte compression = 0, byte filter = 0, byte interlace = 0)
    {
        var data = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), (uint)height);
        data[8] = depth;
        data[9] = colorType;
        data[10] = compression;
        data[11] = filter;
        data[12] = interlace;
        return data;
    }

    public static byte[] Deflate(ReadOnlySpan<byte> raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return output.ToArray();
    }

    public static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(part => part)];

    public static byte[] End() => Chunk("IEND", []);

    /// <summary>A complete PNG, with every row filtered by one filter type.</summary>
    public static byte[] Build(int width, int height, int channels, byte[] pixels, byte filterType = 0)
    {
        var colorType = (byte)(channels == 4 ? 6 : 2);
        return Concat(
            Signature,
            Chunk("IHDR", Header(width, height, colorType: colorType)),
            Chunk("IDAT", Deflate(FilterRows(width, height, channels, pixels, filterType))),
            End());
    }

    public static byte[] FilterRows(int width, int height, int channels, byte[] pixels, byte filterType)
    {
        var rowBytes = width * channels;
        var raw = new byte[(rowBytes + 1) * height];
        for (var y = 0; y < height; y++)
        {
            raw[y * (rowBytes + 1)] = filterType;
            for (var x = 0; x < rowBytes; x++)
            {
                int current = pixels[(y * rowBytes) + x];
                int left = x >= channels ? pixels[(y * rowBytes) + x - channels] : 0;
                int up = y > 0 ? pixels[((y - 1) * rowBytes) + x] : 0;
                int upLeft = y > 0 && x >= channels ? pixels[((y - 1) * rowBytes) + x - channels] : 0;
                var predicted = filterType switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => throw new ArgumentOutOfRangeException(nameof(filterType)),
                };
                raw[(y * (rowBytes + 1)) + 1 + x] = (byte)(current - predicted);
            }
        }

        return raw;
    }

    // The predictor as the PNG specification words it.
    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : (pb <= pc ? b : c);
    }

    public static byte[] RandomPixels(int width, int height, int channels, int seed)
    {
        var pixels = new byte[width * height * channels];
        new Random(seed).NextBytes(pixels);
        return pixels;
    }
}
