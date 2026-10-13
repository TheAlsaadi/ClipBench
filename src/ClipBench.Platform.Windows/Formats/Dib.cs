using System.Buffers.Binary;
using System.Numerics;
using ClipBench.Core.Results;

namespace ClipBench.Platform.Windows.Formats;

/// <summary>
/// Converts between the device-independent bitmaps Windows keeps on the clipboard (CF_DIB and CF_DIBV5)
/// and PNG. A bitmap is read in the layouts programs really use and refused otherwise; a picture is
/// written as a bottom-up 32-bit version 5 bitmap with straight alpha.
/// </summary>
internal static class Dib
{
    /// <summary>The most bytes a bitmap may have: 32 bits for each pixel of the largest picture, and a little for headers.</summary>
    public const int MaxBytes = (int)(Png.MaxPixels * 4) + (1 << 20);

    private const int InfoHeaderSize = 40;
    private const int V4HeaderSize = 108;
    private const int V5HeaderSize = 124;
    private const int Rgb = 0;
    private const int Bitfields = 3;
    private const uint StandardRed = 0x00FF0000;
    private const uint StandardGreen = 0x0000FF00;
    private const uint StandardBlue = 0x000000FF;
    private const uint StandardAlpha = 0xFF000000;
    private const uint SrgbColorSpace = 0x73524742;
    private const int ImagesIntent = 4;

    /// <summary>Converts a bitmap to a PNG.</summary>
    /// <param name="dib">The bitmap as it is on the clipboard: a header, optional masks and palette, and the rows.</param>
    /// <returns>The PNG, or <see cref="ErrorCode.ClipboardUnreadable"/> when the bitmap is damaged, too large or in a layout ClipBench does not read.</returns>
    public static Result<byte[]> ToPng(ReadOnlyMemory<byte> dib)
    {
        var parsed = Layout.Parse(dib.Span);
        if (parsed.Failure is { } failure)
        {
            return Result.Failure<byte[]>(failure);
        }

        var layout = parsed.Layout!;
        var channels = layout.HasAlpha && layout.HasVisibleAlpha(dib.Span) ? 4 : 3;
        return Result.Success(Png.Encode(layout.Width, layout.Height, channels, (y, row) => layout.ReadRow(dib.Span, y, channels, row)));
    }

