using ClipBench.Core.Screen;

namespace ClipBench.Platform.Windows.Screen;

/// <summary>
/// Draws the selection overlay into a pixel buffer: the monitor dimmed, the chosen area left clear, and a border around
/// it. A pure function over memory, so it is tested without a screen. Pixels are premultiplied alpha, one <c>uint</c>
/// each as <c>0xAARRGGBB</c>, the form a layered window wants.
/// </summary>
internal static class OverlayRenderer
{
    /// <summary>Black at about 40 percent: the screen shows through, so the text to select can still be read.</summary>
    public const uint Dim = 0x66000000;

    /// <summary>
    /// The chosen area. Almost, but not quite, see-through: a layered window passes the mouse through pixels with no alpha at all,
    /// and the drag must keep reaching the overlay.
    /// </summary>
    public const uint Clear = 0x01000000;

    /// <summary>An opaque blue, the accent of Windows.</summary>
    public const uint Border = 0xFF0078D7;

    /// <summary>Fills the buffer.</summary>
    /// <param name="pixels">The monitor's pixels, <c>width * height</c> of them, row after row from the top.</param>
    /// <param name="width">The monitor's width in pixels.</param>
    /// <param name="height">The monitor's height in pixels.</param>
    /// <param name="selection">The chosen area relative to the monitor's corner, or null for none. Parts outside the monitor are cut off.</param>
    /// <param name="borderThickness">The thickness of the border drawn just outside the area, in pixels. At least one.</param>
    public static void Draw(Span<uint> pixels, int width, int height, PixelRect? selection, int borderThickness)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(borderThickness, 1);
        if (pixels.Length != (long)width * height)
        {
            throw new ArgumentException("The buffer must hold exactly one value for each pixel.", nameof(pixels));
        }

        pixels.Fill(Dim);
        if (selection is not { Width: > 0, Height: > 0 } area)
        {
            return;
        }

        // Long arithmetic: a selection comes from a drag and its edges can be anywhere.
        var left = (long)area.X;
        var top = (long)area.Y;
        var right = left + area.Width;
        var bottom = top + area.Height;
        if (right <= 0 || bottom <= 0 || left >= width || top >= height)
        {
            return;
        }

        FillRectangle(pixels, width, height, left - borderThickness, top - borderThickness, right + borderThickness, bottom + borderThickness, Border);
        FillRectangle(pixels, width, height, left, top, right, bottom, Clear);
    }

    // Fills the part of the rectangle [left, right) x [top, bottom) that lies inside the buffer.
    private static void FillRectangle(Span<uint> pixels, int width, int height, long left, long top, long right, long bottom, uint color)
    {
        var x0 = (int)Math.Max(left, 0);
        var x1 = (int)Math.Min(right, width);
        var y0 = (int)Math.Max(top, 0);
        var y1 = (int)Math.Min(bottom, height);
        for (var y = y0; y < y1; y++)
        {
            pixels.Slice((y * width) + x0, x1 - x0).Fill(color);
        }
    }
}
