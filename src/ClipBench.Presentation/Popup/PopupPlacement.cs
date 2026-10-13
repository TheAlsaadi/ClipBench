using ClipBench.Core.Screen;

namespace ClipBench.Presentation.Popup;

/// <summary>Works out where the popup goes so that it is fully visible on the monitor it opens on.</summary>
public static class PopupPlacement
{
    /// <summary>The gap between the caret line and the popup, in device pixels.</summary>
    public const int Gap = 4;

    /// <summary>Places the popup next to an anchor.</summary>
    /// <param name="anchor">The caret or pointer, and the monitor it is on.</param>
    /// <param name="widthDip">The popup width in device-independent pixels.</param>
    /// <param name="heightDip">The popup height in device-independent pixels.</param>
    /// <returns>The popup rectangle in device pixels. It opens below the anchor when it fits there, above when it fits there, and is moved inside the work area otherwise.</returns>
    public static PixelRect Compute(ScreenAnchor anchor, double widthDip, double heightDip)
    {
        var scale = double.IsFinite(anchor.DpiScale) && anchor.DpiScale > 0 ? anchor.DpiScale : 1.0;
        var work = anchor.WorkArea;

        // A popup bigger than the monitor shrinks rather than hang off it.
        var width = Math.Min(ToPixels(widthDip, scale), work.Width);
        var height = Math.Min(ToPixels(heightDip, scale), work.Height);

        var x = anchor.Point.X;
        if (x + width > work.Right)
        {
            x = work.Right - width;
        }

        x = Math.Max(x, work.X);

        // Below the caret line when there is room, otherwise above it, otherwise wherever it fits.
        var below = anchor.Point.Y + anchor.Height + Gap;
        int y;
        if (below + height <= work.Bottom)
        {
            y = below;
        }
        else
        {
            var above = anchor.Point.Y - Gap - height;
            y = above >= work.Y ? above : work.Bottom - height;
        }

        // An anchor outside the work area (a stale caret, say) must not take the popup off the monitor.
        y = Math.Max(Math.Min(y, work.Bottom - height), work.Y);

        return new PixelRect(x, y, width, height);
    }

    private static int ToPixels(double dip, double scale) => (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero);
}
