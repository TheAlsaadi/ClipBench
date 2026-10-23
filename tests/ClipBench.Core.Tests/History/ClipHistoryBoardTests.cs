using ClipBench.Core.Boards;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.History;

/// <summary>How boards and pins fit together inside the history: a board holds pinned clips only, in an order.</summary>
public class ClipHistoryBoardTests
{
    private static readonly BoardId _work = BoardId.New();
    private static readonly BoardId _home = BoardId.New();

    private readonly FakeTimeProvider _clock = new();

    private ClipHistory Create(int maxCount = 1000) =>
        new(new HistoryOptions { MaxCount = maxCount }, _clock);

    private static Clip Add(ClipHistory history, string text)
    {
        var clip = TestClips.Text(text);
        _ = history.Add(clip);
        return clip;
    }

    private static string[] Order(ClipHistory history, BoardId board) =>
        [.. history.ClipsOnBoard(board).Select(clip => ((TextContent)clip.Content).Text)];

    private static int[] Positions(ClipHistory history, BoardId board) =>
        [.. history.ClipsOnBoard(board).Select(clip => clip.BoardPosition)];

    // --- the clip itself ---

    [Fact]
    public void A_new_clip_is_on_no_board()
    {
        var clip = TestClips.Text("x");

        clip.OnBoard.ShouldBeNull();
        clip.BoardPosition.ShouldBe(0);
    }

    // --- putting clips on a board ---

    [Fact]
    public void Putting_clips_on_a_board_pins_them_and_places_each_at_the_end()
    {
        var history = Create();
        var a = Add(history, "a");
        var b = Add(history, "b");
        var c = Add(history, "c");

        history.SetBoard(b.Id, _work).Value.IsPinned.ShouldBeTrue();
        history.SetBoard(a.Id, _work);
        history.SetBoard(c.Id, _work);

        Order(history, _work).ShouldBe(["b", "a", "c"]);
        Positions(history, _work).ShouldBe([0, 1, 2]);
        history.Clips.ShouldAllBe(clip => clip.IsPinned && clip.OnBoard == _work);
    }

