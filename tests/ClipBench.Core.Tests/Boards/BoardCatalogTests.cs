using ClipBench.Core.Boards;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Boards;

public sealed class BoardCatalogTests : IDisposable
{
    private static readonly ResultError _storeError = new(ErrorCode.StorageFailure, "the disk said no");

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeClipStore _clipStore = new();
    private readonly FakeBoardStore _boardStore = new();
    private readonly ClipLibrary _library;
    private readonly BoardCatalog _catalog;

    public BoardCatalogTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _clock), _clipStore);
        _catalog = new BoardCatalog(_library, _boardStore);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _catalog.Dispose();
        _library.Dispose();
    }

    private async Task<Board> CreateAsync(string name)
    {
        var created = await _catalog.CreateAsync(name, Ct);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : null);
        return created.Value;
    }

    private async Task<Clip> AddClipAsync(string text)
    {
        var clip = TestClips.Text(text);
        (await _library.AddAsync(clip, Ct)).IsSuccess.ShouldBeTrue();
        return clip;
    }

    private string[] Names() => [.. _catalog.Boards.Select(board => board.Name)];

    private string[] Order(Board board) => [.. _catalog.ClipsOf(board.Id).Select(clip => ((TextContent)clip.Content).Text)];

    // --- creating ---

    [Fact]
    public async Task A_new_catalog_has_no_boards()
    {
        _catalog.Boards.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Creating_a_board_trims_the_name_stores_it_and_puts_it_at_the_end()
    {
        var work = await CreateAsync("  Work ");
        var home = await CreateAsync("Home");

        work.Name.ShouldBe("Work");
        work.Position.ShouldBe(0);
        home.Position.ShouldBe(1);
        Names().ShouldBe(["Work", "Home"]);
        _boardStore.Added.ShouldBe([work, home]);
        _catalog.Find(work.Id).ShouldBe(work);
    }

    [Fact]
    public async Task Creating_a_board_raises_changed_once()
    {
        var raised = 0;
        _catalog.Changed += (_, _) => raised++;

        await CreateAsync("Work");

        raised.ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two\nlines")]
    public async Task A_name_that_is_not_allowed_is_refused_and_nothing_is_stored(string name)
    {
        var created = await _catalog.CreateAsync(name, Ct);

        created.Error.Code.ShouldBe(ErrorCode.InvalidBoardName);
        _catalog.Boards.ShouldBeEmpty();
        _boardStore.Added.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("work")]
    [InlineData("WORK")]
    [InlineData("  Work  ")]
    public async Task A_name_that_is_taken_is_refused_ignoring_capital_letters(string name)
    {
        await CreateAsync("Work");

        var created = await _catalog.CreateAsync(name, Ct);

        created.Error.Code.ShouldBe(ErrorCode.BoardNameTaken);
        _catalog.Boards.Length.ShouldBe(1);
    }

    [Fact]
    public async Task There_can_be_only_so_many_boards()
    {
        for (var i = 0; i < BoardName.MaxBoards; i++)
        {
            await CreateAsync("Board " + i);
        }

        var created = await _catalog.CreateAsync("One too many", Ct);

        created.Error.Code.ShouldBe(ErrorCode.TooManyBoards);
        _catalog.Boards.Length.ShouldBe(BoardName.MaxBoards);
    }

    [Fact]
    public async Task When_the_store_fails_the_board_still_shows_and_the_failure_is_returned()
    {
        _boardStore.Failure = _storeError;

        var created = await _catalog.CreateAsync("Work", Ct);

        created.Error.ShouldBe(_storeError);
        Names().ShouldBe(["Work"]);
    }

    [Fact]
    public async Task A_board_made_after_one_was_deleted_still_goes_at_the_end()
    {
        var a = await CreateAsync("A");
        await CreateAsync("B");
        await _catalog.DeleteAsync(a.Id, Ct);

        var c = await CreateAsync("C");

        c.Position.ShouldBe(2);
        Names().ShouldBe(["B", "C"]);
    }

    // --- renaming ---

    [Fact]
    public async Task Renaming_changes_the_name_and_stores_it()
    {
        var work = await CreateAsync("Work");

        (await _catalog.RenameAsync(work.Id, "  Office ", Ct)).IsSuccess.ShouldBeTrue();

        Names().ShouldBe(["Office"]);
        _catalog.Find(work.Id)!.Position.ShouldBe(0);
        _boardStore.Renamed.ShouldBe([(work.Id, "Office")]);
    }

    [Fact]
    public async Task A_board_can_be_renamed_to_the_same_name_in_other_letters()
    {
        var work = await CreateAsync("work");

        (await _catalog.RenameAsync(work.Id, "Work", Ct)).IsSuccess.ShouldBeTrue();

        Names().ShouldBe(["Work"]);
    }

    [Fact]
    public async Task A_board_cannot_take_the_name_of_another()
    {
        await CreateAsync("Work");
        var home = await CreateAsync("Home");

        var renamed = await _catalog.RenameAsync(home.Id, "WORK", Ct);

        renamed.Error.Code.ShouldBe(ErrorCode.BoardNameTaken);
        Names().ShouldBe(["Work", "Home"]);
    }

    [Fact]
    public async Task Renaming_to_a_name_that_is_not_allowed_or_an_unknown_board_fails()
    {
        var work = await CreateAsync("Work");

        (await _catalog.RenameAsync(work.Id, " ", Ct)).Error.Code.ShouldBe(ErrorCode.InvalidBoardName);
        (await _catalog.RenameAsync(BoardId.New(), "Other", Ct)).Error.Code.ShouldBe(ErrorCode.BoardNotFound);
        _boardStore.Renamed.ShouldBeEmpty();
    }

    [Fact]
    public async Task Renaming_raises_changed()
    {
        var work = await CreateAsync("Work");
        var raised = 0;
        _catalog.Changed += (_, _) => raised++;

        await _catalog.RenameAsync(work.Id, "Office", Ct);

        raised.ShouldBe(1);
    }

    // --- deleting ---

    [Fact]
    public async Task Deleting_a_board_removes_it_and_leaves_its_clips_pinned_and_on_no_board()
    {
        var work = await CreateAsync("Work");
        var a = await AddClipAsync("a");
        await _catalog.AddClipAsync(work.Id, a.Id, Ct);

        (await _catalog.DeleteAsync(work.Id, Ct)).IsSuccess.ShouldBeTrue();

        _catalog.Boards.ShouldBeEmpty();
        _catalog.Find(work.Id).ShouldBeNull();
        _boardStore.Deleted.ShouldBe([work.Id]);
        var clip = _library.Clips.Single();
        clip.OnBoard.ShouldBeNull();
        clip.IsPinned.ShouldBeTrue();
    }

    [Fact]
    public async Task Deleting_a_board_leaves_the_other_boards_alone()
    {
        var work = await CreateAsync("Work");
        var home = await CreateAsync("Home");
        var x = await AddClipAsync("x");
        await _catalog.AddClipAsync(home.Id, x.Id, Ct);

        await _catalog.DeleteAsync(work.Id, Ct);

        Order(home).ShouldBe(["x"]);
        Names().ShouldBe(["Home"]);
    }

    [Fact]
    public async Task Deleting_an_unknown_board_fails()
    {
        (await _catalog.DeleteAsync(BoardId.New(), Ct)).Error.Code.ShouldBe(ErrorCode.BoardNotFound);
        _boardStore.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_the_store_fails_to_delete_the_board_is_gone_from_the_list_and_the_failure_is_returned()
    {
        var work = await CreateAsync("Work");
        _boardStore.Failure = _storeError;

        var deleted = await _catalog.DeleteAsync(work.Id, Ct);

        deleted.Error.ShouldBe(_storeError);
        _catalog.Boards.ShouldBeEmpty();
    }

    // --- clips on a board ---

    [Fact]
    public async Task Putting_clips_on_a_board_pins_them_and_lists_them_in_order()
    {
        var work = await CreateAsync("Work");
        var a = await AddClipAsync("a");
        var b = await AddClipAsync("b");

        (await _catalog.AddClipAsync(work.Id, b.Id, Ct)).IsSuccess.ShouldBeTrue();
        (await _catalog.AddClipAsync(work.Id, a.Id, Ct)).IsSuccess.ShouldBeTrue();

        Order(work).ShouldBe(["b", "a"]);
        _library.Clips.ShouldAllBe(clip => clip.IsPinned);
    }

    [Fact]
    public async Task A_clip_put_on_another_board_leaves_the_first()
    {
        var work = await CreateAsync("Work");
        var home = await CreateAsync("Home");
        var a = await AddClipAsync("a");
        await _catalog.AddClipAsync(work.Id, a.Id, Ct);

        await _catalog.AddClipAsync(home.Id, a.Id, Ct);

        Order(work).ShouldBeEmpty();
        Order(home).ShouldBe(["a"]);
    }

    [Fact]
    public async Task Putting_a_clip_on_an_unknown_board_or_an_unknown_clip_fails()
    {
        var work = await CreateAsync("Work");
        var a = await AddClipAsync("a");

        (await _catalog.AddClipAsync(BoardId.New(), a.Id, Ct)).Error.Code.ShouldBe(ErrorCode.BoardNotFound);
        (await _catalog.AddClipAsync(work.Id, ClipId.New(), Ct)).Error.Code.ShouldBe(ErrorCode.ClipNotFound);
        _library.Clips.Single().OnBoard.ShouldBeNull();
    }

    [Fact]
    public async Task Taking_a_clip_off_its_board_leaves_it_pinned()
    {
        var work = await CreateAsync("Work");
        var a = await AddClipAsync("a");
        await _catalog.AddClipAsync(work.Id, a.Id, Ct);

        (await _catalog.RemoveClipAsync(a.Id, Ct)).IsSuccess.ShouldBeTrue();

        Order(work).ShouldBeEmpty();
        _library.Clips.Single().IsPinned.ShouldBeTrue();
    }

    [Fact]
    public async Task Taking_an_unknown_clip_off_a_board_fails()
    {
        (await _catalog.RemoveClipAsync(ClipId.New(), Ct)).Error.Code.ShouldBe(ErrorCode.ClipNotFound);
    }

    [Fact]
    public async Task Moving_a_clip_changes_the_order_of_the_board()
    {
        var work = await CreateAsync("Work");
        var a = await AddClipAsync("a");
        var b = await AddClipAsync("b");
        var c = await AddClipAsync("c");
        foreach (var clip in new[] { a, b, c })
        {
            await _catalog.AddClipAsync(work.Id, clip.Id, Ct);
        }

        (await _catalog.MoveClipAsync(c.Id, 0, Ct)).IsSuccess.ShouldBeTrue();

        Order(work).ShouldBe(["c", "a", "b"]);
    }

    [Fact]
    public async Task Moving_a_clip_that_is_on_no_board_fails()
    {
        var a = await AddClipAsync("a");

        (await _catalog.MoveClipAsync(a.Id, 0, Ct)).Error.Code.ShouldBe(ErrorCode.ClipNotOnBoard);
    }

    [Fact]
    public async Task A_board_with_nothing_on_it_lists_nothing_and_an_unknown_board_too()
    {
        var work = await CreateAsync("Work");

        Order(work).ShouldBeEmpty();
        _catalog.ClipsOf(BoardId.New()).ShouldBeEmpty();
        await Task.CompletedTask;
    }

    // --- loading ---

    [Fact]
    public async Task Loading_reads_the_boards_in_the_order_of_their_places()
    {
        var home = new Board(BoardId.New(), "Home", 5);
        var work = new Board(BoardId.New(), "Work", 2);
        _boardStore.Stored = [home, work];

        var loaded = await _catalog.LoadAsync(Ct);

        loaded.Value.ShouldBe(2);
        Names().ShouldBe(["Work", "Home"]);
    }

    [Fact]
    public async Task A_board_made_after_loading_goes_after_the_last_one_loaded()
    {
        _boardStore.Stored = [new Board(BoardId.New(), "Work", 7)];
        await _catalog.LoadAsync(Ct);

        var home = await CreateAsync("Home");

        home.Position.ShouldBe(8);
    }

    [Fact]
    public async Task Loading_takes_clips_off_boards_that_no_longer_exist()
    {
        var work = new Board(BoardId.New(), "Work", 0);
        var gone = BoardId.New();
        var onWork = TestClips.Text("on work") with { IsPinned = true, OnBoard = work.Id };
        var onGone = TestClips.Text("on gone") with { IsPinned = true, OnBoard = gone };
        _clipStore.Stored = [onWork, onGone];
        await _library.LoadAsync(Ct);
        _boardStore.Stored = [work];

        await _catalog.LoadAsync(Ct);

        Order(work).ShouldBe(["on work"]);
        _library.Clips.Single(clip => clip.Id == onGone.Id).OnBoard.ShouldBeNull();
        _clipStore.BoardChanges.ShouldHaveSingleItem().ShouldBe([new ClipPlacement(onGone.Id, null, 0)]);
    }

    [Fact]
    public async Task When_the_boards_cannot_be_read_loading_fails_and_changes_nothing()
    {
        _boardStore.Failure = _storeError;

        var loaded = await _catalog.LoadAsync(Ct);

        loaded.Error.ShouldBe(_storeError);
        _catalog.Boards.ShouldBeEmpty();
        _clipStore.BoardChanges.ShouldBeEmpty();
    }

    // --- bugs ---

    [Fact]
    public void Missing_arguments_are_bugs()
    {
        Should.Throw<ArgumentNullException>(() => new BoardCatalog(null!, _boardStore));
        Should.Throw<ArgumentNullException>(() => new BoardCatalog(_library, null!));
        Should.Throw<ArgumentNullException>(() => _catalog.CreateAsync(null!, Ct));
        Should.Throw<ArgumentNullException>(() => _catalog.RenameAsync(BoardId.New(), null!, Ct));
    }
}
