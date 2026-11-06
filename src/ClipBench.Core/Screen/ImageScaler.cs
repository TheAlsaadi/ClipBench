namespace ClipBench.Core.Screen;

/// <summary>
/// Prepares a picture of text to be read. Windows' recognizer returns little or nothing for text set at 13 pixels or
/// less, which is how most screen text is drawn, and for text that touches the edge of the picture, which is how a
/// dragged rectangle is cut. So the picture gets a margin in its own background color and is enlarged threefold, or
/// twofold when that is too large (measured in the ADR 0018 addendum: tightly cropped code was read empty in every
/// case at its own size and 0.72 right when padded and enlarged threefold). Pure and deterministic: bilinear, with the
/// centers of the pixels lined up.
/// </summary>
public static class ImageScaler
{
    /// <summary>The most pixels an enlarged picture may hold (a 2000 x 1500 area enlarged twofold, or 1333 x 1000 threefold). Bigger ones are read as they are.</summary>
    public const int MaxOutputPixels = 12_000_000;

    /// <summary>The longest side an enlarged picture may have. The recognizer of Windows reads up to 10 000.</summary>
    public const int MaxSide = 10_000;

    /// <summary>The largest factor <see cref="Scale"/> accepts.</summary>
    public const int MaxFactor = 8;

    /// <summary>The width, in pixels of the original picture, of the margin <see cref="Prepare(ScreenImage)"/> adds on every side.</summary>
    public const int Margin = 8;

    private const int PreferredFactor = 3;
    private const int FallbackFactor = 2;

    /// <summary>How much to enlarge a picture: threefold when the result stays within the limits, else twofold, else not at all.</summary>
    /// <param name="width">The width in pixels. Greater than zero.</param>
    /// <param name="height">The height in pixels. Greater than zero.</param>
    /// <returns>3, 2 or 1.</returns>
    public static int ChooseFactor(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);

        if (Fits(width, height, PreferredFactor))
        {
            return PreferredFactor;
        }

        return Fits(width, height, FallbackFactor) ? FallbackFactor : 1;
    }

    /// <summary>Gives a picture a margin and enlarges it, ready for the recognizer.</summary>
    /// <param name="source">The picture.</param>
    /// <returns>
    /// A new image, which the caller must dispose, or <see langword="null"/> when even the margin would pass
    /// <see cref="MaxSide"/> and the picture is to be read as it is. The original is not touched; the in-between copy is zeroed.
    /// </returns>
    public static ScreenImage? Prepare(ScreenImage source) => Prepare(source, MaxFactor);

    /// <summary>Gives a picture a margin and enlarges it, by at most the given factor.</summary>
    /// <param name="source">The picture.</param>
    /// <param name="maxFactor">The largest enlargement wanted, from 1 to <see cref="MaxFactor"/>.</param>
    /// <returns>A new image, which the caller must dispose, or null when the picture is to be read as it is.</returns>
    public static ScreenImage? Prepare(ScreenImage source, int maxFactor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFactor, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxFactor, MaxFactor);
        ObjectDisposedException.ThrowIf(source.IsDisposed, source);

        if ((long)source.Width + (2 * Margin) > MaxSide || (long)source.Height + (2 * Margin) > MaxSide)
        {
            return null;
        }

        var padded = Pad(source, Margin);
        var factor = Math.Min(ChooseFactor(padded.Width, padded.Height), maxFactor);
        if (factor == 1)
        {
            return padded;
        }

        try
        {
            return Scale(padded, factor);
        }
        finally
        {
            padded.Dispose();
        }
    }

    /// <summary>The enlargement that was applied to get a prepared picture from its source.</summary>
    /// <param name="source">The original picture.</param>
    /// <param name="prepared">What <see cref="Prepare(ScreenImage)"/> returned, or null when the original is read as it is.</param>
    /// <returns>The factor: 1 when nothing was enlarged.</returns>
    public static int FactorOf(ScreenImage source, ScreenImage? prepared)
    {
        ArgumentNullException.ThrowIfNull(source);
        return prepared is null ? 1 : prepared.Width / (source.Width + (2 * Margin));
    }

    /// <summary>Adds a margin around a picture, in the color most of its edge has, so text that reaches the edge no longer touches it.</summary>
    /// <param name="source">The picture.</param>
    /// <param name="margin">The width of the margin in pixels. Greater than zero.</param>
    /// <returns>A new image, which the caller must dispose.</returns>
    public static ScreenImage Pad(ScreenImage source, int margin)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source.IsDisposed, source);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(margin, 0);

        var width = checked(source.Width + (2 * margin));
        var height = checked(source.Height + (2 * margin));
        var output = new byte[checked(width * height * 4)];
        var (blue, green, red) = EdgeColor(source);

        for (var i = 0; i < output.Length; i += 4)
        {
            output[i] = blue;
            output[i + 1] = green;
            output[i + 2] = red;
            output[i + 3] = 255;
        }

        var rowBytes = source.Width * 4;
        for (var y = 0; y < source.Height; y++)
        {
            Buffer.BlockCopy(source.Pixels, y * rowBytes, output, (((y + margin) * width) + margin) * 4, rowBytes);
        }

        return new ScreenImage(width, height, output);
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

    // The middle value of each channel over the edge pixels: a few pixels of a letter on the edge do not change it.
    private static (byte Blue, byte Green, byte Red) EdgeColor(ScreenImage source)
    {
        var blue = new int[256];
        var green = new int[256];
        var red = new int[256];
        var pixels = source.Pixels;
        var count = 0;

        void Add(int x, int y)
        {
            var i = ((y * source.Width) + x) * 4;
            blue[pixels[i]]++;
            green[pixels[i + 1]]++;
            red[pixels[i + 2]]++;
            count++;
        }

        for (var x = 0; x < source.Width; x++)
        {
            Add(x, 0);
            if (source.Height > 1)
            {
                Add(x, source.Height - 1);
            }
        }

        for (var y = 1; y < source.Height - 1; y++)
        {
            Add(0, y);
            if (source.Width > 1)
            {
                Add(source.Width - 1, y);
            }
        }

        return (Median(blue, count), Median(green, count), Median(red, count));
    }

    private static byte Median(int[] histogram, int count)
    {
        var target = (count + 1) / 2;
        var seen = 0;
        for (var value = 0; value < histogram.Length; value++)
        {
            seen += histogram[value];
            if (seen >= target)
            {
                return (byte)value;
            }
        }

        return 0;
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
