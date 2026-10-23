using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace ClipBench.UiSmokeTests;

/// <summary>
/// Drives the real app with the real keyboard. They need a desktop that nobody is using, so they are tagged as
/// integration tests and run on demand: dotnet test --project tests/ClipBench.UiSmokeTests -c Release --filter-trait "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
[Collection(SharedDesktop.Name)]
public sealed class SmokeTests(AppUnderTest app) : IClassFixture<AppUnderTest>
{
    [Fact]
    public void The_app_starts_with_the_popup_hidden_and_the_history_loaded()
    {
        app.ClosePopup();

        app.Process.HasExited.ShouldBeFalse();
        app.FindPopup().ShouldBeNull();
        app.ReadLog().ShouldContain("HistoryLoaded count=6");
    }

    [Fact]
    public void The_shortcut_opens_the_popup_with_the_search_box_ready_for_typing()
    {
        app.ClosePopup();

        var popup = app.OpenPopup();

        AppUnderTest.SearchBox(popup).Properties.HasKeyboardFocus.Value.ShouldBeTrue();
        app.ClosePopup();
    }

    [Fact]
    public void The_list_shows_the_saved_clips_newest_first_with_their_source_and_age()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();

        var names = AppUnderTest.RowNames(popup);

        // Other tests may have removed a clip by now, so the count is not checked, only the order of the first two.
        names.Length.ShouldBeGreaterThanOrEqualTo(AppUnderTest.SeededTexts.Length - 1);
        names[0].ShouldStartWith("Text, Invoice 2026-10 for ACME Corp, from Notepad, ");
        names[1].ShouldStartWith("Text, Meeting notes: budget review and hiring plan, from Notepad, ");
        app.ClosePopup();
    }

    [Fact]
    public void A_clip_that_looks_like_a_secret_is_hidden_and_never_read_out()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();

        var names = AppUnderTest.RowNames(popup);

        names.ShouldContain(name => name.StartsWith("Text, hidden sensitive content", StringComparison.Ordinal));
        names.ShouldNotContain(name => name.Contains("eyJ", StringComparison.Ordinal));
        app.ClosePopup();
    }

    [Fact]
    public void Typing_filters_the_list_down_to_the_matching_clip()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();

        Keyboard.Type("invoice");

        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length == 1, 10_000).ShouldBeTrue("the list was not filtered. Rows: " + string.Join(" | ", AppUnderTest.RowNames(popup)));
        AppUnderTest.RowNames(popup).Single().ShouldContain("Invoice 2026-10 for ACME Corp");
        app.ClosePopup();
    }

    [Fact]
    public void A_search_with_no_match_shows_an_empty_list()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();

        Keyboard.Type("zzzzqqqq");

        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length == 0, 10_000).ShouldBeTrue("the list still has rows");
        app.ClosePopup();
    }

    [Fact]
    public void Escape_closes_the_popup_and_the_next_opening_starts_with_an_empty_search()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();
        var before = AppUnderTest.Rows(popup).Length;
        Keyboard.Type("invoice");
        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length == 1, 10_000).ShouldBeTrue();

        Keyboard.Type(VirtualKeyShort.ESCAPE);

        AppUnderTest.WaitFor(() => app.FindPopup() is null, 10_000).ShouldBeTrue("Escape did not close the popup");

        var reopened = app.OpenPopup();
        AppUnderTest.SearchBox(reopened).Patterns.Value.Pattern.Value.Value.ShouldBe(string.Empty);
        AppUnderTest.WaitFor(() => AppUnderTest.Rows(reopened).Length == before, 10_000).ShouldBeTrue("the full list did not come back");
        app.ClosePopup();
    }

    [Fact]
    public void Enter_in_the_custom_separator_box_closes_the_box_and_does_not_paste()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();
        var pastesBefore = Pastes();

        popup.FindFirstDescendant(cf => cf.ByAutomationId("SeparatorButton"))!.AsButton().Invoke();
        AppUnderTest.WaitFor(() => popup.FindFirstDescendant(cf => cf.ByName("Custom…")) is not null, 10_000).ShouldBeTrue("the separator menu did not open");
        popup.FindFirstDescendant(cf => cf.ByName("Custom…"))!.Patterns.Invoke.Pattern.Invoke();
        AppUnderTest.WaitFor(() => popup.FindFirstDescendant(cf => cf.ByName("Custom separator"))?.Properties.HasKeyboardFocus.ValueOrDefault == true, 10_000)
            .ShouldBeTrue("the custom separator box did not get the focus");

        Keyboard.Type("|");
        Keyboard.Type(VirtualKeyShort.RETURN);

        AppUnderTest.WaitFor(() => popup.FindFirstDescendant(cf => cf.ByName("Custom separator")) is null, 10_000).ShouldBeTrue("the box did not close");
        Thread.Sleep(1000);
        app.FindPopup().ShouldNotBeNull("Enter in the box must not paste, which closes the popup");
        Pastes().ShouldBe(pastesBefore);
        app.ClosePopup();
    }

    private int Pastes() => app.ReadLog().Split("PasteSucceeded").Length - 1;

    [Fact]
    public void Down_moves_the_highlight_to_the_next_clip()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();
        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length > 1, 10_000).ShouldBeTrue();

        Keyboard.Type(VirtualKeyShort.DOWN);

        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup)[1].Patterns.SelectionItem.Pattern.IsSelected.Value, 10_000).ShouldBeTrue("the second row was not highlighted");
        AppUnderTest.Rows(popup)[0].Patterns.SelectionItem.Pattern.IsSelected.Value.ShouldBeFalse();
        app.ClosePopup();
    }

    [Fact]
    public void Control_P_pins_the_highlighted_clip()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();
        Keyboard.Type("pin me");
        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length == 1, 10_000).ShouldBeTrue();

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_P);

        AppUnderTest.WaitFor(() => AppUnderTest.RowNames(popup).SingleOrDefault()?.Contains(", pinned", StringComparison.Ordinal) == true, 10_000)
            .ShouldBeTrue("the clip was not pinned. Rows: " + string.Join(" | ", AppUnderTest.RowNames(popup)));
        app.ClosePopup();
    }

    [Fact]
    public void Enter_pastes_the_highlighted_clip_into_the_window_that_had_focus_before_the_popup_opened()
    {
        app.ClosePopup();
        using var target = PasteTargetWindow.Create();
        if (!target.TryActivate())
        {
            Assert.Skip("Windows would not let the test window take focus (it protects the window you are working in), so no key was sent. Run this test from a terminal that is in front.");
        }

        var popup = app.OpenPopup();
        Keyboard.Type("invoice");
        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length == 1, 10_000).ShouldBeTrue();

        Keyboard.Type(VirtualKeyShort.ENTER);

        AppUnderTest.WaitFor(() => app.FindPopup() is null, 10_000).ShouldBeTrue("the popup did not close");
        AppUnderTest.WaitFor(() => target.Text == "Invoice 2026-10 for ACME Corp", 15_000).ShouldBeTrue($"the window holds: \"{target.Text}\"");
    }

    [Fact]
    public void Delete_removes_the_highlighted_clip()
    {
        app.ClosePopup();
        var popup = app.OpenPopup();
        Keyboard.Type("disposable");
        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length == 1, 10_000).ShouldBeTrue();

        Keyboard.Type(VirtualKeyShort.DELETE);

        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length == 0, 10_000).ShouldBeTrue("the clip was not removed");
        app.ClosePopup();
        app.ReadLog().ShouldNotContain("StoreFailed");
    }
}
