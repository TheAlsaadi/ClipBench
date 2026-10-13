using Windows.Win32;
using Windows.Win32.Foundation;

namespace ClipBench.Platform.Windows.Clipboard;

/// <summary>The real AddClipboardFormatListener and RemoveClipboardFormatListener, for the window that receives the messages.</summary>
internal sealed class Win32ClipboardNotifications(nint window) : IClipboardNotifications
{
    private readonly HWND _window = new(window);

    /// <inheritdoc/>
    public bool Subscribe() => PInvoke.AddClipboardFormatListener(_window);

    /// <inheritdoc/>
    public void Unsubscribe() => PInvoke.RemoveClipboardFormatListener(_window);
}
