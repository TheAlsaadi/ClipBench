using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Storage.Crypto;
using ClipBench.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace ClipBench.Storage.Clips;

/// <summary>
/// The durable, encrypted copy of the clip history. Content, previews and source app names are sealed with
/// AES-256-GCM and bound to their row, column and format; ids, times, the pin flag, sizes and a keyed content
/// hash are stored in plain columns so retention and listing need no decryption. All SQL is a constant with
/// named parameters. Safe to use from many threads: every operation takes one lock, and a second repository
/// on the same file waits for the database lock instead of failing.
/// </summary>
/// <remarks>
/// A stored value that fails authentication is never returned. Loads skip it and count it. The metadata
/// columns are not authenticated: someone with the file can reorder, pin or expire clips, but cannot read,
/// forge or move content (see the threat model).
/// </remarks>
public sealed class ClipRepository : IAsyncDisposable
{
    private const string CanaryName = "key_check";

    private const string SelectCanary = "SELECT value FROM meta WHERE name = $name;";
    private const string InsertCanary = "INSERT OR IGNORE INTO meta (name, value) VALUES ($name, $value);";
    private const string AnyClips = "SELECT EXISTS (SELECT 1 FROM clips);";

    private const string InsertClip = """
        INSERT OR REPLACE INTO clips
            (id, format, captured_at, expires_at, is_pinned, size_bytes, content_hash, content, preview, source_app)
        VALUES
            ($id, $format, $capturedAt, $expiresAt, $pinned, $size, $hash, $content, $preview, $source);
        """;

    private const string SelectOne = """
        SELECT format, captured_at, is_pinned, content, source_app FROM clips
        WHERE id = $id AND (expires_at IS NULL OR expires_at > $now);
        """;

    private const string SelectAll = """
        SELECT id, format, captured_at, is_pinned, content, source_app FROM clips
        WHERE expires_at IS NULL OR expires_at > $now
        ORDER BY captured_at DESC, id DESC;
        """;

    private const string SelectSummaries = """
        SELECT id, format, captured_at, is_pinned, size_bytes, preview, source_app FROM clips
        WHERE expires_at IS NULL OR expires_at > $now
        ORDER BY captured_at DESC, id DESC;
        """;

    private const string UpdatePinned = "UPDATE clips SET is_pinned = $pinned WHERE id = $id;";
    private const string DeleteOne = "DELETE FROM clips WHERE id = $id;";
    private const string DeleteAll = "DELETE FROM clips;";
    private const string DeleteExpired = "DELETE FROM clips WHERE expires_at IS NOT NULL AND expires_at <= $now RETURNING id;";

    // Unpinned clips only. A clip older than the cutoff goes. Of the rest, newest first, a clip goes when it is
    // past the count, or when the running total of sizes passes the budget (and it is not the newest). The
    // running total only grows, so once one clip does not fit, every older one goes too. This is the same rule
    // as ClipHistory.Trim, and a test checks that they agree.
    private const string Retain = """
        DELETE FROM clips WHERE id IN (
            SELECT id FROM clips WHERE is_pinned = 0 AND captured_at < $cutoff
            UNION
            SELECT id FROM (
                SELECT id,
                       ROW_NUMBER() OVER (ORDER BY captured_at DESC, id DESC) AS position,
                       SUM(size_bytes) OVER (ORDER BY captured_at DESC, id DESC ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS running
                FROM clips
                WHERE is_pinned = 0 AND captured_at >= $cutoff
            )
            WHERE position > $maxCount OR (position > 1 AND running > $maxBytes)
        ) RETURNING id;
        """;

    private const string CheckpointTruncate = "PRAGMA wal_checkpoint(TRUNCATE);";
    private const string Vacuum = "VACUUM;";

    private static readonly byte[] _canaryPlaintext = "ClipBench key check v1"u8.ToArray();

    // Reading is strict: bytes that are not valid UTF-8 mean the record is damaged.
    private static readonly UTF8Encoding _strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly SqliteConnection _connection;
    private readonly ICryptoService _crypto;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxContentBytes;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _disposeLock = new();
    private volatile bool _disposed;
    private Task? _closing;

