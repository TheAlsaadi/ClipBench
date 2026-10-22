using ClipBench.Core.Screen;
using ClipBench.Core.Tray;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ClipBench.Platform.Windows.Tray;

/// <summary>
/// The native popup menu. It is documented, keyboard accessible and always on top of the taskbar, and it follows the
/// system theme less closely than a XAML flyout would (ADR 0014).
/// </summary>
internal sealed unsafe class Win32TrayMenu : ITrayMenu
{
    /// <summary>How an item is described to Windows: a separator, or text with a tick when it is checked.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The flags for AppendMenu.</returns>
    internal static MENU_ITEM_FLAGS FlagsFor(TrayMenuItem item) => item switch
    {
        { IsSeparator: true } => MENU_ITEM_FLAGS.MF_SEPARATOR,
        { IsChecked: true } => MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_CHECKED,
        _ => MENU_ITEM_FLAGS.MF_STRING,
    };

    /// <inheritdoc/>
    public int Show(nint window, PixelPoint at, IReadOnlyList<TrayMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var menu = PInvoke.CreatePopupMenu();
        if (menu.IsNull)
        {
            return 0;
        }

        try
        {
            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    _ = PInvoke.AppendMenu(menu, FlagsFor(item), 0, default(PCWSTR));
                    continue;
                }

                fixed (char* text = item.Text)
                {
                    _ = PInvoke.AppendMenu(menu, FlagsFor(item), (nuint)item.Id, new PCWSTR(text));
                }

                if (item.IsDefault)
                {
                    _ = PInvoke.SetMenuDefaultItem(menu, (uint)item.Id, 0);
                }
            }

            var owner = new HWND(window);

            // Without this the menu would not close when the user clicks somewhere else.
            _ = PInvoke.SetForegroundWindow(owner);
            var chosen = PInvoke.TrackPopupMenuEx(
                menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN),
                at.X,
                at.Y,
                owner,
                null);

            // Windows asks for an empty message afterwards so that the menu does not reappear on the next click.
            _ = PInvoke.PostMessage(owner, PInvoke.WM_NULL, default, default);
            return chosen.Value;
        }
        finally
        {
            _ = PInvoke.DestroyMenu(menu);
        }
    }
}
