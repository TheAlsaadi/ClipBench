using ClipBench.Core.Boards;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Presentation.Boards;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Boards;

public sealed class SidebarViewModelTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakeClipStore _clipStore = new();
    private readonly FakeBoardStore _boardStore = new();
    private readonly ClipLibrary _library;
    private readonly BoardCatalog _catalog;
    private readonly SidebarViewModel _sidebar;
    private int _scopeChanges;

    public SidebarViewModelTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _time), _clipStore);
        _catalog = new BoardCatalog(_library, _boardStore);
        _sidebar = new SidebarViewModel(_catalog, _library, new ImmediateDispatcher());
        _sidebar.ScopeChanged += (_, _) => _scopeChanges++;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _sidebar.Dispose();
        _catalog.Dispose();
        _library.Dispose();
    }

    private async Task<Board> NewBoardAsync(string name)
    {
        var created = await _catalog.CreateAsync(name, Ct);
        created.IsSuccess.ShouldBeTrue();
        return created.Value;
    }

    private async Task<Clip> NewClipAsync(string text, bool pinned = false)
    {
        var clip = TestClips.Text(text, pinned: pinned);
        (await _library.AddAsync(clip, Ct)).IsSuccess.ShouldBeTrue();
        return clip;
    }

    private string[] Names() => [.. _sidebar.Entries.Select(entry => entry.Name)];

    private int[] Counts() => [.. _sidebar.Entries.Select(entry => entry.Count)];

    private async Task EditAsync(string text)
    {
        _sidebar.EditText = text;
        await _sidebar.SubmitEditCommand.ExecuteAsync(null);
    }

    // --- the entries ---

    [Fact]
    public void A_new_sidebar_shows_history_and_pinned_chosen_on_history()
    {
        Names().ShouldBe(["History", "Pinned"]);
        _sidebar.SelectedIndex.ShouldBe(0);
        _sidebar.Scope.ShouldBe(PopupScope.History);
        _sidebar.CurrentBoard.ShouldBeNull();
        _sidebar.IsOpen.ShouldBeFalse();
        _sidebar.IsEditing.ShouldBeFalse();
    }

    [Fact]
    public async Task Boards_follow_in_the_order_of_the_catalog_with_counts_of_their_clips()
    {
        var work = await NewBoardAsync("Work");
        await NewBoardAsync("Home");
        var a = await NewClipAsync("a");
        var b = await NewClipAsync("b");
        await NewClipAsync("loose");
        await _catalog.AddClipAsync(work.Id, a.Id, Ct);
        await _catalog.AddClipAsync(work.Id, b.Id, Ct);

        Names().ShouldBe(["History", "Pinned", "Work", "Home"]);
        Counts().ShouldBe([3, 2, 2, 0]);
        _sidebar.BoardEntries.Select(entry => entry.Name).ShouldBe(["Work", "Home"]);
        _sidebar.Entries[2].Board.ShouldBe(work.Id);
        _sidebar.Entries[0].IsBoard.ShouldBeFalse();
        _sidebar.Entries[2].IsBoard.ShouldBeTrue();
    }

    [Fact]
    public async Task The_counts_follow_the_clips()
    {
        await NewClipAsync("one");
        var two = await NewClipAsync("two");

        Counts().ShouldBe([2, 0]);

        await _library.SetPinnedAsync(two.Id, true, Ct);
        Counts().ShouldBe([2, 1]);

        await _library.RemoveAsync(two.Id, Ct);
        Counts().ShouldBe([1, 0]);
    }

    [Fact]
    public async Task A_screen_reader_hears_the_name_the_kind_and_the_count()
    {
        var work = await NewBoardAsync("Work");
        await NewClipAsync("a");
        var b = await NewClipAsync("b");
        await _catalog.AddClipAsync(work.Id, b.Id, Ct);

        _sidebar.Entries[0].AutomationName.ShouldBe("History, 2 clips");
        _sidebar.Entries[1].AutomationName.ShouldBe("Pinned, 1 clip");
        _sidebar.Entries[2].AutomationName.ShouldBe("Board Work, 1 clip");
    }

    [Fact]
    public async Task Printing_an_entry_never_shows_a_board_name()
    {
        await NewBoardAsync("secret-marker");

        _sidebar.Entries[2].ToString().ShouldNotContain("secret-marker");
    }

    // --- choosing ---

    [Fact]
    public void Toggling_shows_and_hides_the_sidebar()
    {
        _sidebar.ToggleCommand.Execute(null);
        _sidebar.IsOpen.ShouldBeTrue();

        _sidebar.ToggleCommand.Execute(null);
        _sidebar.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task Choosing_an_entry_changes_what_the_list_shows_and_says_so_once()
    {
        var work = await NewBoardAsync("Work");

        _sidebar.SelectEntryCommand.Execute(1);
        _sidebar.Scope.ShouldBe(PopupScope.Pinned);
        _scopeChanges.ShouldBe(1);

        _sidebar.SelectEntryCommand.Execute(2);
        _sidebar.Scope.ShouldBe(PopupScope.Board);
        _sidebar.CurrentBoard.ShouldBe(work.Id);
        _scopeChanges.ShouldBe(2);

        _sidebar.SelectEntryCommand.Execute(2);
        _scopeChanges.ShouldBe(2);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(99)]
    public void An_entry_that_does_not_exist_is_ignored(int index)
    {
        _sidebar.SelectEntryCommand.Execute(index);

        _sidebar.SelectedIndex.ShouldBe(0);
        _scopeChanges.ShouldBe(0);
    }

    [Fact]
    public async Task Next_and_previous_walk_the_entries_and_wrap_around()
    {
        await NewBoardAsync("Work");

        _sidebar.PreviousCommand.Execute(null);
        _sidebar.SelectedIndex.ShouldBe(2);

        _sidebar.NextCommand.Execute(null);
        _sidebar.SelectedIndex.ShouldBe(0);

        _sidebar.NextCommand.Execute(null);
        _sidebar.SelectedIndex.ShouldBe(1);
    }

    [Fact]
    public async Task Resetting_goes_back_to_history_and_leaves_the_sidebar_as_it_was()
    {
        await NewBoardAsync("Work");
        _sidebar.ToggleCommand.Execute(null);
        _sidebar.SelectEntryCommand.Execute(2);
        _sidebar.StartRenameCommand.Execute(null);

        _sidebar.Reset();

        _sidebar.SelectedIndex.ShouldBe(0);
        _sidebar.IsEditing.ShouldBeFalse();
        _sidebar.IsOpen.ShouldBeTrue();
    }

    // --- a new board ---

    [Fact]
    public async Task A_new_board_is_named_made_and_chosen()
    {
        _sidebar.StartNewCommand.Execute(null);
        _sidebar.IsEditing.ShouldBeTrue();
        _sidebar.IsRenaming.ShouldBeFalse();
        _sidebar.EditText.ShouldBeEmpty();

        await EditAsync("  Work  ");

        Names().ShouldBe(["History", "Pinned", "Work"]);
        _sidebar.IsEditing.ShouldBeFalse();
        _sidebar.Scope.ShouldBe(PopupScope.Board);
        _sidebar.Current.Name.ShouldBe("Work");
        _boardStore.Added.Single().Name.ShouldBe("Work");
        _scopeChanges.ShouldBe(1);
    }

    [Fact]
    public async Task A_name_that_is_not_allowed_stays_in_the_box_with_the_reason()
    {
        _sidebar.StartNewCommand.Execute(null);

        await EditAsync("   ");

        _sidebar.IsEditing.ShouldBeTrue();
        _sidebar.EditError.ShouldNotBeNullOrWhiteSpace();
        Names().ShouldBe(["History", "Pinned"]);
    }

    [Fact]
    public async Task A_name_that_is_taken_stays_in_the_box_with_the_reason()
    {
        await NewBoardAsync("Work");
        _sidebar.StartNewCommand.Execute(null);

        await EditAsync("WORK");

        _sidebar.IsEditing.ShouldBeTrue();
        _sidebar.EditError.ShouldBe("Another board already has that name.");
    }

    [Fact]
    public async Task Typing_again_after_a_refusal_clears_the_reason_on_the_next_try()
    {
        _sidebar.StartNewCommand.Execute(null);
        await EditAsync(" ");
        _sidebar.EditError.ShouldNotBeNull();

        await EditAsync("Home");

        _sidebar.EditError.ShouldBeNull();
        Names().ShouldBe(["History", "Pinned", "Home"]);
    }

    [Fact]
    public async Task When_the_board_cannot_be_saved_the_box_says_so_but_the_board_is_there()
    {
        _boardStore.Failure = new ResultError(ErrorCode.StorageFailure, "the disk said no");
        _sidebar.StartNewCommand.Execute(null);

        await EditAsync("Work");

        _sidebar.EditError.ShouldNotBeNull();
        Names().ShouldContain("Work");
    }

    // --- renaming ---

    [Fact]
    public async Task A_board_is_renamed_in_place()
    {
        var work = await NewBoardAsync("Work");
        _sidebar.SelectEntryCommand.Execute(2);

        _sidebar.StartRenameCommand.Execute(null);
        _sidebar.IsEditing.ShouldBeTrue();
        _sidebar.IsRenaming.ShouldBeTrue();
        _sidebar.EditText.ShouldBe("Work");

        await EditAsync("Office");

        Names().ShouldBe(["History", "Pinned", "Office"]);
        _sidebar.Current.Board.ShouldBe(work.Id);
        _sidebar.IsEditing.ShouldBeFalse();
        _boardStore.Renamed.ShouldBe([(work.Id, "Office")]);
    }

    [Fact]
    public void History_and_pinned_cannot_be_renamed()
    {
        _sidebar.StartRenameCommand.Execute(null);
        _sidebar.IsEditing.ShouldBeFalse();

        _sidebar.SelectEntryCommand.Execute(1);
        _sidebar.StartRenameCommand.Execute(null);
        _sidebar.IsEditing.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancelling_the_box_changes_nothing()
    {
        await NewBoardAsync("Work");
        _sidebar.SelectEntryCommand.Execute(2);
        _sidebar.StartRenameCommand.Execute(null);
        _sidebar.EditText = "Something else";

        _sidebar.CancelEditCommand.Execute(null);

        _sidebar.IsEditing.ShouldBeFalse();
        _sidebar.EditError.ShouldBeNull();
        Names().ShouldBe(["History", "Pinned", "Work"]);
        _boardStore.Renamed.ShouldBeEmpty();
    }

    [Fact]
    public async Task Submitting_when_no_box_is_shown_does_nothing()
    {
        await _sidebar.SubmitEditCommand.ExecuteAsync(null);

        _boardStore.Added.ShouldBeEmpty();
    }

    // --- deleting ---

    [Fact]
    public async Task A_board_is_deleted_after_a_question_and_its_clips_stay_pinned()
    {
        var work = await NewBoardAsync("Work");
        var clip = await NewClipAsync("kept");
        await _catalog.AddClipAsync(work.Id, clip.Id, Ct);
        _sidebar.SelectEntryCommand.Execute(2);
        _scopeChanges = 0;

        await _sidebar.DeleteBoardCommand.ExecuteAsync(null);
        _sidebar.IsConfirmingDelete.ShouldBeTrue();
        _sidebar.DeleteQuestion.ShouldContain("Work");
        _sidebar.DeleteQuestion.ShouldContain("stay");
        Names().ShouldContain("Work");

        await _sidebar.DeleteBoardCommand.ExecuteAsync(null);

        _sidebar.IsConfirmingDelete.ShouldBeFalse();
        Names().ShouldBe(["History", "Pinned"]);
        _sidebar.Scope.ShouldBe(PopupScope.History);
        _scopeChanges.ShouldBe(1);
        _boardStore.Deleted.ShouldBe([work.Id]);
        _library.Clips.Single().IsPinned.ShouldBeTrue();
        _library.Clips.Single().OnBoard.ShouldBeNull();
    }

    [Fact]
    public async Task Backing_out_of_the_question_keeps_the_board()
    {
        await NewBoardAsync("Work");
        _sidebar.SelectEntryCommand.Execute(2);
        await _sidebar.DeleteBoardCommand.ExecuteAsync(null);

        _sidebar.Cancel().ShouldBeTrue();

        _sidebar.IsConfirmingDelete.ShouldBeFalse();
        _boardStore.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public async Task Moving_to_another_entry_withdraws_the_question()
    {
        await NewBoardAsync("Work");
        _sidebar.SelectEntryCommand.Execute(2);
        await _sidebar.DeleteBoardCommand.ExecuteAsync(null);

        _sidebar.SelectEntryCommand.Execute(0);

        _sidebar.IsConfirmingDelete.ShouldBeFalse();
    }

    [Fact]
    public async Task History_and_pinned_cannot_be_deleted()
    {
        await _sidebar.DeleteBoardCommand.ExecuteAsync(null);

        _sidebar.IsConfirmingDelete.ShouldBeFalse();
        _boardStore.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public void Cancel_says_whether_there_was_anything_to_back_out_of()
    {
        _sidebar.Cancel().ShouldBeFalse();

        _sidebar.StartNewCommand.Execute(null);
        _sidebar.Cancel().ShouldBeTrue();
        _sidebar.IsEditing.ShouldBeFalse();
    }

    // --- boards changed elsewhere ---

    [Fact]
    public async Task A_board_made_elsewhere_appears_and_the_choice_stays_where_it_was()
    {
        var work = await NewBoardAsync("Work");
        _sidebar.SelectEntryCommand.Execute(2);

        await NewBoardAsync("Home");

        Names().ShouldBe(["History", "Pinned", "Work", "Home"]);
        _sidebar.CurrentBoard.ShouldBe(work.Id);
    }

    [Fact]
    public async Task When_the_chosen_board_is_deleted_elsewhere_the_sidebar_goes_back_to_history_and_says_so()
    {
        var work = await NewBoardAsync("Work");
        _sidebar.SelectEntryCommand.Execute(2);
        _scopeChanges = 0;

        await _catalog.DeleteAsync(work.Id, Ct);

        _sidebar.Scope.ShouldBe(PopupScope.History);
        _scopeChanges.ShouldBe(1);
    }

    [Fact]
    public async Task A_board_renamed_elsewhere_shows_its_new_name_without_changing_the_choice()
    {
        var work = await NewBoardAsync("Work");
        _sidebar.SelectEntryCommand.Execute(2);
        _scopeChanges = 0;

        await _catalog.RenameAsync(work.Id, "Office", Ct);

        _sidebar.Current.Name.ShouldBe("Office");
        _scopeChanges.ShouldBe(0);
    }

    // --- clips and boards ---

    [Fact]
    public async Task Clips_are_put_on_a_board_in_the_order_given_and_pinned()
    {
        var work = await NewBoardAsync("Work");
        var a = await NewClipAsync("a");
        var b = await NewClipAsync("b");

        (await _sidebar.AddClipsAsync(work.Id, [b.Id, a.Id], Ct)).IsSuccess.ShouldBeTrue();

        _catalog.ClipsOf(work.Id).Select(clip => clip.Id).ShouldBe([b.Id, a.Id]);
        _library.Clips.ShouldAllBe(clip => clip.IsPinned);
    }

    [Fact]
    public async Task Putting_clips_on_a_board_that_is_gone_fails()
    {
        var a = await NewClipAsync("a");

        (await _sidebar.AddClipsAsync(BoardId.New(), [a.Id], Ct)).Error.Code.ShouldBe(ErrorCode.BoardNotFound);
    }

    [Fact]
    public async Task Putting_clips_on_a_board_stops_at_the_first_that_cannot_be_put_there()
    {
        var work = await NewBoardAsync("Work");
        var a = await NewClipAsync("a");
        var c = await NewClipAsync("c");

        var result = await _sidebar.AddClipsAsync(work.Id, [a.Id, ClipId.New(), c.Id], Ct);

        result.Error.Code.ShouldBe(ErrorCode.ClipNotFound);
        _catalog.ClipsOf(work.Id).Select(clip => clip.Id).ShouldBe([a.Id]);
    }

    [Fact]
    public async Task Clips_are_taken_off_their_boards_and_stay_pinned()
    {
        var work = await NewBoardAsync("Work");
        var a = await NewClipAsync("a");
        var b = await NewClipAsync("b");
        await _sidebar.AddClipsAsync(work.Id, [a.Id, b.Id], Ct);

        (await _sidebar.RemoveClipsAsync([a.Id], Ct)).IsSuccess.ShouldBeTrue();

        _catalog.ClipsOf(work.Id).Select(clip => clip.Id).ShouldBe([b.Id]);
        _library.Clips.ShouldAllBe(clip => clip.IsPinned);
    }

    [Fact]
    public async Task A_clip_is_moved_within_its_board()
    {
        var work = await NewBoardAsync("Work");
        var a = await NewClipAsync("a");
        var b = await NewClipAsync("b");
        await _sidebar.AddClipsAsync(work.Id, [a.Id, b.Id], Ct);

        (await _sidebar.MoveClipAsync(b.Id, 0, Ct)).IsSuccess.ShouldBeTrue();

        _catalog.ClipsOf(work.Id).Select(clip => clip.Id).ShouldBe([b.Id, a.Id]);
    }

    // --- bugs and clean-up ---

    [Fact]
    public async Task After_disposing_changes_are_no_longer_followed()
    {
        var sidebar = new SidebarViewModel(_catalog, _library, new ImmediateDispatcher());
        sidebar.Dispose();

        await NewBoardAsync("Late");

        sidebar.Entries.Count.ShouldBe(2);
    }

    [Fact]
    public void Missing_arguments_are_bugs()
    {
        Should.Throw<ArgumentNullException>(() => new SidebarViewModel(null!, _library, new ImmediateDispatcher()));
        Should.Throw<ArgumentNullException>(() => new SidebarViewModel(_catalog, null!, new ImmediateDispatcher()));
        Should.Throw<ArgumentNullException>(() => new SidebarViewModel(_catalog, _library, null!));
        Should.Throw<ArgumentNullException>(() => _sidebar.AddClipsAsync(BoardId.New(), null!, Ct));
        Should.Throw<ArgumentNullException>(() => _sidebar.RemoveClipsAsync(null!, Ct));
        Should.Throw<ArgumentNullException>(() => new SidebarEntryViewModel(PopupScope.History, null, null!, 0));
    }
}
