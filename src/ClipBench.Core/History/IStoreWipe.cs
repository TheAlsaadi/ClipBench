using ClipBench.Core.Results;

namespace ClipBench.Core.History;

/// <summary>Deletes everything the app keeps on disk: clips, boards and snippets, leaving the store ready to be used again.</summary>
public interface IStoreWipe
{
    /// <summary>
    /// Deletes every clip, board and snippet, pinned or not, and scrubs the files so that none of it stays behind.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or why the store could not be wiped. After a failure some of it may still be on disk.</returns>
    Task<Result> WipeAsync(CancellationToken cancellationToken);
}
