using ClipBench.Core.Results;

namespace ClipBench.Core.Security;

/// <summary>
/// Wraps a secret so that only the current user on this machine can unwrap it. The Windows
/// implementation uses DPAPI. Failures are results, because a profile can be damaged or restored
/// onto another machine; neither is a bug in the program.
/// </summary>
public interface IKeyProtector
{
    /// <summary>Wraps a secret.</summary>
    /// <param name="secret">The secret, for example the master key.</param>
    /// <returns>The wrapped form, or a failure when the secret could not be protected.</returns>
    Result<byte[]> Protect(ReadOnlySpan<byte> secret);

    /// <summary>Unwraps a secret that <see cref="Protect"/> produced.</summary>
    /// <param name="protectedSecret">The wrapped form.</param>
    /// <returns>The secret, or a failure when it is damaged or belongs to another user or machine.</returns>
    Result<byte[]> Unprotect(ReadOnlySpan<byte> protectedSecret);
}
