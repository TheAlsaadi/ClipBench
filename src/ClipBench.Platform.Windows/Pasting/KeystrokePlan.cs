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
}
