using ClipBench.Core.Results;

namespace ClipBench.Core.Pasting;

/// <summary>Remembers which window had focus, so a paste can go back to it.</summary>
public interface IForegroundTracker
{
    /// <summary>
    /// Records the window that has focus right now. Call it before ClipBench's own window takes focus.
    /// </summary>
    /// <returns>The window, who owns it and whether Windows would block keystrokes sent to it, or a failure when no window has focus.</returns>
    Result<ForegroundTarget> Capture();

    /// <summary>Tells whether a window is the one with focus right now. It looks at nothing else about the window.</summary>
    /// <param name="window">The window.</param>
    /// <returns>True when Windows says the window is the foreground window.</returns>
    bool IsForeground(WindowHandle window);

    /// <summary>Brings a window back to the front and gives it the focus, as when the user clicks it.</summary>
    /// <param name="window">The window.</param>
    /// <returns>True when Windows has made it the window with focus; false when it has closed or Windows refused.</returns>
    bool Restore(WindowHandle window);
}
