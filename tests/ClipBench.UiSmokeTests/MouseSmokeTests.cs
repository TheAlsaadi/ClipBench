using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace ClipBench.UiSmokeTests;

/// <summary>
/// Drives the real app with the real mouse. They need a desktop that nobody is using, so they are tagged as integration
/// tests and run on demand, like the keyboard smoke tests. Each test uses its own clips, because the tests of a class
/// share one app.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SharedDesktop.Name)]
public sealed class MouseSmokeTests(AppUnderTest app) : IClassFixture<AppUnderTest>
{
    [Fact]
    public void Clicking_in_the_sidebar_leaves_the_cursor_in_the_search_box_so_typing_still_works()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();
        AppUnderTest.EnsureSidebar(popup);

        PointClick(AppUnderTest.SidebarEntries(popup)[1].Center());
        PointClick(AppUnderTest.SidebarEntries(popup)[0].Center());

        AppUnderTest.WaitFor(() => AppUnderTest.SearchBox(popup).Properties.HasKeyboardFocus.ValueOrDefault, 10_000)
            .ShouldBeTrue("a click in the sidebar took the cursor out of the search box");
        Keyboard.Type("invoice");
        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length == 1, 10_000).ShouldBeTrue("typing after a click in the sidebar did not search");
        app.ClosePopup();
    }

    [Fact]
    public void The_buttons_open_and_close_the_sidebar_and_close_the_popup()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();
        var wasOpen = AppUnderTest.SidebarEntries(popup).Length > 0;

        PointClick(Button(popup, "SidebarToggleButton").Center());
        AppUnderTest.WaitFor(() => (AppUnderTest.SidebarEntries(popup).Length > 0) != wasOpen, 10_000).ShouldBeTrue("the button did not toggle the sidebar");
        PointClick(Button(popup, "SidebarToggleButton").Center());
        AppUnderTest.WaitFor(() => (AppUnderTest.SidebarEntries(popup).Length > 0) == wasOpen, 10_000).ShouldBeTrue("the button did not toggle the sidebar back");

        PointClick(Button(popup, "CloseButton").Center());
        AppUnderTest.WaitFor(() => app.FindPopup() is null, 10_000).ShouldBeTrue("the close button did not close the popup");
    }

    [Fact]
    public void A_pin_button_shows_when_the_pointer_is_over_a_row_and_pins_that_clip()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();

        AppUnderTest.PointAt(RowWith(popup, "Pin me please").Center());
        AppUnderTest.SaveScreenshot(popup, "mouse-hover-row");

        // Only the row under the pointer shows its buttons. A list item does not list the parts of its template under it.
        AppUnderTest.WaitFor(() => popup.FindFirstDescendant(cf => cf.ByAutomationId("RowPinButton")) is not null, 10_000)
            .ShouldBeTrue("no pin button showed on the row under the pointer. " + app.Describe());
        PointClick(Button(popup, "RowPinButton").Center());

        AppUnderTest.WaitFor(() => RowWith(popup, "Pin me please").Name.Contains(", pinned", StringComparison.Ordinal), 10_000)
            .ShouldBeTrue("the clip was not pinned. Rows: " + string.Join(" | ", AppUnderTest.RowNames(popup)));
        AppUnderTest.SearchBox(popup).Properties.HasKeyboardFocus.Value.ShouldBeTrue("the click took the cursor out of the search box");
        app.ClosePopup();
    }

    [Fact]
    public void The_right_button_opens_a_menu_of_what_can_be_done_with_a_clip_and_it_deletes()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();

        PointRightClick(RowWith(popup, "Disposable clip").Center());
        Thread.Sleep(500);
        AppUnderTest.SaveScreenshot(popup, "mouse-right-click");
        AppUnderTest.WaitFor(() => app.FindMenuItem("Delete") is not null, 10_000).ShouldBeTrue("the right button did not open the menu. " + app.Describe());
        foreach (var wanted in new[] { "Paste", "Pin", "Add to a board" })
        {
            app.FindMenuItem(wanted).ShouldNotBeNull($"the menu has no \"{wanted}\"");
        }

        PointClick(app.FindMenuItem("Delete")!.Center());

        AppUnderTest.WaitFor(() => AppUnderTest.RowNames(popup).All(name => !name.Contains("Disposable clip", StringComparison.Ordinal)), 10_000)
            .ShouldBeTrue("the clip was not deleted. Rows: " + string.Join(" | ", AppUnderTest.RowNames(popup)));
        AppUnderTest.SearchBox(popup).Properties.HasKeyboardFocus.Value.ShouldBeTrue("the menu took the cursor out of the search box");
        app.ClosePopup();
    }

    [Fact]
    public void The_menu_key_opens_the_same_menu_for_the_highlighted_clip_and_Esc_closes_only_the_menu()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();

        Keyboard.Type(VirtualKeyShort.APPS);
        AppUnderTest.WaitFor(() => app.FindMenuItem("Paste") is not null, 10_000).ShouldBeTrue("the menu key did not open the menu");

        Keyboard.Type(VirtualKeyShort.ESCAPE);

        AppUnderTest.WaitFor(() => app.FindMenuItem("Paste") is null, 10_000).ShouldBeTrue("Esc did not close the menu");
        app.FindPopup().ShouldNotBeNull("Esc in the menu must not close the popup");
        app.ClosePopup();
    }

    [Fact]
    public void A_board_can_be_made_filled_and_deleted_with_the_mouse_alone()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();
        if (AppUnderTest.SidebarEntries(popup).Length == 0)
        {
            PointClick(Button(popup, "SidebarToggleButton").Center());
            AppUnderTest.WaitFor(() => AppUnderTest.SidebarEntries(popup).Length > 0, 10_000).ShouldBeTrue("the sidebar did not open");
        }

        PointClick(Button(popup, "NewBoardButton").Center());
        AppUnderTest.WaitFor(() => popup.FindFirstDescendant(cf => cf.ByAutomationId("BoardNameBox"))?.Properties.HasKeyboardFocus.ValueOrDefault == true, 10_000)
            .ShouldBeTrue("the name box did not get the cursor");
        Keyboard.Type("Mouse board");
        PointClick(Button(popup, "BoardNameSaveButton").Center());
        AppUnderTest.WaitFor(() => EntryNamed(popup, "Board Mouse board") is not null, 10_000).ShouldBeTrue("the board did not appear");
        PointClick(EntryNamed(popup, "Board Mouse board")!.Center());
        PointClick(AppUnderTest.SidebarEntries(popup)[0].Center());

        PointRightClick(RowWith(popup, "Meeting notes").Center());
        AppUnderTest.WaitFor(() => app.FindMenuItem("Add to a board") is not null, 10_000).ShouldBeTrue("the menu did not open");
        PointClick(app.FindMenuItem("Add to a board")!.Center());
        AppUnderTest.WaitFor(() => app.FindMenuItem("Mouse board") is not null, 10_000).ShouldBeTrue("the boards did not show in the menu");
        PointClick(app.FindMenuItem("Mouse board")!.Center());
        AppUnderTest.WaitFor(() => EntryNamed(popup, "Board Mouse board, 1 clip") is not null, 10_000)
            .ShouldBeTrue("the clip is not on the board. The sidebar says: " + string.Join(" | ", AppUnderTest.SidebarEntries(popup).Select(entry => entry.Name)));

        PointRightClick(EntryNamed(popup, "Board Mouse board, 1 clip")!.Center());
        AppUnderTest.WaitFor(() => app.FindMenuItem("Delete the board") is not null, 10_000).ShouldBeTrue("the board menu did not open");
        PointClick(app.FindMenuItem("Delete the board")!.Center());
        AppUnderTest.WaitFor(() => popup.FindFirstDescendant(cf => cf.ByAutomationId("DeleteBoardConfirmButton")) is not null, 10_000)
            .ShouldBeTrue("the question about deleting the board did not show with a button");
        PointClick(Button(popup, "DeleteBoardConfirmButton").Center());

        AppUnderTest.WaitFor(() => AppUnderTest.SidebarEntries(popup).All(entry => !entry.Name.StartsWith("Board Mouse board", StringComparison.Ordinal)), 10_000)
            .ShouldBeTrue("the board was not deleted");
        RowWith(popup, "Meeting notes").Name.ShouldContain(", pinned", Case.Sensitive, "the clip must stay pinned when its board goes");
        app.ClosePopup();
    }

    [Fact]
    public void The_transform_picker_has_buttons_for_what_its_keys_do()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();
        Keyboard.Type("invoice");
        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length == 1, 10_000).ShouldBeTrue();

        PointRightClick(RowWith(popup, "Invoice").Center());
        AppUnderTest.WaitFor(() => app.FindMenuItem("Change the text, then paste…") is not null, 10_000).ShouldBeTrue("the menu did not open");
        PointClick(app.FindMenuItem("Change the text, then paste…")!.Center());
        AppUnderTest.WaitFor(() => popup.FindFirstDescendant(cf => cf.ByAutomationId("TransformApplyButton")) is not null, 10_000)
            .ShouldBeTrue("the picker did not show its buttons");
        foreach (var id in new[] { "TransformAddButton", "TransformRemoveButton", "TransformBackButton" })
        {
            popup.FindFirstDescendant(cf => cf.ByAutomationId(id)).ShouldNotBeNull($"the picker has no {id}");
        }

        PointClick(Button(popup, "TransformBackButton").Center());

        AppUnderTest.WaitFor(() => popup.FindFirstDescendant(cf => cf.ByAutomationId("TransformApplyButton")) is null, 10_000).ShouldBeTrue("Back did not close the picker");
        app.FindPopup().ShouldNotBeNull("Back must only close the picker");
        app.ClosePopup();
    }

    private static void PointClick(System.Drawing.Point point)
    {
        AppUnderTest.PointAt(point);
        Mouse.Click(point);
    }

    private static void PointRightClick(System.Drawing.Point point)
    {
        AppUnderTest.PointAt(point);
        Mouse.RightClick(point);
    }

    private static AutomationElement Button(Window popup, string automationId) =>
        popup.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) ?? throw new InvalidOperationException($"The popup has no {automationId}.");

    private static AutomationElement RowWith(Window popup, string text) =>
        AppUnderTest.Rows(popup).First(row => row.Name.Contains(text, StringComparison.Ordinal));

    private static AutomationElement? EntryNamed(Window popup, string startsWith) =>
        AppUnderTest.SidebarEntries(popup).FirstOrDefault(entry => entry.Name.StartsWith(startsWith, StringComparison.Ordinal));
}
