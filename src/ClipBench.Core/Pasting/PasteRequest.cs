using ClipBench.Core.Clips;

namespace ClipBench.Core.Pasting;

/// <summary>What to paste and where.</summary>
public sealed record PasteRequest
{
    /// <summary>The longest wait before the previous clipboard is put back.</summary>
    public static readonly TimeSpan MaxRestoreDelay = TimeSpan.FromSeconds(10);

    /// <summary>What to paste.</summary>
    public required ClipContent Content
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The window to paste into, or null to paste into whatever has focus (queue mode, where the user is already there).</summary>
    public WindowHandle? Target { get; init; }

    /// <summary>True to put the clipboard back the way it was after the paste.</summary>
    public bool RestorePreviousClipboard { get; init; } = true;

    /// <summary>How long to wait after sending Ctrl+V before restoring, so the target has read the clipboard first. From nothing to <see cref="MaxRestoreDelay"/>.</summary>
    public TimeSpan RestoreDelay
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxRestoreDelay);
            field = value;
        }
    } = TimeSpan.FromMilliseconds(250);

    /// <summary>Printing never includes the content: requests end up in logs by accident.</summary>
    /// <returns>The kind and size of the content.</returns>
    public override string ToString() => $"Paste request ({Content.Format}, {Content.SizeInBytes} bytes)";
}
