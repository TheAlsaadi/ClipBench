using ClipBench.Core.Results;
using ClipBench.Core.Security;
using ClipBench.Storage.Clips;
using ClipBench.Storage.Crypto;
using ClipBench.Storage.Keys;

namespace ClipBench.Storage;

/// <summary>
/// The encrypted history, ready to use: the master key, the crypto built from it, and the repository. It
/// decides what happens when the key and the database do not match, and it owns start fresh and wipe all.
/// The master key itself is not kept in memory after the working keys are derived from it.
/// </summary>
public sealed class ClipStorage : IAsyncDisposable
{
    private readonly StoragePaths _paths;
    private readonly MasterKeyStore _keyStore;
    private readonly AesGcmCryptoService _crypto;
    private readonly Lock _closeLock = new();
    private volatile bool _closed;
    private Task? _closing;

    private ClipStorage(StoragePaths paths, MasterKeyStore keyStore, AesGcmCryptoService crypto, ClipRepository repository)
    {
        _paths = paths;
        _keyStore = keyStore;
        _crypto = crypto;
        Repository = repository;
    }

    /// <summary>The clip repository. After a wipe or dispose it refuses to work.</summary>
    public ClipRepository Repository { get; }

    /// <summary>
    /// Opens the history, creating it and its key on the first run. A key or database that cannot be used is
    /// reported and left exactly as it is. In particular a database whose key file is missing is never given a
    /// new key, because that would make the old clips unreadable for good.
    /// </summary>
    /// <param name="paths">Where the files live.</param>
    /// <param name="protector">Wraps and unwraps the master key.</param>
    /// <param name="timeProvider">The clock for expiry and retention.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The storage, <see cref="ErrorCode.HistoryUnreadable"/> when the saved history cannot be decrypted (the caller
    /// can offer <see cref="StartFreshAsync"/>), <see cref="ErrorCode.HistoryTooNew"/> when a newer version wrote it,
    /// <see cref="ErrorCode.KeyProtectionFailed"/> when a first-run key could not be protected, or
    /// <see cref="ErrorCode.StorageFailure"/>.
    /// </returns>
    public static async Task<Result<ClipStorage>> OpenAsync(StoragePaths paths, IKeyProtector protector, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(paths.KeyPath) && HasContent(paths.DatabasePath))
        {
            return Result.Failure<ClipStorage>(StorageErrors.HistoryUnreadable);
        }

        var keyStore = new MasterKeyStore(paths.KeyPath, protector);
        var key = await keyStore.LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
        if (key.IsFailure)
        {
            return Result.Failure<ClipStorage>(key.Error);
        }

        AesGcmCryptoService crypto;
        using (var masterKey = key.Value)
        {
            crypto = new AesGcmCryptoService(masterKey.Bytes);
        }

        Result<ClipRepository> repository;
        try
        {
            repository = await ClipRepository.OpenAsync(paths.DatabasePath, crypto, timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            crypto.Dispose();
            throw;
        }

        if (repository.IsFailure)
        {
            crypto.Dispose();
            return Result.Failure<ClipStorage>(repository.Error);
        }

        return Result.Success(new ClipStorage(paths, keyStore, crypto, repository.Value));
    }

    /// <summary>
    /// Throws away the saved history and its key, then opens an empty history with a new key. This destroys
    /// whatever was saved, so call it only after the user has agreed.
    /// </summary>
    /// <param name="paths">Where the files live.</param>
    /// <param name="protector">Wraps and unwraps the master key.</param>
    /// <param name="timeProvider">The clock for expiry and retention.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new, empty storage, or a failure when the old files could not be removed.</returns>
    public static async Task<Result<ClipStorage>> StartFreshAsync(StoragePaths paths, IKeyProtector protector, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();

        var removed = DeleteDatabaseFiles(paths);
        if (removed.IsFailure)
        {
            return Result.Failure<ClipStorage>(removed.Error);
        }

        var key = await new MasterKeyStore(paths.KeyPath, protector).DeleteAsync(cancellationToken).ConfigureAwait(false);
        return key.IsFailure
            ? Result.Failure<ClipStorage>(key.Error)
            : await OpenAsync(paths, protector, timeProvider, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes everything: every clip, the database and its log, and the key. The storage is closed afterwards and
    /// a new one must be opened, which starts with a new key. Every step is attempted even if one fails.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or the first failure.</returns>
    public async Task<Result> WipeAllAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_closed, this);

        var rows = await Repository.WipeAsync(cancellationToken).ConfigureAwait(false);
        await DisposeAsync().ConfigureAwait(false);
        var files = DeleteDatabaseFiles(_paths);
        var key = await _keyStore.DeleteAsync(cancellationToken).ConfigureAwait(false);

        return rows.IsFailure ? rows : files.IsFailure ? files : key;
    }

    /// <summary>
    /// Closes the database and wipes the working keys from memory. Safe to call twice, and a second caller waits for the
    /// same close.
    /// </summary>
    /// <returns>A task that completes when everything is closed.</returns>
    public ValueTask DisposeAsync()
    {
        lock (_closeLock)
        {
            _closed = true;
            _closing ??= CloseAsync();
            return new ValueTask(_closing);
        }
    }

    private async Task CloseAsync()
    {
        await Repository.DisposeAsync().ConfigureAwait(false);
        _crypto.Dispose();
    }

    private static bool HasContent(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // The database, its write-ahead log and its shared-memory file. They hold ciphertext only, so a plain delete is enough.
    private static Result DeleteDatabaseFiles(StoragePaths paths)
    {
        try
        {
            foreach (var path in new[] { paths.DatabasePath, paths.DatabasePath + "-wal", paths.DatabasePath + "-shm" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(new ResultError(ErrorCode.StorageFailure, "The history files could not be removed."));
        }
    }
}
