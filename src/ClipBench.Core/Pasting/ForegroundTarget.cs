namespace ClipBench.Core.Pasting;

/// <summary>The window that had focus when it was recorded, and what is known about it.</summary>
/// <param name="Window">The window.</param>
/// <param name="ProcessId">The process that owns the window.</param>
/// <param name="ProcessName">The executable name of that process, or null when Windows would not say (a protected process).</param>
/// <param name="BlocksInputFromUs">
/// True when the process has a higher integrity level than ClipBench. Windows then drops keystrokes that
/// ClipBench sends to it (User Interface Privilege Isolation), so a paste would silently do nothing.
/// </param>
public sealed record ForegroundTarget(WindowHandle Window, int ProcessId, string? ProcessName, bool BlocksInputFromUs)
{
    /// <summary>Printing never includes the process name: it can reveal what the user is working on.</summary>
    /// <returns>The window and process id.</returns>
    public override string ToString() => $"Foreground target ({Window}, process {ProcessId})";
}
