using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ClipBench.Core.Results;
using ClipBench.Core.Security;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Cryptography;

namespace ClipBench.Platform.Windows.Security;

/// <summary>
/// Wraps a secret with the Windows data protection API (DPAPI) for the current user. The wrap can only
/// be opened by the same user on the same machine, and only by a caller that supplies the same entropy.
/// </summary>
/// <remarks>
/// The entropy ties a wrap to this application: it is a constant in the source code, not a secret, so it
/// keeps another program that calls DPAPI without knowing it from unwrapping the key by accident, and does
/// not stop malware running as the same user that does know it (see the threat model). The scope is the
/// current user, never the machine, and Windows is told never to show a prompt.
/// </remarks>
public sealed unsafe class DpapiKeyProtector : IKeyProtector
{
    /// <summary>The largest secret that is wrapped. The master key is 32 bytes; this only bounds a call that is a bug.</summary>
    public const int MaxSecretBytes = 64 * 1024;

    /// <summary>The largest wrap that is unwrapped: a secret of the maximum size plus room for DPAPI's header, salt and signature.</summary>
    public const int MaxWrapBytes = MaxSecretBytes + 4096;

    private static readonly byte[] _clipBenchEntropy = Encoding.UTF8.GetBytes("ClipBench v1 master key wrap");

    private readonly byte[] _entropy;

    /// <summary>Creates a protector that wraps with the given entropy.</summary>
    /// <param name="entropy">Application-specific bytes mixed into every wrap. Not empty.</param>
    public DpapiKeyProtector(ReadOnlySpan<byte> entropy)
    {
        if (entropy.IsEmpty)
        {
            throw new ArgumentException("Entropy is what ties a wrap to this application, so it cannot be empty.", nameof(entropy));
        }

        _entropy = entropy.ToArray();
    }

    /// <summary>Creates the protector the app uses, with ClipBench's own entropy.</summary>
    /// <returns>A protector whose wraps only ClipBench's protector can open.</returns>
    public static DpapiKeyProtector ForClipBench() => new(_clipBenchEntropy);

    /// <inheritdoc/>
    public Result<byte[]> Protect(ReadOnlySpan<byte> secret)
    {
        if (secret.IsEmpty || secret.Length > MaxSecretBytes)
        {
            return Failure("There is no usable key to protect.");
        }

        fixed (byte* secretPointer = secret)
        fixed (byte* entropyPointer = _entropy)
        {
            var input = new CRYPT_INTEGER_BLOB { cbData = (uint)secret.Length, pbData = secretPointer };
            var entropy = new CRYPT_INTEGER_BLOB { cbData = (uint)_entropy.Length, pbData = entropyPointer };
            CRYPT_INTEGER_BLOB output = default;

            if (!PInvoke.CryptProtectData(&input, default(PCWSTR), &entropy, null, null, PInvoke.CRYPTPROTECT_UI_FORBIDDEN, &output))
            {
                return Failure($"Windows could not protect the key (error {Marshal.GetLastPInvokeError()}).");
            }

            return Result.Success(TakeOutput(output, zero: false));
        }
    }

    /// <inheritdoc/>
    public Result<byte[]> Unprotect(ReadOnlySpan<byte> protectedSecret)
    {
        if (protectedSecret.IsEmpty || protectedSecret.Length > MaxWrapBytes)
        {
            return Failure("The saved key is damaged.");
        }

        fixed (byte* wrapPointer = protectedSecret)
        fixed (byte* entropyPointer = _entropy)
        {
            var input = new CRYPT_INTEGER_BLOB { cbData = (uint)protectedSecret.Length, pbData = wrapPointer };
            var entropy = new CRYPT_INTEGER_BLOB { cbData = (uint)_entropy.Length, pbData = entropyPointer };
            CRYPT_INTEGER_BLOB output = default;

            if (!PInvoke.CryptUnprotectData(&input, null, &entropy, null, null, PInvoke.CRYPTPROTECT_UI_FORBIDDEN, &output))
            {
                return Failure($"Windows could not unwrap the key (error {Marshal.GetLastPInvokeError()}). It is damaged or belongs to another user or computer.");
            }

            return Result.Success(TakeOutput(output, zero: true));
        }
    }

    // Copies the buffer Windows allocated into a managed array and gives the buffer back. A buffer that
    // held a secret is overwritten first, so the key does not stay behind in freed memory.
    private static byte[] TakeOutput(CRYPT_INTEGER_BLOB output, bool zero)
    {
        try
        {
            var bytes = new ReadOnlySpan<byte>(output.pbData, (int)output.cbData).ToArray();
            if (zero)
            {
                CryptographicOperations.ZeroMemory(new Span<byte>(output.pbData, (int)output.cbData));
            }

            return bytes;
        }
        finally
        {
            PInvoke.LocalFree((HLOCAL)output.pbData);
        }
    }

    private static Result<byte[]> Failure(string message) =>
        Result.Failure<byte[]>(new ResultError(ErrorCode.KeyProtectionFailed, message));
}