    [Fact]
    public void Putting_a_clip_on_the_board_it_is_already_on_changes_nothing()
    {
        var history = Create();
        var a = Add(history, "a");
        var b = Add(history, "b");
        history.SetBoard(a.Id, _work);
        history.SetBoard(b.Id, _work);

        var again = history.SetBoard(a.Id, _work);

        again.Value.BoardPosition.ShouldBe(0);
        Order(history, _work).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void Putting_a_clip_on_another_board_takes_it_off_the_first_and_places_it_at_the_end_of_the_second()
    {
        var history = Create();
        var a = Add(history, "a");
        var b = Add(history, "b");
        var c = Add(history, "c");
        history.SetBoard(a.Id, _work);
        history.SetBoard(b.Id, _home);
        history.SetBoard(c.Id, _home);

        history.SetBoard(a.Id, _home);

        Order(history, _work).ShouldBeEmpty();
        Order(history, _home).ShouldBe(["b", "c", "a"]);
        history.Get(a.Id)!.BoardPosition.ShouldBe(2);
    }

    [Fact]
    public void Taking_a_clip_off_its_board_leaves_it_pinned()
    {
        var history = Create();
        var a = Add(history, "a");
        history.SetBoard(a.Id, _work);

        var taken = history.SetBoard(a.Id, null).Value;

        taken.OnBoard.ShouldBeNull();
        taken.BoardPosition.ShouldBe(0);
        taken.IsPinned.ShouldBeTrue();
        Order(history, _work).ShouldBeEmpty();
    }

    [Fact]
    public void Putting_an_unknown_clip_on_a_board_fails()
    {
        Create().SetBoard(ClipId.New(), _work).Error.Code.ShouldBe(ErrorCode.ClipNotFound);
    }

    // --- a board holds pinned clips only ---

    [Fact]
    public void Unpinning_a_clip_takes_it_off_its_board()
    {
        var history = Create();
        var a = Add(history, "a");
        history.SetBoard(a.Id, _work);

        var unpinned = history.Unpin(a.Id).Value;

        unpinned.IsPinned.ShouldBeFalse();
        unpinned.OnBoard.ShouldBeNull();
        unpinned.BoardPosition.ShouldBe(0);
        Order(history, _work).ShouldBeEmpty();
    }

    [Fact]
    public void Pinning_does_not_put_a_clip_on_a_board()
    {
        var history = Create();
        var a = Add(history, "a");

        history.Pin(a.Id).Value.OnBoard.ShouldBeNull();
    }

    [Fact]
    public void A_clip_on_a_board_is_not_removed_by_the_history_limits()
    {
        var history = Create(maxCount: 1);
        var kept = Add(history, "on the board");
        history.SetBoard(kept.Id, _work);

        Add(history, "newer");
        Add(history, "newest");

        history.Get(kept.Id).ShouldNotBeNull();
        Order(history, _work).ShouldBe(["on the board"]);
    }

    [Fact]
    public void Removing_a_clip_takes_it_off_its_board()
    {
        var history = Create();
        var a = Add(history, "a");
        var b = Add(history, "b");
        history.SetBoard(a.Id, _work);
        history.SetBoard(b.Id, _work);

        history.Remove(a.Id);

        Order(history, _work).ShouldBe(["b"]);
    }

    [Fact]
    public void Clearing_the_unpinned_clips_keeps_the_boards_clips_and_clearing_everything_removes_them()
    {
        var history = Create();
        var a = Add(history, "a");
        Add(history, "loose");
        history.SetBoard(a.Id, _work);

        _ = history.Clear();
        Order(history, _work).ShouldBe(["a"]);

        _ = history.Clear(includePinned: true);
        Order(history, _work).ShouldBeEmpty();
    }

    [Fact]
    public void Copying_the_same_content_again_keeps_the_clip_on_its_board_in_its_place()
    {
        var history = Create();
        var a = Add(history, "a");
        var b = Add(history, "b");
        history.SetBoard(a.Id, _work);
        history.SetBoard(b.Id, _work);

        var added = history.Add(TestClips.Text("a"));

        added.WasDuplicate.ShouldBeTrue();
        added.Clip.OnBoard.ShouldBe(_work);
        added.Clip.BoardPosition.ShouldBe(0);
        Order(history, _work).ShouldBe(["a", "b"]);
    }

    // --- order ---

    [Fact]
    public void A_board_lists_its_clips_by_place_and_not_by_when_they_were_copied()
    {
        var history = Create();
        var first = Add(history, "first copied");
        var second = Add(history, "second copied");
        history.SetBoard(second.Id, _work);
        history.SetBoard(first.Id, _work);

        Order(history, _work).ShouldBe(["second copied", "first copied"]);
    }

    [Fact]
    public void A_board_with_no_clips_lists_nothing()
    {
        Create().ClipsOnBoard(_work).ShouldBeEmpty();
    }

    [Fact]
    public void Moving_a_clip_to_a_new_place_numbers_the_board_again_from_zero()
    {
        var history = Create();
        var a = Add(history, "a");
        var b = Add(history, "b");
        var c = Add(history, "c");
        history.SetBoard(a.Id, _work);
        history.SetBoard(b.Id, _work);
        history.SetBoard(c.Id, _work);

        var changed = history.MoveInBoard(c.Id, 0).Value;

        Order(history, _work).ShouldBe(["c", "a", "b"]);
        Positions(history, _work).ShouldBe([0, 1, 2]);
        changed.Select(clip => ((TextContent)clip.Content).Text).ShouldBe(["c", "a", "b"], ignoreOrder: true);
    }

    [Fact]
    public void Moving_a_clip_down_and_to_the_middle_works()
    {
        var history = Create();
        var a = Add(history, "a");
        var b = Add(history, "b");
        var c = Add(history, "c");
        var d = Add(history, "d");
        foreach (var clip in new[] { a, b, c, d })
        {
            history.SetBoard(clip.Id, _work);
        }

        history.MoveInBoard(a.Id, 2);
        Order(history, _work).ShouldBe(["b", "c", "a", "d"]);

        history.MoveInBoard(d.Id, 1);
        Order(history, _work).ShouldBe(["b", "d", "c", "a"]);
    }

    [Fact]
    public void Moving_past_the_end_means_the_end()
    {
        var history = Create();
        var a = Add(history, "a");
        var b = Add(history, "b");
        history.SetBoard(a.Id, _work);
        history.SetBoard(b.Id, _work);

        history.MoveInBoard(a.Id, 99);

        Order(history, _work).ShouldBe(["b", "a"]);
    }

    [Fact]
    public void Moving_a_clip_to_the_place_it_has_changes_nothing()
    {
        var history = Create();
        var a = Add(history, "a");
        var b = Add(history, "b");
        history.SetBoard(a.Id, _work);
        history.SetBoard(b.Id, _work);

        history.MoveInBoard(a.Id, 0).Value.ShouldBeEmpty();
    }

    [Fact]
    public void Moving_a_clip_closes_up_a_gap_left_by_a_clip_that_was_taken_off()
    {
        var history = Create();
        var a = Add(history, "a");
        var b = Add(history, "b");
        var c = Add(history, "c");
        history.SetBoard(a.Id, _work);
        history.SetBoard(b.Id, _work);
        history.SetBoard(c.Id, _work);
        history.SetBoard(b.Id, null);
        Positions(history, _work).ShouldBe([0, 2]);

        history.MoveInBoard(c.Id, 0);

        Order(history, _work).ShouldBe(["c", "a"]);
        Positions(history, _work).ShouldBe([0, 1]);
    }

    [Fact]
    public void Moving_does_not_touch_other_boards()
    {
        var history = Create();
        var a = Add(history, "a");
        var b = Add(history, "b");
        var x = Add(history, "x");
        history.SetBoard(a.Id, _work);
        history.SetBoard(b.Id, _work);
        history.SetBoard(x.Id, _home);

        var changed = history.MoveInBoard(b.Id, 0).Value;

        changed.ShouldNotContain(clip => clip.Id == x.Id);
        Order(history, _home).ShouldBe(["x"]);
    }

    [Fact]
    public void A_clip_on_no_board_cannot_be_moved()
    {
        var history = Create();
        var a = Add(history, "a");

        history.MoveInBoard(a.Id, 0).Error.Code.ShouldBe(ErrorCode.ClipNotOnBoard);
    }

    [Fact]
    public void An_unknown_clip_cannot_be_moved()
    {
        Create().MoveInBoard(ClipId.New(), 0).Error.Code.ShouldBe(ErrorCode.ClipNotFound);
    }

    [Fact]
    public void A_negative_place_is_a_bug()
    {
        var history = Create();
        var a = Add(history, "a");
        history.SetBoard(a.Id, _work);

        Should.Throw<ArgumentOutOfRangeException>(() => history.MoveInBoard(a.Id, -1));
    }

    // --- releasing and reconciling ---

    [Fact]
    public void Releasing_a_board_takes_its_clips_off_it_and_leaves_them_pinned()
    {
        var history = Create();
        var a = Add(history, "a");
        var b = Add(history, "b");
        var x = Add(history, "x");
        history.SetBoard(a.Id, _work);
        history.SetBoard(b.Id, _work);
        history.SetBoard(x.Id, _home);

        var released = history.ReleaseBoard(_work);

        released.Select(clip => clip.Id).ShouldBe([a.Id, b.Id], ignoreOrder: true);
        history.Get(a.Id)!.OnBoard.ShouldBeNull();
        history.Get(a.Id)!.IsPinned.ShouldBeTrue();
        Order(history, _home).ShouldBe(["x"]);
    }

    [Fact]
    public void Releasing_a_board_with_no_clips_returns_nothing()
    {
        Create().ReleaseBoard(_work).ShouldBeEmpty();
    }

    [Fact]
    public void Reconciling_takes_clips_off_boards_that_do_not_exist()
    {
        var history = Create();
        var a = Add(history, "a");
        var x = Add(history, "x");
        history.SetBoard(a.Id, _work);
        history.SetBoard(x.Id, _home);

        var changed = history.ReconcileBoards(new HashSet<BoardId> { _home });

        changed.Select(clip => clip.Id).ShouldBe([a.Id]);
        history.Get(a.Id)!.OnBoard.ShouldBeNull();
        history.Get(a.Id)!.IsPinned.ShouldBeTrue();
        history.Get(x.Id)!.OnBoard.ShouldBe(_home);
    }

    [Fact]
    public void Reconciling_takes_a_clip_off_its_board_when_it_is_not_pinned()
    {
        var history = Create();
        // Only a damaged database can hold such a clip.
        var damaged = TestClips.Text("damaged") with { OnBoard = _work, BoardPosition = 3, IsPinned = false };
        _ = history.Add(damaged);

        var changed = history.ReconcileBoards(new HashSet<BoardId> { _work });

        changed.Select(clip => clip.Id).ShouldBe([damaged.Id]);
        history.Get(damaged.Id)!.OnBoard.ShouldBeNull();
        history.Get(damaged.Id)!.IsPinned.ShouldBeFalse();
    }

    [Fact]
    public void Reconciling_changes_nothing_when_all_is_in_order()
    {
        var history = Create();
        var a = Add(history, "a");
        history.SetBoard(a.Id, _work);

        history.ReconcileBoards(new HashSet<BoardId> { _work }).ShouldBeEmpty();
    }

    [Fact]
    public void Missing_arguments_are_bugs()
    {
        Should.Throw<ArgumentNullException>(() => Create().ReconcileBoards(null!));
    }
}
