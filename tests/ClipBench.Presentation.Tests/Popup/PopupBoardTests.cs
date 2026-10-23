using ClipBench.Core.Boards;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Presentation.Boards;
using ClipBench.Presentation.Collect;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Popup;

/// <summary>The list follows the sidebar: History, Pinned or one board. Clips are put on boards from the popup.</summary>
public sealed class PopupBoardTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakePasteFlow _paste = new();
    private readonly FakeBoardStore _boardStore = new();
    private readonly ClipLibrary _library;
    private readonly BoardCatalog _catalog;
    private readonly SidebarViewModel _sidebar;
    private readonly Core.Settings.SettingsService _settings = new(new FakeSettingsStore());
    private readonly PopupViewModel _vm;

    public PopupBoardTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _time), new FakeClipStore());
        _catalog = new BoardCatalog(_library, _boardStore);
        _sidebar = TestSidebars.New(_library, _catalog);
        _vm = new PopupViewModel(_library, _paste, new FakeQueueControl(), new CollectToggle(new FakeCollectControl(), new FakeNotifier()), TestPickers.New(), _sidebar, _settings, new ImmediateDispatcher(), _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vm.Dispose();
        _sidebar.Dispose();
        _catalog.Dispose();
        _settings.Dispose();
        _library.Dispose();
    }

    private async Task<Clip> AddAsync(string text, bool pinned = false)
    {
        var clip = TestClips.Text(text, pinned: pinned);
        (await _library.AddAsync(clip, Ct)).IsSuccess.ShouldBeTrue();
        return clip;
    }

    private async Task<Board> NewBoardAsync(string name)
    {
        var created = await _catalog.CreateAsync(name, Ct);
        created.IsSuccess.ShouldBeTrue();
        return created.Value;
    }

    private string[] Shown() => [.. _vm.Rows.Select(row => row.DisplayText)];

    private void Choose(int entry) => _sidebar.SelectEntryCommand.Execute(entry);

    private void Mark(params int[] positions)
    {
        foreach (var position in positions)
        {
            _vm.SelectedIndex = position;
            _vm.ToggleMarkCommand.Execute(null);
        }
    }

    // --- what the list shows ---

    [Fact]
    public async Task The_pinned_view_lists_only_pinned_clips_and_the_search_works_inside_it()
    {
        await AddAsync("loose apple");
        await AddAsync("pinned apple", pinned: true);
        await AddAsync("pinned pear", pinned: true);
        _vm.Open();

        Choose(1);
        Shown().ShouldBe(["pinned pear", "pinned apple"]);

        _vm.SearchText = "apple";
        _time.FireNext().ShouldBeTrue();
        Shown().ShouldBe(["pinned apple"]);
    }

    [Fact]
    public async Task A_board_view_lists_the_clips_of_the_board_in_the_order_of_the_board()
    {
        var work = await NewBoardAsync("Work");
        var first = await AddAsync("first copied");
        var second = await AddAsync("second copied");
        await AddAsync("not on the board");
        await _catalog.AddClipAsync(work.Id, second.Id, Ct);
        await _catalog.AddClipAsync(work.Id, first.Id, Ct);
        _vm.Open();

        Choose(2);

        Shown().ShouldBe(["second copied", "first copied"]);
    }

    [Fact]
    public async Task A_search_in_a_board_view_looks_at_that_board_only()
    {
        var work = await NewBoardAsync("Work");
        var a = await AddAsync("apple on the board");
        await AddAsync("apple off the board");
        await _catalog.AddClipAsync(work.Id, a.Id, Ct);
        _vm.Open();
        Choose(2);

        _vm.SearchText = "apple";
        _time.FireNext().ShouldBeTrue();

        Shown().ShouldBe(["apple on the board"]);
    }

    [Fact]
    public async Task Choosing_another_entry_keeps_the_search_and_highlights_the_first_row()
    {
        await AddAsync("apple one", pinned: true);
        await AddAsync("apple two", pinned: true);
        await AddAsync("apple three");
        _vm.Open();
        _vm.SearchText = "apple";
        _time.FireNext().ShouldBeTrue();
        _vm.SelectedIndex = 2;

        Choose(1);

        _vm.SearchText.ShouldBe("apple");
        Shown().ShouldBe(["apple two", "apple one"]);
        _vm.SelectedIndex.ShouldBe(0);
    }

    [Fact]
    public async Task Opening_the_popup_starts_on_history_again()
    {
        await AddAsync("pinned", pinned: true);
        await AddAsync("loose");
        _vm.Open();
        Choose(1);
        _vm.Close();

        _vm.Open();

        _sidebar.Scope.ShouldBe(PopupScope.History);
        Shown().ShouldBe(["loose", "pinned"]);
    }

    [Fact]
    public async Task A_clip_put_on_the_board_while_it_is_shown_appears_in_the_list()
    {
        var work = await NewBoardAsync("Work");
        var a = await AddAsync("later");
        _vm.Open();
        Choose(2);
        Shown().ShouldBeEmpty();

        await _catalog.AddClipAsync(work.Id, a.Id, Ct);

        Shown().ShouldBe(["later"]);
    }

    [Fact]
    public async Task When_the_shown_board_is_deleted_the_list_goes_back_to_history()
    {
        var work = await NewBoardAsync("Work");
        var a = await AddAsync("on the board");
        await AddAsync("other");
        await _catalog.AddClipAsync(work.Id, a.Id, Ct);
        _vm.Open();
        Choose(2);
        Shown().ShouldBe(["on the board"]);

        await _catalog.DeleteAsync(work.Id, Ct);

        Shown().ShouldBe(["other", "on the board"]);
    }

    [Fact]
    public async Task Marks_are_kept_when_another_view_is_chosen()
    {
        await AddAsync("kept pinned", pinned: true);
        _vm.Open();
        Mark(0);

        Choose(1);

        _vm.MarkedCount.ShouldBe(1);
        _vm.Rows.Single().IsMarked.ShouldBeTrue();
    }

    [Fact]
    public async Task A_clip_in_a_board_view_is_pasted_like_any_other()
    {
        var work = await NewBoardAsync("Work");
        var a = await AddAsync("paste me");
        await _catalog.AddClipAsync(work.Id, a.Id, Ct);
        _vm.Open();
        Choose(2);

        await _vm.PasteCommand.ExecuteAsync(null);

        _paste.Pasted.Single().Id.ShouldBe(a.Id);
    }

    // --- what is said when nothing is listed ---

    [Fact]
    public async Task Each_view_says_what_to_do_when_it_is_empty()
    {
        await NewBoardAsync("Work");
        _vm.Open();
        _vm.EmptyText.ShouldBe("Nothing copied yet. Copy something and it will show up here.");

        Choose(1);
        _vm.EmptyText.ShouldBe("No pinned clips yet. Press Ctrl+P on a clip to pin it.");

        Choose(2);
        _vm.EmptyText.ShouldBe("This board is empty. Press Ctrl+D on a clip to add it.");
        _vm.ShowEmptyState.ShouldBeTrue();
    }

    [Fact]
    public async Task A_search_with_no_match_says_so_in_every_view()
    {
        await AddAsync("apple", pinned: true);
        _vm.Open();
        _vm.SearchText = "zzz";
        _time.FireNext().ShouldBeTrue();

        _vm.EmptyText.ShouldBe("No clips match your search.");
        Choose(1);
        _vm.EmptyText.ShouldBe("No clips match your search.");
    }

    // --- putting clips on boards ---

    [Fact]
    public async Task The_highlighted_clip_is_put_on_a_board_and_pinned_and_the_popup_stays_open()
    {
        var work = await NewBoardAsync("Work");
        var a = await AddAsync("add me");
        _vm.Open();

        await _vm.AddToBoardCommand.ExecuteAsync(work.Id);

        _library.Clips.Single().OnBoard.ShouldBe(work.Id);
        _library.Clips.Single().IsPinned.ShouldBeTrue();
        _vm.IsOpen.ShouldBeTrue();
        _vm.ErrorMessage.ShouldBeNull();
        _sidebar.Entries[2].Count.ShouldBe(1);
        _library.Clips.Single().Id.ShouldBe(a.Id);
    }

    [Fact]
    public async Task Marked_clips_are_put_on_a_board_in_the_order_they_were_marked_and_the_marks_go()
    {
        var work = await NewBoardAsync("Work");
        await AddAsync("a");
        await AddAsync("b");
        await AddAsync("c");
        _vm.Open();
        Mark(2, 0);

        await _vm.AddToBoardCommand.ExecuteAsync(work.Id);

        _catalog.ClipsOf(work.Id).Select(clip => ((TextContent)clip.Content).Text).ShouldBe(["a", "c"]);
        _vm.MarkedCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_board_that_is_gone_is_reported_and_the_marks_stay()
    {
        await AddAsync("a");
        _vm.Open();
        Mark(0);

        await _vm.AddToBoardCommand.ExecuteAsync(BoardId.New());

        _vm.ErrorMessage.ShouldBe("That board no longer exists.");
        _vm.MarkedCount.ShouldBe(1);
    }

    [Fact]
    public async Task With_no_clips_nothing_is_put_on_a_board()
    {
        var work = await NewBoardAsync("Work");
        _vm.Open();

        await _vm.AddToBoardCommand.ExecuteAsync(work.Id);

        _vm.ErrorMessage.ShouldBeNull();
        _boardStore.Added.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_clip_is_taken_off_its_board_and_leaves_the_board_view_but_stays_pinned()
    {
        var work = await NewBoardAsync("Work");
        var a = await AddAsync("a");
        var b = await AddAsync("b");
        await _catalog.AddClipAsync(work.Id, a.Id, Ct);
        await _catalog.AddClipAsync(work.Id, b.Id, Ct);
        _vm.Open();
        Choose(2);
        _vm.SelectedIndex = 0;

        await _vm.RemoveFromBoardCommand.ExecuteAsync(null);

        Shown().ShouldBe(["b"]);
        _library.Clips.ShouldAllBe(clip => clip.IsPinned);
        _library.Clips.Single(clip => clip.Id == a.Id).OnBoard.ShouldBeNull();
    }

    [Fact]
    public async Task Marked_clips_are_all_taken_off_their_board()
    {
        var work = await NewBoardAsync("Work");
        var a = await AddAsync("a");
        var b = await AddAsync("b");
        await _catalog.AddClipAsync(work.Id, a.Id, Ct);
        await _catalog.AddClipAsync(work.Id, b.Id, Ct);
        _vm.Open();
        Choose(2);
        Mark(0, 1);

        await _vm.RemoveFromBoardCommand.ExecuteAsync(null);

        Shown().ShouldBeEmpty();
        _vm.MarkedCount.ShouldBe(0);
    }

    // --- order on a board ---

    [Fact]
    public async Task A_clip_is_moved_up_and_down_a_board_and_the_highlight_goes_with_it()
    {
        var work = await NewBoardAsync("Work");
        var a = await AddAsync("a");
        var b = await AddAsync("b");
        var c = await AddAsync("c");
        foreach (var clip in new[] { a, b, c })
        {
            await _catalog.AddClipAsync(work.Id, clip.Id, Ct);
        }

        _vm.Open();
        Choose(2);
        _vm.SelectedIndex = 2;

        await _vm.MoveUpInBoardCommand.ExecuteAsync(null);
        Shown().ShouldBe(["a", "c", "b"]);
        _vm.Current!.Id.ShouldBe(c.Id);

        await _vm.MoveUpInBoardCommand.ExecuteAsync(null);
        Shown().ShouldBe(["c", "a", "b"]);
        _vm.SelectedIndex.ShouldBe(0);

        await _vm.MoveDownInBoardCommand.ExecuteAsync(null);
        Shown().ShouldBe(["a", "c", "b"]);
        _vm.Current!.Id.ShouldBe(c.Id);
    }

    [Fact]
    public async Task Moving_past_the_top_or_the_bottom_does_nothing()
    {
        var work = await NewBoardAsync("Work");
        var a = await AddAsync("a");
        var b = await AddAsync("b");
        await _catalog.AddClipAsync(work.Id, a.Id, Ct);
        await _catalog.AddClipAsync(work.Id, b.Id, Ct);
        _vm.Open();
        Choose(2);

        _vm.SelectedIndex = 0;
        await _vm.MoveUpInBoardCommand.ExecuteAsync(null);
        _vm.SelectedIndex = 1;
        await _vm.MoveDownInBoardCommand.ExecuteAsync(null);

        Shown().ShouldBe(["a", "b"]);
        _vm.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Moving_does_nothing_outside_a_board_view_or_while_a_search_is_typed()
    {
        var work = await NewBoardAsync("Work");
        var a = await AddAsync("apple");
        var b = await AddAsync("apple pie");
        await _catalog.AddClipAsync(work.Id, a.Id, Ct);
        await _catalog.AddClipAsync(work.Id, b.Id, Ct);
        _vm.Open();

        _vm.SelectedIndex = 1;
        await _vm.MoveUpInBoardCommand.ExecuteAsync(null);
        _catalog.ClipsOf(work.Id).Select(clip => clip.Id).ShouldBe([a.Id, b.Id]);

        Choose(2);
        _vm.SearchText = "apple";
        _time.FireNext().ShouldBeTrue();
        _vm.SelectedIndex = 1;
        await _vm.MoveUpInBoardCommand.ExecuteAsync(null);
        _catalog.ClipsOf(work.Id).Select(clip => clip.Id).ShouldBe([a.Id, b.Id]);
    }

    // --- keys and hints ---

    [Fact]
    public async Task Esc_backs_out_of_the_name_box_first_and_then_closes_the_popup()
    {
        await NewBoardAsync("Work");
        _vm.Open();
        _sidebar.StartNewCommand.Execute(null);

        _vm.EscapeCommand.Execute(null);
        _sidebar.IsEditing.ShouldBeFalse();
        _vm.IsOpen.ShouldBeTrue();

        _vm.EscapeCommand.Execute(null);
        _vm.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task Esc_withdraws_the_question_about_deleting_a_board_before_anything_else()
    {
        await NewBoardAsync("Work");
        _vm.Open();
        Choose(2);
        await _sidebar.DeleteBoardCommand.ExecuteAsync(null);
        _sidebar.IsConfirmingDelete.ShouldBeTrue();

        _vm.EscapeCommand.Execute(null);

        _sidebar.IsConfirmingDelete.ShouldBeFalse();
        _vm.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task The_status_line_follows_what_the_sidebar_is_doing()
    {
        await NewBoardAsync("Work");
        _vm.Open();
        var normal = _vm.StatusText;
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _sidebar.StartNewCommand.Execute(null);
        _vm.StatusText.ShouldBe("Enter save \u00B7 Esc cancel");
        changes.ShouldContain(nameof(PopupViewModel.StatusText));
        _sidebar.CancelEditCommand.Execute(null);
        _vm.StatusText.ShouldBe(normal);

        Choose(2);
        await _sidebar.DeleteBoardCommand.ExecuteAsync(null);
        _vm.StatusText.ShouldBe(_sidebar.DeleteQuestion);
    }

    [Fact]
    public async Task A_board_view_hints_at_moving_and_removing()
    {
        var work = await NewBoardAsync("Work");
        var a = await AddAsync("a");
        await _catalog.AddClipAsync(work.Id, a.Id, Ct);
        _vm.Open();

        Choose(2);

        _vm.StatusText.ShouldBe("Enter paste \u00B7 Ctrl+Up/Down move \u00B7 Ctrl+Shift+D remove \u00B7 Esc close");
    }

    [Fact]
    public async Task Closing_the_popup_cancels_a_name_that_was_being_typed()
    {
        await NewBoardAsync("Work");
        _vm.Open();
        _sidebar.StartNewCommand.Execute(null);

        _vm.Close();

        _sidebar.IsEditing.ShouldBeFalse();
    }

    [Fact]
    public void A_missing_sidebar_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new PopupViewModel(_library, _paste, new FakeQueueControl(), new CollectToggle(new FakeCollectControl(), new FakeNotifier()), TestPickers.New(), null!, _settings, new ImmediateDispatcher(), _time));
    }
}
