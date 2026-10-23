using ClipBench.Core.Pasting;
using ClipBench.Core.Results;

namespace ClipBench.Core.Screen;

/// <summary>Visual and privacy settings that Windows applies to a window from outside.</summary>
public interface IWindowEffects
{
    /// <summary>
    /// Asks Windows to leave a window out of screenshots, screen recordings and screen sharing, so a popup that shows
    /// clips does not end up in a shared screen.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>Success, or <see cref="ErrorCode.PlatformFailure"/> when Windows refused or the window does not exist.</returns>
    Result ExcludeFromCapture(WindowHandle window);

    /// <summary>Tells whether a window is left out of screenshots.</summary>
    /// <param name="window">The window.</param>
    /// <returns>True when <see cref="ExcludeFromCapture"/> is in force for it.</returns>
    bool IsExcludedFromCapture(WindowHandle window);

    /// <summary>Asks Windows to round the corners of a window, as Windows 11 does for its own.</summary>
    /// <param name="window">The window.</param>
    /// <returns>Success, or <see cref="ErrorCode.PlatformFailure"/>. Windows 10 does not support rounded corners, so a failure is not an error to show.</returns>
    Result UseRoundedCorners(WindowHandle window);

    /// <summary>
    /// Asks Windows not to draw its own border line around a window, so the window can draw a border of its own that
    /// follows its theme.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>Success, or <see cref="ErrorCode.PlatformFailure"/>. Windows 10 does not support it, so a failure is not an error to show.</returns>
    Result HideSystemBorder(WindowHandle window);

    /// <summary>
    /// Makes the area for a window's content fill the whole window. A window that has no border or title bar can still keep
    /// a dialog frame, which takes a few pixels on every side and cannot be styled, so the content is smaller than the layout
    /// was made for. No style of the window is changed; the window answers that its content area is all of it. Call it on the
    /// thread that owns the window, once the window exists.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>Success, or <see cref="ErrorCode.PlatformFailure"/> when Windows refused or the window does not exist.</returns>
    Result RemoveFrame(WindowHandle window);
}
