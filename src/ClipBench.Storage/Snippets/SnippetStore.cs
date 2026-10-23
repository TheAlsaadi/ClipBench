using System.Collections.Immutable;
using ClipBench.Core.Results;
using ClipBench.Core.Snippets;
using ClipBench.Storage.Clips;

namespace ClipBench.Storage.Snippets;

/// <summary>Lets the app's snippet catalog keep its snippets in the encrypted database.</summary>
public sealed class SnippetStore : ISnippetStore
{
    private readonly ClipRepository _repository;

    /// <summary>Creates a store over a repository. The caller keeps ownership of the repository.</summary>
    /// <param name="repository">The open repository.</param>
    public SnippetStore(ClipRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <inheritdoc/>
    public async Task<Result<ImmutableArray<Snippet>>> LoadAsync(CancellationToken cancellationToken)
    {
        var loaded = await _repository.LoadSnippetsAsync(cancellationToken).ConfigureAwait(false);
        return loaded.IsFailure ? Result.Failure<ImmutableArray<Snippet>>(loaded.Error) : Result.Success(loaded.Value.Items);
    }

    /// <inheritdoc/>
    public Task<Result> SaveAsync(Snippet snippet, CancellationToken cancellationToken) => _repository.SaveSnippetAsync(snippet, cancellationToken);

    /// <inheritdoc/>
    public Task<Result> DeleteAsync(SnippetId id, CancellationToken cancellationToken) => _repository.DeleteSnippetAsync(id, cancellationToken);
}
