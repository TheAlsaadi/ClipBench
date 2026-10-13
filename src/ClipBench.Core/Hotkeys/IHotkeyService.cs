using ClipBench.Core.Results;

namespace ClipBench.Core.Hotkeys;

/// <summary>Names one registered shortcut, so a press can be matched to it and it can be released.</summary>
/// <param name="Value">The number Windows knows the registration by.</param>
public readonly record struct HotkeyId(int Value);

/// <summary>Tells which registered shortcut was pressed.</summary>
/// <param name="id">The registration that matched.</param>
public sealed class HotkeyPressedEventArgs(HotkeyId id) : EventArgs
{
    /// <summary>The registration that matched.</summary>
    public HotkeyId Id { get; } = id;
}

/// <summary>
/// Global keyboard shortcuts. A shortcut is claimed from Windows with RegisterHotKey; no key hook is
/// installed, so ClipBench never sees any other key press.
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>Raised when a registered shortcut is pressed, on a thread-pool thread.</summary>
    event EventHandler<HotkeyPressedEventArgs>? Pressed;

    /// <summary>Claims a shortcut.</summary>
    /// <param name="hotkey">The shortcut.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The registration, or <see cref="ErrorCode.HotkeyUnavailable"/> when another program has the shortcut.</returns>
    Task<Result<HotkeyId>> RegisterAsync(Hotkey hotkey, CancellationToken cancellationToken);

    /// <summary>Gives a shortcut back to Windows.</summary>
    /// <param name="id">The registration from <see cref="RegisterAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>Success, or <see cref="ErrorCode.HotkeyNotRegistered"/> when the registration is unknown.</returns>
    Task<Result> UnregisterAsync(HotkeyId id, CancellationToken cancellationToken);
}
