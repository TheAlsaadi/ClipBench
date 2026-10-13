using ClipBench.Core.Results;

namespace ClipBench.Storage.Crypto;

/// <summary>Seals and opens stored values and computes the keyed content hash. Every failure is closed.</summary>
public interface ICryptoService
{
    /// <summary>Encrypts a value. A fresh random nonce is used for every call.</summary>
    /// <param name="plaintext">The value to protect.</param>
    /// <param name="associatedData">Data the value is bound to. The same data must be given to open it.</param>
    /// <returns>The nonce, ciphertext and tag, in that order.</returns>
    byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData);

    /// <summary>Verifies and decrypts a sealed value.</summary>
    /// <param name="sealedValue">What <see cref="Encrypt"/> returned.</param>
    /// <param name="associatedData">The data the value was bound to.</param>
    /// <returns>The plaintext, or a failure when the value was altered, truncated, or sealed under another key or binding.</returns>
    Result<byte[]> Decrypt(ReadOnlySpan<byte> sealedValue, ReadOnlySpan<byte> associatedData);

    /// <summary>Computes the keyed hash that identifies duplicate content without revealing it.</summary>
    /// <param name="kind">One byte that is hashed in front of the data, so the same bytes of another kind hash differently.</param>
    /// <param name="data">The content bytes.</param>
    /// <returns>A 32-byte HMAC-SHA256, under a key derived from the master key, of the kind byte followed by the data.</returns>
    byte[] ComputeContentHash(byte kind, ReadOnlySpan<byte> data);
}
