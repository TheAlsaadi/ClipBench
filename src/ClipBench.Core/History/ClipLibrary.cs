using System.Collections.Concurrent;
using System.Collections.Immutable;
using ClipBench.Core.Clips;
using ClipBench.Core.Privacy;
using ClipBench.Core.Results;
using ClipBench.Core.Search;

namespace ClipBench.Core.History;

/// <summary>What <see cref="ClipLibrary.LoadAsync"/> found.</summary>
/// <param name="Loaded">How many clips are now in the library.</param>
/// <param name="Unreadable">How many stored records were skipped because they could not be read.</param>
public readonly record struct LibraryLoad(int Loaded, int Unreadable);

/// <summary>
/// The history as the app uses it: clips in memory for searching, kept in step with the store. A change is made in
/// memory first and then stored, so a failing store never hides a clip the user just copied; the failure is returned
/// and the clip is lost only if the app closes before a later change stores it. Changes are stored one at a time, in
/// the order they were made.
/// </summary>
public sealed class ClipLibrary : IDisposable
{
    private readonly ClipHistory _history;
    private readonly IClipStore _store;
    private readonly ClipSearch _search = new();
    private readonly SemaphoreSlim _changes = new(1, 1);
    private readonly ConcurrentDictionary<ClipId, SensitiveKind> _sensitivity = new();

    /// <summary>Creates an empty library.</summary>
    /// <param name="history">The in-memory history, with its limits.</param>
    /// <param name="store">Where the clips are kept between runs.</param>
    public ClipLibrary(ClipHistory history, IClipStore store)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(store);
        _history = history;
        _store = store;
    }

    /// <summary>
    /// Raised after the clips changed, on the thread that made the change. A subscriber that throws is skipped and
    /// the others are still told.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>A snapshot of the clips, newest first.</summary>
    public ImmutableArray<Clip> Clips => _history.Clips;

    /// <summary>
    /// Loads the stored clips. Stored clips that the history limits push out are deleted from the store; if that
    /// cleanup fails the load still succeeds and the next load tries again.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What was loaded, or why the store could not be read.</returns>
    public async Task<Result<LibraryLoad>> LoadAsync(CancellationToken cancellationToken)
    {
        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (stored.IsFailure)
            {
                return Result.Failure<LibraryLoad>(stored.Error);
            }

            var evicted = new List<Clip>();

            // The store lists newest first and the history puts each added clip on top, so add the oldest first.
            for (var i = stored.Value.Clips.Length - 1; i >= 0; i--)
            {
                evicted.AddRange(_history.Add(stored.Value.Clips[i]).Evicted);
            }

            if (evicted.Count > 0)
            {
                ForgetSensitivity(evicted);
                _ = await _store.DeleteAsync([.. evicted.Select(clip => clip.Id)], cancellationToken).ConfigureAwait(false);
            }

            RaiseChanged();
            return Result.Success(new LibraryLoad(_history.Count, stored.Value.UnreadableCount));
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Adds a clip at the top and stores it. Copying content that is already there moves that clip to the top.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What was stored, or why storing failed. A failure still leaves the clip in the library.</returns>
    public async Task<Result<AddResult>> AddAsync(Clip clip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clip);

        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var added = _history.Add(clip);
            ForgetSensitivity(added.Evicted);

            var saved = await _store.SaveAsync(added.Clip, cancellationToken).ConfigureAwait(false);
            var deleted = added.Evicted.IsEmpty
                ? Result.Success()
                : await _store.DeleteAsync([.. added.Evicted.Select(evicted => evicted.Id)], cancellationToken).ConfigureAwait(false);

            RaiseChanged();

            if (saved.IsFailure)
            {
                return Result.Failure<AddResult>(saved.Error);
            }

            return deleted.IsFailure ? Result.Failure<AddResult>(deleted.Error) : Result.Success(added);
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Pins or unpins a clip.</summary>
    /// <param name="id">The clip id.</param>
    /// <param name="pinned">True to pin.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see cref="ErrorCode.ClipNotFound"/> when the clip is not in the library, or why the change was not stored.</returns>
    public async Task<Result> SetPinnedAsync(ClipId id, bool pinned, CancellationToken cancellationToken)
    {
        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var changed = pinned ? _history.Pin(id) : _history.Unpin(id);
            if (changed.IsFailure)
            {
                return Result.Failure(changed.Error);
            }

            var stored = await _store.SetPinnedAsync(id, pinned, cancellationToken).ConfigureAwait(false);
            RaiseChanged();
            return stored;
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Removes a clip, pinned or not.</summary>
    /// <param name="id">The clip id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see cref="ErrorCode.ClipNotFound"/> when the clip is not in the library, or why the removal was not stored.</returns>
    public async Task<Result> RemoveAsync(ClipId id, CancellationToken cancellationToken)
    {
        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var removed = _history.Remove(id);
            if (removed is null)
            {
                return Result.Failure(new ResultError(ErrorCode.ClipNotFound, "That clip is no longer in the history."));
            }

            _ = _sensitivity.TryRemove(id, out _);
            var stored = await _store.DeleteAsync([id], cancellationToken).ConfigureAwait(false);
            RaiseChanged();
            return stored;
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Searches the clips.</summary>
    /// <param name="query">What to look for. Empty returns the most recent clips.</param>
    /// <param name="limit">The most results.</param>
    /// <returns>The matches, best first.</returns>
    public ImmutableArray<SearchResult> Search(string query, int limit = ClipSearch.DefaultLimit) =>
        _search.Search(_history.Clips, query, limit);

    /// <summary>Tells whether a clip looks like a secret. The scan runs once per clip, the first time it is asked.</summary>
    /// <param name="clip">The clip.</param>
    /// <returns>The kind of secret, or none. Images are never reported.</returns>
    public SensitiveKind GetSensitivity(Clip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return _sensitivity.GetOrAdd(
            clip.Id,
            static (_, content) => content.PlainText is { } text ? SensitiveDetector.Detect(text) : SensitiveKind.None,
            clip.Content);
    }

    /// <summary>Releases the lock that keeps changes in order. Call it once nothing changes the library any more.</summary>
    public void Dispose() => _changes.Dispose();

    private void ForgetSensitivity(IEnumerable<Clip> clips)
    {
        foreach (var clip in clips)
        {
            _ = _sensitivity.TryRemove(clip.Id, out _);
        }
    }

    private void RaiseChanged()
    {
        if (Changed is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A subscriber's bug must not stop the others, or the change that was already made.
            }
        }
    }
}
