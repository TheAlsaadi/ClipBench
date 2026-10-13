namespace ClipBench.Platform.Windows.Pasting;

/// <summary>The waits and limits of a paste.</summary>
internal sealed record PasteTiming
{
    /// <summary>How long to wait for the target window to get focus before giving up.</summary>
    public TimeSpan FocusTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How often to check whether the target has focus.</summary>
    public TimeSpan FocusPollInterval { get; init; } = TimeSpan.FromMilliseconds(20);

    /// <summary>A short wait after focus moved, so the window's own focus is ready before the keys arrive.</summary>
    public TimeSpan FocusSettle { get; init; } = TimeSpan.FromMilliseconds(30);

    /// <summary>How long to wait for the user to let go of Alt, Shift or Windows before giving up.</summary>
    public TimeSpan ModifierTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How often to check the modifier keys.</summary>
    public TimeSpan ModifierPollInterval { get; init; } = TimeSpan.FromMilliseconds(20);

    /// <summary>The most bytes of the user's clipboard to copy for restoring. More than this is not restored.</summary>
    public long SnapshotLimit { get; init; } = 64L * 1024 * 1024;
}
