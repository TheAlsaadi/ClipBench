using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace ClipBench.Platform.Windows.IntegrationTests.Support;

/// <summary>Presses keys through the operating system, for tests of global shortcuts.</summary>
internal static class Keyboard
{
    /// <summary>
    /// Presses and releases one key. Tests use F24, which no keyboard has: a shortcut registered on it takes the
    /// press before any program sees it, so a test never types into the user's window.
    /// </summary>
    public static void Tap(VIRTUAL_KEY key)
    {
        ReadOnlySpan<INPUT> inputs = [Key(key, up: false), Key(key, up: true)];
        var sent = PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>());
        sent.ShouldBe((uint)inputs.Length, "Windows accepted both key events");
    }

    private static INPUT Key(VIRTUAL_KEY key, bool up) => new()
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            ki = new KEYBDINPUT { wVk = key, dwFlags = up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0 },
        },
    };
}
