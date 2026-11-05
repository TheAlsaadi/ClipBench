namespace ClipBench.Core.Screen;

/// <summary>
/// Enlarges a picture of text before it is read. Windows' recognizer returns little or nothing for text set at
/// 13 pixels or less, which is how most screen text is drawn; a smooth twofold enlargement makes the same letters
/// readable (measured in the ADR 0018 addendum: 78% of characters right at the original size, 91% enlarged twofold, and
/// no empty readings). Pure and deterministic: bilinear, with the centers of the pixels lined up.
/// </summary>
public static class ImageScaler
{
    /// <summary>The most pixels an enlarged picture may hold (a 3000 × 1000 area enlarged twofold). Bigger ones are read as they are.</summary>
    public const int MaxOutputPixels = 12_000_000;

    /// <summary>The longest side an enlarged picture may have. The recognizer of Windows reads up to 10 000.</summary>
    public const int MaxSide = 10_000;

    /// <summary>The largest factor <see cref="Scale"/> accepts.</summary>
    public const int MaxFactor = 8;

    private const int PreferredFactor = 2;

    /// <summary>How much to enlarge a picture: twofold when the result stays within the limits, otherwise not at all.</summary>
    /// <param name="width">The width in pixels. Greater than zero.</param>
    /// <param name="height">The height in pixels. Greater than zero.</param>
    /// <returns>2 or 1.</returns>
    public static int ChooseFactor(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);

        return Fits(width, height, PreferredFactor) ? PreferredFactor : 1;
    }

    /// <summary>Makes an enlarged copy. The original is not touched; the copy is zeroed when it is let go of.</summary>
    /// <param name="source">The picture.</param>
    /// <param name="factor">From 1 to <see cref="MaxFactor"/>.</param>
    /// <returns>A new image, which the caller must dispose.</returns>
    public static ScreenImage Scale(ScreenImage source, int factor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source.IsDisposed, source);
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(factor, MaxFactor);

        if (factor == 1)
        {
            return new ScreenImage(source.Width, source.Height, (byte[])source.Pixels.Clone());
        }

        if (!Fits(source.Width, source.Height, factor))
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "The enlarged picture would be too large.");
        }

        var width = source.Width * factor;
        var height = source.Height * factor;
        var output = new byte[checked(width * height * 4)];

        try
        {
            Resample(source, factor, output, width, height);
        }
        catch
        {
            Array.Clear(output);
            throw;
        }

        return new ScreenImage(width, height, output);
    }

    private static bool Fits(int width, int height, int factor)
    {
        var scaledWidth = (long)width * factor;
        var scaledHeight = (long)height * factor;
        return scaledWidth <= MaxSide && scaledHeight <= MaxSide && scaledWidth * scaledHeight <= MaxOutputPixels;
    }

    private static void Resample(ScreenImage source, int factor, byte[] output, int width, int height)
    {
        var input = source.Pixels;
        var sourceWidth = source.Width;
        var sourceHeight = source.Height;

        // Per output column: the two source columns and the weight (0..256) of the second.
        var left = new int[width];
        var right = new int[width];
        var weightX = new int[width];
        for (var x = 0; x < width; x++)
        {
            (left[x], right[x], weightX[x]) = Taps(x, factor, sourceWidth);
        }

        for (var y = 0; y < height; y++)
        {
            var (top, bottom, weightY) = Taps(y, factor, sourceHeight);
            var topRow = top * sourceWidth * 4;
            var bottomRow = bottom * sourceWidth * 4;
            var outRow = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var a = topRow + (left[x] * 4);
                var b = topRow + (right[x] * 4);
                var c = bottomRow + (left[x] * 4);
                var d = bottomRow + (right[x] * 4);
                var wx = weightX[x];
                var o = outRow + (x * 4);

                for (var channel = 0; channel < 4; channel++)
                {
                    var upper = (input[a + channel] * (256 - wx)) + (input[b + channel] * wx);
                    var lower = (input[c + channel] * (256 - wx)) + (input[d + channel] * wx);
                    output[o + channel] = (byte)(((upper * (256 - weightY)) + (lower * weightY) + 32768) >> 16);
                }
            }
        }
    }

    // The source position of the center of output pixel `index` is (index + 0.5) / factor - 0.5, in source pixels.
    private static (int First, int Second, int Weight) Taps(int index, int factor, int sourceLength)
    {
        // Work in 1/(2 * factor) steps so the maths is exact: position = (2 * index + 1 - factor) / (2 * factor).
        var numerator = (2 * index) + 1 - factor;
        if (numerator <= 0)
        {
            return (0, 0, 0);
        }

        var first = numerator / (2 * factor);
        if (first >= sourceLength - 1)
        {
            return (sourceLength - 1, sourceLength - 1, 0);
        }

        var remainder = numerator - (first * 2 * factor);
        var weight = ((remainder * 256) + factor) / (2 * factor);
        return (first, first + 1, weight);
    }
}