    private ClipRepository(SqliteConnection connection, ICryptoService crypto, TimeProvider timeProvider, int maxContentBytes)
    {
        _connection = connection;
        _crypto = crypto;
        _timeProvider = timeProvider;
        _maxContentBytes = maxContentBytes;
    }

    /// <summary>Opens the database, creating or migrating it, and checks that the key is the one it was created with.</summary>
    /// <param name="databasePath">Where the database file is. Missing folders are created.</param>
    /// <param name="crypto">Seals and opens the stored values.</param>
    /// <param name="timeProvider">The clock for expiry and retention.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The repository. <see cref="ErrorCode.HistoryUnreadable"/> when the file is not a usable database or was
    /// written under another key (nothing is changed), <see cref="ErrorCode.HistoryTooNew"/> when a newer
    /// version of the app wrote it, or <see cref="ErrorCode.StorageFailure"/>.
    /// </returns>
    public static Task<Result<ClipRepository>> OpenAsync(string databasePath, ICryptoService crypto, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        OpenAsync(databasePath, crypto, timeProvider, ClipContentCodec.MaxEncodedBytes, cancellationToken);

    internal static async Task<Result<ClipRepository>> OpenAsync(string databasePath, ICryptoService crypto, TimeProvider timeProvider, int maxContentBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(crypto);
        ArgumentNullException.ThrowIfNull(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() => OpenCore(databasePath, crypto, timeProvider, maxContentBytes), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves a clip, replacing the stored clip with the same id, and any other clip with the same content,
    /// so the database never holds the same content twice. <see cref="ClipHistory"/> already removes
    /// duplicates, so the replacement is a safety net.
    /// </summary>
    /// <param name="clip">The clip as it should now be stored, pin included.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, <see cref="ErrorCode.ContentTooLarge"/> (nothing is saved), or a storage failure.</returns>
    public Task<Result> SaveAsync(Clip clip, CancellationToken cancellationToken) => SaveAsync(clip, null, cancellationToken);

    /// <summary>Saves a clip that expires at a given time. See <see cref="SaveAsync(Clip, CancellationToken)"/>.</summary>
    /// <param name="clip">The clip as it should now be stored, pin included.</param>
    /// <param name="expiresAt">When the clip must disappear, or null for never. Saving again with null clears an expiry.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, <see cref="ErrorCode.ContentTooLarge"/> (nothing is saved), or a storage failure.</returns>
    public async Task<Result> SaveAsync(Clip clip, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clip);

        return await RunAsync(
            connection =>
            {
                var encoded = ClipContentCodec.Encode(clip.Content, _maxContentBytes);
                if (encoded.IsFailure)
                {
                    return Result.Failure(encoded.Error);
                }

                var format = clip.Content.Format;
                var plain = encoded.Value;
                try
                {
                    var hash = _crypto.ComputeContentHash((byte)format, plain);
                    var content = _crypto.Encrypt(plain, RecordAad.ForClip(clip.Id, "content", format));
                    var preview = _crypto.Encrypt(Encoding.UTF8.GetBytes(clip.Content.GetPreview()), RecordAad.ForClip(clip.Id, "preview", format));
                    var source = clip.SourceApp is null
                        ? null
                        : _crypto.Encrypt(Encoding.UTF8.GetBytes(clip.SourceApp), RecordAad.ForClip(clip.Id, "source_app", format));

                    using var command = connection.CreateCommand();
                    command.CommandText = InsertClip;
                    Blob(command, "$id", IdBytes(clip.Id));
                    Integer(command, "$format", (long)format);
                    Integer(command, "$capturedAt", clip.CapturedAt.UtcTicks);
                    Integer(command, "$expiresAt", expiresAt?.UtcTicks);
                    Integer(command, "$pinned", clip.IsPinned ? 1 : 0);
                    Integer(command, "$size", clip.SizeInBytes);
                    Blob(command, "$hash", hash);
                    Blob(command, "$content", content);
                    Blob(command, "$preview", preview);
                    Blob(command, "$source", source);
                    command.ExecuteNonQuery();
                    return Result.Success();
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one clip.</summary>
    /// <param name="id">The clip id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The clip, <see cref="ErrorCode.ClipNotFound"/> (also for an expired clip), or <see cref="ErrorCode.DecryptionFailed"/> when the stored data was altered.</returns>
    public Task<Result<Clip>> GetAsync(ClipId id, CancellationToken cancellationToken) =>
        RunAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = SelectOne;
                Blob(command, "$id", IdBytes(id));
                Integer(command, "$now", _timeProvider.GetUtcNow().UtcTicks);
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return Result.Failure<Clip>(new ResultError(ErrorCode.ClipNotFound, "That clip is no longer in the history."));
                }

                return ReadClip(id, (ClipFormat)reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2) != 0, reader.GetFieldValue<byte[]>(3), SourceOrNull(reader, 4));
            },
            cancellationToken);

    /// <summary>Reads every clip that has not expired, newest first. A clip that fails its integrity check is skipped and counted.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The clips and how many records were skipped.</returns>
    public Task<Result<LoadedItems<Clip>>> LoadAllAsync(CancellationToken cancellationToken) =>
        RunAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = SelectAll;
                Integer(command, "$now", _timeProvider.GetUtcNow().UtcTicks);
                using var reader = command.ExecuteReader();
                var clips = ImmutableArray.CreateBuilder<Clip>();
                var unreadable = 0;
                while (reader.Read())
                {
                    var id = IdFrom(reader, 0);
                    var clip = ReadClip(id, (ClipFormat)reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3) != 0, reader.GetFieldValue<byte[]>(4), SourceOrNull(reader, 5));
                    if (clip.IsSuccess)
                    {
                        clips.Add(clip.Value);
                    }
                    else
                    {
                        unreadable++;
                    }
                }

                return Result.Success(new LoadedItems<Clip>(clips.ToImmutable(), unreadable));
            },
            cancellationToken);

    /// <summary>
    /// Reads what a list needs for every clip that has not expired, newest first, without decrypting any content.
    /// A record whose preview or source fails its integrity check is skipped and counted.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The summaries and how many records were skipped.</returns>
    public Task<Result<LoadedItems<ClipSummary>>> LoadSummariesAsync(CancellationToken cancellationToken) =>
        RunAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = SelectSummaries;
                Integer(command, "$now", _timeProvider.GetUtcNow().UtcTicks);
                using var reader = command.ExecuteReader();
                var summaries = ImmutableArray.CreateBuilder<ClipSummary>();
                var unreadable = 0;
                while (reader.Read())
                {
                    var summary = ReadSummary(reader);
                    if (summary.IsSuccess)
                    {
                        summaries.Add(summary.Value);
                    }
                    else
                    {
                        unreadable++;
                    }
                }

                return Result.Success(new LoadedItems<ClipSummary>(summaries.ToImmutable(), unreadable));
            },
            cancellationToken);

    /// <summary>Pins or unpins a stored clip.</summary>
    /// <param name="id">The clip id.</param>
    /// <param name="pinned">True to pin.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or <see cref="ErrorCode.ClipNotFound"/>.</returns>
    public Task<Result> SetPinnedAsync(ClipId id, bool pinned, CancellationToken cancellationToken) =>
        RunAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = UpdatePinned;
                Integer(command, "$pinned", pinned ? 1 : 0);
                Blob(command, "$id", IdBytes(id));
                return command.ExecuteNonQuery() == 0
                    ? Result.Failure(new ResultError(ErrorCode.ClipNotFound, "That clip is no longer in the history."))
                    : Result.Success();
            },
            cancellationToken);

    /// <summary>
    /// Deletes clips, pinned or not. A deleted value is overwritten on disk, not just marked free, and the write-ahead
    /// log is emptied so that the copy written when the clip was saved does not stay in it. Emptying the log has to wait
    /// for any other connection that is reading the database, so it is best effort while another process has it open.
    /// </summary>
    /// <param name="ids">The clips to delete. Ids that are not stored are ignored.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many clips were deleted.</returns>
    public async Task<Result<int>> DeleteAsync(IEnumerable<ClipId> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.ToArray();

        return await RunAsync(
            connection =>
            {
                using var transaction = connection.BeginTransaction(deferred: false);
                var removed = 0;
                foreach (var id in wanted)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = DeleteOne;
                    Blob(command, "$id", IdBytes(id));
                    removed += command.ExecuteNonQuery();
                }

                transaction.Commit();
                if (removed > 0)
                {
                    Execute(connection, CheckpointTruncate);
                }

                return Result.Success(removed);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes every clip whose expiry has arrived, pinned or not: an expiry is asked for explicitly. Removed values are
    /// scrubbed from the write-ahead log as in <see cref="DeleteAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The ids that were deleted, so the in-memory history can drop them too.</returns>
    public Task<Result<ImmutableArray<ClipId>>> SweepExpiredAsync(CancellationToken cancellationToken) =>
        RunAsync(
            connection =>
            {
                var removed = RemoveExpired(connection, transaction: null);
                if (!removed.IsEmpty)
                {
                    Execute(connection, CheckpointTruncate);
                }

                return Result.Success(removed);
            },
            cancellationToken);

    /// <summary>
    /// Applies the history limits to the stored clips, by the same rules as <see cref="ClipHistory.Trim"/>: unpinned
    /// clips only, too old first, then the count, then the size budget. Expired clips are removed as well, and removed
    /// values are scrubbed from the write-ahead log as in <see cref="DeleteAsync"/>.
    /// </summary>
    /// <param name="options">The limits.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The ids that were deleted.</returns>
    public async Task<Result<ImmutableArray<ClipId>>> RetainAsync(HistoryOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        return await RunAsync(
            connection =>
            {
                using var transaction = connection.BeginTransaction(deferred: false);
                var removed = RemoveExpired(connection, transaction).ToBuilder();

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = Retain;

                // An age longer than the calendar means "keep forever". Subtracting it from a date would throw, so the
                // cutoff stops at the earliest date, which no clip is older than.
                Integer(command, "$cutoff", Math.Max(DateTimeOffset.MinValue.UtcTicks, _timeProvider.GetUtcNow().UtcTicks - options.MaxAge.Ticks));
                Integer(command, "$maxCount", options.MaxCount);
                Integer(command, "$maxBytes", options.MaxTotalBytes);
                removed.AddRange(ReadIds(command));

                transaction.Commit();
                if (removed.Count > 0)
                {
                    Execute(connection, CheckpointTruncate);
                }

                return Result.Success(removed.ToImmutable());
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes every clip, pinned or not, then rebuilds the database file and empties the write-ahead log, so none
    /// of the removed values stays in the files. The repository can be used again afterwards.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or a storage failure.</returns>
    public Task<Result> WipeAsync(CancellationToken cancellationToken) =>
        RunAsync(
            connection =>
            {
                Execute(connection, DeleteAll);
                Execute(connection, Vacuum);

                // Last, so the log that VACUUM wrote to is emptied and no removed page image is left in it.
                Execute(connection, CheckpointTruncate);
                return Result.Success();
            },
            cancellationToken);

    /// <summary>
    /// Waits for running operations, then closes the database. Safe to call twice, and a second caller waits for the
    /// same close, so the files can be deleted as soon as any call has returned.
    /// </summary>
    /// <returns>A task that completes when the database is closed.</returns>
    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            _disposed = true;
            _closing ??= CloseAsync();
            return new ValueTask(_closing);
        }
    }

    private async Task CloseAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Result<ClipRepository> OpenCore(string databasePath, ICryptoService crypto, TimeProvider timeProvider, int maxContentBytes)
    {
        SqliteConnection? connection = null;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,

                // No pooling: closing the repository must close the file, so it can be wiped and deleted.
                Pooling = false,

                // How long to wait for another connection's lock before giving up, in seconds.
                DefaultTimeout = 15,
            }.ToString());
            connection.Open();

            var pragmas = ApplyPragmas(connection);
            if (pragmas.IsFailure)
            {
                connection.Dispose();
                return Result.Failure<ClipRepository>(pragmas.Error);
            }

            var migrated = SchemaMigrator.Apply(connection, Migrations.All, timeProvider);
            if (migrated.IsFailure)
            {
                connection.Dispose();
                return Result.Failure<ClipRepository>(migrated.Error);
            }

            var checkedKey = CheckKey(connection, crypto);
            if (checkedKey.IsFailure)
            {
                connection.Dispose();
                return Result.Failure<ClipRepository>(checkedKey.Error);
            }

            return Result.Success(new ClipRepository(connection, crypto, timeProvider, maxContentBytes));
        }
        catch (SqliteException exception)
        {
            connection?.Dispose();
            return Result.Failure<ClipRepository>(SqliteErrors.From(exception));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            connection?.Dispose();
            return Result.Failure<ClipRepository>(new ResultError(ErrorCode.StorageFailure, "The history database could not be created or opened."));
        }
    }

    // Write-ahead logging, secure delete and a few hardening settings. Secure delete is a security property, so
    // the open fails if it cannot be switched on. Write-ahead logging only affects speed and file layout.
    private static Result ApplyPragmas(SqliteConnection connection)
    {
        Execute(connection, "PRAGMA journal_mode = WAL;");
        Execute(connection, "PRAGMA synchronous = NORMAL;");
        Execute(connection, "PRAGMA secure_delete = ON;");
        Execute(connection, "PRAGMA trusted_schema = OFF;");
        Execute(connection, "PRAGMA temp_store = MEMORY;");
        Execute(connection, "PRAGMA foreign_keys = ON;");

        using var verify = connection.CreateCommand();
        verify.CommandText = "PRAGMA secure_delete;";
        return Convert.ToInt64(verify.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 0
            ? Result.Failure(new ResultError(ErrorCode.StorageFailure, "The history database could not turn on secure delete."))
            : Result.Success();
    }

    // The key check proves the key is the one the database was created with before any clip is read: a wrong
    // key is reported once, as one clear error, instead of as a failure for every clip.
    private static Result CheckKey(SqliteConnection connection, ICryptoService crypto)
    {
        var aad = RecordAad.ForMeta(CanaryName);
        var stored = ReadCanary(connection);
        if (stored is null)
        {
            // A database with clips but no key check cannot be verified, so it is not trusted with a new one.
            using (var any = connection.CreateCommand())
            {
                any.CommandText = AnyClips;
                if (Convert.ToInt64(any.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 0)
                {
                    return Unreadable();
                }
            }

            using var insert = connection.CreateCommand();
            insert.CommandText = InsertCanary;
            insert.Parameters.AddWithValue("$name", CanaryName);
            Blob(insert, "$value", crypto.Encrypt(_canaryPlaintext, aad));
            insert.ExecuteNonQuery();

            // Another process may have written it first. Verify whichever one is there.
            stored = ReadCanary(connection);
        }

        var opened = stored is null ? default : crypto.Decrypt(stored, aad);
        return stored is not null && opened.IsSuccess && opened.Value.AsSpan().SequenceEqual(_canaryPlaintext)
            ? Result.Success()
            : Unreadable();
    }

    private static byte[]? ReadCanary(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SelectCanary;
        command.Parameters.AddWithValue("$name", CanaryName);
        return command.ExecuteScalar() as byte[];
    }

    private static ResultError DamagedRecord { get; } = new(ErrorCode.StorageFailure, "A stored clip could not be read.");

    private static Result Unreadable() => Result.Failure(StorageErrors.HistoryUnreadable);

    // [ConstantExpected] makes the compiler's analyzers reject any call that passes anything but a literal or a
    // constant, so the one place that assigns a variable to CommandText can only ever receive constant SQL.
    private static void Execute(SqliteConnection connection, [ConstantExpected] string sql)
    {
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Every caller must pass a constant: see [ConstantExpected] above.
        command.CommandText = sql;
#pragma warning restore CA2100
        command.ExecuteNonQuery();
    }

    // Runs one operation on the connection. The lock is held for the whole operation, the work runs off the
    // caller's thread (a UI thread must not wait for a database), and database errors become results.
    private async Task<Result<T>> RunAsync<T>(Func<SqliteConnection, Result<T>> work, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await Task.Run(() => Guard(work), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // For operations with no value: the same, with the plain Result in and out.
    private async Task<Result> RunAsync(Func<SqliteConnection, Result> work, CancellationToken cancellationToken)
    {
        var outcome = await RunAsync(
            connection =>
            {
                var result = work(connection);
                return result.IsSuccess ? Result.Success(true) : Result.Failure<bool>(result.Error);
            },
            cancellationToken).ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error);
    }

    private Result<T> Guard<T>(Func<SqliteConnection, Result<T>> work)
    {
        try
        {
            return work(_connection);
        }
        catch (SqliteException exception)
        {
            return Result.Failure<T>(SqliteErrors.From(exception));
        }
    }

    private ImmutableArray<ClipId> RemoveExpired(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = DeleteExpired;
        Integer(command, "$now", _timeProvider.GetUtcNow().UtcTicks);
        return [.. ReadIds(command)];
    }

    private static List<ClipId> ReadIds(SqliteCommand command)
    {
        var ids = new List<ClipId>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(IdFrom(reader, 0));
        }

        return ids;
    }

    // Opens one stored clip. Anything that is not exactly what was written fails closed.
    private Result<Clip> ReadClip(ClipId id, ClipFormat format, long capturedTicks, bool pinned, byte[] sealedContent, byte[]? sealedSource)
    {
        // The time is a plain column, so it can hold any number. One that is not a date is a damaged record.
        if (!TryTime(capturedTicks, out var capturedAt))
        {
            return Result.Failure<Clip>(DamagedRecord);
        }

        // The format is bound into every sealed value, so a format that was never written fails here.
        var plain = _crypto.Decrypt(sealedContent, RecordAad.ForClip(id, "content", format));
        if (plain.IsFailure)
        {
            return Result.Failure<Clip>(plain.Error);
        }

        try
        {
            var content = ClipContentCodec.Decode(format, plain.Value, _maxContentBytes);
            if (content.IsFailure)
            {
                return Result.Failure<Clip>(content.Error);
            }

            var source = OpenText(sealedSource, id, "source_app", format);
            return source.IsFailure
                ? Result.Failure<Clip>(source.Error)
                : Result.Success(new Clip(id, content.Value, capturedAt, source.Value, pinned));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain.Value);
        }
    }

    private Result<ClipSummary> ReadSummary(SqliteDataReader reader)
    {
        var id = IdFrom(reader, 0);
        var format = (ClipFormat)reader.GetInt64(1);
        if (!TryTime(reader.GetInt64(2), out var capturedAt))
        {
            return Result.Failure<ClipSummary>(DamagedRecord);
        }

        var preview = OpenText(reader.GetFieldValue<byte[]>(5), id, "preview", format);
        var source = OpenText(SourceOrNull(reader, 6), id, "source_app", format);
        if (preview.IsFailure || source.IsFailure)
        {
            return Result.Failure<ClipSummary>(preview.IsFailure ? preview.Error : source.Error);
        }

        return Result.Success(new ClipSummary(
            id,
            format,
            capturedAt,
            reader.GetInt64(3) != 0,
            reader.GetInt64(4),
            preview.Value ?? string.Empty,
            source.Value));
    }

    // Opens a sealed text column. A missing value is a missing value, not a failure.
    private Result<string?> OpenText(byte[]? sealedValue, ClipId id, string column, ClipFormat format)
    {
        if (sealedValue is null)
        {
            return Result.Success<string?>(null);
        }

        var plain = _crypto.Decrypt(sealedValue, RecordAad.ForClip(id, column, format));
        if (plain.IsFailure)
        {
            return Result.Failure<string?>(plain.Error);
        }

        try
        {
            return Result.Success<string?>(_strictUtf8.GetString(plain.Value));
        }
        catch (ArgumentException)
        {
            return Result.Failure<string?>(DamagedRecord);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain.Value);
        }
    }

    private static byte[] IdBytes(ClipId id) => id.Value.ToByteArray(bigEndian: true);

    private static ClipId IdFrom(SqliteDataReader reader, int ordinal) => new(new Guid(reader.GetFieldValue<byte[]>(ordinal), bigEndian: true));

    private static bool TryTime(long ticks, out DateTimeOffset time)
    {
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            time = default;
            return false;
        }

        time = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }

    private static byte[]? SourceOrNull(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<byte[]>(ordinal);

    private static void Blob(SqliteCommand command, string name, byte[]? value) =>
        command.Parameters.Add(name, SqliteType.Blob).Value = (object?)value ?? DBNull.Value;

    private static void Integer(SqliteCommand command, string name, long? value) =>
        command.Parameters.Add(name, SqliteType.Integer).Value = value.HasValue ? value.Value : DBNull.Value;
}
