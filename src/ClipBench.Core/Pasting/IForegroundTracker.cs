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
}
