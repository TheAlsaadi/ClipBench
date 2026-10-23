using ClipBench.Core.Boards;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Storage.Boards;
using ClipBench.Storage.Clips;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Boards;

/// <summary>The adapters the app uses: the same database seen through the Core interfaces.</summary>
public sealed class BoardStoreTests : RepositoryTestBase
{
    [Fact]
    public async Task A_board_goes_in_comes_out_is_renamed_and_is_deleted_through_the_store()
    {
        var repository = await OpenAsync();
        var store = new BoardStore(repository);
        var work = new Board(BoardId.New(), "Work", 0);

        (await store.AddAsync(work, Ct)).IsSuccess.ShouldBeTrue();
        (await store.LoadAsync(Ct)).Value.ShouldBe([work]);

        (await store.RenameAsync(work.Id, "Office", Ct)).IsSuccess.ShouldBeTrue();
        (await store.LoadAsync(Ct)).Value.ShouldBe([work with { Name = "Office" }]);

        (await store.DeleteAsync(work.Id, Ct)).IsSuccess.ShouldBeTrue();
        (await store.LoadAsync(Ct)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Boards_that_cannot_be_read_are_left_out_of_the_list()
    {
        var repository = await OpenAsync();
        var store = new BoardStore(repository);
        var a = new Board(BoardId.New(), "A", 0);
        var b = new Board(BoardId.New(), "B", 1);
        await store.AddAsync(a, Ct);
        await store.AddAsync(b, Ct);
        using (var connection = Raw())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE boards SET name = x'00' WHERE position = 1;";
            command.ExecuteNonQuery();
        }

        (await store.LoadAsync(Ct)).Value.ShouldBe([a]);
    }

    [Fact]
    public async Task A_closed_repository_is_a_bug_in_the_caller_and_not_a_result()
    {
        var repository = await OpenAsync();
        var store = new BoardStore(repository);
        await repository.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() => store.LoadAsync(Ct));
    }

    [Fact]
    public async Task The_clip_store_puts_clips_on_boards()
    {
        var repository = await OpenAsync();
        var clips = new ClipStore(repository);
        var clip = TestClips.Text("a", pinned: true);
        await clips.SaveAsync(clip, Ct);
        var board = BoardId.New();

        (await clips.SetBoardsAsync([new ClipPlacement(clip.Id, board, 2)], Ct)).IsSuccess.ShouldBeTrue();

        var loaded = (await clips.LoadAsync(Ct)).Value.Clips.Single();
        loaded.OnBoard.ShouldBe(board);
        loaded.BoardPosition.ShouldBe(2);
    }

    [Fact]
    public void A_missing_repository_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new BoardStore(null!));
    }
}
