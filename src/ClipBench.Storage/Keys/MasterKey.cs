using System.Security.Cryptography;

namespace ClipBench.Storage.Keys;

/// <summary>
/// The 32-byte key every other key is derived from. It exists in memory only while the app runs, and
/// is wiped when disposed. Printing it shows no bytes.
/// </summary>
public sealed class MasterKey : IDisposable
{
    /// <summary>The key length in bytes.</summary>
    public const int Size = 32;

    private readonly byte[] _bytes;
    private bool _disposed;

    /// <summary>Wraps a copy of the given bytes.</summary>
    /// <param name="bytes">Exactly <see cref="Size"/> bytes.</param>
    public MasterKey(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size)
        {
            throw new ArgumentException($"A master key is {Size} bytes.", nameof(bytes));
        }

        _bytes = bytes.ToArray();
    }

    /// <summary>The key bytes. Reading them after the key was disposed is a bug.</summary>
    public ReadOnlySpan<byte> Bytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _bytes;
        }
    }

    /// <summary>Creates a new random key.</summary>
    /// <returns>A key from the operating system's random number generator.</returns>
    public static MasterKey Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(Size);
        try
        {
            return new MasterKey(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>Wipes the key bytes.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CryptographicOperations.ZeroMemory(_bytes);
    }

    /// <summary>Never shows the key.</summary>
    /// <returns>The type name.</returns>
    public override string ToString() => nameof(MasterKey);
}
