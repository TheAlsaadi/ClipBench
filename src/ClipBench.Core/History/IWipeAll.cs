using ClipBench.Core.Results;

namespace ClipBench.Core.History;

/// <summary>Deletes everything ClipBench keeps. The Settings window asks for it after the user confirmed.</summary>
public interface IWipeAll
{
    /// <summary>Deletes every clip, board and snippet, on disk and in memory.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>Success, or why the store could not be wiped.</returns>
    Task<Result> WipeAllAsync(CancellationToken cancellationToken);
}
