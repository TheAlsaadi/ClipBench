using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace ClipBench.Platform.Windows.Hotkeys;

/// <summary>The real RegisterHotKey and UnregisterHotKey, for the window that receives the presses.</summary>
internal sealed class Win32HotkeyApi(nint window) : IHotkeyApi
{
    private readonly HWND _window = new(window);

    /// <inheritdoc/>
    public bool Register(int id, uint modifiers, uint virtualKey, out int error)
    {
        var registered = PInvoke.RegisterHotKey(_window, id, (HOT_KEY_MODIFIERS)modifiers, virtualKey);
        error = registered ? 0 : Marshal.GetLastPInvokeError();
        return registered;
    }

    /// <inheritdoc/>
    public bool Unregister(int id) => PInvoke.UnregisterHotKey(_window, id);
}
