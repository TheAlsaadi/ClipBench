using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;

namespace ClipBench.Storage.Clips;

/// <summary>Lets the app's clip library keep its clips in the encrypted database.</summary>
public sealed class ClipStore : IClipStore
{
    private readonly ClipRepository _repository;

    /// <summary>Creates a store over a repository. The caller keeps ownership of the repository.</summary>
    /// <param name="repository">The open repository.</param>
    public ClipStore(ClipRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <inheritdoc/>
    public async Task<Result<StoredClips>> LoadAsync(CancellationToken cancellationToken)
    {
        var loaded = await _repository.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return loaded.IsFailure
            ? Result.Failure<StoredClips>(loaded.Error)
            : Result.Success(new StoredClips(loaded.Value.Items, loaded.Value.UnreadableCount));
    }

    /// <inheritdoc/>
    public Task<Result> SaveAsync(Clip clip, CancellationToken cancellationToken) => _repository.SaveAsync(clip, cancellationToken);

    /// <inheritdoc/>
    public Task<Result> SetPinnedAsync(ClipId id, bool pinned, CancellationToken cancellationToken) =>
        _repository.SetPinnedAsync(id, pinned, cancellationToken);

    /// <inheritdoc/>
    public Task<Result> SetBoardsAsync(IReadOnlyList<ClipPlacement> placements, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    /// <inheritdoc/>
    public async Task<Result> DeleteAsync(IReadOnlyCollection<ClipId> ids, CancellationToken cancellationToken)
    {
        var deleted = await _repository.DeleteAsync(ids, cancellationToken).ConfigureAwait(false);
        return deleted.IsFailure ? Result.Failure(deleted.Error) : Result.Success();
    }
}
