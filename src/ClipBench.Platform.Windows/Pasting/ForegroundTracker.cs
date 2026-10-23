using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Clipboard;

namespace ClipBench.Platform.Windows.Pasting;

/// <summary>Looks at a window the way a paste needs to know it.</summary>
internal interface ITargetInspector
{
    /// <summary>Describes a window.</summary>
    /// <param name="window">The window.</param>
    /// <returns>The window, its process and whether keystrokes sent to it would be blocked, or <see cref="ErrorCode.TargetNotFound"/>.</returns>
    Result<ForegroundTarget> Describe(WindowHandle window);
}

/// <summary>
/// Records which window has focus, so a paste can go back to it, and whether Windows would let keystrokes
/// reach it. User Interface Privilege Isolation drops input sent to a program that runs with more rights than
/// the sender, silently, so the paste has to find that out first and say so.
/// </summary>
internal sealed class ForegroundTracker : IForegroundTracker, ITargetInspector
{
    private readonly IWindowApi _windows;
    private readonly IProcessLookup _processes;
    private readonly IProcessIntegrity _integrity;

    /// <summary>Creates a tracker.</summary>
    /// <param name="windows">The window calls.</param>
    /// <param name="processes">Finds the program a process runs.</param>
    /// <param name="integrity">Reads the trust level of processes.</param>
    public ForegroundTracker(IWindowApi windows, IProcessLookup processes, IProcessIntegrity integrity)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(integrity);
        _windows = windows;
        _processes = processes;
        _integrity = integrity;
    }

    /// <inheritdoc/>
    public Result<ForegroundTarget> Capture()
    {
        var window = _windows.GetForegroundWindow();
        return window == 0
            ? NotFound("No window has focus.")
            : Describe(new WindowHandle(window));
    }

    /// <inheritdoc/>
    public bool Restore(WindowHandle window)
    {
        var handle = (nint)window.Value;
        if (window.IsNone || !_windows.IsWindow(handle))
        {
            return false;
        }

        if (_windows.GetForegroundWindow() == handle)
        {
            return true;
        }

        // Windows may say no, or yes without doing it. Only the foreground window itself is believed.
        _ = _windows.SetForegroundWindow(handle);
        return _windows.GetForegroundWindow() == handle;
    }

    /// <inheritdoc/>
    public Result<ForegroundTarget> Describe(WindowHandle window)
    {
        var handle = (nint)window.Value;
        if (window.IsNone || !_windows.IsWindow(handle))
        {
            return NotFound("The window to paste into is gone.");
        }

        var process = _windows.GetWindowProcessId(handle);
        if (process == 0)
        {
            return NotFound("The window to paste into is gone.");
        }

        var level = _integrity.Query(process);
        if (level.Outcome == IntegrityOutcome.NotFound)
        {
            return NotFound("The program to paste into has closed.");
        }

        // A level that is known decides it. A process that refuses to be asked has more rights than ClipBench: that
        // is the reason it refuses.
        var blocks = level.Outcome == IntegrityOutcome.AccessDenied || level.Level > _integrity.OwnLevel;
        var name = OwnerProcess.FileNameOf(_processes.GetProcessImagePath(process));
        return Result.Success(new ForegroundTarget(window, (int)process, name, blocks));
    }

    private static Result<ForegroundTarget> NotFound(string message) =>
        Result.Failure<ForegroundTarget>(new ResultError(ErrorCode.TargetNotFound, message));
}
