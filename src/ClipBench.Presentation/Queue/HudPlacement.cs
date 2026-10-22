using ClipBench.Core.Screen;

namespace ClipBench.Presentation.Queue;

/// <summary>Works out where the queue window goes: the bottom-right corner of the work area, out of the way.</summary>
public static class HudPlacement
{
    /// <summary>The gap between the window and the edges of the work area, in device-independent pixels.</summary>
    public const double MarginDip = 16;

    /// <summary>Places the window.</summary>
    /// <param name="anchor">A point on the monitor to use, with its work area and scale.</param>
    /// <param name="widthDip">The window width in device-independent pixels.</param>
    /// <param name="heightDip">The window height in device-independent pixels.</param>
    /// <returns>The window rectangle in device pixels.</returns>
    public static PixelRect Compute(ScreenAnchor anchor, double widthDip, double heightDip)
    {
        var scale = double.IsFinite(anchor.DpiScale) && anchor.DpiScale > 0 ? anchor.DpiScale : 1.0;
        var work = anchor.WorkArea;

        // A window bigger than the work area shrinks rather than hang off it.
        var width = Math.Min(ToPixels(widthDip, scale), work.Width);
        var height = Math.Min(ToPixels(heightDip, scale), work.Height);
        var margin = ToPixels(MarginDip, scale);

        var x = Math.Max(work.Right - margin - width, work.X);
        var y = Math.Max(work.Bottom - margin - height, work.Y);
        return new PixelRect(x, y, width, height);
    }

    private static int ToPixels(double dip, double scale) => (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero);
}
