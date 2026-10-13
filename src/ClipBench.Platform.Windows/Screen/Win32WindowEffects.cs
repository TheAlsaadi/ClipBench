using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ClipBench.Platform.Windows.Screen;

/// <summary>The real window effects.</summary>
internal sealed unsafe class Win32WindowEffects : IWindowEffects
{
    /// <inheritdoc/>
    public Result ExcludeFromCapture(WindowHandle window)
    {
        if (window.IsNone)
        {
            return Failure("That window does not exist.");
        }

        return PInvoke.SetWindowDisplayAffinity(ToHwnd(window), WINDOW_DISPLAY_AFFINITY.WDA_EXCLUDEFROMCAPTURE)
            ? Result.Success()
            : Failure("Windows would not hide the window from screen capture.");
    }

    /// <inheritdoc/>
    public bool IsExcludedFromCapture(WindowHandle window)
    {
        if (window.IsNone)
        {
            return false;
        }

        return PInvoke.GetWindowDisplayAffinity(ToHwnd(window), out var affinity) && affinity == (uint)WINDOW_DISPLAY_AFFINITY.WDA_EXCLUDEFROMCAPTURE;
    }

    /// <inheritdoc/>
    public Result UseRoundedCorners(WindowHandle window)
    {
        if (window.IsNone)
        {
            return Failure("That window does not exist.");
        }

        var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        var hresult = PInvoke.DwmSetWindowAttribute(
            ToHwnd(window),
            DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
            &preference,
            (uint)sizeof(DWM_WINDOW_CORNER_PREFERENCE));

        return hresult.Succeeded ? Result.Success() : Failure("Windows would not round the window corners.");
    }

    private static HWND ToHwnd(WindowHandle window) => new((nint)window.Value);

    private static Result Failure(string message) => Result.Failure(new ResultError(ErrorCode.PlatformFailure, message));
}
