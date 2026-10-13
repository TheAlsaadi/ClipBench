using System.Security.Cryptography;
using ClipBench.Core.Results;
using ClipBench.Core.Security;

namespace ClipBench.Storage.Keys;

/// <summary>
/// Keeps the master key on disk, wrapped by an <see cref="IKeyProtector"/>. The file is the four bytes
/// <c>CBK1</c> followed by the wrapped key. A key file that exists but cannot be used is reported and
/// never replaced: a new key would make every saved clip permanently unreadable, so the caller must
/// decide to start fresh. The file lives in the folder the caller chose and inherits that folder's
/// permissions (for <c>%LOCALAPPDATA%</c> that is the current user only).
/// </summary>
public sealed class MasterKeyStore
{
    // A wrapped 32-byte key is a few hundred bytes. Anything bigger is not a key file, and is never read.
    private const int MaxFileBytes = 4096;

    private static readonly byte[] _magic = "CBK1"u8.ToArray();

    private readonly IKeyProtector _protector;

    /// <summary>Creates a store for one key file.</summary>
    /// <param name="keyFilePath">Where the wrapped key is kept.</param>
    /// <param name="protector">Wraps and unwraps the key.</param>
    public MasterKeyStore(string keyFilePath, IKeyProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyFilePath);
        ArgumentNullException.ThrowIfNull(protector);
        KeyFilePath = keyFilePath;
        _protector = protector;
    }

    /// <summary>Where the wrapped key is kept.</summary>
    public string KeyFilePath { get; }

    /// <summary>Unwraps the stored key, or creates and stores a new one when there is no key file.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The key. <see cref="ErrorCode.HistoryUnreadable"/> when the file exists but cannot be used (nothing is
    /// changed), <see cref="ErrorCode.KeyProtectionFailed"/> when a new key could not be protected (nothing is
    /// written), or <see cref="ErrorCode.StorageFailure"/> when the disk refused.
    /// </returns>
    public async Task<Result<MasterKey>> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return File.Exists(KeyFilePath)
                ? await LoadAsync(cancellationToken).ConfigureAwait(false)
                : await CreateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<MasterKey>(DiskError());
        }
    }

    /// <summary>
    /// Deletes the key file. A file of the expected size is overwritten with random bytes first, so the
    /// wrapped key does not stay in the file's old disk blocks. That is best effort: a solid-state drive may
    /// keep old blocks that no program can reach. Deleting a file that does not exist succeeds.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or a storage failure.</returns>
    public async Task<Result> DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!File.Exists(KeyFilePath))
            {
                return Result.Success();
            }

            var length = new FileInfo(KeyFilePath).Length;
            if (length is > 0 and <= MaxFileBytes)
            {
                await using var stream = new FileStream(
                    KeyFilePath,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 1,
                    FileOptions.WriteThrough | FileOptions.Asynchronous);
                await stream.WriteAsync(RandomNumberGenerator.GetBytes((int)length), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Delete(KeyFilePath);
            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(DiskError());
        }
    }

    private static ResultError DiskError() =>
        new(ErrorCode.StorageFailure, "The encryption key file could not be read or written.");

    private static Result<MasterKey> Unreadable() => Result.Failure<MasterKey>(StorageErrors.HistoryUnreadable);

    private static Result<MasterKey> NotProtected() =>
        Result.Failure<MasterKey>(new ResultError(
            ErrorCode.KeyProtectionFailed,
            "Windows could not protect the encryption key, so nothing was saved."));

    private async Task<Result<MasterKey>> LoadAsync(CancellationToken cancellationToken)
    {
        var length = new FileInfo(KeyFilePath).Length;
        if (length <= _magic.Length || length > MaxFileBytes)
        {
            return Unreadable();
        }

        var content = await File.ReadAllBytesAsync(KeyFilePath, cancellationToken).ConfigureAwait(false);
        if (!content.AsSpan().StartsWith(_magic))
        {
            return Unreadable();
        }

        var unwrapped = _protector.Unprotect(content.AsSpan(_magic.Length));
        if (unwrapped.IsFailure)
        {
            return Unreadable();
        }

        var secret = unwrapped.Value;
        try
        {
            return secret.Length == MasterKey.Size ? Result.Success(new MasterKey(secret)) : Unreadable();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private async Task<Result<MasterKey>> CreateAsync(CancellationToken cancellationToken)
    {
        var key = MasterKey.Generate();
        var keep = false;
        try
        {
            var wrapped = _protector.Protect(key.Bytes);
            if (wrapped.IsFailure || !UnwrapsTo(wrapped.Value, key))
            {
                return NotProtected();
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(KeyFilePath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write beside the target and move into place, so a crash never leaves half a key file.
            var temporary = $"{KeyFilePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await WriteDurablyAsync(temporary, [.. _magic, .. wrapped.Value], cancellationToken).ConfigureAwait(false);
                File.Move(temporary, KeyFilePath);
            }
            catch (IOException) when (File.Exists(KeyFilePath))
            {
                // Another process created the key first. Its key wins and is never overwritten.
                return await LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                File.Delete(temporary);
            }

            keep = true;
            return Result.Success(key);
        }
        finally
        {
            if (!keep)
            {
                key.Dispose();
            }
        }
    }

    // A wrap that cannot be unwrapped again must never be written: it would orphan everything saved under the key.
    private bool UnwrapsTo(byte[] wrapped, MasterKey key)
    {
        var check = _protector.Unprotect(wrapped);
        if (check.IsFailure)
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(check.Value, key.Bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(check.Value);
        }
    }

    private static async Task WriteDurablyAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1,
            FileOptions.WriteThrough | FileOptions.Asynchronous);
        await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
