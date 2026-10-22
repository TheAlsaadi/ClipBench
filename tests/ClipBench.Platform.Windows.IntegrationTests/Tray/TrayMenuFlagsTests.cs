using ClipBench.Core.Tray;
using ClipBench.Platform.Windows.Tray;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ClipBench.Platform.Windows.IntegrationTests.Tray;

/// <summary>How each menu item is described to Windows. The menu itself is modal, so it is checked by hand.</summary>
public class TrayMenuFlagsTests
{
    [Fact]
    public void A_separator_is_a_separator()
    {
        Win32TrayMenu.FlagsFor(TrayMenuItem.Separator).ShouldBe(MENU_ITEM_FLAGS.MF_SEPARATOR);
    }

    [Fact]
    public void An_item_is_text_without_a_tick()
    {
        Win32TrayMenu.FlagsFor(new TrayMenuItem(2, "Pause capturing")).ShouldBe(MENU_ITEM_FLAGS.MF_STRING);
    }

    [Fact]
    public void A_ticked_item_shows_its_tick()
    {
        Win32TrayMenu.FlagsFor(new TrayMenuItem(7, "Restore clipboard after paste", IsChecked: true))
            .ShouldBe(MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_CHECKED);
    }
}
