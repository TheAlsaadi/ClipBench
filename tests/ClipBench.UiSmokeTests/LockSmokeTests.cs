using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace ClipBench.UiSmokeTests;

/// <summary>
/// Drives the real app with the real mouse and keyboard: the lock that keeps the popup on screen, and the handle that moves it.
/// They need a desktop that nobody is using, so they are tagged as integration tests and run on demand.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SharedDesktop.Name)]
public sealed class LockSmokeTests(AppUnderTest app) : IClassFixture<AppUnderTest>
{
    private const string Invoice = "Invoice 2026-10 for ACME Corp";

    [Fact]
    public void A_locked_popup_stays_when_another_window_is_used_and_a_click_on_a_clip_pastes_into_that_window()
    {
        app.ClosePopup();
        using var target = PasteTargetWindow.Create();
        if (!target.TryActivate())
        {
            Assert.Skip("Windows would not let the test window take focus (it protects the window you are working in), so no key was sent. Run this test from a terminal that is in front.");
        }

        var popup = app.OpenPopup();
        PointClick(Control(popup, "LockButton").Center());
        AppUnderTest.WaitFor(() => IsLocked(popup), 10_000).ShouldBeTrue("the lock button did not lock the popup");

        // The program in front is used: the popup is not put away, and it is still on top of it.
        target.TryActivate().ShouldBeTrue("the test window could not be brought back to the front");
        Thread.Sleep(800);
        app.FindPopup().ShouldNotBeNull("clicking another window put a locked popup away");

        target.Clear();
        PointClick(RowWith(popup, "Invoice").Center());

        AppUnderTest.WaitFor(() => target.Text == Invoice, 15_000).ShouldBeTrue($"a click on a clip did not paste into the window in front. It holds: \"{target.Text}\"");
        target.IsInFront.ShouldBeTrue("the click on the popup took the focus away from the window it pasted into");
        app.FindPopup().ShouldNotBeNull("a paste put a locked popup away");

        // And it can paste again, since it stays.
        target.Clear();
        PointClick(RowWith(popup, "Invoice").Center());
        AppUnderTest.WaitFor(() => target.Text == Invoice, 15_000).ShouldBeTrue($"a second paste from the locked popup did not arrive. It holds: \"{target.Text}\"");

        PointClick(Control(popup, "CloseButton").Center());
        AppUnderTest.WaitFor(() => app.FindPopup() is null, 10_000).ShouldBeTrue("the close button did not close a locked popup");
        var reopened = app.OpenPopup();
        IsLocked(reopened).ShouldBeFalse("a popup that was closed must open unlocked");
        app.ClosePopup();
    }

    [Fact]
    public void Clicking_the_search_box_of_a_locked_popup_takes_the_focus_so_typing_works_and_Esc_gives_it_back()
    {
        app.ClosePopup();
        using var target = PasteTargetWindow.Create();
        if (!target.TryActivate())
        {
            Assert.Skip("Windows would not let the test window take focus, so no key was sent. Run this test from a terminal that is in front.");
        }

        var popup = app.OpenPopup();
        PointClick(Control(popup, "LockButton").Center());
        AppUnderTest.WaitFor(() => IsLocked(popup), 10_000).ShouldBeTrue("the lock button did not lock the popup");
        target.TryActivate().ShouldBeTrue();
        Thread.Sleep(500);

        PointClick(AppUnderTest.SearchBox(popup).Center());
        AppUnderTest.WaitFor(() => AppUnderTest.SearchBox(popup).Properties.HasKeyboardFocus.ValueOrDefault, 10_000)
            .ShouldBeTrue("a click in the search box of a locked popup did not give it the cursor. " + app.Describe());
        Keyboard.Type("invoice");
        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length == 1, 10_000).ShouldBeTrue("typing in a locked popup did not search");

        Keyboard.Type(VirtualKeyShort.ESCAPE);

