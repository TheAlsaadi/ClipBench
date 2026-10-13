using Windows.Win32;
using Windows.Win32.Foundation;

namespace ClipBench.Platform.Windows.Pasting;

/// <summary>The real window calls.</summary>
internal sealed unsafe class Win32WindowApi : IWindowApi
{
    /// <inheritdoc/>
    public nint GetForegroundWindow() => (nint)PInvoke.GetForegroundWindow().Value;

    /// <inheritdoc/>
    public bool IsWindow(nint window) => PInvoke.IsWindow(new HWND(window));

    /// <inheritdoc/>
    public bool SetForegroundWindow(nint window) => PInvoke.SetForegroundWindow(new HWND(window));

    /// <inheritdoc/>
    public uint GetWindowProcessId(nint window)
    {
        uint process = 0;
        var thread = PInvoke.GetWindowThreadProcessId(new HWND(window), &process);
        return thread == 0 ? 0 : process;
    }
}
