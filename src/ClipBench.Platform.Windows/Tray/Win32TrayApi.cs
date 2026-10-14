using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ClipBench.Platform.Windows.Tray;

/// <summary>The real notification-area calls.</summary>
internal sealed unsafe class Win32TrayApi : ITrayApi
{
    private const uint Version4 = 4;

    /// <inheritdoc/>
    public uint TaskbarCreatedMessage { get; } = PInvoke.RegisterWindowMessage("TaskbarCreated");

    /// <inheritdoc/>
    public nint LoadIcon(string path)
    {
        // The small icon size of the primary monitor is what the notification area draws.
        var width = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
        var height = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSMICON);
        fixed (char* file = path)
        {
            var handle = PInvoke.LoadImage(HINSTANCE.Null, new PCWSTR(file), GDI_IMAGE_TYPE.IMAGE_ICON, width, height, IMAGE_FLAGS.LR_LOADFROMFILE);
            return handle.IsNull ? 0 : (nint)handle.Value;
        }
    }

    /// <inheritdoc/>
    public void DestroyIcon(nint icon) => PInvoke.DestroyIcon(new HICON(icon));

    /// <inheritdoc/>
    public bool Add(nint window, uint iconId, uint callbackMessage, nint icon, string toolTip)
    {
        var data = Data(window, iconId, NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        data.uCallbackMessage = callbackMessage;
        data.hIcon = new HICON(icon);
        Copy(toolTip, data.szTip.AsSpan());

        return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data);
    }

    /// <inheritdoc/>
    public bool UseVersion4(nint window, uint iconId)
    {
        var data = Data(window, iconId, 0);
        data.Anonymous.uVersion = Version4;
        return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data);
    }

    /// <inheritdoc/>
    public bool Modify(nint window, uint iconId, string toolTip)
    {
        var data = Data(window, iconId, NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        Copy(toolTip, data.szTip.AsSpan());
        return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data);
    }

    /// <inheritdoc/>
    public bool Delete(nint window, uint iconId)
    {
        var data = Data(window, iconId, 0);
        return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
    }

    /// <inheritdoc/>
    public bool Notify(nint window, uint iconId, string title, string message)
    {
        var data = Data(window, iconId, NOTIFY_ICON_DATA_FLAGS.NIF_INFO);
        Copy(title, data.szInfoTitle.AsSpan());
        Copy(message, data.szInfo.AsSpan());
        data.dwInfoFlags = NOTIFY_ICON_INFOTIP_FLAGS.NIIF_INFO;
        return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data);
    }

    private static NOTIFYICONDATAW Data(nint window, uint iconId, NOTIFY_ICON_DATA_FLAGS flags) => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = new HWND(window),
        uID = iconId,
        uFlags = flags,
    };

    // The fixed-size text fields hold a terminated string. The caller has already cut the text to fit; this keeps room for the end mark.
    private static void Copy(string text, Span<char> target)
    {
        var length = Math.Min(text.Length, target.Length - 1);
        text.AsSpan(0, length).CopyTo(target);
        target[length] = '\0';
    }
}
