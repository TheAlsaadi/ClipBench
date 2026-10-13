using ClipBench.Core.Clips;
using ClipBench.Core.Pasting;

namespace ClipBench.Core.Tests.Pasting;

public class PasteTargetTests
{
    [Fact]
    public void No_window_is_a_handle_of_zero()
    {
        WindowHandle.None.Value.ShouldBe(0);
        WindowHandle.None.IsNone.ShouldBeTrue();
        default(WindowHandle).IsNone.ShouldBeTrue();
        new WindowHandle(0x1A2B).IsNone.ShouldBeFalse();
    }

    [Fact]
    public void Handles_with_the_same_value_are_equal()
    {
        new WindowHandle(5).ShouldBe(new WindowHandle(5));
        new WindowHandle(5).ShouldNotBe(new WindowHandle(6));
    }

    [Fact]
    public void A_handle_prints_in_hex_so_it_can_be_matched_with_tools()
    {
        new WindowHandle(0x1A2B).ToString().ShouldBe("Window 0x1A2B");
        WindowHandle.None.ToString().ShouldBe("No window");
    }

    [Fact]
    public void A_paste_request_defaults_to_restoring_the_clipboard_after_a_quarter_of_a_second()
    {
        var request = new PasteRequest { Content = new TextContent("x") };

        request.RestorePreviousClipboard.ShouldBeTrue();
        request.RestoreDelay.ShouldBe(TimeSpan.FromMilliseconds(250));
        request.Target.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(250)]
    [InlineData(10_000)]
    public void A_restore_delay_from_nothing_to_ten_seconds_is_accepted(int milliseconds)
    {
        var request = new PasteRequest { Content = new TextContent("x"), RestoreDelay = TimeSpan.FromMilliseconds(milliseconds) };

        request.RestoreDelay.TotalMilliseconds.ShouldBe(milliseconds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void A_restore_delay_outside_that_range_is_a_bug(int milliseconds)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new PasteRequest { Content = new TextContent("x"), RestoreDelay = TimeSpan.FromMilliseconds(milliseconds) });
    }

    [Fact]
    public void A_paste_request_prints_the_kind_and_size_but_never_the_content()
    {
        var request = new PasteRequest { Content = new TextContent("hunter2 is my password"), Target = new WindowHandle(9) };

        request.ToString().ShouldNotContain("hunter2");
        request.ToString().ShouldContain("Text");
    }

    [Fact]
    public void A_foreground_target_prints_neither_the_process_name_nor_anything_else_private()
    {
        var target = new ForegroundTarget(new WindowHandle(0x10), 1234, "keepass.exe", BlocksInputFromUs: true);

        target.ToString().ShouldNotContain("keepass");
        target.ToString().ShouldContain("0x10");
    }
}
