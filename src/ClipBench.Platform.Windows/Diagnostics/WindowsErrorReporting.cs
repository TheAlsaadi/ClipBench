using ClipBench.Core.Results;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.ErrorReporting;

namespace ClipBench.Platform.Windows.Diagnostics;

/// <summary>
/// Keeps clip content out of the reports Windows makes when a program crashes. The heap is where ClipBench holds the
/// history it has decrypted, so a report must never include it.
/// </summary>
public static class WindowsErrorReporting
{
    /// <summary>Tells Windows Error Reporting to leave the heap out of any crash report for this process.</summary>
    /// <returns>Success once Windows reports the flag as set, or the reason it is not.</returns>
    public static Result ExcludeHeap()
    {
        var set = PInvoke.WerSetFlags(WER_FAULT_REPORTING.WER_FAULT_REPORTING_FLAG_NOHEAP);
        return set.Succeeded && IsHeapExcluded()
            ? Result.Success()
            : Result.Failure(new ResultError(ErrorCode.PlatformFailure, "Windows would not leave memory out of its crash reports."));
    }

    // Asks Windows what it will do, rather than trusting the call that set it.
    internal static bool IsHeapExcluded()
    {
        // The pseudo-handle of the current process needs no closing.
        using var self = new SafeProcessHandle(PInvoke.GetCurrentProcess(), ownsHandle: false);
        return PInvoke.WerGetFlags(self, out var flags).Succeeded
            && flags.HasFlag(WER_FAULT_REPORTING.WER_FAULT_REPORTING_FLAG_NOHEAP);
    }
}
