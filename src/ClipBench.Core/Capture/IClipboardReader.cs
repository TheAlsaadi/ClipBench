using ClipBench.Core.Results;

namespace ClipBench.Core.Capture;

/// <summary>Reads what is on the clipboard now.</summary>
public interface IClipboardReader
{
    /// <summary>
    /// Reads the clipboard if the filter allows it. The filter is applied before any content is read, so
    /// a change that carries a privacy marker, comes from an excluded program or is ClipBench's own is
    /// never copied into memory: the capture says why it was rejected and holds no content.
    /// </summary>
    /// <param name="filter">The rules for what is recorded.</param>
    /// <param name="cancellationToken">Cancels the wait for a busy clipboard.</param>
    /// <returns>
    /// The capture, accepted or rejected; <see cref="ErrorCode.ClipboardEmpty"/> when nothing usable is
    /// on the clipboard; <see cref="ErrorCode.ClipboardBusy"/> when another program kept it open;
    /// <see cref="ErrorCode.ClipboardUnreadable"/> when the content is damaged or over the safety limit.
    /// </returns>
    Task<Result<ClipboardCapture>> ReadAsync(CaptureFilter filter, CancellationToken cancellationToken);
}
