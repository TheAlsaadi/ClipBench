using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Pasting;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Collect;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Popup;

/// <summary>A locked popup stays on screen: after a paste, when another window is used, and until the user closes it.</summary>
public sealed class PopupLockTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakeClipStore _store = new();
    private readonly FakePasteFlow _paste = new();
    private readonly FakeQueueControl _queue = new();
    private readonly ClipLibrary _library;
    private readonly SettingsService _settings = new(new FakeSettingsStore());
    private readonly PopupViewModel _vm;
    private int _closeRequests;

    public PopupLockTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _time), _store);
        _vm = new PopupViewModel(_library, _paste, _queue, new CollectToggle(new FakeCollectControl(), new FakeNotifier()), TestPickers.New(), TestSidebars.New(_library), TestSnippets.New(), _settings, new ImmediateDispatcher(), _time);
        _vm.CloseRequested += (_, _) => _closeRequests++;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vm.Dispose();
        _library.Dispose();
        _settings.Dispose();
    }

    private async Task CopyAsync(params string[] texts)
    {
        foreach (var text in texts)
        {
            (await _library.AddAsync(TestClips.Text(text), Ct)).IsSuccess.ShouldBeTrue();
        }
    }

    private async Task<PopupViewModel> LockedAsync(params string[] texts)
    {
        await CopyAsync(texts);
        _vm.Open();
        _vm.ToggleLockCommand.Execute(null);
        return _vm;
    }

    // --- locking and unlocking ---

    [Fact]
    public async Task The_lock_button_locks_an_open_popup_and_a_second_press_unlocks_it()
    {
        await CopyAsync("one");
        _vm.Open();

        _vm.ToggleLockCommand.Execute(null);
        _vm.IsLocked.ShouldBeTrue();

        _vm.ToggleLockCommand.Execute(null);
        _vm.IsLocked.ShouldBeFalse();
        _vm.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void A_popup_that_is_not_shown_cannot_be_locked()
    {
        _vm.ToggleLockCommand.Execute(null);

        _vm.IsLocked.ShouldBeFalse();
        _paste.Holds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Locking_and_unlocking_tell_the_paste_flow()
    {
        await CopyAsync("one");
        _vm.Open();

        _vm.ToggleLockCommand.Execute(null);
        _vm.ToggleLockCommand.Execute(null);

        _paste.Holds.ShouldBe([true, false]);
    }

    [Fact]
    public async Task Closing_the_popup_unlocks_it_and_lets_go_of_the_hold()
    {
        await LockedAsync("one");

        _vm.CloseNowCommand.Execute(null);

        _vm.IsOpen.ShouldBeFalse();
        _vm.IsLocked.ShouldBeFalse();
        _paste.Holds.ShouldBe([true, false]);
        _closeRequests.ShouldBe(1);
    }

    [Fact]
    public async Task A_popup_opened_again_is_not_locked()
    {
        await LockedAsync("one");
        _vm.CloseNowCommand.Execute(null);

        _vm.Open();

        _vm.IsLocked.ShouldBeFalse();
    }

    // --- pasting ---

    [Fact]
    public async Task A_paste_from_a_locked_popup_leaves_it_on_screen()
    {
        await LockedAsync("one", "two");

        await _vm.PasteCommand.ExecuteAsync(null);

        _paste.Pasted.Select(clip => ((TextContent)clip.Content).Text).ShouldBe(["two"]);
        _vm.IsOpen.ShouldBeTrue();
        _vm.IsLocked.ShouldBeTrue();
        _closeRequests.ShouldBe(0);
    }

    [Fact]
    public async Task A_paste_from_an_unlocked_popup_still_hides_it_before_pasting()
    {
        await CopyAsync("one");
        _vm.Open();
        var openWhenPasting = true;
        _paste.OnPaste = () => openWhenPasting = _vm.IsOpen;

        await _vm.PasteCommand.ExecuteAsync(null);

        openWhenPasting.ShouldBeFalse();
        _closeRequests.ShouldBe(1);
    }

    [Fact]
    public async Task After_a_paste_a_locked_popup_starts_over_with_a_fresh_list()
    {
        await LockedAsync("alpha", "beta");
        _vm.SearchText = "alp";
        _time.FireNext().ShouldBeTrue();
        _vm.Rows.Count.ShouldBe(1);

        await _vm.PasteCommand.ExecuteAsync(null);

        _vm.SearchText.ShouldBe(string.Empty);
        _vm.Rows.Select(row => row.DisplayText).ShouldBe(["beta", "alpha"]);
        _vm.SelectedIndex.ShouldBe(0);
    }

    [Fact]
    public async Task Marked_clips_pasted_from_a_locked_popup_leave_it_on_screen_with_no_marks()
    {
        await LockedAsync("one", "two");
        _vm.ToggleMarkCommand.Execute(null);
        _vm.MoveDownCommand.Execute(null);
        _vm.ToggleMarkCommand.Execute(null);

        await _vm.PasteCommand.ExecuteAsync(null);

        _paste.PastedContent.ShouldHaveSingleItem();
        _vm.IsOpen.ShouldBeTrue();
        _vm.MarkedCount.ShouldBe(0);
        _closeRequests.ShouldBe(0);
    }

    [Fact]
    public async Task A_table_pasted_from_a_locked_popup_leaves_it_on_screen()
    {
        await LockedAsync("a\tb", "c\td");
        _vm.ToggleMarkCommand.Execute(null);
        _vm.MoveDownCommand.Execute(null);
        _vm.ToggleMarkCommand.Execute(null);

        await _vm.PasteTableCommand.ExecuteAsync(TableLayout.Column);

        _paste.PastedContent.ShouldHaveSingleItem();
        _vm.IsOpen.ShouldBeTrue();
        _closeRequests.ShouldBe(0);
    }

    [Fact]
    public async Task A_changed_text_pasted_from_a_locked_popup_leaves_it_on_screen()
    {
        await LockedAsync("hello");
        _vm.OpenTransformsCommand.Execute(null);
        var upper = _vm.Transforms.Items.ToList().FindIndex(item => item.Name == "UPPER CASE");
        _vm.Transforms.SelectedIndex = upper;

        await _vm.ConfirmTransformCommand.ExecuteAsync(null);

        _paste.PastedContent.Single().ShouldBe(new TextContent("HELLO"));
        _vm.IsOpen.ShouldBeTrue();
        _vm.IsTransformsOpen.ShouldBeFalse();
        _closeRequests.ShouldBe(0);
    }

    [Fact]
    public async Task A_queue_started_from_a_locked_popup_gets_the_focus_in_the_program_it_pastes_into()
    {
        await LockedAsync("one", "two");

        await _vm.QueueCommand.ExecuteAsync(null);

        _queue.Started.ShouldHaveSingleItem();
        _paste.Returns.ShouldBe(1);
        _vm.IsOpen.ShouldBeTrue();
        _closeRequests.ShouldBe(0);
    }

    // --- Esc and the shortcut ---

    [Fact]
    public async Task Esc_in_a_locked_popup_gives_the_focus_back_and_leaves_it_on_screen()
    {
        await LockedAsync("one");

        _vm.EscapeCommand.Execute(null);

        _paste.Returns.ShouldBe(1);
        _vm.IsOpen.ShouldBeTrue();
        _vm.IsLocked.ShouldBeTrue();
        _closeRequests.ShouldBe(0);
    }

    [Fact]
    public async Task Esc_first_undoes_what_is_open_before_it_gives_the_focus_back()
    {
        await LockedAsync("one");
        _vm.OpenTransformsCommand.Execute(null);

        _vm.EscapeCommand.Execute(null);

        _vm.IsTransformsOpen.ShouldBeFalse();
        _paste.Returns.ShouldBe(0);
    }

    [Fact]
    public async Task Esc_in_an_unlocked_popup_still_closes_it()
    {
        await CopyAsync("one");
        _vm.Open();

        _vm.EscapeCommand.Execute(null);

        _vm.IsOpen.ShouldBeFalse();
        _paste.Returns.ShouldBe(0);
    }

    [Fact]
    public async Task Taking_up_a_locked_popup_again_records_the_window_in_front_and_keeps_the_list()
    {
        await LockedAsync("one", "two");
        _vm.MoveDownCommand.Execute(null);
        var begins = _paste.Begins;

        _vm.Refocus();

        _paste.Begins.ShouldBe(begins + 1);
        _paste.RecordedTargets[^1].ShouldBeTrue();
        _vm.SelectedIndex.ShouldBe(1);
        _vm.IsLocked.ShouldBeTrue();
    }

    [Fact]
    public async Task Taking_up_a_locked_popup_from_the_tray_icon_keeps_the_window_on_record()
    {
        await LockedAsync("one");
        var begins = _paste.Begins;

        _vm.Refocus(pasteIntoPreviousWindow: false);

        _paste.Begins.ShouldBe(begins);
    }

    // --- other things that used to hide the popup ---

    [Fact]
    public async Task Opening_the_snippet_editor_from_a_locked_popup_leaves_it_on_screen()
    {
        await LockedAsync("one");
        var asked = 0;
        _vm.EditSnippetsRequested += (_, _) => asked++;

        _vm.EditSnippetsCommand.Execute(null);

        asked.ShouldBe(1);
        _vm.IsOpen.ShouldBeTrue();
    }
}
