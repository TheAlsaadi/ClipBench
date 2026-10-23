using System.Collections.Immutable;
using ClipBench.Core.Results;

namespace ClipBench.Core.Boards;

/// <summary>Where the boards are kept between runs. The storage library implements it over the encrypted database.</summary>
public interface IBoardStore
{
    /// <summary>Reads every board, in the order they are shown.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The boards, or why they could not be read.</returns>
    Task<Result<ImmutableArray<Board>>> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Saves a new board.</summary>
    /// <param name="board">The board.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the board was not saved.</returns>
    Task<Result> AddAsync(Board board, CancellationToken cancellationToken);

    /// <summary>Changes the name of a stored board.</summary>
    /// <param name="id">The board id.</param>
    /// <param name="name">The new name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the name was not stored.</returns>
    Task<Result> RenameAsync(BoardId id, string name, CancellationToken cancellationToken);

    /// <summary>Deletes a board and takes every clip off it, in one step. The clips themselves stay.</summary>
    /// <param name="id">The board id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the board was not deleted.</returns>
    Task<Result> DeleteAsync(BoardId id, CancellationToken cancellationToken);
}
