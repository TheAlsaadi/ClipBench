using System.Text;
using ClipBench.Core.Boards;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Storage.Clips;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Clips;

/// <summary>Which clip is on which board is kept with the clip, and the names of the boards are sealed.</summary>
public sealed class ClipRepositoryBoardTests : RepositoryTestBase
{
    private static readonly BoardId _work = BoardId.New();
    private static readonly BoardId _home = BoardId.New();

    private static Clip OnBoard(string text, BoardId board, int position) =>
        TestClips.Text(text, pinned: true) with { OnBoard = board, BoardPosition = position };

    private static async Task<Clip> LoadOneAsync(ClipRepository repository, Clip clip) =>
        (await repository.GetAsync(clip.Id, Ct)).Value;

    // --- the board a clip is on ---

    [Fact]
    public async Task A_clip_keeps_its_board_and_its_place_when_it_is_saved_and_read_back()
    {
        await using var repository = await OpenAsync();
        var clip = OnBoard("on a board", _work, 3);
        await SaveAll(repository, clip);

        var one = await LoadOneAsync(repository, clip);
        var all = (await repository.LoadAllAsync(Ct)).Value.Items;

        one.OnBoard.ShouldBe(_work);
        one.BoardPosition.ShouldBe(3);
        all.Single().OnBoard.ShouldBe(_work);
        all.Single().BoardPosition.ShouldBe(3);
    }

    [Fact]
    public async Task A_clip_on_no_board_comes_back_on_none()
    {
        await using var repository = await OpenAsync();
        var clip = TestClips.Text("loose");
        await SaveAll(repository, clip);

        var one = await LoadOneAsync(repository, clip);

        one.OnBoard.ShouldBeNull();
        one.BoardPosition.ShouldBe(0);
    }

    [Fact]
    public async Task Saving_a_clip_again_keeps_the_board_it_was_saved_with()
    {
        await using var repository = await OpenAsync();
        var clip = OnBoard("moved to the top", _work, 1);
        await SaveAll(repository, clip);

        await SaveAll(repository, clip with { CapturedAt = clip.CapturedAt.AddMinutes(5) });

        (await LoadOneAsync(repository, clip)).OnBoard.ShouldBe(_work);
    }

    [Fact]
    public async Task Clips_on_boards_come_back_after_the_database_is_reopened()
    {
        var clip = OnBoard("survives", _home, 2);
        await using (var first = await OpenAsync())
        {
            await SaveAll(first, clip);
        }

        await using var second = await OpenAsync();

        var loaded = (await second.LoadAllAsync(Ct)).Value.Items.Single();
        loaded.OnBoard.ShouldBe(_home);
        loaded.BoardPosition.ShouldBe(2);
    }

    // --- changing boards ---

    [Fact]
    public async Task Clips_are_put_on_boards_moved_and_taken_off_in_one_step()
    {
        await using var repository = await OpenAsync();
        var a = TestClips.Text("a", pinned: true);
        var b = TestClips.Text("b", pinned: true);
        var c = OnBoard("c", _home, 4);
        await SaveAll(repository, a, b, c);

        var result = await repository.SetBoardsAsync(
            [new ClipPlacement(a.Id, _work, 0), new ClipPlacement(b.Id, _work, 1), new ClipPlacement(c.Id, null, 0)],
            Ct);

        result.IsSuccess.ShouldBeTrue();
        (await LoadOneAsync(repository, a)).OnBoard.ShouldBe(_work);
        var movedB = await LoadOneAsync(repository, b);
        movedB.OnBoard.ShouldBe(_work);
        movedB.BoardPosition.ShouldBe(1);
        var freed = await LoadOneAsync(repository, c);
        freed.OnBoard.ShouldBeNull();
        freed.BoardPosition.ShouldBe(0);
        freed.IsPinned.ShouldBeTrue();
    }

    [Fact]
    public async Task A_clip_that_is_not_stored_is_ignored()
    {
        await using var repository = await OpenAsync();
        var a = TestClips.Text("a", pinned: true);
        await SaveAll(repository, a);

        var result = await repository.SetBoardsAsync([new ClipPlacement(ClipId.New(), _work, 0), new ClipPlacement(a.Id, _work, 0)], Ct);

        result.IsSuccess.ShouldBeTrue();
        (await LoadOneAsync(repository, a)).OnBoard.ShouldBe(_work);
    }

