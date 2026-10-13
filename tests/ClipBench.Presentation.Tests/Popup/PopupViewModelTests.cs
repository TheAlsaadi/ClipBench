using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Rows;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Popup;

public sealed class PopupViewModelTests : IDisposable
{
    private const string Jwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    private readonly ManualTimeProvider _time = new();
    private readonly FakeClipStore _store = new();
    private readonly FakePasteFlow _paste = new();
    private readonly ImmediateDispatcher _dispatcher = new();
    private readonly ClipLibrary _library;
    private readonly PopupViewModel _vm;

    public PopupViewModelTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _time), _store);
        _vm = new PopupViewModel(_library, _paste, _dispatcher, _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vm.Dispose();
        _library.Dispose();
    }

    /// <summary>Copies the texts in order, so the last one is the newest.</summary>
    private async Task CopyAsync(params string[] texts)
    {
        foreach (var text in texts)
        {
            (await _library.AddAsync(TestClips.Text(text), Ct)).IsSuccess.ShouldBeTrue();
        }
    }

    private string[] Shown() => [.. _vm.Rows.Select(row => row.DisplayText)];

    private void Type(string text)
    {
        _vm.SearchText = text;
        _time.FireNext().ShouldBeTrue("typing should have started the search delay");
    }

    // --- opening ---

    [Fact]
    public async Task Opening_begins_the_paste_flow_before_the_popup_counts_as_open()
    {
        await CopyAsync("one");
        var openWhenBegun = true;
        _paste.OnBegin = () => openWhenBegun = _vm.IsOpen;

        _vm.Open();

        _paste.Begins.ShouldBe(1);
        openWhenBegun.ShouldBeFalse();
        _vm.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task Opening_lists_the_clips_newest_first_and_highlights_the_first()
    {
        await CopyAsync("one", "two", "three");

        _vm.Open();

        Shown().ShouldBe(["three", "two", "one"]);
        _vm.SelectedIndex.ShouldBe(0);
        _vm.Current!.DisplayText.ShouldBe("three");
        _vm.ShowEmptyState.ShouldBeFalse();
    }

    [Fact]
    public void Opening_with_nothing_copied_says_so()
    {
        _vm.Open();

        _vm.Rows.ShouldBeEmpty();
        _vm.SelectedIndex.ShouldBe(-1);
        _vm.Current.ShouldBeNull();
        _vm.ShowEmptyState.ShouldBeTrue();
        _vm.EmptyText.ShouldBe("Nothing copied yet. Copy something and it will show up here.");
    }

    [Fact]
    public async Task Opening_again_starts_clean()
    {
        await CopyAsync("alpha", "beta");
        _vm.Open();
        Type("alp");
        _vm.ToggleMarkCommand.Execute(null);
        _vm.Close();

        _vm.Open();

        _vm.SearchText.ShouldBe(string.Empty);
        Shown().ShouldBe(["beta", "alpha"]);
        _vm.MarkedCount.ShouldBe(0);
        _vm.SelectedIndex.ShouldBe(0);
        _vm.IsConfirmingDelete.ShouldBeFalse();
        _vm.ErrorMessage.ShouldBeNull();
    }

    // --- searching ---

    [Fact]
    public async Task Typing_waits_for_a_pause_before_the_list_is_searched()
    {
        await CopyAsync("alpha", "beta");
        _vm.Open();

        _vm.SearchText = "alp";

        Shown().ShouldBe(["beta", "alpha"]);
        _time.RequestedDelays.Last().ShouldBe(PopupViewModel.SearchDelay);
        PopupViewModel.SearchDelay.ShouldBe(TimeSpan.FromMilliseconds(150));

        _time.FireNext().ShouldBeTrue();

        Shown().ShouldBe(["alpha"]);
    }

    [Fact]
    public async Task Typing_again_restarts_the_wait_and_only_the_last_text_is_searched()
    {
        await CopyAsync("alpha", "beta");
        _vm.Open();

        _vm.SearchText = "a";
        _vm.SearchText = "bet";

        _time.RequestedDelays.Count.ShouldBe(2);
        _time.FireNext().ShouldBeTrue();
        Shown().ShouldBe(["beta"]);
        _time.FireNext().ShouldBeFalse("the first keystroke must not leave a second search behind");
    }

    [Fact]
    public async Task A_new_search_puts_the_highlight_on_the_top_result()
    {
        await CopyAsync("apple", "apricot", "avocado");
        _vm.Open();
        _vm.MoveDownCommand.Execute(null);
        _vm.MoveDownCommand.Execute(null);

        Type("ap");

        Shown().ShouldBe(["apricot", "apple"]);
        _vm.SelectedIndex.ShouldBe(0);
    }

    [Fact]
    public async Task A_search_that_finds_nothing_says_so_and_clearing_it_brings_everything_back()
    {
        await CopyAsync("alpha", "beta");
        _vm.Open();

        Type("zzz");

        _vm.Rows.ShouldBeEmpty();
        _vm.ShowEmptyState.ShouldBeTrue();
        _vm.EmptyText.ShouldBe("No clips match your search.");
        _vm.SelectedIndex.ShouldBe(-1);

        Type(string.Empty);

        Shown().ShouldBe(["beta", "alpha"]);
        _vm.SelectedIndex.ShouldBe(0);
    }

    [Fact]
    public async Task Clearing_the_search_when_the_popup_closes_does_not_start_a_search()
    {
        await CopyAsync("alpha");
        _vm.Open();
        _vm.SearchText = "al";

        _vm.Close();

        _time.FireNext().ShouldBeFalse("closing must cancel the pending search");
        _vm.SearchText.ShouldBe(string.Empty);
    }

    // --- moving ---

    [Fact]
    public async Task Down_and_up_move_the_highlight_and_wrap_around_the_ends()
    {
        await CopyAsync("one", "two", "three");
        _vm.Open();

        _vm.MoveDownCommand.Execute(null);
        _vm.SelectedIndex.ShouldBe(1);
        _vm.MoveDownCommand.Execute(null);
        _vm.SelectedIndex.ShouldBe(2);
        _vm.MoveDownCommand.Execute(null);
        _vm.SelectedIndex.ShouldBe(0);
        _vm.MoveUpCommand.Execute(null);
        _vm.SelectedIndex.ShouldBe(2);
        _vm.MoveUpCommand.Execute(null);
        _vm.SelectedIndex.ShouldBe(1);
    }

    [Fact]
    public void Moving_with_nothing_listed_does_nothing()
    {
        _vm.Open();

        _vm.MoveDownCommand.Execute(null);
        _vm.MoveUpCommand.Execute(null);

        _vm.SelectedIndex.ShouldBe(-1);
    }

    [Fact]
    public async Task Moving_clears_the_message_about_the_last_action()
    {
        await CopyAsync("one", "two");
        _vm.Open();
        _vm.ErrorMessage = "That change could not be saved.";

        _vm.MoveDownCommand.Execute(null);

        _vm.ErrorMessage.ShouldBeNull();
    }

    // --- marking ---

    [Fact]
    public async Task Space_marks_and_unmarks_the_highlighted_row()
    {
        await CopyAsync("one", "two");
        _vm.Open();

        _vm.ToggleMarkCommand.Execute(null);
        _vm.MoveDownCommand.Execute(null);
        _vm.ToggleMarkCommand.Execute(null);

        _vm.MarkedCount.ShouldBe(2);
        _vm.Rows.All(row => row.IsMarked).ShouldBeTrue();

        _vm.ToggleMarkCommand.Execute(null);

        _vm.MarkedCount.ShouldBe(1);
        _vm.Rows[1].IsMarked.ShouldBeFalse();
    }

    [Fact]
    public void Marking_with_nothing_listed_does_nothing()
    {
        _vm.Open();

        Should.NotThrow(() => _vm.ToggleMarkCommand.Execute(null));
        _vm.MarkedCount.ShouldBe(0);
    }

    // --- pasting ---

    [Fact]
    public async Task Enter_hides_the_popup_and_then_pastes_the_highlighted_clip()
    {
        await CopyAsync("one", "two");
        _vm.Open();
        _vm.CloseRequested += (_, _) => _paste.Order.Add("close");
        _vm.MoveDownCommand.Execute(null);

        await _vm.PasteCommand.ExecuteAsync(null);

        _paste.Order.ShouldBe(["begin", "close", "paste"]);
        _paste.Pasted.Single().Content.ShouldBe(new TextContent("one"));
        _vm.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task Enter_with_nothing_listed_pastes_nothing_and_keeps_the_popup_open()
    {
        _vm.Open();

        await _vm.PasteCommand.ExecuteAsync(null);

        _paste.Pasted.ShouldBeEmpty();
        _vm.IsOpen.ShouldBeTrue();
    }

    // --- pinning ---

    [Fact]
    public async Task Ctrl_P_pins_a_clip_and_pressing_it_again_unpins_it()
    {
        await CopyAsync("keep");
        _vm.Open();

        await _vm.TogglePinCommand.ExecuteAsync(null);

        _vm.Rows.Single().IsPinned.ShouldBeTrue();
        _library.Clips.Single().IsPinned.ShouldBeTrue();
        _store.PinChanges.Single().Pinned.ShouldBeTrue();

        await _vm.TogglePinCommand.ExecuteAsync(null);

        _vm.Rows.Single().IsPinned.ShouldBeFalse();
        _store.PinChanges.Last().Pinned.ShouldBeFalse();
    }

    [Fact]
    public async Task A_pin_that_could_not_be_saved_is_reported()
    {
        await CopyAsync("keep");
        _vm.Open();
        _store.Failure = new ResultError(ErrorCode.StorageFailure, "disk");

        await _vm.TogglePinCommand.ExecuteAsync(null);

        _vm.ErrorMessage.ShouldBe("That change could not be saved.");
    }

    [Fact]
    public async Task Pinning_keeps_the_highlight_on_the_same_clip()
    {
        await CopyAsync("one", "two", "three");
        _vm.Open();
        _vm.MoveDownCommand.Execute(null);
        var id = _vm.Current!.Id;

        await _vm.TogglePinCommand.ExecuteAsync(null);

        _vm.Current!.Id.ShouldBe(id);
    }

    [Fact]
    public async Task The_view_is_told_when_there_is_a_message_to_show_and_when_it_goes_away()
    {
        await CopyAsync("one", "two");
        _vm.Open();
        List<string?> changed = [];
        _vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        _vm.HasError.ShouldBeFalse();

        _store.Failure = new ResultError(ErrorCode.StorageFailure, "disk");
        await _vm.TogglePinCommand.ExecuteAsync(null);

        _vm.HasError.ShouldBeTrue();
        changed.ShouldContain(nameof(PopupViewModel.HasError));

        _vm.MoveDownCommand.Execute(null);

        _vm.HasError.ShouldBeFalse();
    }

    // --- deleting ---

    [Fact]
    public async Task Delete_removes_an_ordinary_clip_at_once_and_keeps_the_highlight_in_place()
    {
        await CopyAsync("one", "two", "three");
        _vm.Open();
        _vm.MoveDownCommand.Execute(null);
        var removed = _vm.Current!.Id;

        await _vm.DeleteCommand.ExecuteAsync(null);

        Shown().ShouldBe(["three", "one"]);
        _vm.SelectedIndex.ShouldBe(1);
        _store.Deleted.ShouldBe([removed]);
    }

    [Fact]
    public async Task Deleting_the_last_row_moves_the_highlight_up()
    {
        await CopyAsync("one", "two");
        _vm.Open();
        _vm.MoveDownCommand.Execute(null);

        await _vm.DeleteCommand.ExecuteAsync(null);

        Shown().ShouldBe(["two"]);
        _vm.SelectedIndex.ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_the_only_row_leaves_the_empty_state()
    {
        await CopyAsync("only");
        _vm.Open();

        await _vm.DeleteCommand.ExecuteAsync(null);

        _vm.Rows.ShouldBeEmpty();
        _vm.SelectedIndex.ShouldBe(-1);
        _vm.ShowEmptyState.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_on_a_pinned_clip_asks_first_and_the_second_press_removes_it()
    {
        (await _library.AddAsync(TestClips.Text("keep", pinned: true), Ct)).IsSuccess.ShouldBeTrue();
        _vm.Open();

        await _vm.DeleteCommand.ExecuteAsync(null);

        _vm.IsConfirmingDelete.ShouldBeTrue();
        _vm.Rows.Count.ShouldBe(1);
        _vm.StatusText.ShouldContain("Delete again");
        _store.Deleted.ShouldBeEmpty();

        await _vm.DeleteCommand.ExecuteAsync(null);

        _vm.IsConfirmingDelete.ShouldBeFalse();
        _vm.Rows.ShouldBeEmpty();
        _store.Deleted.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Escape_first_withdraws_the_question_and_only_then_closes()
    {
        (await _library.AddAsync(TestClips.Text("keep", pinned: true), Ct)).IsSuccess.ShouldBeTrue();
        _vm.Open();
        var closes = 0;
        _vm.CloseRequested += (_, _) => closes++;
        await _vm.DeleteCommand.ExecuteAsync(null);

        _vm.EscapeCommand.Execute(null);

        _vm.IsConfirmingDelete.ShouldBeFalse();
        _vm.IsOpen.ShouldBeTrue();
        closes.ShouldBe(0);
        _vm.Rows.Count.ShouldBe(1);

        _vm.EscapeCommand.Execute(null);

        _vm.IsOpen.ShouldBeFalse();
        closes.ShouldBe(1);
    }

    [Fact]
    public async Task Moving_withdraws_the_question_about_deleting_a_pinned_clip()
    {
        (await _library.AddAsync(TestClips.Text("one"), Ct)).IsSuccess.ShouldBeTrue();
        (await _library.AddAsync(TestClips.Text("keep", pinned: true), Ct)).IsSuccess.ShouldBeTrue();
        _vm.Open();
        await _vm.DeleteCommand.ExecuteAsync(null);

        _vm.MoveDownCommand.Execute(null);

        _vm.IsConfirmingDelete.ShouldBeFalse();
    }

    [Fact]
    public async Task A_delete_that_could_not_be_saved_is_reported()
    {
        await CopyAsync("one");
        _vm.Open();
        _store.Failure = new ResultError(ErrorCode.StorageFailure, "disk");

        await _vm.DeleteCommand.ExecuteAsync(null);

        _vm.ErrorMessage.ShouldBe("That change could not be saved.");
    }

    [Fact]
    public void Delete_with_nothing_listed_does_nothing()
    {
        _vm.Open();

        Should.NotThrow(() => _vm.DeleteCommand.Execute(null));
        _vm.IsConfirmingDelete.ShouldBeFalse();
    }

    // --- revealing ---

    [Fact]
    public async Task Reveal_shows_and_hides_a_sensitive_clip()
    {
        await CopyAsync(Jwt);
        _vm.Open();
        var row = _vm.Current!;
        row.IsSensitive.ShouldBeTrue();
        row.DisplayText.ShouldBe(ClipRowViewModel.Mask);

        _vm.RevealCommand.Execute(null);

        row.IsRevealed.ShouldBeTrue();
        row.DisplayText.ShouldNotBe(ClipRowViewModel.Mask);

        _vm.RevealCommand.Execute(null);

        row.DisplayText.ShouldBe(ClipRowViewModel.Mask);
    }

    [Fact]
    public async Task Reveal_does_nothing_for_an_ordinary_clip()
    {
        await CopyAsync("lunch");
        _vm.Open();

        _vm.RevealCommand.Execute(null);

        _vm.Current!.IsRevealed.ShouldBeFalse();
    }

    // --- the hints ---

    [Fact]
    public async Task The_hints_name_the_keys_and_mention_reveal_only_for_a_hidden_clip()
    {
        await CopyAsync("lunch");
        _vm.Open();
        _vm.StatusText.ShouldContain("Enter");
        _vm.StatusText.ShouldContain("Space");
        _vm.StatusText.ShouldContain("Esc");
        _vm.StatusText.ShouldNotContain("reveal");

        // The new clip lands above the highlighted one, which stays highlighted; move up onto the hidden clip.
        await CopyAsync(Jwt);
        _vm.MoveUpCommand.Execute(null);
        _vm.Current!.IsSensitive.ShouldBeTrue();
        _vm.StatusText.ShouldContain("Ctrl+R");
    }

    [Fact]
    public void With_nothing_listed_the_hint_is_only_how_to_close()
    {
        _vm.Open();

        _vm.StatusText.ShouldBe("Esc close");
    }

    // --- closing ---

    [Fact]
    public async Task Closing_hides_the_popup_and_forgets_everything()
    {
        await CopyAsync("one", "two");
        _vm.Open();
        var closes = 0;
        _vm.CloseRequested += (_, _) => closes++;
        _vm.ToggleMarkCommand.Execute(null);

        _vm.Close();

        _vm.IsOpen.ShouldBeFalse();
        _vm.Rows.ShouldBeEmpty();
        _vm.SelectedIndex.ShouldBe(-1);
        _vm.SearchText.ShouldBe(string.Empty);
        _vm.MarkedCount.ShouldBe(0);
        _vm.IsConfirmingDelete.ShouldBeFalse();
        _vm.ErrorMessage.ShouldBeNull();
        closes.ShouldBe(1);
    }

    [Fact]
    public async Task Escape_closes_the_popup()
    {
        await CopyAsync("one");
        _vm.Open();

        _vm.EscapeCommand.Execute(null);

        _vm.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task The_close_command_closes_even_while_a_question_is_pending()
    {
        (await _library.AddAsync(TestClips.Text("keep", pinned: true), Ct)).IsSuccess.ShouldBeTrue();
        _vm.Open();
        await _vm.DeleteCommand.ExecuteAsync(null);

        _vm.CloseNowCommand.Execute(null);

        _vm.IsOpen.ShouldBeFalse();
        _vm.IsConfirmingDelete.ShouldBeFalse();
    }

    // --- changes made elsewhere ---

    [Fact]
    public async Task A_clip_copied_while_the_popup_is_open_appears_and_the_highlight_stays_on_its_clip()
    {
        await CopyAsync("one", "two");
        _vm.Open();
        _vm.MoveDownCommand.Execute(null);
        var id = _vm.Current!.Id;

        await CopyAsync("fresh");

        Shown().ShouldBe(["fresh", "two", "one"]);
        _vm.Current!.Id.ShouldBe(id);
        _vm.SelectedIndex.ShouldBe(2);
    }

    [Fact]
    public async Task A_marked_row_stays_marked_when_the_list_is_refreshed()
    {
        await CopyAsync("one", "two");
        _vm.Open();
        _vm.ToggleMarkCommand.Execute(null);

        await CopyAsync("fresh");

        _vm.Rows.Count.ShouldBe(3);
        _vm.Rows.Single(row => row.IsMarked).DisplayText.ShouldBe("two");
        _vm.MarkedCount.ShouldBe(1);
    }

    [Fact]
    public async Task Closing_twice_tells_the_view_once()
    {
        await CopyAsync("one");
        _vm.Open();
        var closes = 0;
        _vm.CloseRequested += (_, _) => closes++;

        _vm.Close();
        _vm.Close();

        closes.ShouldBe(1);
    }

    [Fact]
    public async Task Changes_while_the_popup_is_closed_wait_for_the_next_opening()
    {
        _vm.Open();
        _vm.Close();

        await CopyAsync("while closed");

        _vm.Rows.ShouldBeEmpty();
        _vm.Open();
        Shown().ShouldBe(["while closed"]);
    }

    [Fact]
    public async Task Library_changes_reach_the_list_through_the_dispatcher()
    {
        _vm.Open();
        var before = _dispatcher.Posts;

        await CopyAsync("new");

        _dispatcher.Posts.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task After_dispose_library_changes_are_ignored()
    {
        await CopyAsync("one");
        _vm.Open();

        _vm.Dispose();
        await CopyAsync("two");

        Shown().ShouldBe(["one"]);
    }

    [Fact]
    public void Disposing_twice_is_fine()
    {
        _vm.Dispose();

        Should.NotThrow(_vm.Dispose);
    }
}
