using System.Collections.Immutable;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;

namespace ClipBench.Core.Boards;

/// <summary>
/// The boards as the app uses them: the list of boards, kept in step with the store, and the putting of clips on them.
/// A change is made in memory first and then stored, as in <see cref="ClipLibrary"/>. Changes are made one at a time.
/// </summary>
public sealed class BoardCatalog : IDisposable
{
    private readonly ClipLibrary _library;
    private readonly IBoardStore _store;
    private readonly SemaphoreSlim _changes = new(1, 1);
    private readonly Lock _gate = new();
    private ImmutableArray<Board> _boards = [];

    /// <summary>Creates an empty catalog.</summary>
    /// <param name="library">The clips, which carry which board they are on.</param>
    /// <param name="store">Where the boards are kept between runs.</param>
    public BoardCatalog(ClipLibrary library, IBoardStore store)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(store);
        _library = library;
        _store = store;
    }

    /// <summary>Raised after the list of boards changed. A subscriber that throws is skipped and the others are still told.</summary>
    public event EventHandler? Changed;

    /// <summary>The boards, in the order they are shown.</summary>
    public ImmutableArray<Board> Boards
    {
        get
        {
            lock (_gate)
            {
                return _boards;
            }
        }
    }

    /// <summary>Finds a board.</summary>
    /// <param name="id">The board id.</param>
    /// <returns>The board, or null.</returns>
    public Board? Find(BoardId id) => Boards.FirstOrDefault(board => board.Id == id);

    /// <summary>The clips on a board, in their order on it.</summary>
    /// <param name="id">The board id.</param>
    /// <returns>The clips.</returns>
    public ImmutableArray<Clip> ClipsOf(BoardId id) => _library.ClipsOnBoard(id);

    /// <summary>Loads the boards. Call it after the library is loaded: clips that sit on a board that no longer exists are taken off.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many boards there are, or why the store could not be read.</returns>
    public async Task<Result<int>> LoadAsync(CancellationToken cancellationToken)
    {
        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (stored.IsFailure)
            {
                return Result.Failure<int>(stored.Error);
            }

            var boards = stored.Value.OrderBy(board => board.Position).ToImmutableArray();
            SetBoards(boards);

            // If the clean-up cannot be stored the memory is right all the same, and the next load tries again.
            _ = await _library.ReconcileBoardsAsync(boards.Select(board => board.Id).ToHashSet(), cancellationToken).ConfigureAwait(false);

            RaiseChanged();
            return Result.Success(boards.Length);
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Makes a board at the end of the list.</summary>
    /// <param name="name">What to call it.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The board, or why it could not be made. A failure to store still leaves the board in memory.</returns>
    public async Task<Result<Board>> CreateAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var valid = BoardName.Validate(name);
            if (valid.IsFailure)
            {
                return Result.Failure<Board>(valid.Error);
            }

            var current = Boards;
            if (IsTaken(current, valid.Value, except: null))
            {
                return Result.Failure<Board>(NameTaken());
            }

            if (current.Length >= BoardName.MaxBoards)
            {
                return Result.Failure<Board>(new ResultError(ErrorCode.TooManyBoards, $"There can be at most {BoardName.MaxBoards} boards."));
            }

            var board = new Board(BoardId.New(), valid.Value, current.IsEmpty ? 0 : current.Max(existing => existing.Position) + 1);
            SetBoards(current.Add(board));

            var stored = await _store.AddAsync(board, cancellationToken).ConfigureAwait(false);
            RaiseChanged();
            return stored.IsFailure ? Result.Failure<Board>(stored.Error) : Result.Success(board);
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Renames a board.</summary>
    /// <param name="id">The board id.</param>
    /// <param name="name">The new name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the board could not be renamed.</returns>
    public async Task<Result> RenameAsync(BoardId id, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var valid = BoardName.Validate(name);
            if (valid.IsFailure)
            {
                return Result.Failure(valid.Error);
            }

            var current = Boards;
            var index = IndexOf(current, id);
            if (index < 0)
            {
                return NotFound();
            }

            if (IsTaken(current, valid.Value, except: id))
            {
                return Result.Failure(NameTaken());
            }

            SetBoards(current.SetItem(index, current[index] with { Name = valid.Value }));

            var stored = await _store.RenameAsync(id, valid.Value, cancellationToken).ConfigureAwait(false);
            RaiseChanged();
            return stored;
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Deletes a board. Its clips stay, still pinned, and are on no board.</summary>
    /// <param name="id">The board id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the board could not be deleted.</returns>
    public async Task<Result> DeleteAsync(BoardId id, CancellationToken cancellationToken)
    {
        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Boards;
            var index = IndexOf(current, id);
            if (index < 0)
            {
                return NotFound();
            }

            SetBoards(current.RemoveAt(index));
            _library.ReleaseBoard(id);

            // The store takes the clips off the board in the same step as it deletes the board.
            var stored = await _store.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            RaiseChanged();
            return stored;
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Puts a clip on a board, at the end. The clip is pinned. A clip on another board moves.</summary>
    /// <param name="board">The board id.</param>
    /// <param name="clip">The clip id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the clip could not be put on the board.</returns>
    public async Task<Result> AddClipAsync(BoardId board, ClipId clip, CancellationToken cancellationToken)
    {
        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Find(board) is null
                ? NotFound()
                : await _library.SetBoardAsync(clip, board, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Takes a clip off its board. It stays pinned.</summary>
    /// <param name="clip">The clip id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the clip could not be taken off.</returns>
    public Task<Result> RemoveClipAsync(ClipId clip, CancellationToken cancellationToken) =>
        _library.SetBoardAsync(clip, null, cancellationToken);

    /// <summary>Moves a clip to a place in its board.</summary>
    /// <param name="clip">The clip id.</param>
    /// <param name="newIndex">Where it should come, counting from 0.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the clip could not be moved.</returns>
    public Task<Result> MoveClipAsync(ClipId clip, int newIndex, CancellationToken cancellationToken) =>
        _library.MoveInBoardAsync(clip, newIndex, cancellationToken);

    /// <summary>Releases the lock that keeps changes in order. Call it once nothing changes the boards any more.</summary>
    public void Dispose() => _changes.Dispose();

    private static int IndexOf(ImmutableArray<Board> boards, BoardId id)
    {
        for (var i = 0; i < boards.Length; i++)
        {
            if (boards[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsTaken(ImmutableArray<Board> boards, string name, BoardId? except) =>
        boards.Any(board => board.Id != except && string.Equals(board.Name, name, StringComparison.OrdinalIgnoreCase));

    private static ResultError NameTaken() =>
        new(ErrorCode.BoardNameTaken, "Another board already has that name.");

    private static Result NotFound() =>
        Result.Failure(new ResultError(ErrorCode.BoardNotFound, "That board no longer exists."));

    private void SetBoards(ImmutableArray<Board> boards)
    {
        lock (_gate)
        {
            _boards = boards;
        }
    }

    private void RaiseChanged() => Changed.RaiseEach(this);
}