    [Fact]
    public async Task Nothing_to_change_is_fine()
    {
        await using var repository = await OpenAsync();

        (await repository.SetBoardsAsync([], Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Changing_boards_changes_no_sealed_value()
    {
        await using var repository = await OpenAsync();
        var a = TestClips.Text("a", pinned: true, source: "a.exe");
        await SaveAll(repository, a);
        var content = RawBlob("content", a.Id);
        var source = RawBlob("source_app", a.Id);

        await repository.SetBoardsAsync([new ClipPlacement(a.Id, _work, 0)], Ct);

        RawBlob("content", a.Id).ShouldBe(content);
        RawBlob("source_app", a.Id).ShouldBe(source);
    }

    // --- pinning ---

    [Fact]
    public async Task Unpinning_a_clip_takes_it_off_its_board_in_the_database()
    {
        await using var repository = await OpenAsync();
        var clip = OnBoard("on a board", _work, 2);
        await SaveAll(repository, clip);

        await repository.SetPinnedAsync(clip.Id, false, Ct);

        var loaded = await LoadOneAsync(repository, clip);
        loaded.IsPinned.ShouldBeFalse();
        loaded.OnBoard.ShouldBeNull();
        loaded.BoardPosition.ShouldBe(0);
    }

    [Fact]
    public async Task Pinning_a_clip_does_not_touch_its_board()
    {
        await using var repository = await OpenAsync();
        var clip = OnBoard("on a board", _work, 2);
        await SaveAll(repository, clip);

        await repository.SetPinnedAsync(clip.Id, true, Ct);

        (await LoadOneAsync(repository, clip)).OnBoard.ShouldBe(_work);
    }

    // --- the boards ---

    [Fact]
    public async Task Boards_are_saved_and_read_back_in_the_order_of_their_places_with_names_in_any_script()
    {
        await using var repository = await OpenAsync();
        var third = new Board(BoardId.New(), "\u0645\u0631\u062D\u0628\u0627", 9);
        var first = new Board(BoardId.New(), "Work", 1);
        var second = new Board(BoardId.New(), "Plans \U0001F680 caf\u00E9", 5);
        foreach (var board in new[] { third, first, second })
        {
            (await repository.SaveBoardAsync(board, Ct)).IsSuccess.ShouldBeTrue();
        }

        var loaded = (await repository.LoadBoardsAsync(Ct)).Value;

        loaded.UnreadableCount.ShouldBe(0);
        loaded.Items.ShouldBe([first, second, third]);
    }

    [Fact]
    public async Task Boards_come_back_after_the_database_is_reopened()
    {
        var work = new Board(BoardId.New(), "Work", 0);
        await using (var first = await OpenAsync())
        {
            await first.SaveBoardAsync(work, Ct);
        }

        await using var second = await OpenAsync();

        (await second.LoadBoardsAsync(Ct)).Value.Items.ShouldBe([work]);
    }

    [Fact]
    public async Task A_database_with_no_boards_loads_none()
    {
        await using var repository = await OpenAsync();

        var loaded = (await repository.LoadBoardsAsync(Ct)).Value;

        loaded.Items.ShouldBeEmpty();
        loaded.UnreadableCount.ShouldBe(0);
    }

    [Fact]
    public async Task Saving_a_board_again_replaces_it()
    {
        await using var repository = await OpenAsync();
        var work = new Board(BoardId.New(), "Work", 0);
        await repository.SaveBoardAsync(work, Ct);

        await repository.SaveBoardAsync(work with { Name = "Office", Position = 3 }, Ct);

        (await repository.LoadBoardsAsync(Ct)).Value.Items.ShouldBe([work with { Name = "Office", Position = 3 }]);
    }

    [Fact]
    public async Task Renaming_a_board_changes_only_its_name()
    {
        await using var repository = await OpenAsync();
        var work = new Board(BoardId.New(), "Work", 4);
        await repository.SaveBoardAsync(work, Ct);

        (await repository.RenameBoardAsync(work.Id, "Office", Ct)).IsSuccess.ShouldBeTrue();

        (await repository.LoadBoardsAsync(Ct)).Value.Items.ShouldBe([work with { Name = "Office" }]);
    }

    [Fact]
    public async Task Renaming_a_board_that_is_not_stored_says_so()
    {
        await using var repository = await OpenAsync();

        (await repository.RenameBoardAsync(BoardId.New(), "Office", Ct)).Error.Code.ShouldBe(ErrorCode.BoardNotFound);
    }

    [Fact]
    public async Task Deleting_a_board_takes_its_clips_off_it_in_the_same_step_and_leaves_them_and_other_boards_alone()
    {
        await using var repository = await OpenAsync();
        var work = new Board(BoardId.New(), "Work", 0);
        var home = new Board(BoardId.New(), "Home", 1);
        await repository.SaveBoardAsync(work, Ct);
        await repository.SaveBoardAsync(home, Ct);
        var onWork = OnBoard("on work", work.Id, 0);
        var onHome = OnBoard("on home", home.Id, 0);
        await SaveAll(repository, onWork, onHome);

        (await repository.DeleteBoardAsync(work.Id, Ct)).IsSuccess.ShouldBeTrue();

        (await repository.LoadBoardsAsync(Ct)).Value.Items.ShouldBe([home]);
        var freed = await LoadOneAsync(repository, onWork);
        freed.OnBoard.ShouldBeNull();
        freed.BoardPosition.ShouldBe(0);
        freed.IsPinned.ShouldBeTrue();
        (await LoadOneAsync(repository, onHome)).OnBoard.ShouldBe(home.Id);
    }

    [Fact]
    public async Task Deleting_a_board_that_is_not_stored_says_so_and_changes_nothing()
    {
        await using var repository = await OpenAsync();
        var onWork = OnBoard("on work", _work, 0);
        await SaveAll(repository, onWork);

        (await repository.DeleteBoardAsync(BoardId.New(), Ct)).Error.Code.ShouldBe(ErrorCode.BoardNotFound);

        (await LoadOneAsync(repository, onWork)).OnBoard.ShouldBe(_work);
    }

    // --- sealed ---

    [Fact]
    public async Task A_board_name_is_not_stored_as_text()
    {
        const string Name = "BOARDNAME-9f31c2";
        await using var repository = await OpenAsync();
        var board = new Board(BoardId.New(), Name, 0);
        await repository.SaveBoardAsync(board, Ct);

        using var connection = Raw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM boards;";
        var stored = (byte[])command.ExecuteScalar()!;

        stored.Length.ShouldBe(12 + Encoding.UTF8.GetByteCount(Name) + 16);
        stored.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Name)).ShouldBe(-1);
    }

    [Fact]
    public async Task Changing_a_byte_of_a_board_name_makes_that_board_unreadable_and_no_other()
    {
        await using var repository = await OpenAsync();
        var a = new Board(BoardId.New(), "A", 0);
        var b = new Board(BoardId.New(), "B", 1);
        var c = new Board(BoardId.New(), "C", 2);
        foreach (var board in new[] { a, b, c })
        {
            await repository.SaveBoardAsync(board, Ct);
        }

        var sealedName = RawBoardName(b.Id);
        sealedName[^1] ^= 0x01;
        RawSetBoardName(b.Id, sealedName);

        var loaded = (await repository.LoadBoardsAsync(Ct)).Value;

        loaded.UnreadableCount.ShouldBe(1);
        loaded.Items.ShouldBe([a, c]);
    }

    [Fact]
    public async Task A_name_copied_from_one_board_to_another_does_not_open()
    {
        await using var repository = await OpenAsync();
        var a = new Board(BoardId.New(), "A", 0);
        var b = new Board(BoardId.New(), "B", 1);
        await repository.SaveBoardAsync(a, Ct);
        await repository.SaveBoardAsync(b, Ct);

        RawSetBoardName(b.Id, RawBoardName(a.Id));

        var loaded = (await repository.LoadBoardsAsync(Ct)).Value;
        loaded.Items.ShouldBe([a]);
        loaded.UnreadableCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_board_name_is_not_a_value_that_a_clip_column_will_open_either()
    {
        await using var repository = await OpenAsync();
        var board = new Board(BoardId.New(), "Work", 0);
        await repository.SaveBoardAsync(board, Ct);
        var clip = TestClips.Text("victim", source: "x.exe");
        await SaveAll(repository, clip);

        RawSet("source_app", clip.Id, RawBoardName(board.Id));

        (await repository.GetAsync(clip.Id, Ct)).Error.Code.ShouldBe(ErrorCode.DecryptionFailed);
    }

    // --- wiping ---

    [Fact]
    public async Task Wiping_removes_the_boards_too()
    {
        await using var repository = await OpenAsync();
        await repository.SaveBoardAsync(new Board(BoardId.New(), "Work", 0), Ct);
        await SaveAll(repository, OnBoard("x", _work, 0));

        (await repository.WipeAsync(Ct)).IsSuccess.ShouldBeTrue();

        RawCount("SELECT COUNT(*) FROM boards").ShouldBe(0);
        RawCount("SELECT COUNT(*) FROM snippets").ShouldBe(0);
        (await repository.LoadBoardsAsync(Ct)).Value.Items.ShouldBeEmpty();
    }

    private byte[] RawBoardName(BoardId id)
    {
        using var connection = Raw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM boards WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.Value.ToByteArray(bigEndian: true));
        return (byte[])command.ExecuteScalar()!;
    }

    private void RawSetBoardName(BoardId id, byte[] value)
    {
        using var connection = Raw();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE boards SET name = $value WHERE id = $id;";
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$id", id.Value.ToByteArray(bigEndian: true));
        command.ExecuteNonQuery().ShouldBe(1);
    }
}
