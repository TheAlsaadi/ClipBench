using System.Buffers.Binary;

namespace ClipBench.Platform.Windows.IntegrationTests.Support;

/// <summary>Builds device-independent bitmaps (the data behind CF_DIB and CF_DIBV5) byte by byte.</summary>
internal static class DibBuilder
{
    public const int BiRgb = 0;
    public const int BiBitfields = 3;

    /// <summary>A BITMAPINFOHEADER of 40 bytes.</summary>
    public static byte[] InfoHeader(int width, int height, int bitCount, int compression = BiRgb, int clrUsed = 0, short planes = 1, int headerSize = 40)
    {
        var header = new byte[Math.Max(40, headerSize)];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), height);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(12, 2), planes);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(14, 2), (short)bitCount);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16, 4), compression);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(32, 4), clrUsed);
        return header;
    }

    /// <summary>A BITMAPV4HEADER (108 bytes) or BITMAPV5HEADER (124 bytes) that carries its own masks.</summary>
    public static byte[] ExtendedHeader(int headerSize, int width, int height, int bitCount, uint red, uint green, uint blue, uint alpha, int compression = BiBitfields, int clrUsed = 0)
    {
        var header = InfoHeader(width, height, bitCount, compression, clrUsed, headerSize: headerSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40, 4), red);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44, 4), green);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(48, 4), blue);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(52, 4), alpha);
        return header;
    }

    /// <summary>The three colour masks that follow a 40-byte header when the compression is BI_BITFIELDS.</summary>
    public static byte[] Masks(uint red, uint green, uint blue)
    {
        var masks = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(0, 4), red);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(4, 4), green);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(8, 4), blue);
        return masks;
    }

    /// <summary>A colour table. Each entry is written as red, green, blue, as the test states it, and stored blue, green, red, zero.</summary>
    public static byte[] Palette(params (byte R, byte G, byte B)[] colours) =>
        [.. colours.SelectMany(colour => new byte[] { colour.B, colour.G, colour.R, 0 })];

    /// <summary>
    /// Rows of pixel data. The rows are given top first, without padding; each is padded to a multiple of
    /// four bytes, and the order is reversed for the usual bottom-up bitmap.
    /// </summary>
    public static byte[] Rows(IReadOnlyList<byte[]> topFirst, bool bottomUp = true)
    {
        var ordered = bottomUp ? topFirst.Reverse() : topFirst;
        return [.. ordered.SelectMany(row => row.Concat(new byte[(4 - (row.Length % 4)) % 4]))];
    }

    public static byte[] Join(params byte[][] parts) => [.. parts.SelectMany(part => part)];

    /// <summary>A small valid 24-bit bitmap, which the refusal tests start from.</summary>
    public static byte[] Good24(int width = 2, int height = 2)
    {
        var row = new byte[width * 3];
        return Join(InfoHeader(width, height, 24), Rows([.. Enumerable.Repeat(row, height)]));
    }
}
