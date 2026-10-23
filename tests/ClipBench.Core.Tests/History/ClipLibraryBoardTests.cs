using ClipBench.Core.Boards;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.History;

/// <summary>The library keeps boards in step with the store: memory first, then the store.</summary>
public sealed class ClipLibraryBoardTests : IDisposable
{
    private static readonly BoardId _work = BoardId.New();
    private static readonly BoardId _home = BoardId.New();
    private static readonly ResultError _storeError = new(ErrorCode.StorageFailure, "the disk said no");

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeClipStore _store = new();
    private readonly ClipLibrary _library;

    public ClipLibraryBoardTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _clock), _store);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _library.Dispose();

    private async Task<Clip> AddAsync(string text, bool pinned = false)
    {
        var clip = TestClips.Text(text, pinned: pinned);
        (await _library.AddAsync(clip, Ct)).IsSuccess.ShouldBeTrue();
        return clip;
    }

    private string[] Order(BoardId board) =>
        [.. _library.ClipsOnBoard(board).Select(clip => ((TextContent)clip.Content).Text)];

    // --- putting clips on a board ---

    [Fact]
    public async Task Putting_a_clip_on_a_board_pins_it_in_memory_and_in_the_store_and_stores_its_place()
    {
        var clip = await AddAsync("a");

        (await _library.SetBoardAsync(clip.Id, _work, Ct)).IsSuccess.ShouldBeTrue();

        _library.Clips.Single().OnBoard.ShouldBe(_work);
        _library.Clips.Single().IsPinned.ShouldBeTrue();
        _store.PinChanges.ShouldBe([(clip.Id, true)]);
        _store.BoardChanges.ShouldHaveSingleItem().ShouldBe([new ClipPlacement(clip.Id, _work, 0)]);
    }

    [Fact]
    public async Task A_clip_that_is_pinned_already_is_not_pinned_again_in_the_store()
    {
        var clip = await AddAsync("a", pinned: true);

        await _library.SetBoardAsync(clip.Id, _work, Ct);

        _store.PinChanges.ShouldBeEmpty();
        _store.BoardChanges.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Each_clip_goes_to_the_end_of_the_board()
    {
        var a = await AddAsync("a");
        var b = await AddAsync("b");

        await _library.SetBoardAsync(b.Id, _work, Ct);
        await _library.SetBoardAsync(a.Id, _work, Ct);

        Order(_work).ShouldBe(["b", "a"]);
        _store.BoardChanges[^1].ShouldBe([new ClipPlacement(a.Id, _work, 1)]);
    }

    [Fact]
    public async Task Taking_a_clip_off_its_board_stores_that_and_leaves_it_pinned()
    {
        var clip = await AddAsync("a");
        await _library.SetBoardAsync(clip.Id, _work, Ct);

        (await _library.SetBoardAsync(clip.Id, null, Ct)).IsSuccess.ShouldBeTrue();

        _library.Clips.Single().OnBoard.ShouldBeNull();
        _library.Clips.Single().IsPinned.ShouldBeTrue();
        _store.BoardChanges[^1].ShouldBe([new ClipPlacement(clip.Id, null, 0)]);
    }

    [Fact]
    public async Task Putting_an_unknown_clip_on_a_board_fails_and_stores_nothing()
    {
        var result = await _library.SetBoardAsync(ClipId.New(), _work, Ct);

        result.Error.Code.ShouldBe(ErrorCode.ClipNotFound);
        _store.BoardChanges.ShouldBeEmpty();
        _store.PinChanges.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_the_store_fails_the_change_still_shows_and_the_failure_is_returned()
    {
        var clip = await AddAsync("a");
        _store.Failure = _storeError;

        var result = await _library.SetBoardAsync(clip.Id, _work, Ct);

        result.Error.ShouldBe(_storeError);
        _library.Clips.Single().OnBoard.ShouldBe(_work);
    }

    [Fact]
    public async Task Changes_to_a_board_raise_changed()
    {
        var clip = await AddAsync("a");
        var raised = 0;
        _library.Changed += (_, _) => raised++;

        await _library.SetBoardAsync(clip.Id, _work, Ct);
        await _library.SetBoardAsync(clip.Id, null, Ct);

        raised.ShouldBe(2);
    }

    [Fact]
    public async Task Putting_a_clip_on_the_board_it_is_on_already_stores_nothing_new()
    {
        var clip = await AddAsync("a");
        await _library.SetBoardAsync(clip.Id, _work, Ct);
        var stored = _store.BoardChanges.Count;

        await _library.SetBoardAsync(clip.Id, _work, Ct);

        _store.BoardChanges.Count.ShouldBe(stored);
    }

    // --- unpinning ---

    [Fact]
    public async Task Unpinning_a_clip_takes_it_off_its_board_in_memory_and_asks_the_store_to_do_the_same()
    {
        var clip = await AddAsync("a");
        await _library.SetBoardAsync(clip.Id, _work, Ct);

        await _library.SetPinnedAsync(clip.Id, false, Ct);

        _library.Clips.Single().OnBoard.ShouldBeNull();
        _store.PinChanges[^1].ShouldBe((clip.Id, false));
    }

    // --- moving ---

    [Fact]
    public async Task Moving_a_clip_stores_the_new_place_of_every_clip_that_changed_place()
    {
        var a = await AddAsync("a");
        var b = await AddAsync("b");
        var c = await AddAsync("c");
        await _library.SetBoardAsync(a.Id, _work, Ct);
        await _library.SetBoardAsync(b.Id, _work, Ct);
        await _library.SetBoardAsync(c.Id, _work, Ct);

        (await _library.MoveInBoardAsync(c.Id, 0, Ct)).IsSuccess.ShouldBeTrue();

        Order(_work).ShouldBe(["c", "a", "b"]);
        _store.BoardChanges[^1].ShouldBe(
            [new ClipPlacement(c.Id, _work, 0), new ClipPlacement(a.Id, _work, 1), new ClipPlacement(b.Id, _work, 2)],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Moving_a_clip_to_the_place_it_has_stores_nothing()
    {
        var a = await AddAsync("a");
        await _library.SetBoardAsync(a.Id, _work, Ct);
        var stored = _store.BoardChanges.Count;

        (await _library.MoveInBoardAsync(a.Id, 0, Ct)).IsSuccess.ShouldBeTrue();

        _store.BoardChanges.Count.ShouldBe(stored);
    }

    [Fact]
    public async Task A_clip_that_cannot_be_moved_says_why()
    {
        var a = await AddAsync("a");

        (await _library.MoveInBoardAsync(a.Id, 0, Ct)).Error.Code.ShouldBe(ErrorCode.ClipNotOnBoard);
        (await _library.MoveInBoardAsync(ClipId.New(), 0, Ct)).Error.Code.ShouldBe(ErrorCode.ClipNotFound);
    }

    // --- releasing and reconciling ---

    [Fact]
    public async Task Releasing_a_board_changes_memory_only_because_the_board_store_does_the_rest()
    {
        var a = await AddAsync("a");
        await _library.SetBoardAsync(a.Id, _work, Ct);
        var stored = _store.BoardChanges.Count;
        var raised = 0;
        _library.Changed += (_, _) => raised++;

        _library.ReleaseBoard(_work);

        _library.Clips.Single().OnBoard.ShouldBeNull();
        _library.Clips.Single().IsPinned.ShouldBeTrue();
        _store.BoardChanges.Count.ShouldBe(stored);
        raised.ShouldBe(1);
    }

    [Fact]
    public void Releasing_a_board_with_no_clips_raises_nothing()
    {
        var raised = 0;
        _library.Changed += (_, _) => raised++;

        _library.ReleaseBoard(_work);

        raised.ShouldBe(0);
    }

    [Fact]
    public async Task Reconciling_stores_the_clips_it_took_off_boards_that_do_not_exist()
    {
        var a = await AddAsync("a");
        var x = await AddAsync("x");
        await _library.SetBoardAsync(a.Id, _work, Ct);
        await _library.SetBoardAsync(x.Id, _home, Ct);

        var result = await _library.ReconcileBoardsAsync(new HashSet<BoardId> { _home }, Ct);

        result.IsSuccess.ShouldBeTrue();
        _library.ClipsOnBoard(_work).ShouldBeEmpty();
        _library.ClipsOnBoard(_home).Length.ShouldBe(1);
        _store.BoardChanges[^1].ShouldBe([new ClipPlacement(a.Id, null, 0)]);
    }

    [Fact]
    public async Task Reconciling_when_all_is_in_order_stores_nothing_and_raises_nothing()
    {
        var a = await AddAsync("a");
        await _library.SetBoardAsync(a.Id, _work, Ct);
        var stored = _store.BoardChanges.Count;
        var raised = 0;
        _library.Changed += (_, _) => raised++;

        (await _library.ReconcileBoardsAsync(new HashSet<BoardId> { _work }, Ct)).IsSuccess.ShouldBeTrue();

        _store.BoardChanges.Count.ShouldBe(stored);
        raised.ShouldBe(0);
    }

    [Fact]
    public async Task Stored_clips_come_back_on_their_boards()
    {
        var a = TestClips.Text("a") with { IsPinned = true, OnBoard = _work, BoardPosition = 1 };
        var b = TestClips.Text("b") with { IsPinned = true, OnBoard = _work, BoardPosition = 0 };
        _store.Stored = [a, b];

        await _library.LoadAsync(Ct);

        Order(_work).ShouldBe(["b", "a"]);
    }

    [Fact]
    public void Missing_arguments_are_bugs()
    {
        Should.Throw<ArgumentNullException>(() => _library.ReconcileBoardsAsync(null!, Ct));
    }
}
