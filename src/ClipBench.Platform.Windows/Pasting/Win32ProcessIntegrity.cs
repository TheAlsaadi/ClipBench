using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace ClipBench.Platform.Windows.Pasting;

/// <summary>
/// Reads the mandatory integrity level of a process from its token. It asks for query rights only. A process
/// with more rights than ClipBench refuses even that, and the refusal is reported as "access denied", which is
/// the answer the caller needs.
/// </summary>
internal sealed unsafe class Win32ProcessIntegrity : IProcessIntegrity
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidParameter = 87;

    /// <summary>Reads ClipBench's own level.</summary>
    public Win32ProcessIntegrity()
    {
        var own = QueryToken(PInvoke.GetCurrentProcess(), ownsHandle: false);
        OwnLevel = own.Outcome == IntegrityOutcome.Known ? own.Level : 0x2000;   // medium, the level of an ordinary user program
    }

    /// <inheritdoc/>
    public int OwnLevel { get; }

    /// <inheritdoc/>
    public IntegrityResult Query(uint processId)
    {
        var process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsNull)
        {
            return new IntegrityResult(Marshal.GetLastPInvokeError() == ErrorInvalidParameter ? IntegrityOutcome.NotFound : IntegrityOutcome.AccessDenied);
        }

        return QueryToken(process, ownsHandle: true);
    }

    private static IntegrityResult QueryToken(HANDLE process, bool ownsHandle)
    {
        try
        {
            HANDLE token;
            if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, &token))
            {
                return new IntegrityResult(Marshal.GetLastPInvokeError() == ErrorAccessDenied ? IntegrityOutcome.AccessDenied : IntegrityOutcome.NotFound);
            }

            try
            {
                uint needed = 0;
                PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenIntegrityLevel, null, 0, &needed);
                if (needed == 0 || needed > 1024)
                {
                    return new IntegrityResult(IntegrityOutcome.AccessDenied);
                }

                Span<byte> buffer = stackalloc byte[(int)needed];
                fixed (byte* pointer = buffer)
                {
                    if (!PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenIntegrityLevel, pointer, needed, &needed))
                    {
                        return new IntegrityResult(IntegrityOutcome.AccessDenied);
                    }

                    // The level is the last sub-authority of the label's SID.
                    var sid = ((TOKEN_MANDATORY_LABEL*)pointer)->Label.Sid;
                    var count = *PInvoke.GetSidSubAuthorityCount(sid);
                    return count == 0
                        ? new IntegrityResult(IntegrityOutcome.AccessDenied)
                        : new IntegrityResult(IntegrityOutcome.Known, (int)*PInvoke.GetSidSubAuthority(sid, (uint)(count - 1)));
                }
            }
            finally
            {
                PInvoke.CloseHandle(token);
            }
        }
        finally
        {
            if (ownsHandle)
            {
                PInvoke.CloseHandle(process);
            }
        }
    }
}
