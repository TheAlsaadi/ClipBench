using System.Collections.Immutable;
using ClipBench.Core.Results;

namespace ClipBench.Core.Snippets;

/// <summary>
/// The snippets as the app uses them: the list, kept in step with the store. A change is made in memory first and then
/// stored, as in <see cref="History.ClipLibrary"/>. Changes are made one at a time.
/// </summary>
public sealed class SnippetCatalog : IDisposable
{
    private readonly ISnippetStore _store;
    private readonly SemaphoreSlim _changes = new(1, 1);
    private readonly Lock _gate = new();
    private ImmutableArray<Snippet> _snippets = [];

    /// <summary>Creates an empty catalog.</summary>
    /// <param name="store">Where the snippets are kept between runs.</param>
    public SnippetCatalog(ISnippetStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>Raised after the list changed. A subscriber that throws is skipped and the others are still told.</summary>
    public event EventHandler? Changed;

    /// <summary>The snippets, in the order they were made.</summary>
    public ImmutableArray<Snippet> Snippets
    {
        get
        {
            lock (_gate)
            {
                return _snippets;
            }
        }
    }

    /// <summary>Finds a snippet.</summary>
    /// <param name="id">The snippet id.</param>
    /// <returns>The snippet, or null.</returns>
    public Snippet? Find(SnippetId id) => Snippets.FirstOrDefault(snippet => snippet.Id == id);

    /// <summary>Loads the snippets.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many snippets there are, or why the store could not be read.</returns>
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

            var snippets = stored.Value.OrderBy(snippet => snippet.Position).ToImmutableArray();
            SetSnippets(snippets);
            RaiseChanged();
            return Result.Success(snippets.Length);
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Makes a snippet at the end of the list.</summary>
    /// <param name="name">What to call it.</param>
    /// <param name="body">The text.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The snippet, or why it could not be made. A failure to store still leaves the snippet in memory.</returns>
    public async Task<Result<Snippet>> CreateAsync(string name, string body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(body);

        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var checkedName = Check(name, body, except: null);
            if (checkedName.IsFailure)
            {
                return Result.Failure<Snippet>(checkedName.Error);
            }

            var current = Snippets;
            if (current.Length >= SnippetName.MaxSnippets)
            {
                return Result.Failure<Snippet>(new ResultError(ErrorCode.TooManySnippets, $"There can be at most {SnippetName.MaxSnippets} snippets."));
            }

            var snippet = new Snippet(SnippetId.New(), checkedName.Value, body, current.IsEmpty ? 0 : current.Max(existing => existing.Position) + 1);
            SetSnippets(current.Add(snippet));

            var stored = await _store.SaveAsync(snippet, cancellationToken).ConfigureAwait(false);
            RaiseChanged();
            return stored.IsFailure ? Result.Failure<Snippet>(stored.Error) : Result.Success(snippet);
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Changes the name and the text of a snippet.</summary>
    /// <param name="id">The snippet id.</param>
    /// <param name="name">The new name.</param>
    /// <param name="body">The new text.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the snippet could not be changed.</returns>
    public async Task<Result> UpdateAsync(SnippetId id, string name, string body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(body);

        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Snippets;
            var index = IndexOf(current, id);
            if (index < 0)
            {
                return NotFound();
            }

            var checkedName = Check(name, body, except: id);
            if (checkedName.IsFailure)
            {
                return Result.Failure(checkedName.Error);
            }

            var updated = current[index] with { Name = checkedName.Value, Body = body };
            SetSnippets(current.SetItem(index, updated));

            var stored = await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            RaiseChanged();
            return stored;
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Deletes a snippet.</summary>
    /// <param name="id">The snippet id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success or why the snippet could not be deleted.</returns>
    public async Task<Result> DeleteAsync(SnippetId id, CancellationToken cancellationToken)
    {
        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Snippets;
            var index = IndexOf(current, id);
            if (index < 0)
            {
                return NotFound();
            }

            SetSnippets(current.RemoveAt(index));
            var stored = await _store.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            RaiseChanged();
            return stored;
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Releases the lock that keeps changes in order. Call it once nothing changes the snippets any more.</summary>
    public void Dispose() => _changes.Dispose();

    private static int IndexOf(ImmutableArray<Snippet> snippets, SnippetId id)
    {
        for (var i = 0; i < snippets.Length; i++)
        {
            if (snippets[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static Result NotFound() =>
        Result.Failure(new ResultError(ErrorCode.SnippetNotFound, "That snippet no longer exists."));

    // The name must be allowed and free, and the text must be one the engine can read. A text with warnings is allowed.
    private Result<string> Check(string name, string body, SnippetId? except)
    {
        var valid = SnippetName.Validate(name);
        if (valid.IsFailure)
        {
            return valid;
        }

        var parsed = SnippetEngine.Parse(body);
        if (parsed.IsFailure)
        {
            return Result.Failure<string>(parsed.Error);
        }

        return Snippets.Any(snippet => snippet.Id != except && string.Equals(snippet.Name, valid.Value, StringComparison.OrdinalIgnoreCase))
            ? Result.Failure<string>(new ResultError(ErrorCode.SnippetNameTaken, "Another snippet already has that name."))
            : valid;
    }

    private void SetSnippets(ImmutableArray<Snippet> snippets)
    {
        lock (_gate)
        {
            _snippets = snippets;
        }
    }

    private void RaiseChanged() => Changed.RaiseEach(this);
}
