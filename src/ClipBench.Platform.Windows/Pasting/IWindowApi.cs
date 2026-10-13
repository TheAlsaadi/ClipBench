using ClipBench.Core.Hotkeys;

namespace ClipBench.Platform.Windows.Pasting;

/// <summary>The window calls that decide where a paste goes.</summary>
internal interface IWindowApi
{
    /// <summary>The window that has focus.</summary>
    /// <returns>The handle, or 0 when none has.</returns>
    nint GetForegroundWindow();

    /// <summary>Whether a window still exists.</summary>
    /// <param name="window">The handle.</param>
    /// <returns>True when it does.</returns>
    bool IsWindow(nint window);

    /// <summary>
    /// Asks Windows to give a window focus. Windows may refuse (it protects the window the user is working in),
    /// so the answer is not trusted: callers check <see cref="GetForegroundWindow"/> afterwards.
    /// </summary>
    /// <param name="window">The handle.</param>
    /// <returns>True when Windows says it did.</returns>
    bool SetForegroundWindow(nint window);

    /// <summary>The process that owns a window.</summary>
    /// <param name="window">The handle.</param>
    /// <returns>The process id, or 0 when the window is gone.</returns>
    uint GetWindowProcessId(nint window);
}

/// <summary>One key going down or up.</summary>
/// <param name="VirtualKey">The Windows virtual-key code.</param>
/// <param name="KeyUp">True for the release.</param>
internal readonly record struct KeyEvent(ushort VirtualKey, bool KeyUp);

/// <summary>The keyboard, for sending a paste.</summary>
internal interface IKeyboardApi
{
    /// <summary>Which modifier keys are physically held down right now.</summary>
    /// <returns>The modifiers.</returns>
    HotkeyModifiers GetHeldModifiers();

    /// <summary>Sends key events to the window that has focus, as if typed.</summary>
    /// <param name="events">The events, in order.</param>
    /// <returns>How many Windows accepted. Fewer than were sent means the input was blocked.</returns>
    int Send(ReadOnlySpan<KeyEvent> events);
}

/// <summary>How much trust a process runs with, which decides whether Windows lets keystrokes reach it.</summary>
internal interface IProcessIntegrity
{
    /// <summary>ClipBench's own level.</summary>
    int OwnLevel { get; }

    /// <summary>Asks for the level of another process.</summary>
    /// <param name="processId">The process id.</param>
    /// <returns>The level, or why it is not known.</returns>
    IntegrityResult Query(uint processId);
}

/// <summary>Whether an integrity level is known.</summary>
internal enum IntegrityOutcome
{
    /// <summary>The level was read.</summary>
    Known,

    /// <summary>Windows refused to say. Processes with more rights than ClipBench refuse, so this means "probably higher".</summary>
    AccessDenied,

    /// <summary>The process no longer exists.</summary>
    NotFound,
}

/// <summary>The answer to an integrity question.</summary>
/// <param name="Outcome">Whether the level is known.</param>
/// <param name="Level">The level (Windows' mandatory label RID: low 0x1000, medium 0x2000, high 0x3000, system 0x4000) when known.</param>
internal readonly record struct IntegrityResult(IntegrityOutcome Outcome, int Level = 0);
