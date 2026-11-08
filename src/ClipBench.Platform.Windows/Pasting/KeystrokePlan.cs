using ClipBench.Core.Hotkeys;

namespace ClipBench.Platform.Windows.Pasting;

/// <summary>The key events that make a paste.</summary>
internal static class KeystrokePlan
{
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;

    /// <summary>
    /// Ctrl+V. When the user already holds Ctrl (queue paste: they hold it and tap V), Ctrl is left down and only V
    /// is tapped: releasing it would make their next tap a plain V.
    /// </summary>
    /// <param name="held">The modifier keys the user is holding.</param>
    /// <returns>The events, in order.</returns>
    public static KeyEvent[] CtrlV(HotkeyModifiers held) => (held & HotkeyModifiers.Control) != 0
        ? [new KeyEvent(VkV, false), new KeyEvent(VkV, true)]
        : [new KeyEvent(VkControl, false), new KeyEvent(VkV, false), new KeyEvent(VkV, true), new KeyEvent(VkControl, true)];

    /// <summary>
    /// The key-ups that let go of every key pressed in the first <paramref name="accepted"/> events of a plan and not
    /// let go of within them, last pressed first. Windows takes events in order and stops at the first it refuses, so
    /// a short count leaves those keys logically down, and a Ctrl stuck down would turn the user's next keys into shortcuts.
    /// </summary>
    /// <param name="plan">The events that were sent.</param>
    /// <param name="accepted">How many of them Windows took.</param>
    /// <returns>The key-ups to send, or none.</returns>
    public static KeyEvent[] LetGoAfter(ReadOnlySpan<KeyEvent> plan, int accepted)
    {
        var down = new List<ushort>();
        foreach (var key in plan[..Math.Clamp(accepted, 0, plan.Length)])
        {
            if (key.KeyUp)
            {
                down.Remove(key.VirtualKey);
            }
            else if (!down.Contains(key.VirtualKey))
            {
                down.Add(key.VirtualKey);
            }
        }

        down.Reverse();
        return [.. down.Select(key => new KeyEvent(key, true))];
    }
}
