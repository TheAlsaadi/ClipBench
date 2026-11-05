using ClipBench.Core.Boards;
using ClipBench.Core.Capture;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Snippets;

namespace ClipBench.Core.History;

/// <summary>
/// Deletes everything ClipBench keeps: the clips, the boards and the snippets, on disk and in memory. A collect session and
/// a paste queue hold copies of clips in memory, so they are ended first. Nothing is left visible that is still on disk:
/// when the store cannot be wiped, memory keeps what it had and the reason is returned.
/// </summary>
public sealed class WipeService : IWipeAll
{
    private readonly IStoreWipe _store;
    private readonly ClipLibrary _library;
    private readonly BoardCatalog _boards;
    private readonly SnippetCatalog _snippets;
    private readonly ICollectControl _collect;
    private readonly IQueueControl _queue;
    private readonly IActivityLog _log;

    /// <summary>Creates the service.</summary>
    /// <param name="store">Deletes the files' content.</param>
    /// <param name="library">The clips in memory.</param>
    /// <param name="boards">The boards in memory.</param>
    /// <param name="snippets">The snippets in memory.</param>
    /// <param name="collect">A collect session, which is thrown away.</param>
    /// <param name="queue">A paste queue, which is stopped.</param>
    /// <param name="log">Where the wipe is recorded. It never receives clip content.</param>
    public WipeService(
        IStoreWipe store,
        ClipLibrary library,
        BoardCatalog boards,
        SnippetCatalog snippets,
        ICollectControl collect,
        IQueueControl queue,
        IActivityLog log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(boards);
        ArgumentNullException.ThrowIfNull(snippets);
        ArgumentNullException.ThrowIfNull(collect);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(log);
        _store = store;
        _library = library;
        _boards = boards;
        _snippets = snippets;
        _collect = collect;
        _queue = queue;
        _log = log;
    }

    /// <inheritdoc/>
    public async Task<Result> WipeAllAsync(CancellationToken cancellationToken)
    {
        // These hold copies of clips whatever happens next, so they go even when the disk cannot be wiped.
        _collect.Cancel();
        await _queue.StopAsync(cancellationToken).ConfigureAwait(false);

        var wiped = await _library.WipeAsync(_store, cancellationToken).ConfigureAwait(false);
        if (wiped.IsFailure)
        {
            _log.Write(ActivityLevel.Warning, ActivityEvent.StoreFailed, new ActivityDetails { Error = wiped.Error.Code });
            return wiped;
        }

        _boards.Forget();
        _snippets.Forget();
        _log.Write(ActivityLevel.Information, ActivityEvent.HistoryWiped);
        return wiped;
    }
}
