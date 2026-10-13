using System.Text;
using ClipBench.Core.Results;
using ClipBench.Core.Security;

namespace ClipBench.Storage.Tests.Support;

/// <summary>
/// A reversible stand-in for DPAPI. A wrapped secret is a marker, the user it belongs to, and the
/// scrambled secret. Unwrapping something without the marker, or something wrapped for another user,
/// fails, like the real thing does.
/// </summary>
internal sealed class FakeKeyProtector(string user = "alice") : IKeyProtector
{
    private static readonly byte[] _marker = "FAKEWRAP"u8.ToArray();

    private readonly byte[] _user = Encoding.UTF8.GetBytes(user);

    /// <summary>When true, <see cref="Protect"/> fails.</summary>
    public bool FailProtect { get; set; }

    /// <summary>When true, <see cref="Unprotect"/> fails.</summary>
    public bool FailUnprotect { get; set; }

    /// <summary>When set, <see cref="Protect"/> returns this instead of a valid wrap.</summary>
    public byte[]? ProtectOutput { get; set; }

    /// <summary>When set, <see cref="Unprotect"/> returns this instead of the real secret.</summary>
    public byte[]? UnprotectOutput { get; set; }

    /// <summary>Runs inside <see cref="Protect"/>, to simulate another process acting at the same moment.</summary>
    public Action? OnProtect { get; set; }

    public int ProtectCalls { get; private set; }

    public int UnprotectCalls { get; private set; }

    public Result<byte[]> Protect(ReadOnlySpan<byte> secret)
    {
        ProtectCalls++;
        OnProtect?.Invoke();
        if (FailProtect)
        {
            return Failure("fake protect failure");
        }

        if (ProtectOutput is not null)
        {
            return Result.Success(ProtectOutput);
        }

        var header = _marker.Length + 1 + _user.Length;
        var wrapped = new byte[header + secret.Length];
        _marker.CopyTo(wrapped, 0);
        wrapped[_marker.Length] = (byte)_user.Length;
        _user.CopyTo(wrapped, _marker.Length + 1);
        for (var i = 0; i < secret.Length; i++)
        {
            wrapped[header + i] = (byte)(secret[i] ^ Mask(i));
        }

        return Result.Success(wrapped);
    }

    public Result<byte[]> Unprotect(ReadOnlySpan<byte> protectedSecret)
    {
        UnprotectCalls++;
        var header = _marker.Length + 1 + _user.Length;
        var belongsToUs = protectedSecret.Length >= header
            && protectedSecret.StartsWith(_marker)
            && protectedSecret[_marker.Length] == _user.Length
            && protectedSecret.Slice(_marker.Length + 1, _user.Length).SequenceEqual(_user);
        if (FailUnprotect || !belongsToUs)
        {
            return Failure("fake unprotect failure");
        }

        if (UnprotectOutput is not null)
        {
            return Result.Success(UnprotectOutput);
        }

        var body = protectedSecret[header..];
        var secret = new byte[body.Length];
        for (var i = 0; i < body.Length; i++)
        {
            secret[i] = (byte)(body[i] ^ Mask(i));
        }

        return Result.Success(secret);
    }

    private static Result<byte[]> Failure(string message) =>
        Result.Failure<byte[]>(new ResultError(ErrorCode.KeyProtectionFailed, message));

    private byte Mask(int index) => unchecked((byte)(_user[0] + index));
}
