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
    private const uint WmMouseActivate = 0x0021;
    private const uint WmActivate = 0x0006;
    private const int MaNoActivate = 3;
    private const int NoActivateStyle = 0x08000000;   // WS_EX_NOACTIVATE
    private const nuint SubclassId = 1;
    private const nuint KeepFocusId = 2;

    // Asked how large the client area of a window is whenever its frame is worked out, it answers that it is the whole
    // window. It holds no state, so one callback serves every window, and it stays alive for as long as this object does.
    private readonly SUBCLASSPROC _answerWithWholeWindow =
        (HWND handle, uint message, WPARAM wParam, LPARAM lParam, nuint id, nuint data) =>
            message == WmNcCalcSize && wParam.Value != 0 ? new LRESULT(0) : PInvoke.DefSubclassProc(handle, message, wParam, lParam);

    // A window that keeps the focus where it was (the reference data of its subclass says so): asked whether a click should make
    // it the window with focus, it answers no, and when it is put behind another window it gets the style that tells Windows never
    // to make it the window with focus on a click, because the child windows of a WinUI window take the click first and
    // activate the window by themselves. The style is lifted again by BringToFront, for when the focus is wanted. It holds no
    // state itself: the data belongs to the window, so a window that goes away takes it along.
    private readonly SUBCLASSPROC _keepFocusOnClick =
        (HWND handle, uint message, WPARAM wParam, LPARAM lParam, nuint id, nuint data) =>
        {
            if (data != 0)
            {
                if (message == WmMouseActivate)
                {
                    return new LRESULT(MaNoActivate);
                }

                if (message == WmActivate && (wParam.Value & 0xFFFF) == 0)
                {
                    SetNoActivateStyle(handle, on: true);
                }
            }

            return PInvoke.DefSubclassProc(handle, message, wParam, lParam);
        };

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

        // Installing the same callback on a window again changes nothing, and the handle of a closed window that is given to
        // a new one gets it too, so there is nothing to remember per window.
        var hwnd = ToHwnd(window);
        if (!PInvoke.SetWindowSubclass(hwnd, _answerWithWholeWindow, SubclassId, 0))
        {
            return Failure("Windows would not remove the window frame.");
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

    /// <inheritdoc/>
    /// <remarks>It must be called on the thread that owns the window, which is what Windows requires of a subclass.</remarks>
    public Result SetActivatesOnClick(WindowHandle window, bool activates)
    {
        if (window.IsNone || !PInvoke.IsWindow(ToHwnd(window)))
        {
            return Failure("That window does not exist.");
        }

        // Installing the callback again only changes its data, which is how the state is changed.
        var hwnd = ToHwnd(window);
        if (!PInvoke.SetWindowSubclass(hwnd, _keepFocusOnClick, KeepFocusId, activates ? 0u : 1u))
        {
            return Failure("Windows would not change how the window reacts to a click.");
        }

        // A window that is in front keeps its ability to be activated until it is put behind another one; any other gets the
        // style at once. Going back to normal lifts it.
        SetNoActivateStyle(hwnd, on: !activates && PInvoke.GetForegroundWindow() != hwnd);
        return Result.Success();
    }

    /// <inheritdoc/>
    public Result BringToFront(WindowHandle window)
    {
        if (window.IsNone || !PInvoke.IsWindow(ToHwnd(window)))
        {
            return Failure("That window does not exist.");
        }

        // The style that keeps a locked window from taking the focus is lifted: this is the focus being asked for.
        SetNoActivateStyle(ToHwnd(window), on: false);

        // SetForegroundWindow is refused for a window that is already visible unless the program has just had the user's input,
        // and the shortcut does not count. Switching to a window, as the task switcher does, is allowed.
        PInvoke.SwitchToThisWindow(ToHwnd(window), true);
        return Result.Success();
    }

    private static void SetNoActivateStyle(HWND hwnd, bool on)
    {
        var style = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var wanted = on ? style | NoActivateStyle : style & ~NoActivateStyle;
        if (wanted != style)
        {
            _ = PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, wanted);
        }
    }

    private static HWND ToHwnd(WindowHandle window) => new((nint)window.Value);

    private static Result Failure(string message) => Result.Failure(new ResultError(ErrorCode.PlatformFailure, message));
}
