using System.Collections.Immutable;
using ClipBench.Core.Results;

namespace ClipBench.Core.Snippets;

/// <summary>Where the snippets are kept between runs. The storage library implements it over the encrypted database.</summary>
public interface ISnippetStore
{
    /// <summary>Reads every snippet, in the order they are shown.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The snippets, or why they could not be read.</returns>
    Task<Result<ImmutableArray<Snippet>>> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Saves a snippet, replacing the stored snippet with the same id.</summary>
    /// <param name="snippet">The snippet.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the snippet was not saved.</returns>
    Task<Result> SaveAsync(Snippet snippet, CancellationToken cancellationToken);

    /// <summary>Deletes a snippet for good.</summary>
    /// <param name="id">The snippet id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the snippet was not deleted.</returns>
    Task<Result> DeleteAsync(SnippetId id, CancellationToken cancellationToken);
}