    /// <summary>Converts a PNG to a bitmap that Windows can put on the clipboard as CF_DIBV5.</summary>
    /// <param name="png">A PNG that ClipBench wrote.</param>
    /// <returns>The bitmap, or <see cref="ErrorCode.ClipboardWriteFailed"/> when the PNG is not of the kind ClipBench writes.</returns>
    public static Result<byte[]> FromPng(ReadOnlySpan<byte> png)
    {
        var decoded = Png.Decode(png);
        if (decoded.IsFailure)
        {
            return Result.Failure<byte[]>(new ResultError(ErrorCode.ClipboardWriteFailed, "That picture cannot be put on the clipboard."));
        }

        var image = decoded.Value;
        var pixelBytes = image.Width * image.Height * 4;
        var dib = new byte[V5HeaderSize + pixelBytes];

        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0, 4), V5HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4, 4), image.Width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8, 4), image.Height);   // positive: the bottom row comes first
        BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(12, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(14, 2), 32);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(16, 4), Bitfields);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(20, 4), pixelBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(40, 4), StandardRed);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(44, 4), StandardGreen);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(48, 4), StandardBlue);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(52, 4), StandardAlpha);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(56, 4), SrgbColorSpace);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(108, 4), ImagesIntent);

        for (var y = 0; y < image.Height; y++)
        {
            var source = image.Pixels.AsSpan(y * image.Width * image.Channels, image.Width * image.Channels);
            var target = dib.AsSpan(V5HeaderSize + ((image.Height - 1 - y) * image.Width * 4), image.Width * 4);
            for (var x = 0; x < image.Width; x++)
            {
                target[4 * x] = source[(image.Channels * x) + 2];
                target[(4 * x) + 1] = source[(image.Channels * x) + 1];
                target[(4 * x) + 2] = source[image.Channels * x];
                target[(4 * x) + 3] = image.Channels == 4 ? source[(4 * x) + 3] : (byte)255;
            }
        }

        return Result.Success(dib);
    }

    // One colour channel inside a 16 or 32 bit pixel: where its bits are and how to scale them to 0-255.
    private readonly record struct Channel(uint Mask)
    {
        public int Shift => BitOperations.TrailingZeroCount(Mask);

        public uint Maximum => Mask >> Shift;

        public byte Extract(uint pixel)
        {
            var value = (pixel & Mask) >> Shift;
            return Maximum == 255 ? (byte)value : (byte)((((ulong)value * 255) + (Maximum / 2)) / Maximum);
        }
    }

    private sealed record ParseResult(Layout? Layout, ResultError? Failure);

    // Everything needed to find and read the pixels of a bitmap, worked out and checked once.
    private sealed class Layout
    {
        private readonly int _bitCount;
        private readonly bool _topDown;
        private readonly long _pixelStart;
        private readonly long _stride;
        private readonly uint[]? _palette;
        private readonly Channel _red;
        private readonly Channel _green;
        private readonly Channel _blue;
        private readonly Channel _alpha;
        private readonly bool _standardBytes;

        private Layout(int width, int height, int bitCount, bool topDown, long pixelStart, long stride, uint[]? palette, uint red, uint green, uint blue, uint alpha)
        {
            Width = width;
            Height = height;
            _bitCount = bitCount;
            _topDown = topDown;
            _pixelStart = pixelStart;
            _stride = stride;
            _palette = palette;
            _red = new Channel(red);
            _green = new Channel(green);
            _blue = new Channel(blue);
            _alpha = new Channel(alpha);
            _standardBytes = bitCount == 32 && red == StandardRed && green == StandardGreen && blue == StandardBlue && alpha is 0 or StandardAlpha;
        }

        public int Width { get; }

        public int Height { get; }

        public bool HasAlpha => _alpha.Mask != 0;

        public static ParseResult Parse(ReadOnlySpan<byte> data)
        {
            if (data.Length < InfoHeaderSize)
            {
                return Fail("The picture on the clipboard is damaged.");
            }

            var headerSize = BinaryPrimitives.ReadInt32LittleEndian(data);
            if (headerSize is not (InfoHeaderSize or V4HeaderSize or V5HeaderSize) || data.Length < headerSize)
            {
                return Fail("That kind of picture on the clipboard is not supported.");
            }

            var width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4));
            var signedHeight = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8, 4));
            var planes = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(12, 2));
            var bitCount = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(14, 2));
            var compression = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(16, 4));
            var colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(32, 4));

            if (planes != 1 || width < 1 || signedHeight == 0 || signedHeight == int.MinValue || colorsUsed < 0)
            {
                return Fail("The picture on the clipboard is damaged.");
            }

            if (bitCount is not (1 or 4 or 8 or 16 or 24 or 32)
                || compression is not (Rgb or Bitfields)
                || (compression == Bitfields && bitCount is not (16 or 32)))
            {
                return Fail("That kind of picture on the clipboard is not supported.");
            }

            var height = Math.Abs(signedHeight);

            // Checked before any size is multiplied: the dimensions alone decide how much a bitmap may use.
            if ((long)width * height > Png.MaxPixels)
            {
                return Fail("The picture is too large to keep.");
            }

            if (!TryReadMasks(data, headerSize, bitCount, compression, out var masks, out var maskBytes))
            {
                return Fail("The picture on the clipboard is damaged.");
            }

            // A colour table follows the header (and any masks). Low bit depths need one; the others may carry one too.
            var maxColors = bitCount <= 8 ? 1L << bitCount : int.MaxValue;
            if (colorsUsed > maxColors)
            {
                return Fail("The picture on the clipboard is damaged.");
            }

            var colorCount = bitCount <= 8 && colorsUsed == 0 ? maxColors : colorsUsed;
            var paletteStart = headerSize + maskBytes;
            var pixelStart = paletteStart + (colorCount * 4);
            var stride = (((long)width * bitCount) + 31) / 32 * 4;
            var rowBytes = (((long)width * bitCount) + 7) / 8;

            // The padding after the last row is optional: some programs leave it off.
            if (pixelStart + (stride * (height - 1)) + rowBytes > data.Length)
            {
                return Fail("The picture on the clipboard is damaged.");
            }

            uint[]? palette = null;
            if (bitCount <= 8)
            {
                palette = new uint[colorCount];
                for (var i = 0; i < palette.Length; i++)
                {
                    palette[i] = BinaryPrimitives.ReadUInt32LittleEndian(data[(paletteStart + (4 * i))..]);
                }
            }

            return new ParseResult(
                new Layout(width, height, bitCount, signedHeight < 0, pixelStart, stride, palette, masks.Red, masks.Green, masks.Blue, masks.Alpha),
                null);
        }

        // True when some pixel has a non-zero alpha. Programs often leave an alpha mask in the header while
        // leaving every alpha value zero, which means "no alpha", and a picture read as invisible would be useless.
        public bool HasVisibleAlpha(ReadOnlySpan<byte> data)
        {
            for (var y = 0; y < Height; y++)
            {
                var row = data[(int)RowOffset(y)..];
                for (var x = 0; x < Width; x++)
                {
                    if (_standardBytes ? row[(4 * x) + 3] != 0 : (ReadPixel(row, x) & _alpha.Mask) != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // Writes one output row (top row is 0) as red, green, blue and, with four channels, alpha.
        public void ReadRow(ReadOnlySpan<byte> data, int y, int channels, Span<byte> output)
        {
            var row = data[(int)RowOffset(y)..];
            switch (_bitCount)
            {
                case 24:
                    for (var x = 0; x < Width; x++)
                    {
                        output[3 * x] = row[(3 * x) + 2];
                        output[(3 * x) + 1] = row[(3 * x) + 1];
                        output[(3 * x) + 2] = row[3 * x];
                    }

                    break;
                case 16 or 32:
                    for (var x = 0; x < Width; x++)
                    {
                        if (_standardBytes)
                        {
                            output[channels * x] = row[(4 * x) + 2];
                            output[(channels * x) + 1] = row[(4 * x) + 1];
                            output[(channels * x) + 2] = row[4 * x];
                            if (channels == 4)
                            {
                                output[(4 * x) + 3] = row[(4 * x) + 3];
                            }
                        }
                        else
                        {
                            var pixel = ReadPixel(row, x);
                            output[channels * x] = _red.Extract(pixel);
                            output[(channels * x) + 1] = _green.Extract(pixel);
                            output[(channels * x) + 2] = _blue.Extract(pixel);
                            if (channels == 4)
                            {
                                output[(4 * x) + 3] = _alpha.Extract(pixel);
                            }
                        }
                    }

                    break;
                default:
                    for (var x = 0; x < Width; x++)
                    {
                        var index = PaletteIndex(row, x);
                        var color = index < _palette!.Length ? _palette[index] : 0u;   // a number past the table is black
                        output[3 * x] = (byte)(color >> 16);
                        output[(3 * x) + 1] = (byte)(color >> 8);
                        output[(3 * x) + 2] = (byte)color;
                    }

                    break;
            }
        }

        private long RowOffset(int y) => _pixelStart + (_stride * (_topDown ? y : Height - 1 - y));

        private uint ReadPixel(ReadOnlySpan<byte> row, int x) => _bitCount == 16
            ? BinaryPrimitives.ReadUInt16LittleEndian(row[(2 * x)..])
            : BinaryPrimitives.ReadUInt32LittleEndian(row[(4 * x)..]);

        private int PaletteIndex(ReadOnlySpan<byte> row, int x) => _bitCount switch
        {
            8 => row[x],
            4 => (row[x / 2] >> ((x % 2 == 0) ? 4 : 0)) & 0x0F,
            _ => (row[x / 8] >> (7 - (x % 8))) & 1,
        };

        private static bool TryReadMasks(ReadOnlySpan<byte> data, int headerSize, int bitCount, int compression, out (uint Red, uint Green, uint Blue, uint Alpha) masks, out int maskBytes)
        {
            masks = (StandardRed, StandardGreen, StandardBlue, 0);
            maskBytes = 0;

            if (bitCount is 24 or <= 8)
            {
                return true;
            }

            if (compression == Rgb)
            {
                // The default layouts: five bits for each colour in 16 bits, and blue, green, red in 32.
                masks = bitCount == 16 ? (0x7C00, 0x03E0, 0x001F, 0) : masks;
                return true;
            }

            if (headerSize == InfoHeaderSize)
            {
                maskBytes = 12;
                if (data.Length < InfoHeaderSize + maskBytes)
                {
                    return false;
                }

                masks = (
                    BinaryPrimitives.ReadUInt32LittleEndian(data[40..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(data[44..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(data[48..]),
                    0);
            }
            else
            {
                masks = (
                    BinaryPrimitives.ReadUInt32LittleEndian(data[40..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(data[44..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(data[48..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(data[52..]));
            }

            var limit = bitCount == 16 ? 0xFFFFu : uint.MaxValue;
            return IsOneRun(masks.Red, limit) && IsOneRun(masks.Green, limit) && IsOneRun(masks.Blue, limit)
                && (masks.Alpha == 0 || IsOneRun(masks.Alpha, limit));
        }

        // A mask must be one unbroken run of bits inside the pixel: 0x00FF00FF would mix two channels.
        private static bool IsOneRun(uint mask, uint limit)
        {
            if (mask == 0 || (mask & ~limit) != 0)
            {
                return false;
            }

            var shifted = mask >> BitOperations.TrailingZeroCount(mask);
            return (shifted & (shifted + 1)) == 0;
        }

        private static ParseResult Fail(string message) => new(null, new ResultError(ErrorCode.ClipboardUnreadable, message));
    }
}