        app.FindPopup().ShouldNotBeNull("Esc closed a locked popup");
        AppUnderTest.WaitFor(() => target.IsInFront, 10_000).ShouldBeTrue("Esc did not give the focus back to the window the popup was opened from");
        PointClick(Control(popup, "CloseButton").Center());
        AppUnderTest.WaitFor(() => app.FindPopup() is null, 10_000).ShouldBeTrue("the close button did not close a locked popup");
    }

    [Fact]
    public void The_shortcut_takes_up_a_locked_popup_and_gives_the_focus_back_when_it_is_pressed_in_it()
    {
        app.ClosePopup();
        using var target = PasteTargetWindow.Create();
        if (!target.TryActivate())
        {
            Assert.Skip("Windows would not let the test window take focus, so no key was sent. Run this test from a terminal that is in front.");
        }

        var popup = app.OpenPopup();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_K);
        AppUnderTest.WaitFor(() => IsLocked(popup), 10_000).ShouldBeTrue("Ctrl+K did not lock the popup");
        target.TryActivate().ShouldBeTrue();
        Thread.Sleep(500);

        Keyboard.Type(VirtualKeyShort.F24);
        AppUnderTest.WaitFor(() => !target.IsInFront, 10_000).ShouldBeTrue("the shortcut did not bring the locked popup to the front");
        AppUnderTest.WaitFor(() => AppUnderTest.SearchBox(popup).Properties.HasKeyboardFocus.ValueOrDefault, 10_000)
            .ShouldBeTrue("the shortcut did not take up the locked popup");

        Keyboard.Type(VirtualKeyShort.F24);
        AppUnderTest.WaitFor(() => target.IsInFront, 10_000).ShouldBeTrue("the shortcut pressed in the locked popup did not give the focus back");
        app.FindPopup().ShouldNotBeNull("the shortcut put a locked popup away");

        // Unlocking makes it an ordinary popup again: it has the focus, and a click elsewhere puts it away.
        Keyboard.Type(VirtualKeyShort.F24);
        AppUnderTest.WaitFor(() => !target.IsInFront, 10_000).ShouldBeTrue("the shortcut did not bring the locked popup to the front a second time");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_K);
        AppUnderTest.WaitFor(() => !IsLocked(popup), 10_000).ShouldBeTrue("Ctrl+K did not unlock the popup. " + app.Describe() + "; target in front: " + target.IsInFront);
        target.TryActivate().ShouldBeTrue();
        AppUnderTest.WaitFor(() => app.FindPopup() is null, 10_000).ShouldBeTrue("an unlocked popup stayed after another window was used");
    }

    [Fact]
    public void The_popup_follows_the_mouse_when_its_handle_is_dragged_and_stays_where_it_is_put_while_locked()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();
        var before = popup.BoundingRectangle;
        var handle = Control(popup, "DragHandle");

        // Away from the corner of the screen it is next to, whichever it is.
        var screen = System.Windows.Forms.Screen.FromPoint(before.Location).WorkingArea;
        var dx = before.X > screen.X + 300 ? -200 : 200;
        var dy = before.Y > screen.Y + 200 ? -100 : 100;
        AppUnderTest.DragBy(handle.Center(), dx, dy);

        var after = popup.BoundingRectangle;
        AppUnderTest.SaveScreenshot(popup, "popup-dragged");
        Math.Abs(after.X - before.X).ShouldBeGreaterThan(Math.Abs(dx) / 2, $"the popup did not follow the mouse sideways: {before} -> {after}");
        Math.Abs(after.Y - before.Y).ShouldBeGreaterThan(Math.Abs(dy) / 2, $"the popup did not follow the mouse up or down: {before} -> {after}");
        Math.Sign(after.X - before.X).ShouldBe(Math.Sign(dx));
        Math.Sign(after.Y - before.Y).ShouldBe(Math.Sign(dy));
        AppUnderTest.SearchBox(popup).Properties.HasKeyboardFocus.Value.ShouldBeTrue("moving the popup took the cursor out of the search box");

        // Locked, it can be moved too, and the shortcut brings it back to the same place instead of moving it.
        PointClick(Control(popup, "LockButton").Center());
        AppUnderTest.WaitFor(() => IsLocked(popup), 10_000).ShouldBeTrue();
        AppUnderTest.DragBy(Control(popup, "DragHandle").Center(), -dx / 2, -dy / 2);
        var placed = popup.BoundingRectangle;
        Math.Abs(placed.X - after.X).ShouldBeGreaterThan(Math.Abs(dx) / 4, $"a locked popup could not be moved: {after} -> {placed}");
        Keyboard.Type(VirtualKeyShort.F24);
        Thread.Sleep(500);
        popup.BoundingRectangle.ShouldBe(placed, "the shortcut moved a locked popup");
        PointClick(Control(popup, "CloseButton").Center());
        AppUnderTest.WaitFor(() => app.FindPopup() is null, 10_000).ShouldBeTrue();
    }

    private static bool IsLocked(Window popup) =>
        Control(popup, "LockButton").Patterns.Toggle.PatternOrDefault?.ToggleState.ValueOrDefault == ToggleState.On;

    // A click the way a hand makes it: the button is pressed, held for a moment and let go.
    private static void PointClick(System.Drawing.Point point)
    {
        AppUnderTest.PointAt(point);
        Mouse.Down(MouseButton.Left);
        Thread.Sleep(150);
        Mouse.Up(MouseButton.Left);
    }

    private static AutomationElement Control(Window popup, string automationId) =>
        popup.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) ?? throw new InvalidOperationException($"The popup has no {automationId}.");

    private static AutomationElement RowWith(Window popup, string text) =>
        AppUnderTest.Rows(popup).First(row => row.Name.Contains(text, StringComparison.Ordinal));
}
