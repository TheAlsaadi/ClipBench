using ClipBench.Core.Screen;
using ClipBench.Platform.Windows.Tray;

namespace ClipBench.Platform.Windows.IntegrationTests.Tray;

public class TrayMessageTests
{
    private const int NinSelect = 0x400;
    private const int NinKeySelect = 0x401;
    private const int ContextMenu = 0x7B;
    private const int LeftButtonUp = 0x202;
    private const int MouseMove = 0x200;

    // The low word holds the notification and the high word the icon id.
    private static nint Params(int notification, int iconId) => (nint)(((long)iconId << 16) | (uint)(notification & 0xFFFF));

    // The low word holds x and the high word y, each a signed 16-bit number.
    private static nint Point(int x, int y) => (nint)(((long)(short)y << 16) | (ushort)(short)x);

    [Theory]
    [InlineData(NinSelect)]
    [InlineData(NinKeySelect)]
    public void A_click_or_the_keyboard_selects_the_icon(int notification)
    {
        var message = TrayMessage.Decode(Point(10, 20), Params(notification, 7));

        message.Event.ShouldBe(TrayEvent.Select);
        message.IconId.ShouldBe(7u);
    }

    [Fact]
    public void The_menu_is_asked_for_with_the_context_menu_message_and_knows_where()
    {
        var message = TrayMessage.Decode(Point(1500, 1010), Params(ContextMenu, 1));

        message.Event.ShouldBe(TrayEvent.ContextMenu);
        message.Anchor.ShouldBe(new PixelPoint(1500, 1010));
    }

    [Fact]
    public void Screen_positions_left_of_or_above_the_main_monitor_are_negative()
    {
        var message = TrayMessage.Decode(Point(-1700, -20), Params(ContextMenu, 1));

        message.Anchor.ShouldBe(new PixelPoint(-1700, -20));
    }

    [Theory]
    [InlineData(LeftButtonUp)]
    [InlineData(MouseMove)]
    [InlineData(0x201)]
    [InlineData(0x205)]
    [InlineData(0x0)]
    [InlineData(0xFFFF)]
    public void Everything_else_is_ignored(int notification)
    {
        TrayMessage.Decode(Point(5, 5), Params(notification, 1)).Event.ShouldBe(TrayEvent.Ignored);
    }

    [Fact]
    public void Bits_above_the_two_words_are_ignored()
    {
        // A 64-bit parameter may carry garbage above the low 32 bits; only the two words count.
        var noisy = (nint)(0x7FFF_0000_0000_0000L | (long)Params(NinSelect, 3));

        var message = TrayMessage.Decode(Point(1, 2), noisy);

        message.Event.ShouldBe(TrayEvent.Select);
        message.IconId.ShouldBe(3u);
    }

    // --- text limits ---

    [Fact]
    public void Text_that_fits_is_left_alone()
    {
        TrayText.Fit("ClipBench · capturing", TrayText.MaxToolTip).ShouldBe("ClipBench · capturing");
        TrayText.Fit(new string('a', 127), TrayText.MaxToolTip).Length.ShouldBe(127);
    }

    [Fact]
    public void Text_that_is_too_long_is_cut_with_an_ellipsis_and_fits_the_limit()
    {
        var fitted = TrayText.Fit(new string('a', 300), TrayText.MaxToolTip);

        fitted.Length.ShouldBe(127);
        fitted.ShouldEndWith("…");
        fitted[..126].ShouldBe(new string('a', 126));
    }

    [Fact]
    public void A_pair_of_utf16_units_is_never_split()
    {
        var text = new string('a', 125) + "😀😀";

        var fitted = TrayText.Fit(text, 127);

        fitted.ShouldBe(new string('a', 125) + "…");
        fitted.Length.ShouldBeLessThanOrEqualTo(127);
    }

    [Fact]
    public void The_limits_are_the_ones_of_the_notification_area()
    {
        TrayText.MaxToolTip.ShouldBe(127);
        TrayText.MaxTitle.ShouldBe(63);
        TrayText.MaxMessage.ShouldBe(255);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_stays_nothing(string? text)
    {
        TrayText.Fit(text!, 10).ShouldBe(string.Empty);
    }
}
