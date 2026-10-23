using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace ClipBench.UiSmokeTests;

/// <summary>
/// The transform picker, the boards and the snippets, driven through the real app with the real keyboard into a real
/// window. Like the other smoke tests they need a desktop nobody is using and run on demand. Each test uses names of its
/// own, so they do not depend on the order they run in.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SharedDesktop.Name)]
public sealed class TransformBoardSnippetSmokeTests(AppUnderTest app) : IClassFixture<AppUnderTest>
{
    [Fact]
    public void Shift_Enter_opens_the_picker_and_Enter_pastes_the_clip_with_the_chosen_transform_applied()
    {
        using var target = ActiveTarget();
        var popup = app.OpenPopup();
        FindOnly(popup, "invoice");

        Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.RETURN);
        WaitForFocus(popup, "TransformFilterBox");
        AppUnderTest.SaveScreenshot(popup, "transform-picker");
        Keyboard.Type("upper");
        Keyboard.Type(VirtualKeyShort.RETURN);

        AppUnderTest.WaitFor(() => app.FindPopup() is null, 10_000).ShouldBeTrue("the popup did not close");
        AppUnderTest.WaitFor(() => target.Text == "INVOICE 2026-10 FOR ACME CORP", 15_000).ShouldBeTrue($"the window holds: \"{target.Text}\"");
    }

    [Fact]
    public void Ctrl_Space_builds_a_chain_that_runs_in_order()
    {
        using var target = ActiveTarget();
        var popup = app.OpenPopup();
        FindOnly(popup, "invoice");

        Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.RETURN);
        WaitForFocus(popup, "TransformFilterBox");
        Keyboard.Type("upper");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SPACE);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type("url enc");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SPACE);
        AppUnderTest.WaitFor(() => ChainText(popup).Contains("URL encode", StringComparison.Ordinal), 10_000).ShouldBeTrue($"the chain says: \"{ChainText(popup)}\"");
        Keyboard.Type(VirtualKeyShort.RETURN);

        AppUnderTest.WaitFor(() => target.Text == "INVOICE%202026-10%20FOR%20ACME%20CORP", 15_000).ShouldBeTrue($"the window holds: \"{target.Text}\"");
    }

    [Fact]
    public void Esc_in_the_picker_goes_back_to_the_list_and_the_stored_clip_is_never_changed()
    {
        var popup = app.OpenPopup();
        FindOnly(popup, "invoice");
        Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.RETURN);
        WaitForFocus(popup, "TransformFilterBox");

        Keyboard.Type(VirtualKeyShort.ESCAPE);

        AppUnderTest.WaitFor(() => popup.FindFirstDescendant(cf => cf.ByAutomationId("TransformFilterBox")) is null, 10_000).ShouldBeTrue("the picker did not close");
        app.FindPopup().ShouldNotBeNull("one Esc must only close the picker");
        AppUnderTest.RowNames(popup).Single().ShouldContain("Invoice 2026-10 for ACME Corp");
        app.ClosePopup();
    }

    [Fact]
    public void A_board_is_made_a_clip_is_put_on_it_and_taken_off_again()
    {
        var popup = app.OpenPopup();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_B);
        AppUnderTest.WaitFor(() => SidebarNames(popup).Length > 0, 10_000).ShouldBeTrue("the sidebar did not appear");
        SidebarNames(popup).ShouldContain(name => name.StartsWith("History", StringComparison.Ordinal));

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_N);
        WaitForFocus(popup, "BoardNameBox");
        Keyboard.Type("Smoke board");
        Keyboard.Type(VirtualKeyShort.RETURN);
        AppUnderTest.WaitFor(() => SidebarNames(popup).Any(name => name.StartsWith("Board Smoke board", StringComparison.Ordinal)), 10_000).ShouldBeTrue("the board did not appear");
        AppUnderTest.SaveScreenshot(popup, "sidebar-with-board");

        ChooseEntry(popup, "History");
        FindOnly(popup, "meeting");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_D);
        Thread.Sleep(500);
        Keyboard.Type(VirtualKeyShort.RETURN);
        AppUnderTest.WaitFor(() => SidebarNames(popup).Any(name => name.StartsWith("Board Smoke board, 1 clip", StringComparison.Ordinal)), 10_000)
            .ShouldBeTrue($"the board does not hold the clip. The sidebar says: {string.Join(" | ", SidebarNames(popup))}");

        // Back in the search box, the board shows the clip on its own.
        ChooseEntry(popup, "Board Smoke board");
        AppUnderTest.WaitFor(() => AppUnderTest.RowNames(popup).Length == 0 || AppUnderTest.RowNames(popup).Length == 1, 5_000).ShouldBeTrue();
        Keyboard.Type(VirtualKeyShort.BACK, VirtualKeyShort.BACK);
        AppUnderTest.WaitFor(() => AppUnderTest.RowNames(popup).Length == 1, 10_000).ShouldBeTrue($"the board lists: {string.Join(" | ", AppUnderTest.RowNames(popup))}");
        AppUnderTest.RowNames(popup).Single().ShouldContain("Meeting notes");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_D);
        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length == 0, 10_000).ShouldBeTrue("the clip did not leave the board");
        app.ClosePopup();
    }

    [Fact]
    public void A_snippet_is_written_in_the_window_and_pasted_with_a_typed_value()
    {
        using var target = ActiveTarget();
        var popup = app.OpenPopup();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_B);
        AppUnderTest.WaitFor(() => SidebarNames(popup).Length > 0, 10_000).ShouldBeTrue("the sidebar did not appear");
        ChooseEntry(popup, "Snippets");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_E);
        Window? editor = null;
        AppUnderTest.WaitFor(() => (editor = app.FindWindow("ClipBench snippets")) is not null, 15_000).ShouldBeTrue("the snippets window did not open");
        AppUnderTest.WaitFor(() => editor!.FindFirstDescendant(cf => cf.ByAutomationId("SnippetNameBox"))?.Properties.HasKeyboardFocus.ValueOrDefault == true, 10_000)
            .ShouldBeTrue("the name box did not get the focus");

        Keyboard.Type("Smoke greeting");
        editor!.FindFirstDescendant(cf => cf.ByAutomationId("SnippetBodyBox"))!.AsTextBox().Enter("Hello {input:Name}, it is {date:yyyy}.");
        AppUnderTest.SaveScreenshot(editor, "snippets-window");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        AppUnderTest.WaitFor(() => editor.FindAllDescendants(cf => cf.ByAutomationId("SnippetNames")).Length > 0
                && editor.FindFirstDescendant(cf => cf.ByAutomationId("SnippetNames"))!.FindAllChildren().Any(item => item.Name == "Smoke greeting"), 10_000)
            .ShouldBeTrue("the snippet was not saved");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        AppUnderTest.WaitFor(() => app.FindWindow("ClipBench snippets") is null, 10_000).ShouldBeTrue("the snippets window did not hide");

        // The window is gone: paste the snippet from the popup into the target window.
        target.TryActivate().ShouldBeTrue("Windows would not let the test window take focus");
        popup = app.OpenPopup();
        ChooseEntry(popup, "Snippets");
        Keyboard.Type("greeting");
        AppUnderTest.WaitFor(() => SnippetRows(popup).Length == 1, 10_000).ShouldBeTrue("the snippet was not found by its name");
        Keyboard.Type(VirtualKeyShort.RETURN);
        WaitForFocus(popup, "Name");
        Keyboard.Type("Sam");
        Keyboard.Type(VirtualKeyShort.RETURN);

        AppUnderTest.WaitFor(() => target.Text == $"Hello Sam, it is {DateTime.Now:yyyy}.", 15_000).ShouldBeTrue($"the window holds: \"{target.Text}\"");
    }

    private PasteTargetWindow ActiveTarget()
    {
        app.ClosePopup();
        app.EndQueue();
        var target = PasteTargetWindow.Create();
        if (!target.TryActivate())
        {
            target.Dispose();
            Assert.Skip("Windows would not let the test window take focus (it protects the window you are working in), so no key was sent. Run this test from a terminal that is in front.");
        }

        return target;
    }

    // Types the search and waits for exactly one row, so Shift+Enter and the like act on a known clip.
    private static void FindOnly(Window popup, string search)
    {
        Keyboard.Type(search);
        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length == 1, 10_000).ShouldBeTrue($"no single clip matches \"{search}\"");
    }

    private static void WaitForFocus(Window popup, string automationIdOrName) =>
        AppUnderTest.WaitFor(
            () => popup.FindFirstDescendant(cf => cf.ByAutomationId(automationIdOrName)) is { } byId
                ? byId.Properties.HasKeyboardFocus.ValueOrDefault
                : popup.FindFirstDescendant(cf => cf.ByName(automationIdOrName)) is { } byName && byName.Properties.HasKeyboardFocus.ValueOrDefault,
            10_000).ShouldBeTrue($"{automationIdOrName} did not get the focus");

    private static string ChainText(Window popup) =>
        popup.FindFirstDescendant(cf => cf.ByAutomationId("TransformChain"))?.Properties.Name.ValueOrDefault ?? string.Empty;

    private static string[] SidebarNames(Window popup) =>
        popup.FindFirstDescendant(cf => cf.ByAutomationId("SidebarList"))?.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Select(item => item.Name).ToArray() ?? [];

    private static AutomationElement[] SnippetRows(Window popup) =>
        popup.FindFirstDescendant(cf => cf.ByAutomationId("SnippetList"))?.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)) ?? [];

    // The sidebar is chosen with Ctrl and the number of its entry, counted from 1.
    private static void ChooseEntry(Window popup, string startsWith)
    {
        var names = SidebarNames(popup);
        var index = Array.FindIndex(names, name => name.StartsWith(startsWith, StringComparison.Ordinal));
        index.ShouldBeGreaterThanOrEqualTo(0, $"no sidebar entry starts with \"{startsWith}\". The sidebar says: {string.Join(" | ", names)}");
        (index + 1).ShouldBeLessThanOrEqualTo(9, "Ctrl and a number reach the first nine entries only");
        VirtualKeyShort[] digits =
        [
            VirtualKeyShort.KEY_1, VirtualKeyShort.KEY_2, VirtualKeyShort.KEY_3, VirtualKeyShort.KEY_4, VirtualKeyShort.KEY_5,
            VirtualKeyShort.KEY_6, VirtualKeyShort.KEY_7, VirtualKeyShort.KEY_8, VirtualKeyShort.KEY_9,
        ];
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, digits[index]);
        Thread.Sleep(300);
    }
}
