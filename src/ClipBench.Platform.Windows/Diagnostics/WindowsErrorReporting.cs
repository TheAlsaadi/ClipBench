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
    /// <returns>Success, or the reason Windows refused.</returns>
    public static Result ExcludeHeap()
    {
        var set = PInvoke.WerSetFlags(WER_FAULT_REPORTING.WER_FAULT_REPORTING_FLAG_NOHEAP);
        return set.Succeeded
            ? Result.Success()
            : Result.Failure(new ResultError(ErrorCode.PlatformFailure, "Windows would not leave memory out of its crash reports."));
    }

    /// <summary>Says whether crash reports for this process leave the heap out.</summary>
    /// <returns><see langword="true"/> when the heap is left out.</returns>
    public static bool IsHeapExcluded()
    {
        // The pseudo-handle of the current process needs no closing.
        using var self = new SafeProcessHandle(PInvoke.GetCurrentProcess(), ownsHandle: false);
        return PInvoke.WerGetFlags(self, out var flags).Succeeded
            && flags.HasFlag(WER_FAULT_REPORTING.WER_FAULT_REPORTING_FLAG_NOHEAP);
    }
}
