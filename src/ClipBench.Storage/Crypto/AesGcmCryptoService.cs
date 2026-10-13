using System.Security.Cryptography;
using System.Text;
using ClipBench.Core.Results;

namespace ClipBench.Storage.Crypto;

/// <summary>
/// AES-256-GCM with a random 96-bit nonce per value, and an HMAC-SHA256 content hash. The master key is
/// never used directly: separate encryption and hash keys are derived from it with HKDF, so a weakness
/// in one use cannot reach the other. A sealed value is <c>nonce(12) || ciphertext || tag(16)</c>.
/// </summary>
/// <remarks>
/// The nonce source is deliberately not injectable. A caller-supplied nonce is the classic way to reuse
/// one, which breaks GCM completely. Tests prove uniqueness instead. Safe to use from many threads: each
/// call builds its own <see cref="AesGcm"/> and shares only the derived keys, which are read under a shared
/// lock. <see cref="Dispose"/> takes the exclusive lock before it wipes them, so no call can seal or open
/// a value with a half-wiped or zeroed key.
/// </remarks>
public sealed class AesGcmCryptoService : ICryptoService, IDisposable
{
    /// <summary>The default cap on one value: above the largest capture the filter allows.</summary>
    public const int DefaultMaxPlaintextBytes = 128 * 1024 * 1024;

    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly byte[] _encryptionInfo = Encoding.ASCII.GetBytes("ClipBench v1 value encryption");
    private static readonly byte[] _hashInfo = Encoding.ASCII.GetBytes("ClipBench v1 content hash");

    private readonly byte[] _encryptionKey;
    private readonly byte[] _hashKey;
    private readonly int _maxPlaintextBytes;
    private readonly ReaderWriterLockSlim _keysInUse = new();
    private bool _disposed;

    /// <summary>Derives the working keys from the master key. The master key is not kept.</summary>
    /// <param name="masterKey">The 32-byte master key.</param>
    /// <param name="maxPlaintextBytes">The largest value to seal or open.</param>
    public AesGcmCryptoService(ReadOnlySpan<byte> masterKey, int maxPlaintextBytes = DefaultMaxPlaintextBytes)
    {
        if (masterKey.Length != KeySize)
        {
            throw new ArgumentException($"The master key must be {KeySize} bytes.", nameof(masterKey));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxPlaintextBytes, 1);

        _maxPlaintextBytes = maxPlaintextBytes;
        _encryptionKey = new byte[KeySize];
        _hashKey = new byte[KeySize];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, _encryptionKey, salt: [], info: _encryptionInfo);
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, _hashKey, salt: [], info: _hashInfo);
    }

    /// <inheritdoc/>
    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(plaintext.Length, _maxPlaintextBytes, nameof(plaintext));

        var sealedValue = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = sealedValue.AsSpan(0, NonceSize);
        var ciphertext = sealedValue.AsSpan(NonceSize, plaintext.Length);
        var tag = sealedValue.AsSpan(NonceSize + plaintext.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);
        _keysInUse.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var aes = new AesGcm(_encryptionKey, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        }
        finally
        {
            _keysInUse.ExitReadLock();
        }

        return sealedValue;
    }

    /// <inheritdoc/>
    public Result<byte[]> Decrypt(ReadOnlySpan<byte> sealedValue, ReadOnlySpan<byte> associatedData)
    {
        // Check the size before allocating anything, so a damaged length cannot cause a huge allocation.
        if (sealedValue.Length < NonceSize + TagSize || sealedValue.Length - NonceSize - TagSize > _maxPlaintextBytes)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Failed();
        }

        var plaintext = new byte[sealedValue.Length - NonceSize - TagSize];
        _keysInUse.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var aes = new AesGcm(_encryptionKey, TagSize);
            aes.Decrypt(
                sealedValue[..NonceSize],
                sealedValue.Slice(NonceSize, plaintext.Length),
                sealedValue[(NonceSize + plaintext.Length)..],
                plaintext,
                associatedData);
            return Result.Success(plaintext);
        }
        catch (CryptographicException)
        {
            // Fail closed: nothing that may be unauthenticated leaves this method.
            CryptographicOperations.ZeroMemory(plaintext);
            return Failed();
        }
        finally
        {
            _keysInUse.ExitReadLock();
        }
    }

    /// <inheritdoc/>
    public byte[] ComputeContentHash(byte kind, ReadOnlySpan<byte> data)
    {
        _keysInUse.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // The kind byte and the data are hashed one after the other, so a large clip is not copied to put a byte in front.
            using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, _hashKey);
            hmac.AppendData([kind]);
            hmac.AppendData(data);
            return hmac.GetHashAndReset();
        }
        finally
        {
            _keysInUse.ExitReadLock();
        }
    }

    /// <summary>Wipes the derived keys, after any call that is using them has finished. The service cannot be used afterwards.</summary>
    public void Dispose()
    {
        _keysInUse.EnterWriteLock();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CryptographicOperations.ZeroMemory(_encryptionKey);
            CryptographicOperations.ZeroMemory(_hashKey);
        }
        finally
        {
            _keysInUse.ExitWriteLock();
        }
    }

    private static Result<byte[]> Failed() =>
        Result.Failure<byte[]>(new ResultError(ErrorCode.DecryptionFailed, "Stored data failed its integrity check."));
}
