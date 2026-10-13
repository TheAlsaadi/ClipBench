using System.Runtime.InteropServices;
using ClipBench.Core.Hotkeys;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace ClipBench.Platform.Windows.Pasting;

/// <summary>The real keyboard: which modifier keys are held, and SendInput. No hook is installed; ClipBench never sees any key but its own.</summary>
internal sealed class Win32KeyboardApi : IKeyboardApi
{
    /// <inheritdoc/>
    public HotkeyModifiers GetHeldModifiers()
    {
        var held = HotkeyModifiers.None;
        held |= IsDown(VIRTUAL_KEY.VK_CONTROL) ? HotkeyModifiers.Control : 0;
        held |= IsDown(VIRTUAL_KEY.VK_MENU) ? HotkeyModifiers.Alt : 0;
        held |= IsDown(VIRTUAL_KEY.VK_SHIFT) ? HotkeyModifiers.Shift : 0;
        held |= IsDown(VIRTUAL_KEY.VK_LWIN) || IsDown(VIRTUAL_KEY.VK_RWIN) ? HotkeyModifiers.Windows : 0;
        return held;
    }

    /// <inheritdoc/>
    public int Send(ReadOnlySpan<KeyEvent> events)
    {
        var inputs = new INPUT[events.Length];
        for (var i = 0; i < events.Length; i++)
        {
            inputs[i] = new INPUT
            {
                type = INPUT_TYPE.INPUT_KEYBOARD,
                Anonymous = new INPUT._Anonymous_e__Union
                {
                    ki = new KEYBDINPUT { wVk = (VIRTUAL_KEY)events[i].VirtualKey, dwFlags = events[i].KeyUp ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0 },
                },
            };
        }

        return (int)PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>());
    }

    // The high bit of the result is set while the key is down.
    private static bool IsDown(VIRTUAL_KEY key) => PInvoke.GetAsyncKeyState((int)key) < 0;
}
