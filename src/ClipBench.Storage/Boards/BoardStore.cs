using System.Collections.Immutable;
using ClipBench.Core.Boards;
using ClipBench.Core.Results;
using ClipBench.Storage.Clips;

namespace ClipBench.Storage.Boards;

/// <summary>Lets the app's board catalog keep its boards in the encrypted database.</summary>
public sealed class BoardStore : IBoardStore
{
    private readonly ClipRepository _repository;

    /// <summary>Creates a store over a repository. The caller keeps ownership of the repository.</summary>
    /// <param name="repository">The open repository.</param>
    public BoardStore(ClipRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <inheritdoc/>
    public async Task<Result<ImmutableArray<Board>>> LoadAsync(CancellationToken cancellationToken)
    {
        var loaded = await _repository.LoadBoardsAsync(cancellationToken).ConfigureAwait(false);
        return loaded.IsFailure ? Result.Failure<ImmutableArray<Board>>(loaded.Error) : Result.Success(loaded.Value.Items);
    }

    /// <inheritdoc/>
    public Task<Result> AddAsync(Board board, CancellationToken cancellationToken) => _repository.SaveBoardAsync(board, cancellationToken);

    /// <inheritdoc/>
    public Task<Result> RenameAsync(BoardId id, string name, CancellationToken cancellationToken) =>
        _repository.RenameBoardAsync(id, name, cancellationToken);

    /// <inheritdoc/>
    public Task<Result> DeleteAsync(BoardId id, CancellationToken cancellationToken) => _repository.DeleteBoardAsync(id, cancellationToken);
}
