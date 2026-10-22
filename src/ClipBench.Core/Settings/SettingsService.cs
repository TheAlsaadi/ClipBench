using ClipBench.Core.Results;

namespace ClipBench.Core.Settings;

/// <summary>
/// The settings in force, kept in step with the store. A change is made in memory first and then saved, so a store
/// that fails never undoes what the user chose; the failure is returned. Saves happen one at a time, in the order
/// the changes were made. Safe to use from many threads.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private readonly ISettingsStore _store;
    private readonly SemaphoreSlim _changes = new(1, 1);
    private volatile AppSettings _current = AppSettings.Default;

    /// <summary>Creates the service with the default settings.</summary>
    /// <param name="store">Where the settings are kept between runs.</param>
    public SettingsService(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// Raised after the settings changed, on the thread that changed them. A subscriber that throws is skipped and
    /// the others are still told.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>The settings in force.</summary>
    public AppSettings Current => _current;

    /// <summary>Loads the saved settings. Anything that cannot be used is replaced by its default.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>How they were found.</returns>
    public async Task<SettingsLoadStatus> LoadAsync(CancellationToken cancellationToken)
    {
        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            _current = loaded.Settings.Normalize();
            RaiseChanged();
            return loaded.Status;
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <summary>Changes the settings and saves them. A change that makes no difference is neither saved nor announced.</summary>
    /// <param name="change">Makes the new settings from the current ones. The result is normalized.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Success, or why the change was not saved. A failure still leaves the change in force.</returns>
    public async Task<Result> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var updated = change(_current).Normalize();
            if (updated == _current)
            {
                return Result.Success();
            }

            _current = updated;
            RaiseChanged();
            return await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _changes.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _changes.Dispose();

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
