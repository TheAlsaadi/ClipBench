using ClipBench.Core.Pasting;
using ClipBench.Core.Screen;

namespace ClipBench.Platform.Windows.Screen;

/// <summary>Finds where to put the popup: at the text caret when the focused window shows one, otherwise at the pointer.</summary>
internal sealed class ScreenLocator : IScreenLocator
{
    // Used only when Windows will not describe a monitor: a common size, at normal scale, anchored at the origin.
    private static readonly PixelRect _fallbackWorkArea = new(0, 0, 1920, 1080);

    private readonly IScreenApi _api;

    /// <summary>Creates the locator.</summary>
    /// <param name="api">The screen calls.</param>
    public ScreenLocator(IScreenApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    /// <inheritdoc/>
    public ScreenAnchor Locate(WindowHandle? foreground)
    {
        var caret = foreground is { IsNone: false } window ? _api.GetCaret((nint)window.Value) : null;

        PixelPoint point;
        int height;
        if (caret is { Height: > 0 } found)
        {
            point = found.TopLeft;
            height = found.Height;
        }
        else
        {
            point = _api.GetCursor();
            height = 0;
        }

        var monitor = _api.GetMonitor(point);
        var workArea = monitor is { WorkArea: { Width: > 0, Height: > 0 } } valid ? valid.WorkArea : _fallbackWorkArea;
        var scale = monitor is { DpiScale: > 0 } && double.IsFinite(monitor.Value.DpiScale) ? monitor.Value.DpiScale : 1.0;

        return new ScreenAnchor(point, height, workArea, scale);
    }
}
