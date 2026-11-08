using ClipBench.Core.Hotkeys;
using ClipBench.Platform.Windows.Pasting;

namespace ClipBench.Platform.Windows.IntegrationTests.Pasting;

public class KeystrokePlanTests
{
    private const ushort Control = 0x11;
    private const ushort V = 0x56;

    [Fact]
    public void Paste_presses_control_then_v_and_lets_go_in_the_opposite_order()
    {
        KeystrokePlan.CtrlV(HotkeyModifiers.None).ShouldBe(
            [new KeyEvent(Control, false), new KeyEvent(V, false), new KeyEvent(V, true), new KeyEvent(Control, true)]);
    }

    [Fact]
    public void When_the_user_is_already_holding_control_only_v_is_pressed_so_control_stays_down_for_their_next_press()
    {
        // Queue paste: the user holds Ctrl and taps V repeatedly. Letting go of Ctrl here would make their
        // next tap a plain V.
        KeystrokePlan.CtrlV(HotkeyModifiers.Control).ShouldBe([new KeyEvent(V, false), new KeyEvent(V, true)]);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "Control")]
    [InlineData(2, "V Control")]
    [InlineData(3, "Control")]
    [InlineData(4, "")]
    public void When_Windows_takes_only_the_first_keys_the_keys_they_pressed_are_let_go_last_pressed_first(int accepted, string expected)
    {
        var plan = KeystrokePlan.CtrlV(HotkeyModifiers.None);

        KeystrokePlan.LetGoAfter(plan, accepted).ShouldBe(Ups(expected));
    }

    [Fact]
    public void When_the_user_holds_control_and_only_v_down_was_taken_v_is_let_go_and_control_is_left_to_the_user()
    {
        var plan = KeystrokePlan.CtrlV(HotkeyModifiers.Control);

        KeystrokePlan.LetGoAfter(plan, 1).ShouldBe([new KeyEvent(V, true)]);
    }

    private static KeyEvent[] Ups(string keys) =>
    [
        .. keys.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(key => new KeyEvent(key == "V" ? V : Control, true)),
    ];
}
