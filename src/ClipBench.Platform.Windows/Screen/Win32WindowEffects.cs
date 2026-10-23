using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ClipBench.Platform.Windows.Screen;

/// <summary>The real window effects.</summary>
internal sealed unsafe class Win32WindowEffects : IWindowEffects
{
    // DWMWA_COLOR_NONE: the value that asks for no border line at all.
    private const uint DwmColorNone = 0xFFFFFFFE;

    private const uint WmNcCalcSize = 0x0083;
    private const nuint SubclassId = 1;

    // The windows whose frame was removed, with the callback that does it.
    private readonly Dictionary<long, SUBCLASSPROC> _subclasses = [];

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

    /// <inheritdoc/>
    public Result HideSystemBorder(WindowHandle window)
    {
        if (window.IsNone)
        {
            return Failure("That window does not exist.");
        }

        var none = DwmColorNone;
        var hresult = PInvoke.DwmSetWindowAttribute(
            ToHwnd(window),
            DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR,
            &none,
            sizeof(uint));

        return hresult.Succeeded ? Result.Success() : Failure("Windows would not remove the window border.");
    }

    /// <inheritdoc/>
    /// <remarks>It must be called on the thread that owns the window, which is what Windows requires of a subclass.</remarks>
    public Result RemoveFrame(WindowHandle window)
    {
        if (window.IsNone || !PInvoke.IsWindow(ToHwnd(window)))
        {
            return Failure("That window does not exist.");
        }

        var hwnd = ToHwnd(window);
        lock (_subclasses)
        {
            if (!_subclasses.ContainsKey(window.Value))
            {
                // The window is asked how large its client area is whenever its frame is worked out, and answers that it is
                // the whole window. The styles of the window are left alone: the presenter of a WinUI window puts them back.
                SUBCLASSPROC callback = (HWND handle, uint message, WPARAM wParam, LPARAM lParam, nuint id, nuint data) =>
                    message == WmNcCalcSize && wParam.Value != 0 ? new LRESULT(0) : PInvoke.DefSubclassProc(handle, message, wParam, lParam);
                if (!PInvoke.SetWindowSubclass(hwnd, callback, SubclassId, 0))
                {
                    return Failure("Windows would not remove the window frame.");
                }

                // The callback has to stay alive for as long as the window is subclassed.
                _subclasses[window.Value] = callback;
            }
        }

        // The frame is worked out again, which asks the window for its client area.
        var applied = PInvoke.SetWindowPos(
            hwnd,
            HWND.Null,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
        return applied ? Result.Success() : Failure("Windows would not remove the window frame.");
    }

    private static HWND ToHwnd(WindowHandle window) => new((nint)window.Value);

    private static Result Failure(string message) => Result.Failure(new ResultError(ErrorCode.PlatformFailure, message));
}
