using ClipBench.Core.Screen;

namespace ClipBench.Platform.Windows.Screen;

/// <summary>Where a window reports its text caret, on the screen, in device pixels.</summary>
/// <param name="TopLeft">The top-left corner of the caret.</param>
/// <param name="Height">The height of the caret.</param>
internal readonly record struct CaretInfo(PixelPoint TopLeft, int Height);

/// <summary>A monitor's usable area and scale.</summary>
/// <param name="WorkArea">The area without the taskbar.</param>
/// <param name="DpiScale">1.0 at 96 dpi.</param>
internal readonly record struct MonitorInfo(PixelRect WorkArea, double DpiScale);

/// <summary>The screen and caret calls the locator needs, kept apart so the logic can be tested without a screen.</summary>
internal interface IScreenApi
{
    /// <summary>Asks the thread that owns a window where its caret is.</summary>
    /// <param name="window">The window that has focus.</param>
    /// <returns>The caret, or null when the window shows none (browsers and most modern programs draw their own).</returns>
    CaretInfo? GetCaret(nint window);

    /// <summary>The mouse pointer position on the screen.</summary>
    PixelPoint GetCursor();

    /// <summary>The monitor that holds a point, or the nearest one.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The monitor, or null when Windows would not say.</returns>
    MonitorInfo? GetMonitor(PixelPoint point);
}
