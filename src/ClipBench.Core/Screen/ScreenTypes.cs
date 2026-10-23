using ClipBench.Core.Pasting;

namespace ClipBench.Core.Screen;

/// <summary>A point on the virtual screen, in device pixels. Monitors left of or above the main one have negative values.</summary>
/// <param name="X">Pixels from the left edge of the main monitor.</param>
/// <param name="Y">Pixels from the top edge of the main monitor.</param>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>A rectangle on the virtual screen, in device pixels.</summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    /// <summary>The first column outside the rectangle on the right.</summary>
    public int Right => X + Width;

    /// <summary>The first row outside the rectangle at the bottom.</summary>
    public int Bottom => Y + Height;
}

/// <summary>Where the popup should appear: next to the text caret or the mouse pointer, and the room there is.</summary>
/// <param name="Point">The top-left corner of the caret, or the pointer position when no caret is known.</param>
/// <param name="Height">The height of the caret in pixels, or zero for a pointer. The popup opens below this line.</param>
/// <param name="WorkArea">The usable area of the monitor that holds the point, without the taskbar.</param>
/// <param name="DpiScale">That monitor's scale: 1.0 at 96 dpi, 1.5 at 144 dpi.</param>
public readonly record struct ScreenAnchor(PixelPoint Point, int Height, PixelRect WorkArea, double DpiScale);

/// <summary>Finds where to put the popup.</summary>
public interface IScreenLocator
{
    /// <summary>
    /// Looks for the text caret in the window that has focus and falls back to the mouse pointer when that window
    /// does not report one. Never fails: with no better information the pointer on the main monitor is used.
    /// </summary>
    /// <param name="foreground">The window that has focus, or null.</param>
    /// <returns>The anchor point and the monitor it is on.</returns>
    ScreenAnchor Locate(WindowHandle? foreground);

    /// <summary>Where the mouse pointer is, in device pixels on the whole desktop.</summary>
    /// <returns>The pointer position, or the origin when Windows will not say.</returns>
    PixelPoint CursorPosition();
}
