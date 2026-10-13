using ClipBench.Core.Hotkeys;
using ClipBench.Core.Results;

namespace ClipBench.Core.Tests.Hotkeys;

public class HotkeyTests
{
    // --- parsing what the user typed ---

    [Theory]
    [InlineData("Win+Alt+V", HotkeyModifiers.Windows | HotkeyModifiers.Alt, 0x56)]
    [InlineData("Ctrl+Shift+Space", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x20)]
    [InlineData("Ctrl+Alt+F12", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x7B)]
    [InlineData("Alt+1", HotkeyModifiers.Alt, 0x31)]
    [InlineData("Ctrl+Enter", HotkeyModifiers.Control, 0x0D)]
    [InlineData("Win+PageDown", HotkeyModifiers.Windows, 0x22)]
    [InlineData("Win+Left", HotkeyModifiers.Windows, 0x25)]
    public void A_typed_shortcut_is_understood(string typed, HotkeyModifiers modifiers, int virtualKey)
    {
        var result = Hotkey.Parse(typed);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Modifiers.ShouldBe(modifiers);
        result.Value.VirtualKey.ShouldBe(virtualKey);
    }

    [Theory]
    [InlineData("win+alt+v")]
    [InlineData("WIN+ALT+V")]
    [InlineData(" Win + Alt + V ")]
    [InlineData("Alt+Win+V")]
    [InlineData("Windows+Alt+V")]
    public void Case_spacing_order_and_the_long_name_of_a_modifier_do_not_matter(string typed)
    {
        Hotkey.Parse(typed).Value.ShouldBe(new Hotkey(HotkeyModifiers.Windows | HotkeyModifiers.Alt, 0x56));
    }

    [Theory]
    [InlineData("Control+K", HotkeyModifiers.Control)]
    [InlineData("Ctrl+K", HotkeyModifiers.Control)]
    [InlineData("Esc+Ctrl", HotkeyModifiers.Control)]
    public void The_key_may_come_before_the_modifiers_and_ctrl_has_two_names(string typed, HotkeyModifiers expected)
    {
        var result = Hotkey.Parse(typed);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Modifiers.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("+V")]
    [InlineData("Ctrl++V")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Win")]
    [InlineData("Ctrl+V+C")]
    [InlineData("Ctrl+Ctrl+V")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+VV")]
    [InlineData("Ctrl+10")]
    public void Text_that_is_not_a_shortcut_is_rejected_with_a_clear_error(string typed)
    {
        var result = Hotkey.Parse(typed);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.HotkeyInvalid);
        result.Error.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("V")]
    [InlineData("Shift+V")]
    [InlineData("Space")]
    [InlineData("Enter")]
    public void A_shortcut_a_user_chooses_cannot_take_a_plain_typing_key(string typed)
    {
        // Registering V, Shift+V or Space globally would stop the user typing in every program.
        var result = Hotkey.Parse(typed);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.HotkeyInvalid);
    }

    [Theory]
    [InlineData("F9")]
    [InlineData("Shift+F9")]
    [InlineData("F24")]
    public void A_function_key_is_fine_without_a_strong_modifier(string typed)
    {
        Hotkey.Parse(typed).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_failed_parse_does_not_repeat_the_typed_text_beyond_what_the_user_needs()
    {
        // Typed text can be pasted by accident, so the message must not echo a long input back.
        var result = Hotkey.Parse(new string('x', 5000));

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.Length.ShouldBeLessThan(300);
    }

    // --- showing a shortcut ---

    [Theory]
    [InlineData(HotkeyModifiers.Windows | HotkeyModifiers.Alt, 0x56, "Win+Alt+V")]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x20, "Ctrl+Shift+Space")]
    [InlineData(HotkeyModifiers.Windows | HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x70, "Win+Ctrl+Alt+Shift+F1")]
    [InlineData(HotkeyModifiers.None, 0x1B, "Esc")]
    [InlineData(HotkeyModifiers.Control, 0x2E, "Ctrl+Delete")]
    public void A_shortcut_is_shown_with_a_fixed_modifier_order(HotkeyModifiers modifiers, int virtualKey, string expected)
    {
        new Hotkey(modifiers, virtualKey).ToString().ShouldBe(expected);
    }

    [Fact]
    public void Every_named_key_survives_being_shown_and_read_back()
    {
        foreach (var name in Hotkey.KeyNames)
        {
            var typed = "Ctrl+" + name;

            var parsed = Hotkey.Parse(typed);

            parsed.IsSuccess.ShouldBeTrue(typed);
            parsed.Value.ToString().ShouldBe(typed);
        }
    }

    [Fact]
    public void The_named_keys_are_the_letters_digits_function_keys_and_the_navigation_keys()
    {
        // 26 letters, 10 digits, F1 to F24, and Space, Enter, Esc, Tab, Backspace, Insert, Delete, Home, End,
        // PageUp, PageDown and the four arrows.
        Hotkey.KeyNames.Count.ShouldBe(26 + 10 + 24 + 15);
        Hotkey.KeyNames.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(Hotkey.KeyNames.Count);
    }

    // --- building one in code ---

    [Fact]
    public void Two_shortcuts_with_the_same_modifiers_and_key_are_equal()
    {
        new Hotkey(HotkeyModifiers.Control, 0x56).ShouldBe(new Hotkey(HotkeyModifiers.Control, 0x56));
        new Hotkey(HotkeyModifiers.Control, 0x56).ShouldNotBe(new Hotkey(HotkeyModifiers.Alt, 0x56));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0x07)]   // not a named key
    [InlineData(0x10)]   // Shift itself
    [InlineData(0x11)]   // Control itself
    [InlineData(0x5B)]   // the left Windows key itself
    [InlineData(0x100)]
    public void A_key_that_is_not_one_of_the_named_keys_is_a_bug(int virtualKey)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Hotkey(HotkeyModifiers.Control, virtualKey));
    }

    [Fact]
    public void A_modifier_value_that_does_not_exist_is_a_bug()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Hotkey((HotkeyModifiers)0x40, 0x56));
    }

    [Fact]
    public void The_default_hotkey_is_the_one_the_owner_chose_in_ADR_0004()
    {
        Hotkey.Default.ToString().ShouldBe("Win+Alt+V");
    }
}
