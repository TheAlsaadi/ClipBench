using ClipBench.Core.Capture;
using ClipBench.Core.Hotkeys;

namespace ClipBench.Core.Tests.Hotkeys;

public class HotkeyEventTests
{
    [Fact]
    public void A_hotkey_press_names_the_registration_that_matched()
    {
        new HotkeyPressedEventArgs(new HotkeyId(7)).Id.ShouldBe(new HotkeyId(7));
    }

    [Fact]
    public void A_clipboard_change_carries_its_sequence_number()
    {
        new ClipboardChangedEventArgs(12345).SequenceNumber.ShouldBe(12345);
    }
}
