using ClipBench.Core.Screen;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ClipBench.Platform.Windows.Screen;

/// <summary>The real screen and caret calls.</summary>
internal sealed unsafe class Win32ScreenApi : IScreenApi
{
    private const double BaseDpi = 96.0;

    /// <inheritdoc/>
    public CaretInfo? GetCaret(nint window)
    {
        var thread = PInvoke.GetWindowThreadProcessId(new HWND(window), null);
        if (thread == 0)
        {
            return null;
        }

        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        if (!PInvoke.GetGUIThreadInfo(thread, ref info) || info.hwndCaret.IsNull)
        {
            return null;
        }

        var height = info.rcCaret.bottom - info.rcCaret.top;
        if (height <= 0)
        {
            return null;
        }

        // The caret rectangle is in the client area of the window that owns the caret.
        var topLeft = new global::System.Drawing.Point(info.rcCaret.left, info.rcCaret.top);
        if (!PInvoke.ClientToScreen(info.hwndCaret, ref topLeft))
        {
            return null;
        }

        return new CaretInfo(new PixelPoint(topLeft.X, topLeft.Y), height);
    }

    /// <inheritdoc/>
    public PixelPoint GetCursor() =>
        PInvoke.GetCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : new PixelPoint(0, 0);

    /// <inheritdoc/>
    public MonitorInfo? GetMonitor(PixelPoint point)
    {
        var monitor = PInvoke.MonitorFromPoint(new global::System.Drawing.Point(point.X, point.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (monitor.IsNull)
        {
            return null;
        }

        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (!PInvoke.GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var work = info.rcWork;
        var area = new PixelRect(work.left, work.top, work.right - work.left, work.bottom - work.top);

        // Effective dpi is what the monitor is set to. Without it the monitor is treated as normal scale.
        var scale = 1.0;
        if (PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded && dpiX > 0)
        {
            scale = dpiX / BaseDpi;
        }

        return new MonitorInfo(area, scale);
    }
}
